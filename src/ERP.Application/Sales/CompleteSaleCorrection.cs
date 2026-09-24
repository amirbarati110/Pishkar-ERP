using System.Globalization;
using ERP.Application.Accounting;
using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

public sealed record CompleteSaleCorrectionCommand(
    SaleId SaleId,
    PaymentMethod PaymentMethod,
    decimal TaxRatePercent,
    bool ApproveCreditOverLimit = false);

public interface ICompleteSaleCorrectionHandler
{
    Task<Result<CompletedSale>> ExecuteAsync(CompleteSaleCorrectionCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Finalizes a «صورتحساب اصلاحی» draft (<see cref="StartSaleCorrectionHandler"/>):
/// draws its own ascending number from the same sequence as any invoice
/// (§10.10 — a correction is its own real financial document, not an edit of
/// the original's), and writes the audit entry linking the two. Unlike
/// <see cref="CompleteSaleHandler"/>, it never touches <c>StockLedger</c> — the
/// goods in this version's lines are exactly the goods on the original, which
/// already left the shelf once; consuming FIFO stock again here would deduct
/// the same sale from inventory twice.
/// </summary>
public sealed class CompleteSaleCorrectionHandler : ICompleteSaleCorrectionHandler
{
    private readonly ISaleRepository _sales;
    private readonly ISaleNumberGenerator _numbers;
    private readonly ICustomerRepository _customers;
    private readonly ICustomerLedgerReader _customerLedger;
    private readonly IAuditWriter _audit;
    private readonly IJournalEntryRepository _journal;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CompleteSaleCorrectionHandler(
        ISaleRepository sales,
        ISaleNumberGenerator numbers,
        ICustomerRepository customers,
        ICustomerLedgerReader customerLedger,
        IAuditWriter audit,
        IJournalEntryRepository journal,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _sales = sales;
        _numbers = numbers;
        _customers = customers;
        _customerLedger = customerLedger;
        _audit = audit;
        _journal = journal;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<CompletedSale>> ExecuteAsync(
        CompleteSaleCorrectionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sale = await _sales.GetAsync(command.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<CompletedSale>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        if (sale.CorrectsSaleId is not { } originalSaleId)
        {
            // Defensive — StartSaleCorrectionHandler is the only way to create
            // one of these, so this should be unreachable in practice.
            return Result.Failure<CompletedSale>(
                "sales.correction.not-a-correction",
                "این فاکتور اصلاحیه نیست.");
        }

        try
        {
            var rejection = await SaleCreditPolicy.EvaluateAsync(
                    sale, command.PaymentMethod, command.TaxRatePercent, command.ApproveCreditOverLimit,
                    _customers, _customerLedger, cancellationToken)
                .ConfigureAwait(false);
            if (rejection is not null)
            {
                return Result.Failure<CompletedSale>(rejection.Code, rejection.Message);
            }

            var number = await _numbers.NextAsync(cancellationToken).ConfigureAwait(false);
            var totals = sale.Complete(number, command.PaymentMethod, command.TaxRatePercent, _clock.UtcNow);

            await _sales.SaveAsync(sale, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "sales.sale.corrected",
                    nameof(Domain.Sales.Sale),
                    sale.Id.ToString(),
                    null,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"corrects={originalSaleId};number={number};total={totals.Total.Rials};method={command.PaymentMethod};reason={sale.Note}"),
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);

            // اصلاحیه سند حسابداری تازه‌ی خودش را می‌گیرد — سند فاکتور اصلی
            // دست‌نخورده می‌ماند، دقیقاً مثل خود ردیف Sale (پیوست و.۱). ولی بخش فروشِ
            // آن سند با یک سند «برگشت» خنثی می‌شود؛ وگرنه درآمد هر دو فاکتور در دفتر
            // می‌ماند (ممیزی ۱۴۰۵/۰۷/۰۲: ۹٫۸ میلیون به جای ۴٫۹).
            var replacedEntry = await _journal
                    .GetBySourceAsync(JournalSourceType.Sale, originalSaleId.ToString(), cancellationToken)
                    .ConfigureAwait(false)
                ?? await _journal
                    .GetBySourceAsync(JournalSourceType.SaleCorrection, originalSaleId.ToString(), cancellationToken)
                    .ConfigureAwait(false);
            if (replacedEntry is not null
                && SaleJournalEntryFactory.ReverseSalePart(replacedEntry, sale.Id.ToString(), _clock.UtcNow) is { } reversal)
            {
                await _journal.SaveAsync(reversal, cancellationToken).ConfigureAwait(false);
            }

            var journalEntry = SaleJournalEntryFactory.Create(
                JournalSourceType.SaleCorrection, sale.Id.ToString(), totals, command.PaymentMethod, _clock.UtcNow);
            if (journalEntry is not null)
            {
                await _journal.SaveAsync(journalEntry, cancellationToken).ConfigureAwait(false);
            }

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new CompletedSale(number, totals));
        }
        catch (Domain.Common.DomainException exception)
        {
            return Result.Failure<CompletedSale>("sales.sale.invalid", exception.Message);
        }
    }
}
