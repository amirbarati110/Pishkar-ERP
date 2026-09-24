using System.Globalization;
using ERP.Application.Accounting;
using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <param name="Lines">Which products, how many, and where each goes (shelf / damaged / waste).</param>
/// <param name="RefundMethod">Cash out of the drawer, back to the card, or off what the customer owes.</param>
/// <param name="RefundServiceCharge">
/// «خدمات/هزینه هم پس داده شود» — the cashier's choice, never automatic (user decision
/// 1405/07/02). With it, a return may hold no goods at all (a delivery that never happened).
/// </param>
public sealed record CompleteSaleReturnCommand(
    SaleId SaleId,
    IReadOnlyList<ReturnLineRequest> Lines,
    PaymentMethod RefundMethod,
    string? Reason,
    bool RefundServiceCharge = false);

/// <summary>What the cashier needs right after «ثبت مرجوعی»: the number to read out and the money to hand back.</summary>
public sealed record CompletedSaleReturn(ReturnNumber Number, Money Refund, PaymentMethod RefundMethod);

public interface ICompleteSaleReturnHandler
{
    Task<Result<CompletedSaleReturn>> ExecuteAsync(CompleteSaleReturnCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// «مرجوعی» (§6.25): prices the return, puts goods marked «برگشت به انبار»
/// back on the shelf at the cost they left at, posts its accounting entry,
/// and writes the audit trail — one unit of work, so a return either
/// happens completely or not at all (§2.9), exactly like the sale it undoes.
/// </summary>
public sealed class CompleteSaleReturnHandler : ICompleteSaleReturnHandler
{
    private readonly ISaleRepository _sales;
    private readonly ISaleReturnRepository _returns;
    private readonly ISaleLineCostRepository _costs;
    private readonly IStockLedgerRepository _stockLedgers;
    private readonly IReturnNumberGenerator _numbers;
    private readonly IAuditWriter _audit;
    private readonly IJournalEntryRepository _journal;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CompleteSaleReturnHandler(
        ISaleRepository sales,
        ISaleReturnRepository returns,
        ISaleLineCostRepository costs,
        IStockLedgerRepository stockLedgers,
        IReturnNumberGenerator numbers,
        IAuditWriter audit,
        IJournalEntryRepository journal,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _sales = sales;
        _returns = returns;
        _costs = costs;
        _stockLedgers = stockLedgers;
        _numbers = numbers;
        _audit = audit;
        _journal = journal;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<CompletedSaleReturn>> ExecuteAsync(
        CompleteSaleReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sale = await _sales.GetAsync(command.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<CompletedSaleReturn>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        if (await _sales.HasCorrectionAsync(sale.Id, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<CompletedSaleReturn>(
                "sales.return.superseded",
                "این فاکتور اصلاح شده؛ مرجوعی را روی صورتحساب اصلاحی بگیرید.");
        }

        try
        {
            var previouslyReturned = await _returns.GetReturnedAsync(sale.Id, cancellationToken).ConfigureAwait(false);
            var unitCosts = await UnitCostsOfAsync(sale, cancellationToken).ConfigureAwait(false);

            // Everything that can turn the return away runs before stock moves
            // and before a number is drawn, as with a sale.
            var serviceChargeRefunded = command.RefundServiceCharge
                && await _returns.HasRefundedServiceChargeAsync(sale.Id, cancellationToken).ConfigureAwait(false);
            var priced = SaleReturn.Price(sale, command.Lines, previouslyReturned, unitCosts, command.RefundServiceCharge);
            if (command.RefundServiceCharge)
            {
                // throws when there is no service charge or it already went back
                _ = SaleReturn.PriceServiceCharge(sale, serviceChargeRefunded);
            }

            var number = await _numbers.NextAsync(cancellationToken).ConfigureAwait(false);
            var saleReturn = SaleReturn.Create(
                sale, number, command.Lines, command.RefundMethod, command.Reason,
                previouslyReturned, unitCosts, _clock.UtcNow, command.RefundServiceCharge, serviceChargeRefunded);

            var reference = $"return:{saleReturn.Id}";
            var today = PersianDate.GregorianDateInIran(_clock.UtcNow);
            foreach (var line in priced.Where(row => row.Disposition == ReturnDisposition.ToStock))
            {
                var ledger = await _stockLedgers
                    .GetAsync(line.ProductId, sale.WarehouseId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? StockLedger.Empty(line.ProductId, sale.WarehouseId);

                ledger.ReceiveReturn(line.Quantity, line.UnitCost!.Value, today, reference);
                await _stockLedgers.SaveAsync(ledger, cancellationToken).ConfigureAwait(false);
            }

            await _returns.AddAsync(saleReturn, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "sales.return.completed",
                    nameof(SaleReturn),
                    saleReturn.Id.ToString(),
                    null,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"number={number};sale={sale.Number};refund={saleReturn.RefundTotal.Rials};method={command.RefundMethod};reason={saleReturn.Reason}"),
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);

            var journalEntry = SaleReturnJournalEntryFactory.Create(saleReturn, _clock.UtcNow);
            if (journalEntry is not null)
            {
                await _journal.SaveAsync(journalEntry, cancellationToken).ConfigureAwait(false);
            }

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new CompletedSaleReturn(number, saleReturn.RefundTotal, command.RefundMethod));
        }
        catch (DomainException exception)
        {
            return Result.Failure<CompletedSaleReturn>("sales.return.invalid", exception.Message);
        }
        catch (DataConflictException exception)
        {
            // another till gave the same service charge back a moment earlier
            return Result.Failure<CompletedSaleReturn>(exception.Code, exception.Message);
        }
    }

    /// <summary>
    /// Costs live on the invoice that consumed the stock. A «صورتحساب اصلاحی»
    /// consumes none of its own (it only restates the money), so its goods
    /// were costed on the sale it corrects.
    /// </summary>
    private async Task<IReadOnlyDictionary<Domain.Catalog.ProductId, Money>> UnitCostsOfAsync(
        Sale sale,
        CancellationToken cancellationToken)
    {
        return await _costs
            .GetUnitCostsAsync(sale.CorrectsSaleId ?? sale.Id, cancellationToken)
            .ConfigureAwait(false);
    }
}
