using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

/// <summary>«پیدا کردن Invoice» (§6.25) — by the number the customer holds or the cashier reads off the receipt.</summary>
public sealed record FindSaleForReturnQuery(long Number);

public interface IFindSaleForReturnHandler
{
    Task<Result<SaleId>> ExecuteAsync(FindSaleForReturnQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Finds the completed invoice for a number. When that invoice was corrected,
/// the answer is the correction — the one the customer effectively holds and
/// the only one a return may be taken against.
/// </summary>
public sealed class FindSaleForReturnHandler : IFindSaleForReturnHandler
{
    private readonly ISaleReadReader _reader;

    public FindSaleForReturnHandler(ISaleReadReader reader)
    {
        _reader = reader;
    }

    public async Task<Result<SaleId>> ExecuteAsync(FindSaleForReturnQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Number <= 0)
        {
            return Result.Failure<SaleId>("sales.return.number-invalid", "شماره فاکتور را درست وارد کنید.");
        }

        var found = await _reader.FindCompletedByNumberAsync(query.Number, cancellationToken).ConfigureAwait(false);
        return found is null
            ? Result.Failure<SaleId>("sales.return.not-found", "فاکتور ثبت‌شده‌ای با این شماره پیدا نشد.")
            : Result.Success(found.SaleId);
    }
}

/// <summary>One invoice row as the return window shows it, with how much of it can still go back.</summary>
public sealed record ReturnableLine(
    ProductId ProductId,
    string ProductName,
    string UnitSymbol,
    decimal SoldQuantity,
    decimal ReturnedQuantity,
    Money UnitPrice)
{
    public decimal RemainingQuantity => SoldQuantity - ReturnedQuantity;
}

public sealed record ReturnableSale(
    SaleId SaleId,
    SaleNumber Number,
    DateTimeOffset CompletedAtUtc,
    CustomerId? CustomerId,
    PaymentMethod PaymentMethod,
    Money Total,
    IReadOnlyList<ReturnableLine> Lines);

public sealed record GetReturnableSaleQuery(SaleId SaleId);

public interface IGetReturnableSaleHandler
{
    Task<Result<ReturnableSale>> ExecuteAsync(GetReturnableSaleQuery query, CancellationToken cancellationToken);
}

public sealed class GetReturnableSaleHandler : IGetReturnableSaleHandler
{
    private readonly ISaleRepository _sales;
    private readonly ISaleReturnRepository _returns;
    private readonly ISaleReadReader _reader;

    public GetReturnableSaleHandler(ISaleRepository sales, ISaleReturnRepository returns, ISaleReadReader reader)
    {
        _sales = sales;
        _returns = returns;
        _reader = reader;
    }

    public async Task<Result<ReturnableSale>> ExecuteAsync(GetReturnableSaleQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sale = await _sales.GetAsync(query.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null || sale.Status != SaleStatus.Completed || sale.Number is not { } number
            || sale.PaymentMethod is not { } paymentMethod || sale.CompletedAtUtc is not { } completedAt)
        {
            return Result.Failure<ReturnableSale>("sales.return.not-found", "فاکتور ثبت‌شده‌ای با این شناسه پیدا نشد.");
        }

        if (await _sales.HasCorrectionAsync(sale.Id, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ReturnableSale>(
                "sales.return.superseded",
                "این فاکتور اصلاح شده؛ مرجوعی را روی صورتحساب اصلاحی بگیرید.");
        }

        var returned = await _returns.GetReturnedAsync(sale.Id, cancellationToken).ConfigureAwait(false);
        var products = await _reader
            .ReadProductsAsync(sale.WarehouseId, sale.Lines.Select(line => line.ProductId).ToList(), cancellationToken)
            .ConfigureAwait(false);

        var lines = sale.Lines
            .Select(line =>
            {
                products.TryGetValue(line.ProductId, out var info);
                return new ReturnableLine(
                    line.ProductId,
                    info?.Name ?? "کالای حذف‌شده",
                    info?.UnitSymbol ?? string.Empty,
                    line.Quantity.Value,
                    returned.TryGetValue(line.ProductId, out var previous) ? previous.Quantity : 0,
                    line.UnitPrice);
            })
            .ToList();

        return Result.Success(new ReturnableSale(
            sale.Id, number, completedAt, sale.CustomerId, paymentMethod,
            sale.Totals?.Total ?? sale.Subtotal, lines));
    }
}

public sealed record PreviewSaleReturnQuery(SaleId SaleId, IReadOnlyList<ReturnLineRequest> Lines);

/// <summary>What the customer would get back for the rows picked so far.</summary>
public sealed record SaleReturnPreview(Money Net, Money Tax, Money Refund, Money RestockCost);

public interface IPreviewSaleReturnHandler
{
    Task<Result<SaleReturnPreview>> ExecuteAsync(PreviewSaleReturnQuery query, CancellationToken cancellationToken);
}

/// <summary>Runs the very pricing <see cref="SaleReturn.Create"/> uses, so the number on the screen is the number that is refunded.</summary>
public sealed class PreviewSaleReturnHandler : IPreviewSaleReturnHandler
{
    private readonly ISaleRepository _sales;
    private readonly ISaleReturnRepository _returns;
    private readonly ISaleLineCostRepository _costs;

    public PreviewSaleReturnHandler(ISaleRepository sales, ISaleReturnRepository returns, ISaleLineCostRepository costs)
    {
        _sales = sales;
        _returns = returns;
        _costs = costs;
    }

    public async Task<Result<SaleReturnPreview>> ExecuteAsync(PreviewSaleReturnQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sale = await _sales.GetAsync(query.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<SaleReturnPreview>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        try
        {
            var returned = await _returns.GetReturnedAsync(sale.Id, cancellationToken).ConfigureAwait(false);
            var costs = await _costs.GetUnitCostsAsync(sale.CorrectsSaleId ?? sale.Id, cancellationToken).ConfigureAwait(false);
            var lines = SaleReturn.Price(sale, query.Lines, returned, costs);

            var net = lines.Aggregate(Money.Zero, (total, line) => total.Add(line.Net));
            var tax = lines.Aggregate(Money.Zero, (total, line) => total.Add(line.Tax));
            var restock = lines.Aggregate(Money.Zero, (total, line) => total.Add(line.RestockCost));
            return Result.Success(new SaleReturnPreview(net, tax, net.Add(tax), restock));
        }
        catch (DomainException exception)
        {
            return Result.Failure<SaleReturnPreview>("sales.return.invalid", exception.Message);
        }
    }
}
