using ERP.Application.Accounting;
using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory;

public sealed record ReceiveOpeningStockCommand(
    ProductId ProductId,
    WarehouseId WarehouseId,
    decimal Quantity,
    long UnitCostRials,
    DateOnly ReceivedOn);

public interface IReceiveOpeningStockHandler
{
    Task<Result<bool>> ExecuteAsync(
        ReceiveOpeningStockCommand command,
        CancellationToken cancellationToken);
}

public sealed class ReceiveOpeningStockHandler : IReceiveOpeningStockHandler
{
    private readonly IStockLedgerRepository _stockLedgers;
    private readonly IAuditWriter _audit;
    private readonly IJournalEntryRepository _journal;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public ReceiveOpeningStockHandler(
        IStockLedgerRepository stockLedgers,
        IAuditWriter audit,
        IJournalEntryRepository journal,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _stockLedgers = stockLedgers;
        _audit = audit;
        _journal = journal;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<bool>> ExecuteAsync(
        ReceiveOpeningStockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var ledger = await _stockLedgers.GetAsync(
                command.ProductId,
                command.WarehouseId,
                cancellationToken).ConfigureAwait(false);
            ledger ??= StockLedger.Empty(command.ProductId, command.WarehouseId);

            ledger.ReceiveOpeningStock(
                Quantity.Create(command.Quantity),
                Money.FromRials(command.UnitCostRials),
                command.ReceivedOn);

            await _stockLedgers.SaveAsync(ledger, cancellationToken).ConfigureAwait(false);
            await OpeningStockJournal.PostAsync(_journal, ledger.Layers[^1], _clock.UtcNow, cancellationToken).ConfigureAwait(false);

            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "inventory.opening-stock.received",
                    nameof(StockLedger),
                    $"{command.ProductId}:{command.WarehouseId}",
                    null,
                    command.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(true);
        }
        catch (DomainException exception)
        {
            return Result.Failure<bool>("inventory.opening-stock.invalid", exception.Message);
        }
    }
}
