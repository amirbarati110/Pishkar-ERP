using ERP.Domain.Common;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

public sealed record ListSalesOfDayQuery(PersianDate Date);

/// <param name="TotalsByPaymentMethod">
/// Only methods that were actually used that day appear. This is what the
/// cashier checks the till and the card terminal against at the end of the
/// day; invoices with no stored total are left out of it and counted in
/// <see cref="InvoicesWithoutTotal"/> so the gap is visible, not hidden.
/// </param>
public sealed record SalesOfDay(
    PersianDate Date,
    IReadOnlyList<SaleListItem> Sales,
    Money Total,
    IReadOnlyDictionary<PaymentMethod, Money> TotalsByPaymentMethod,
    int InvoicesWithoutTotal);

public interface IListSalesOfDayHandler
{
    Task<SalesOfDay> ExecuteAsync(ListSalesOfDayQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// «فاکتورهای امروز» — or any other day. The day is the Tehran calendar day
/// (<see cref="PersianDate"/>): an invoice issued at 00:30 belongs to the new
/// date even though its UTC timestamp does not.
/// </summary>
public sealed class ListSalesOfDayHandler : IListSalesOfDayHandler
{
    private readonly ISaleReadReader _reader;

    public ListSalesOfDayHandler(ISaleReadReader reader)
    {
        _reader = reader;
    }

    public async Task<SalesOfDay> ExecuteAsync(ListSalesOfDayQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sales = await _reader
            .ListCompletedAsync(query.Date.StartUtc, query.Date.EndUtc, cancellationToken)
            .ConfigureAwait(false);

        var total = Money.Zero;
        var byMethod = new Dictionary<PaymentMethod, Money>();
        var withoutTotal = 0;

        foreach (var sale in sales)
        {
            if (sale.Amount is not { } amount || sale.PaymentMethod is not { } method)
            {
                withoutTotal++;
                continue;
            }

            total = total.Add(amount);
            byMethod[method] = byMethod.GetValueOrDefault(method, Money.Zero).Add(amount);
        }

        return new SalesOfDay(query.Date, sales, total, byMethod, withoutTotal);
    }
}

public sealed record ListHeldSalesQuery;

public interface IListHeldSalesHandler
{
    Task<IReadOnlyList<SaleListItem>> ExecuteAsync(ListHeldSalesQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// «فاکتورهای معلق» (source-of-truth §6.13 Hold/Resume): invoices started but
/// not yet completed or cancelled. There is no separate «held» status — a
/// draft that still has items <i>is</i> held, whether the cashier pressed
/// «تعلیق» or the program closed with the tab open, so nothing a customer was
/// promised can disappear. Empty drafts are left out: they are carts that
/// were opened and never used.
///
/// Known limit: nothing yet stops two tills from resuming the same held
/// invoice at once; that needs the till/user identity of Phase 0.
/// </summary>
public sealed class ListHeldSalesHandler : IListHeldSalesHandler
{
    private readonly ISaleReadReader _reader;

    public ListHeldSalesHandler(ISaleReadReader reader)
    {
        _reader = reader;
    }

    public Task<IReadOnlyList<SaleListItem>> ExecuteAsync(ListHeldSalesQuery query, CancellationToken cancellationToken)
    {
        return _reader.ListDraftsWithItemsAsync(cancellationToken);
    }
}
