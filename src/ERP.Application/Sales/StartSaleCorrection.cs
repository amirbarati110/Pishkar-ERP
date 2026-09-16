using ERP.Application.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <param name="Reason">
/// Mandatory — a posted invoice is never edited or deleted (Codex rule 15), so
/// every correction is a deliberate, accountable act, not a quiet fix. Stored
/// on the correction's own <see cref="Sale.Note"/>.
/// </param>
public sealed record StartSaleCorrectionCommand(SaleId OriginalSaleId, string Reason);

public interface IStartSaleCorrectionHandler
{
    Task<Result<SaleId>> ExecuteAsync(StartSaleCorrectionCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Opens a «صورتحساب اصلاحی» draft standing in for a completed sale
/// (source-of-truth §10.10). The original invoice is read but never written
/// to here or anywhere downstream — see <see cref="Sale.OpenCorrection"/>.
///
/// Deliberately narrow for this version: only discount, service charge, tax
/// rate and payment method can differ from the original once the draft is
/// edited through the ordinary <see cref="SetSaleChargesHandler"/> and
/// <see cref="CompleteSaleCorrectionHandler"/> — quantity, item and buyer
/// changes are refused here because they need «مرجوعی» (stock return,
/// §6.25) or «ابطال» (void) respectively, neither built yet, and pretending
/// to support them would silently corrupt inventory or the customer's
/// account instead of failing honestly.
/// </summary>
public sealed class StartSaleCorrectionHandler : IStartSaleCorrectionHandler
{
    private readonly ISaleRepository _sales;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public StartSaleCorrectionHandler(ISaleRepository sales, IUnitOfWork unitOfWork, IClock clock)
    {
        _sales = sales;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SaleId>> ExecuteAsync(
        StartSaleCorrectionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<SaleId>("sales.correction.reason-required", "دلیل اصلاح فاکتور را بنویسید.");
        }

        var original = await _sales.GetAsync(command.OriginalSaleId, cancellationToken).ConfigureAwait(false);
        if (original is null)
        {
            return Result.Failure<SaleId>("sales.sale.not-found", "فاکتور اصلی یافت نشد.");
        }

        if (original.Status != SaleStatus.Completed)
        {
            return Result.Failure<SaleId>(
                "sales.correction.not-completed",
                "فقط فاکتور ثبت‌شده و پرداخت‌شده قابل اصلاح است.");
        }

        if (original.CorrectsSaleId is not null)
        {
            return Result.Failure<SaleId>(
                "sales.correction.corrects-a-correction",
                "این فاکتور خودش یک اصلاحیه است؛ زنجیره‌ی اصلاحیه پشت اصلاحیه پشتیبانی نمی‌شود.");
        }

        if (await _sales.HasCorrectionAsync(original.Id, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<SaleId>(
                "sales.correction.already-corrected",
                "برای این فاکتور قبلاً یک اصلاحیه ثبت شده است.");
        }

        var correction = Sale.OpenCorrection(original, _clock.UtcNow);
        correction.SetNote(command.Reason);

        await _sales.SaveAsync(correction, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(correction.Id);
    }
}
