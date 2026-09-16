using System.Globalization;
using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Application.Inventory;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <param name="ApproveCreditOverLimit">
/// Set only after the screen has shown that this نسیه sale takes the customer
/// past their credit limit and someone has confirmed it (§6.7 «Manager
/// Approval»). There is no permission system yet (appendix A.6), so the flag
/// cannot check that the person confirming is a manager; the approval is
/// recorded in the audit trail with the user who completed the sale.
/// </param>
public sealed record CompleteSaleCommand(
    SaleId SaleId,
    PaymentMethod PaymentMethod,
    long DiscountRials,
    decimal TaxRatePercent,
    bool AllowNegativeStock = false,
    bool ApproveCreditOverLimit = false);

/// <summary>
/// What the cashier needs right after «ثبت»: the number to read out and print,
/// and the footer amounts.
/// </summary>
public sealed record CompletedSale(SaleNumber Number, SaleTotals Totals);

public interface ICompleteSaleHandler
{
    Task<Result<CompletedSale>> ExecuteAsync(CompleteSaleCommand command, CancellationToken cancellationToken);
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
    private readonly ISaleNumberGenerator _numbers;
    private readonly ICustomerRepository _customers;
    private readonly ICustomerLedgerReader _customerLedger;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CompleteSaleHandler(
        ISaleRepository sales,
        IStockLedgerRepository stockLedgers,
        ISaleNumberGenerator numbers,
        ICustomerRepository customers,
        ICustomerLedgerReader customerLedger,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _sales = sales;
        _stockLedgers = stockLedgers;
        _numbers = numbers;
        _customers = customers;
        _customerLedger = customerLedger;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<CompletedSale>> ExecuteAsync(
        CompleteSaleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sale = await _sales.GetAsync(command.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<CompletedSale>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        try
        {
            if (command.DiscountRials > 0)
            {
                sale.ApplyDiscount(Money.FromRials(command.DiscountRials));
            }

            // Every check that can turn the sale away runs before stock moves
            // and before a number is drawn.
            var creditRejection = await CheckCreditAsync(sale, command, cancellationToken).ConfigureAwait(false);
            if (creditRejection is not null)
            {
                return creditRejection;
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

            // Drawn last, after every check that can still reject the sale, so an
            // ordinary validation failure does not leave a gap in the numbering.
            var number = await _numbers.NextAsync(cancellationToken).ConfigureAwait(false);
            var totals = sale.Complete(number, command.PaymentMethod, command.TaxRatePercent, _clock.UtcNow);

            await _sales.SaveAsync(sale, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "sales.sale.completed",
                    nameof(Domain.Sales.Sale),
                    sale.Id.ToString(),
                    null,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"number={number};total={totals.Total.Rials};method={command.PaymentMethod};customer={sale.CustomerId}"),
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);

            if (command.ApproveCreditOverLimit && command.PaymentMethod == PaymentMethod.Credit)
            {
                await _audit.WriteAsync(
                    new AuditEntry(
                        Guid.NewGuid(),
                        _userContext.UserId,
                        "sales.credit.over-limit-approved",
                        nameof(Domain.Sales.Sale),
                        sale.Id.ToString(),
                        null,
                        string.Create(CultureInfo.InvariantCulture, $"number={number};total={totals.Total.Rials}"),
                        _clock.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new CompletedSale(number, totals));
        }
        catch (InsufficientStockException exception)
        {
            // Its own code, so the screen can offer the one way forward
            // (completing with a tracked shortfall) instead of a dead end.
            return Result.Failure<CompletedSale>("sales.sale.insufficient-stock", exception.Message);
        }
        catch (DomainException exception)
        {
            return Result.Failure<CompletedSale>("sales.sale.invalid", exception.Message);
        }
    }

    /// <summary>
    /// Source-of-truth §6.7, via the policy <see cref="CompleteSaleCorrectionHandler"/>
    /// shares — see <see cref="SaleCreditPolicy"/>. Returns a failure to hand
    /// back to the screen, or null when the sale may go ahead.
    /// </summary>
    private async Task<Result<CompletedSale>?> CheckCreditAsync(
        Sale sale,
        CompleteSaleCommand command,
        CancellationToken cancellationToken)
    {
        var rejection = await SaleCreditPolicy.EvaluateAsync(
                sale, command.PaymentMethod, command.TaxRatePercent, command.ApproveCreditOverLimit,
                _customers, _customerLedger, cancellationToken)
            .ConfigureAwait(false);

        return rejection is null ? null : Result.Failure<CompletedSale>(rejection.Code, rejection.Message);
    }
}
