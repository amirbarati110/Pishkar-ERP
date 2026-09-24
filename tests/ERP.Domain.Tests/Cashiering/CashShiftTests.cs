using ERP.Domain.Cashiering;
using ERP.Domain.Common;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;

namespace ERP.Domain.Tests.Cashiering;

public sealed class CashShiftTests
{
    private static readonly WarehouseId Warehouse = WarehouseId.New();
    private static readonly UserId Cashier = UserId.New();
    private static readonly DateTimeOffset OpenedAt = new(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OpenStartsAsOpenWithTheGivenStartingCash()
    {
        var shift = CashShift.Open(Warehouse, Cashier, Money.FromTomans(200_000), OpenedAt);

        Assert.Equal(CashShiftStatus.Open, shift.Status);
        Assert.Equal(2_000_000, shift.OpeningCash.Rials);
        Assert.Equal(Cashier, shift.OpenedByUserId);
        Assert.Null(shift.ClosedAtUtc);
        Assert.Null(shift.ExpectedCash);
        Assert.Null(shift.Variance);
    }

    [Fact]
    public void ClosingWithExactlyTheExpectedAmountHasZeroVariance()
    {
        var shift = CashShift.Open(Warehouse, Cashier, Money.FromTomans(200_000), OpenedAt);
        var closedAt = OpenedAt.AddHours(8);

        shift.Close(
            countedCash: Money.FromTomans(650_000),
            cashSalesDuringShift: Money.FromTomans(450_000),
            cashRefundsDuringShift: Money.Zero,
            cashReceiptsDuringShift: Money.Zero,
            closedByUserId: Cashier,
            closedAtUtc: closedAt,
            note: null);

        Assert.Equal(CashShiftStatus.Closed, shift.Status);
        Assert.Equal(6_500_000, shift.ExpectedCash!.Value.Rials);
        Assert.Equal(0, shift.Variance);
        Assert.Equal(closedAt, shift.ClosedAtUtc);
    }

    [Fact]
    public void CashRefundedForReturnsLowersTheExpectedDrawer()
    {
        var shift = CashShift.Open(Warehouse, Cashier, Money.FromTomans(200_000), OpenedAt);

        shift.Close(
            Money.FromTomans(600_000),
            Money.FromTomans(450_000),
            Money.FromTomans(50_000),
            Money.Zero,
            Cashier,
            OpenedAt.AddHours(8),
            null);

        Assert.Equal(Money.FromTomans(600_000).Rials, shift.ExpectedCash!.Value.Rials); // ۲۰۰ + ۴۵۰ − ۵۰
        Assert.Equal(0, shift.Variance);
        Assert.Equal(Money.FromTomans(50_000).Rials, shift.CashRefundsDuringShift!.Value.Rials);
    }

    [Fact]
    public void RefundsBiggerThanEverythingInTheDrawerNeverProduceANegativeExpectedAmount()
    {
        var shift = CashShift.Open(Warehouse, Cashier, Money.Zero, OpenedAt);

        shift.Close(
            Money.Zero,
            Money.FromTomans(10_000),
            Money.FromTomans(30_000),
            Money.Zero,
            Cashier,
            OpenedAt.AddHours(8),
            null);

        Assert.Equal(0, shift.ExpectedCash!.Value.Rials);
    }

    [Fact]
    public void ClosingShortOfTheExpectedAmountRecordsANegativeVariance()
    {
        var shift = CashShift.Open(Warehouse, Cashier, Money.FromTomans(200_000), OpenedAt);

        shift.Close(
            Money.FromTomans(600_000),
            Money.FromTomans(450_000),
            Money.Zero,
            Money.Zero,
            Cashier,
            OpenedAt.AddHours(8),
            note: "۵۰ هزار تومان کم بود");

        Assert.Equal(-50_000 * 10, shift.Variance);
        Assert.Equal("۵۰ هزار تومان کم بود", shift.Note);
    }

    [Fact]
    public void ClosingMoreThanExpectedRecordsAPositiveVariance()
    {
        var shift = CashShift.Open(Warehouse, Cashier, Money.FromTomans(200_000), OpenedAt);

        shift.Close(Money.FromTomans(700_000), Money.FromTomans(450_000), Money.Zero, Money.Zero, Cashier, OpenedAt.AddHours(8), null);

        Assert.Equal(50_000 * 10, shift.Variance);
    }

    [Fact]
    public void AClosedShiftCannotBeClosedAgain()
    {
        var shift = CashShift.Open(Warehouse, Cashier, Money.FromTomans(200_000), OpenedAt);
        shift.Close(Money.FromTomans(600_000), Money.FromTomans(400_000), Money.Zero, Money.Zero, Cashier, OpenedAt.AddHours(8), null);

        var exception = Assert.Throws<DomainException>(() =>
            shift.Close(Money.FromTomans(600_000), Money.FromTomans(400_000), Money.Zero, Money.Zero, Cashier, OpenedAt.AddHours(9), null));

        Assert.Equal("این شیفت قبلاً بسته شده است.", exception.Message);
    }

    [Fact]
    public void ClosingRejectsATimeBeforeTheShiftOpened()
    {
        var shift = CashShift.Open(Warehouse, Cashier, Money.FromTomans(200_000), OpenedAt);

        var exception = Assert.Throws<DomainException>(() =>
            shift.Close(Money.FromTomans(600_000), Money.FromTomans(400_000), Money.Zero, Money.Zero, Cashier, OpenedAt.AddHours(-1), null));

        Assert.Equal("زمان بستن شیفت نمی‌تواند قبل از زمان بازکردن آن باشد.", exception.Message);
    }

    [Fact]
    public void BlankClosingNoteIsStoredAsNull()
    {
        var shift = CashShift.Open(Warehouse, Cashier, Money.FromTomans(200_000), OpenedAt);

        shift.Close(Money.FromTomans(600_000), Money.FromTomans(400_000), Money.Zero, Money.Zero, Cashier, OpenedAt.AddHours(8), "   ");

        Assert.Null(shift.Note);
    }

    [Fact]
    public void CashReceivedFromCustomersRaisesTheExpectedDrawer()
    {
        // «دریافت از مشتری» in cash goes into the drawer like a sale does (audit 1405/07/02)
        var shift = CashShift.Open(Warehouse, Cashier, Money.FromTomans(200_000), OpenedAt);

        shift.Close(
            Money.FromTomans(950_000),
            Money.FromTomans(450_000),
            Money.FromTomans(50_000),
            Money.FromTomans(350_000),
            Cashier,
            OpenedAt.AddHours(8),
            null);

        Assert.Equal(Money.FromTomans(950_000).Rials, shift.ExpectedCash!.Value.Rials); // ۲۰۰ + ۴۵۰ + ۳۵۰ − ۵۰
        Assert.Equal(0, shift.Variance);
        Assert.Equal(Money.FromTomans(350_000).Rials, shift.CashReceiptsDuringShift!.Value.Rials);
    }
}
