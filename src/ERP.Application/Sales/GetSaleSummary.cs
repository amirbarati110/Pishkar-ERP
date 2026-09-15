using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

public sealed record GetSaleSummaryQuery(SaleId SaleId);

/// <summary>
/// A cart line for display. Deliberately has no product name: the Sale
/// aggregate only stores ProductId (it is not a read-model), and the cashier's
/// screen already knows each product's name from the search result it was
/// added from — no join is needed for the one client that reads this today.
/// </summary>
public sealed record SaleLineSummary(
    ProductId ProductId,
    decimal Quantity,
    Money UnitPrice,
    Money LineTotal);

public sealed record SaleSummary(
    SaleId SaleId,
    SaleStatus Status,
    IReadOnlyList<SaleLineSummary> Lines,
    Money Subtotal,
    Money Discount);

public interface IGetSaleSummaryHandler
{
    Task<Result<SaleSummary>> ExecuteAsync(GetSaleSummaryQuery query, CancellationToken cancellationToken);
}

public sealed class GetSaleSummaryHandler : IGetSaleSummaryHandler
{
    private readonly ISaleRepository _sales;

    public GetSaleSummaryHandler(ISaleRepository sales)
    {
        _sales = sales;
    }

    public async Task<Result<SaleSummary>> ExecuteAsync(
        GetSaleSummaryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sale = await _sales.GetAsync(query.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<SaleSummary>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        var lines = sale.Lines
            .Select(line => new SaleLineSummary(line.ProductId, line.Quantity.Value, line.UnitPrice, line.LineTotal))
            .ToList();

        return Result.Success(new SaleSummary(sale.Id, sale.Status, lines, sale.Subtotal, sale.Discount));
    }
}
