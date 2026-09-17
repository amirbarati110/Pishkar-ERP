using ERP.Domain.Accounting;
using ERP.Domain.Common;

namespace ERP.Domain.Tests.Accounting;

public sealed class JournalEntryTests
{
    private static readonly DateTimeOffset PostedAt = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateAcceptsABalancedEntry()
    {
        var entry = JournalEntry.Create(
            JournalSourceType.Sale,
            "sale-1",
            PostedAt,
            [
                JournalLine.Debited(AccountCode.Cash, Money.FromTomans(245_000)),
                JournalLine.Credited(AccountCode.SalesRevenue, Money.FromTomans(245_000)),
            ]);

        Assert.Equal(JournalSourceType.Sale, entry.SourceType);
        Assert.Equal("sale-1", entry.SourceId);
        Assert.Equal(2, entry.Lines.Count);
    }

    [Fact]
    public void CreateRejectsAnUnbalancedEntry()
    {
        var exception = Assert.Throws<DomainException>(() => JournalEntry.Create(
            JournalSourceType.Sale,
            "sale-1",
            PostedAt,
            [
                JournalLine.Debited(AccountCode.Cash, Money.FromTomans(245_000)),
                JournalLine.Credited(AccountCode.SalesRevenue, Money.FromTomans(200_000)),
            ]));

        Assert.Equal("سند حسابداری نامتوازن است؛ جمع بدهکار باید برابر جمع بستانکار باشد.", exception.Message);
    }

    [Fact]
    public void CreateRejectsFewerThanTwoLines()
    {
        var exception = Assert.Throws<DomainException>(() => JournalEntry.Create(
            JournalSourceType.Sale,
            "sale-1",
            PostedAt,
            [JournalLine.Debited(AccountCode.Cash, Money.FromTomans(245_000))]));

        Assert.Equal("سند حسابداری باید حداقل دو سطر داشته باشد.", exception.Message);
    }

    [Fact]
    public void CreateRejectsABlankSourceId()
    {
        var exception = Assert.Throws<DomainException>(() => JournalEntry.Create(
            JournalSourceType.Sale,
            " ",
            PostedAt,
            [
                JournalLine.Debited(AccountCode.Cash, Money.FromTomans(245_000)),
                JournalLine.Credited(AccountCode.SalesRevenue, Money.FromTomans(245_000)),
            ]));

        Assert.Equal("سند حسابداری باید به یک رکورد منبع وصل باشد.", exception.Message);
    }

    [Fact]
    public void ThreeLinesCanStillBalanceWhenTaxIsSplitOut()
    {
        var entry = JournalEntry.Create(
            JournalSourceType.Sale,
            "sale-2",
            PostedAt,
            [
                JournalLine.Debited(AccountCode.Cash, Money.FromTomans(269_500)),
                JournalLine.Credited(AccountCode.SalesRevenue, Money.FromTomans(245_000)),
                JournalLine.Credited(AccountCode.VatPayable, Money.FromTomans(24_500)),
            ]);

        Assert.Equal(3, entry.Lines.Count);
    }

    [Fact]
    public void DebitedRejectsAZeroAmount()
    {
        var exception = Assert.Throws<DomainException>(() => JournalLine.Debited(AccountCode.Cash, Money.Zero));

        Assert.Equal("سطر سند حسابداری نمی‌تواند مبلغ صفر داشته باشد.", exception.Message);
    }
}
