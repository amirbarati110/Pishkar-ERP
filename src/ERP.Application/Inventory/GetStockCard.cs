using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory;

// «کاردکس کالا» (the user's request 1405/06/30 — «به شدت کاربردی»; Baran calls it «کارتکس کالا»):
// every stock movement of one product in date order, with what came in, what went out and
// the running balance after each. It answers «چرا موجودی این کالا این‌قدر است؟».
//
// Quantity and value («کاردکس تعدادی-ریالی»): every movement also carries its FIFO cost when the
// store knows it — an opening layer's cost, the cost a sale drew from the layers (recorded on the
// sale line since migration V014), the original cost a return put back. A movement whose cost was
// never recorded (a sale made before V014, a sale beyond stock) has NO value here, and the running
// stock value stops being known from there on — it is shown as unknown, never guessed.

/// <summary>The document a movement came from, when the store can name it.</summary>
public enum StockCardDocumentKind
{
    None = 0,
    Sale = 1,
    Return = 2,
}

/// <summary>One movement as read from storage — no balance yet.</summary>
/// <param name="RelatedSaleNumber">
/// The invoice a click on this row should open: the sale's own number for a sale, the
/// ORIGINAL invoice's number for a return. Null for movements with no invoice (opening stock…).
/// </param>
/// <param name="Value">The FIFO cost of the whole movement (quantity × unit cost), or null when it was never recorded.</param>
public sealed record StockCardMovement(
    DateTimeOffset OccurredAtUtc,
    StockMovementType Type,
    decimal Quantity,
    StockCardDocumentKind DocumentKind,
    long? DocumentNumber,
    long? RelatedSaleNumber = null,
    Money? Value = null);

/// <summary>The product's header and all its movements, oldest first.</summary>
public sealed record StockCardSource(
    string ProductName,
    string? Sku,
    string UnitSymbol,
    IReadOnlyList<StockCardMovement> Movements);

public sealed record StockCardEntry(
    DateTimeOffset OccurredAtUtc,
    StockMovementType Type,
    StockCardDocumentKind DocumentKind,
    long? DocumentNumber,
    decimal In,
    decimal Out,
    decimal Balance,
    long? RelatedSaleNumber = null,
    Money? Value = null,
    Money? BalanceValue = null);

/// <param name="OpeningBalance">Stock just before the first listed row — what was already there when the chosen period started.</param>
/// <param name="ClosingBalance">Stock after the last listed row (the current stock when the period runs up to now).</param>
/// <param name="OpeningValue">Value of the stock just before the first listed row; null when it cannot be known (an earlier movement has no recorded cost).</param>
/// <param name="ClosingValue">Value of the stock after the last listed row; null when unknown, same reason.</param>
/// <param name="TotalInValue">Cost of what came in during the period (only movements with a recorded cost).</param>
/// <param name="TotalOutValue">Cost of what went out during the period (only movements with a recorded cost).</param>
/// <param name="UnvaluedMovements">How many listed movements have no recorded cost — the screen says so instead of hiding it.</param>
public sealed record StockCardPage(
    string ProductName,
    string? Sku,
    string UnitSymbol,
    decimal OpeningBalance,
    decimal TotalIn,
    decimal TotalOut,
    decimal ClosingBalance,
    IReadOnlyList<StockCardEntry> Entries,
    Money? OpeningValue = null,
    Money? ClosingValue = null,
    Money TotalInValue = default,
    Money TotalOutValue = default,
    int UnvaluedMovements = 0);

/// <param name="FromUtc">Inclusive start of the period; null = from the beginning.</param>
/// <param name="ToUtc">Exclusive end of the period; null = until now.</param>
public sealed record StockCardQuery(ProductId ProductId, DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null);

public interface IStockCardReader
{
    /// <summary>Null when the product does not exist. Archived products are readable — their history stays.</summary>
    Task<StockCardSource?> ReadAsync(ProductId productId, CancellationToken cancellationToken);
}

public interface IGetStockCardHandler
{
    /// <summary>Null when the product does not exist.</summary>
    Task<StockCardPage?> ExecuteAsync(StockCardQuery query, CancellationToken cancellationToken);
}

public sealed class GetStockCardHandler : IGetStockCardHandler
{
    private readonly IStockCardReader _reader;

