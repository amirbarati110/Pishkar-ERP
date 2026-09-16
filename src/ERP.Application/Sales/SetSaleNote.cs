using ERP.Application.Common;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <summary>«توضیحات فاکتور» on an open invoice. Null/blank clears it.</summary>
public sealed record SetSaleNoteCommand(SaleId SaleId, string? Note);

public interface ISetSaleNoteHandler
{
    Task<Result<bool>> ExecuteAsync(SetSaleNoteCommand command, CancellationToken cancellationToken);
}

public sealed class SetSaleNoteHandler : ISetSaleNoteHandler
{
    private readonly ISaleRepository _sales;
    private readonly IUnitOfWork _unitOfWork;

    public SetSaleNoteHandler(ISaleRepository sales, IUnitOfWork unitOfWork)
    {
        _sales = sales;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> ExecuteAsync(SetSaleNoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sale = await _sales.GetAsync(command.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<bool>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        try
        {
            sale.SetNote(command.Note);
        }
        catch (DomainException exception)
        {
            return Result.Failure<bool>("sales.sale.invalid-note", exception.Message);
        }

        await _sales.SaveAsync(sale, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(true);
    }
}
