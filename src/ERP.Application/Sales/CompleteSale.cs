using System.Globalization;
using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

public sealed record CompleteSaleCommand(
    SaleId SaleId,
    PaymentMethod PaymentMethod,
    long DiscountRials,
    decimal TaxRatePercent,
    bool AllowNegativeStock = false);

public interface ICompleteSaleHandler
{
    Task<Result<SaleTotals>> ExecuteAsync(CompleteSaleCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Finalizes a Draft sale: pulls FIFO stock for every line, marks the sale
/// Completed, and writes the audit entry — all through repositories sharing one
/// <see cref="IUnitOfWork"/>, so it commits or rolls back as a single unit
/// (source-of-truth §2.9: "Invoice / Payment / Stock / Accounting — either all
/// commit or the operation rolls back"). Stock is pulled before the sale is
/// marked Completed, so an <see cref="InsufficientStockException"/> partway
/// through aborts the whole transaction instead of leaving a paid sale with an
/// unfulfilled line.
///
/// <see cref="CompleteSaleCommand.AllowNegativeStock"/> is a real retail need,
/// not an edge case: goods often physically leave the store before whoever
/// received them has entered it into the system yet. When set, a shortage no
/// longer blocks the sale — <see cref="StockLedger.ConsumeFifoAllowingBackorder"/>
/// covers what it can and records the rest as a tracked backorder instead of
/// throwing. It defaults to false and must be turned on per-sale by whoever is
/// completing it (a cashier/manager decision, not a silent default —
/// source-of-truth §3.14 on deliberate triggers for risk-bearing actions).
/// </summary>
public sealed class CompleteSaleHandler : ICompleteSaleHandler
{
    private readonly ISaleRepository _sales;
    private readonly IStockLedgerRepository _stockLedgers;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CompleteSaleHandler(
        ISaleRepository sales,
        IStockLedgerRepository stockLedgers,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _sales = sales;
        _stockLedgers = stockLedgers;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<SaleTotals>> ExecuteAsync(
        CompleteSaleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sale = await _sales.GetAsync(command.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<SaleTotals>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        try
        {
            if (command.DiscountRials > 0)
            {
                sale.ApplyDiscount(Money.FromRials(command.DiscountRials));
            }

            foreach (var line in sale.Lines)
            {
                var ledger = await _stockLedgers
                    .GetAsync(line.ProductId, sale.WarehouseId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? StockLedger.Empty(line.ProductId, sale.WarehouseId);

                if (command.AllowNegativeStock)
                {
                    ledger.ConsumeFifoAllowingBackorder(line.Quantity, $"sale:{sale.Id}");
                }
                else
                {
                    ledger.ConsumeFifo(line.Quantity, $"sale:{sale.Id}");
                }

                await _stockLedgers.SaveAsync(ledger, cancellationToken).ConfigureAwait(false);
            }

            var totals = sale.Complete(command.PaymentMethod, command.TaxRatePercent, _clock.UtcNow);

            await _sales.SaveAsync(sale, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "sales.sale.completed",
                    nameof(Domain.Sales.Sale),
                    sale.Id.ToString(),
                    null,
                    totals.Total.Rials.ToString(CultureInfo.InvariantCulture),
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(totals);
        }
        catch (DomainException exception)
        {
            return Result.Failure<SaleTotals>("sales.sale.invalid", exception.Message);
        }
    }
}