    public GetStockCardHandler(IStockCardReader reader)
    {
        _reader = reader;
    }

    public async Task<StockCardPage?> ExecuteAsync(StockCardQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = await _reader.ReadAsync(query.ProductId, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return null;
        }

        // The balance is a running sum over the WHOLE history, so a period that starts in
        // the middle still shows the true stock — the rows before it only feed the
        // opening balance, they are not listed.
        var balance = 0m;
        var openingBalance = 0m;
        var value = 0L;
        var valueKnown = true;                 // turns false at the first movement with no recorded cost
        Money? openingValue = Money.Zero;
        var entries = new List<StockCardEntry>();

        foreach (var movement in source.Movements.OrderBy(item => item.OccurredAtUtc))
        {
            var isIn = IsIncrease(movement.Type);
            balance += isIn ? movement.Quantity : -movement.Quantity;
            if (movement.Value is { } cost)
            {
                value += isIn ? cost.Rials : -cost.Rials;
            }
            else
            {
                valueKnown = false;
            }

            var balanceValue = valueKnown ? Money.FromRials(value) : (Money?)null;

            if (query.FromUtc is { } from && movement.OccurredAtUtc < from)
            {
                openingBalance = balance;
                openingValue = balanceValue;
                continue;
            }

            if (query.ToUtc is { } to && movement.OccurredAtUtc >= to)
            {
                continue;
            }

            entries.Add(new StockCardEntry(
                movement.OccurredAtUtc,
                movement.Type,
                movement.DocumentKind,
                movement.DocumentNumber,
                isIn ? movement.Quantity : 0,
                isIn ? 0 : movement.Quantity,
                balance,
                movement.RelatedSaleNumber,
                movement.Value,
                balanceValue));
        }

        var closing = entries.Count > 0 ? entries[^1].Balance : openingBalance;
        var closingValue = entries.Count > 0 ? entries[^1].BalanceValue : openingValue;
        return new StockCardPage(
            source.ProductName,
            source.Sku,
            source.UnitSymbol,
            openingBalance,
            entries.Sum(entry => entry.In),
            entries.Sum(entry => entry.Out),
            closing,
            entries,
            openingValue,
            closingValue,
            Money.FromRials(entries.Where(entry => entry.In > 0 && entry.Value is not null).Sum(entry => entry.Value!.Value.Rials)),
            Money.FromRials(entries.Where(entry => entry.Out > 0 && entry.Value is not null).Sum(entry => entry.Value!.Value.Rials)),
            entries.Count(entry => entry.Value is null));
    }

    /// <summary>Opening balance, receipts, returns to stock and upward corrections add stock; sales (also beyond stock) and downward corrections take it away.</summary>
    public static bool IsIncrease(StockMovementType type) => type is
        StockMovementType.OpeningBalance or
        StockMovementType.Receipt or
        StockMovementType.Return or
        StockMovementType.AdjustmentIncrease;
}

/// <summary>
/// The kardex for whoever is signed in: quantities for everyone, cost columns (فی · مبلغ · مبلغ
/// مانده) only with <see cref="AccessRight.ViewCostAndProfit"/> (§15.2 «View Cost») — stripped here,
/// on the server side, not merely hidden on screen.
/// </summary>
public sealed class CostAwareStockCardHandler : IGetStockCardHandler
{
    private readonly IGetStockCardHandler _inner;
    private readonly ERP.Application.Identity.IAccessChecker _access;

    public CostAwareStockCardHandler(IGetStockCardHandler inner, ERP.Application.Identity.IAccessChecker access)
    {
        _inner = inner;
        _access = access;
    }

    public async Task<StockCardPage?> ExecuteAsync(StockCardQuery query, CancellationToken cancellationToken)
    {
        var page = await _inner.ExecuteAsync(query, cancellationToken).ConfigureAwait(false);
        if (page is null
            || await _access.CheckAsync(AccessRight.ViewCostAndProfit, cancellationToken).ConfigureAwait(false) is null)
        {
            return page;
        }

        return page with
        {
            Entries = page.Entries.Select(entry => entry with { Value = null, BalanceValue = null }).ToList(),
            OpeningValue = null,
            ClosingValue = null,
            TotalInValue = Money.Zero,
            TotalOutValue = Money.Zero,
            UnvaluedMovements = 0,
        };
    }
}
