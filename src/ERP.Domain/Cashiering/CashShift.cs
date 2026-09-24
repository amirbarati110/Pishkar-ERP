using ERP.Domain.Common;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;

namespace ERP.Domain.Cashiering;

/// <summary>
/// «شیفت صندوق پایه» (milestone-1 §6/§8, build order step 8). One cashier
/// opens the till with a counted starting amount, sells through the shift,
/// then closes it by counting the drawer again — the difference from what
/// cash sales say should be there is the شیفت's own honesty check, not
/// something the cashier can silently skip.
/// </summary>
public sealed class CashShift : Entity<CashShiftId>
{
    private CashShift(
        CashShiftId id,
        WarehouseId warehouseId,
        UserId openedByUserId,
        DateTimeOffset openedAtUtc,
        Money openingCash)
        : base(id)
    {
        WarehouseId = warehouseId;
        OpenedByUserId = openedByUserId;
        OpenedAtUtc = openedAtUtc;
        OpeningCash = openingCash;
        Status = CashShiftStatus.Open;
    }

    public WarehouseId WarehouseId { get; }

    public UserId OpenedByUserId { get; }

    public DateTimeOffset OpenedAtUtc { get; }

    public Money OpeningCash { get; }

    public CashShiftStatus Status { get; private set; }

    public UserId? ClosedByUserId { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    /// <summary>What the cashier actually counted in the drawer at close.</summary>
    public Money? CountedCash { get; private set; }

    /// <summary>Total of cash-method sales completed between open and close — read from Sales by the Application layer, not this module (module boundary rule §5).</summary>
    public Money? CashSalesDuringShift { get; private set; }

    /// <summary>
    /// Cash handed back to customers between open and close — for returns, and for a
    /// «صورتحساب اصلاحی» that lowered what a cash invoice charged. It left the drawer just as
    /// sales put cash in.
    /// </summary>
    public Money? CashRefundsDuringShift { get; private set; }

    /// <summary>
    /// «دریافت از مشتری» paid in cash at this till between open and close — not a sale, but cash
    /// that went into the drawer all the same. Null on shifts closed before it was recorded.
    /// </summary>
    public Money? CashReceiptsDuringShift { get; private set; }

    public string? Note { get; private set; }

    /// <summary>OpeningCash + CashSalesDuringShift + CashReceiptsDuringShift − CashRefundsDuringShift — what the drawer should hold. Only meaningful once closed. Never below zero: a drawer cannot hold less than nothing, and refunds that outrun it show up as the variance.</summary>
    public Money? ExpectedCash => CashSalesDuringShift is { } sales
        ? Expected(OpeningCash, sales, CashReceiptsDuringShift ?? Money.Zero, CashRefundsDuringShift ?? Money.Zero)
        : null;

    /// <summary>The one formula for what a drawer should hold — for a closed shift and for the live figure of an open one.</summary>
    public static Money Expected(Money openingCash, Money cashSales, Money cashReceipts, Money cashRefunds) =>
        Money.FromRials(Math.Max(0, openingCash.Rials + cashSales.Rials + cashReceipts.Rials - cashRefunds.Rials));

    /// <summary>
    /// Rials, signed — positive means extra cash found, negative means short.
    /// A plain <see cref="long"/> because <see cref="Money"/> can never hold a
    /// negative amount (every other use of it is a price or a total, which
    /// really can't be negative); a variance is a delta, not an amount. Null
    /// until closed.
    /// </summary>
    public long? Variance => CountedCash is { } counted && ExpectedCash is { } expected
        ? counted.Rials - expected.Rials
        : null;

    public static CashShift Open(
        WarehouseId warehouseId,
        UserId openedByUserId,
        Money openingCash,
        DateTimeOffset openedAtUtc)
    {
        return new CashShift(CashShiftId.New(), warehouseId, openedByUserId, openedAtUtc, openingCash);
    }

    internal static CashShift Rehydrate(
        CashShiftId id,
        WarehouseId warehouseId,
        UserId openedByUserId,
        DateTimeOffset openedAtUtc,
        Money openingCash,
        CashShiftStatus status,
        UserId? closedByUserId,
        DateTimeOffset? closedAtUtc,
        Money? countedCash,
        Money? cashSalesDuringShift,
        Money? cashRefundsDuringShift,
        Money? cashReceiptsDuringShift,
        string? note)
    {
        return new CashShift(id, warehouseId, openedByUserId, openedAtUtc, openingCash)
        {
            Status = status,
            ClosedByUserId = closedByUserId,
            ClosedAtUtc = closedAtUtc,
            CountedCash = countedCash,
            CashSalesDuringShift = cashSalesDuringShift,
            CashRefundsDuringShift = cashRefundsDuringShift,
            CashReceiptsDuringShift = cashReceiptsDuringShift,
            Note = note,
        };
    }

    /// <param name="cashSalesDuringShift">Summed by the Application layer from Sales, covering exactly [OpenedAtUtc, closedAtUtc).</param>
    /// <param name="cashRefundsDuringShift">Cash handed back (returns, corrections that lowered a cash invoice) in the same window, summed the same way.</param>
    /// <param name="cashReceiptsDuringShift">Cash «دریافت از مشتری» at this till in the same window.</param>
    public void Close(
        Money countedCash,
        Money cashSalesDuringShift,
        Money cashRefundsDuringShift,
        Money cashReceiptsDuringShift,
        UserId closedByUserId,
        DateTimeOffset closedAtUtc,
        string? note)
    {
        if (Status == CashShiftStatus.Closed)
        {
            throw new DomainException("این شیفت قبلاً بسته شده است.");
        }

        if (closedAtUtc < OpenedAtUtc)
        {
            throw new DomainException("زمان بستن شیفت نمی‌تواند قبل از زمان بازکردن آن باشد.");
        }

        CountedCash = countedCash;
        CashSalesDuringShift = cashSalesDuringShift;
        CashRefundsDuringShift = cashRefundsDuringShift;
        CashReceiptsDuringShift = cashReceiptsDuringShift;
        ClosedByUserId = closedByUserId;
        ClosedAtUtc = closedAtUtc;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = CashShiftStatus.Closed;
    }
}
