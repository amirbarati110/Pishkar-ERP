using ERP.Application.Accounting;
using ERP.Application.Audit;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;

namespace ERP.Application.Importing;

public sealed record CommitProductImportCommand(
    IReadOnlyList<PreparedProductImportRow> Rows,
    WarehouseId WarehouseId);

public interface ICommitProductImportHandler
{
    Task<Result<int>> ExecuteAsync(CommitProductImportCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Writes a previously-previewed batch (<see cref="ProductImportPreparer"/>)
/// in one transaction — «کل Import Batch را Rollback کرد» (milestone-1 §15
/// acceptance criterion 4). Nothing here re-decides validity; a batch is only
/// ever committed after <see cref="ProductImportPreview.CanImport"/> was
/// true, but a duplicate barcode can still slip in between preview and
/// commit (another till importing at the same moment) — caught here against
/// the real database, and the whole batch is refused, not just that row.
/// </summary>
public sealed class CommitProductImportHandler : ICommitProductImportHandler
{
    private readonly IProductRepository _products;
    private readonly IStockLedgerRepository _stockLedgers;
    private readonly IAuditWriter _audit;
    private readonly IJournalEntryRepository _journal;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CommitProductImportHandler(
        IProductRepository products,
        IStockLedgerRepository stockLedgers,
        IAuditWriter audit,
        IJournalEntryRepository journal,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _products = products;
        _stockLedgers = stockLedgers;
        _audit = audit;
        _journal = journal;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<int>> ExecuteAsync(CommitProductImportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Rows.Count == 0)
        {
            return Result.Failure<int>("import.product.nothing-to-import", "ردیفی برای وارد کردن انتخاب نشده است.");
        }

        var receivedOn = PersianDate.GregorianDateInIran(_clock.UtcNow);

        try
        {
            foreach (var row in command.Rows)
            {
                var product = Product.Create(row.Name, row.Sku, row.CategoryId, row.BaseUnitId, row.SalePrice);

                foreach (var rawBarcode in row.Barcodes)
                {
                    var barcode = ProductBarcode.Create(rawBarcode);
                    if (await _products.BarcodeExistsAsync(barcode.Value, cancellationToken).ConfigureAwait(false))
                    {
                        // هیچ‌چیز از این دسته تا این‌جا commit نشده — بازگشت
                        // بدون فراخوانی CommitAsync یعنی رول‌بک کل تراکنش.
                        return Result.Failure<int>(
                            "import.product.duplicate-barcode",
                            $"ردیف {row.RowNumber}: بارکد {barcode.Value} قبلاً برای کالای دیگری ثبت شده است.");
                    }

                    product.AddBarcode(barcode.Value);
                }

                await _products.AddAsync(product, cancellationToken).ConfigureAwait(false);

                if (row.OpeningStockQuantity is { } quantity)
                {
                    var ledger = StockLedger.Empty(product.Id, command.WarehouseId);
                    ledger.ReceiveOpeningStock(
                        Quantity.Create(quantity),
                        row.OpeningStockUnitCost ?? Money.Zero,
                        receivedOn);
                    await _stockLedgers.SaveAsync(ledger, cancellationToken).ConfigureAwait(false);
                    await OpeningStockJournal.PostAsync(_journal, ledger.Layers[^1], _clock.UtcNow, cancellationToken).ConfigureAwait(false);
                }

                await _audit.WriteAsync(
                    new AuditEntry(
                        Guid.NewGuid(),
                        _userContext.UserId,
                        "catalog.product.imported",
                        nameof(Product),
                        product.Id.ToString(),
                        null,
                        $"row={row.RowNumber};name={product.Name}",
                        _clock.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(command.Rows.Count);
        }
        catch (DomainException exception)
        {
            return Result.Failure<int>("import.product.invalid", exception.Message);
        }
        catch (DataConflictException exception)
        {
            return Result.Failure<int>(exception.Code, exception.Message);
        }
    }
}
