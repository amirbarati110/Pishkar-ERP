using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

public sealed record GetSaleDetailsQuery(SaleId SaleId);

/// <summary>A cart row as the reference screen lays it out: کالا (کد، موجودی) · واحد · تعداد · قیمت واحد · تخفیف · مبلغ.</summary>
public sealed record SaleLineDetails(
    ProductId ProductId,
    string ProductName,
    string? Sku,
    string UnitSymbol,
    decimal Quantity,
    Money UnitPrice,
    Money CatalogPrice,
    bool PriceOverridden,
    Money Discount,
    Money LineTotal,
    StockBalance Available);

/// <param name="CustomerName">Null for «مشتری نقدی».</param>
/// <param name="Totals">Fixed footer of a completed invoice; null while it is a draft.</param>
public sealed record SaleDetails(
    SaleId SaleId,
    SaleStatus Status,
    SaleNumber? Number,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    CustomerId? CustomerId,
    string? CustomerName,
    IReadOnlyList<SaleLineDetails> Lines,
    Money Subtotal,
    Money Discount,
    Money ServiceCharge,
    PaymentMethod? PaymentMethod,
    SaleTotals? Totals);

public interface IGetSaleDetailsHandler
{
    Task<Result<SaleDetails>> ExecuteAsync(GetSaleDetailsQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Everything needed to draw one invoice — an open tab, a resumed held
/// invoice, or a completed one being reviewed. The amounts come from the
/// <see cref="Sale"/> aggregate itself, so this view and completion can never
/// disagree; storage only adds names, units and stock.
/// </summary>
public sealed class GetSaleDetailsHandler : IGetSaleDetailsHandler
{
    private readonly ISaleRepository _sales;
    private readonly ISaleReadReader _reader;
    private readonly ICustomerRepository _customers;

    public GetSaleDetailsHandler(ISaleRepository sales, ISaleReadReader reader, ICustomerRepository customers)
    {
        _sales = sales;
        _reader = reader;
        _customers = customers;
    }

    public async Task<Result<SaleDetails>> ExecuteAsync(GetSaleDetailsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sale = await _sales.GetAsync(query.SaleId, cancellationToken).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure<SaleDetails>("sales.sale.not-found", "فاکتور یافت نشد.");
        }

        var products = await _reader
            .ReadProductsAsync(sale.WarehouseId, sale.Lines.Select(line => line.ProductId).ToList(), cancellationToken)
            .ConfigureAwait(false);

        var customerName = sale.CustomerId is { } customerId
            ? (await _customers.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false))?.Name
            : null;

        var lines = sale.Lines
            .Select(line =>
            {
                // A product row can only be missing if it was deleted outside the
                // application; show the row rather than hide money on the invoice.
                var product = products.GetValueOrDefault(line.ProductId)
                    ?? new SaleProductInfo(line.ProductId, "کالای حذف‌شده", null, string.Empty, StockBalance.From(0));

                return new SaleLineDetails(
                    line.ProductId,
                    product.Name,
                    product.Sku,
                    product.UnitSymbol,
                    line.Quantity.Value,
                    line.UnitPrice,
                    line.CatalogPrice,
                    line.PriceOverridden,
                    line.Discount,
                    line.LineTotal,
                    product.Available);
            })
            .ToList();

        return Result.Success(new SaleDetails(
            sale.Id,
            sale.Status,
            sale.Number,
            sale.OpenedAtUtc,
            sale.CompletedAtUtc,
            sale.CustomerId,
            customerName,
            lines,
            sale.Subtotal,
            sale.Discount,
            sale.ServiceCharge,
            sale.PaymentMethod,
            sale.Totals));
    }
}
