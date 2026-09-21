using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Presentation.Features.Inventory;

namespace ERP.Desktop.Tests.Features.Inventory;

public sealed class StockCardViewModelTests
{
    // 2026-09-20 08:30 UTC = یکشنبه ۲۹ شهریور ۱۴۰۵، ۱۲:۰۰ به وقت تهران
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 8, 30, 0, TimeSpan.Zero);

    private static ProductListRow Product() =>
        new(ProductId.New(), "rice-1", "برنج", CategoryId.New(), "مواد غذایی", UnitId.New(), "عدد", Money.FromTomans(245_000), 8, null);

    private static StockCardEntry Entry(StockMovementType type, decimal inQty, decimal outQty, decimal balance, StockCardDocumentKind kind = StockCardDocumentKind.None, long? number = null) =>
        new(Now, type, kind, number, inQty, outQty, balance);

    private static (StockCardViewModel Vm, FakeHandler Handler) Create(StockCardPage? page)
    {
        var handler = new FakeHandler(page);
        return (new StockCardViewModel(handler, new FixedClock(Now)), handler);
    }

    [Fact]
    public async Task LoadShowsRowsWithPersianTypesDocumentsAndTotals()
    {
        var (vm, _) = Create(new StockCardPage(
            "برنج", "rice-1", "عدد", 0, 11, 3, 8,
            [
                Entry(StockMovementType.OpeningBalance, 10, 0, 10),
                Entry(StockMovementType.Sale, 0, 3, 7, StockCardDocumentKind.Sale, 1258),
                Entry(StockMovementType.Return, 1, 0, 8, StockCardDocumentKind.Return, 3),
            ]));

        await vm.LoadAsync(Product(), CancellationToken.None);

        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal("موجودی اول دوره", vm.Rows[0].TypeText);
        Assert.Equal("فاکتور ۱۲۵۸", vm.Rows[1].DocumentText);
        Assert.Equal("۳", vm.Rows[1].OutText);
        Assert.Equal(string.Empty, vm.Rows[1].InText);
        Assert.Equal("مرجوعی ۳", vm.Rows[2].DocumentText);
        Assert.Equal("۸", vm.Rows[2].BalanceText);
        Assert.Equal("۱۴۰۵/۰۶/۲۹  ۱۲:۰۰", vm.Rows[0].DateText);
        Assert.Equal("۱۱", vm.TotalInText);
        Assert.Equal("۳", vm.TotalOutText);
        Assert.Equal("۸", vm.ClosingText);
        Assert.Equal("کد rice-۱ · واحد عدد", vm.CodeLine);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task OnlyRowsWithAnInvoiceCanOpenOne()
    {
        var (vm, _) = Create(new StockCardPage("برنج", null, "عدد", 0, 10, 3, 7,
            [
                Entry(StockMovementType.OpeningBalance, 10, 0, 10),
                new StockCardEntry(Now, StockMovementType.Sale, StockCardDocumentKind.Sale, 1258, 0, 3, 7, RelatedSaleNumber: 1258),
                new StockCardEntry(Now, StockMovementType.Return, StockCardDocumentKind.Return, 2, 1, 0, 8, RelatedSaleNumber: 1258),
            ]));

        await vm.LoadAsync(Product(), CancellationToken.None);

        Assert.False(vm.Rows[0].CanOpenInvoice);
        Assert.True(vm.Rows[1].CanOpenInvoice);
        Assert.Equal(1258, vm.Rows[2].RelatedSaleNumber); // a return opens the ORIGINAL invoice
        Assert.Contains("فاکتور", vm.Rows[1].AccessibleName);
    }

    [Fact]
    public async Task RowsShowTheFifoCostInTomansAndAnUnrecordedCostIsADashNeverZero()
    {
        var (vm, _) = Create(new StockCardPage("برنج", null, "عدد", 0, 10, 4, 6,
            [
                new StockCardEntry(Now, StockMovementType.OpeningBalance, StockCardDocumentKind.None, null, 10, 0, 10, null, Money.FromRials(20_000_000), Money.FromRials(20_000_000)),
                new StockCardEntry(Now, StockMovementType.Sale, StockCardDocumentKind.Sale, 3, 0, 3, 7, 3, Money.FromRials(6_000_000), Money.FromRials(14_000_000)),
                new StockCardEntry(Now, StockMovementType.Sale, StockCardDocumentKind.Sale, 4, 0, 1, 6, 4, null, null),
            ],
            OpeningValue: Money.Zero, ClosingValue: null, TotalInValue: Money.FromRials(20_000_000), TotalOutValue: Money.FromRials(6_000_000), UnvaluedMovements: 1));

        await vm.LoadAsync(Product(), CancellationToken.None);

        Assert.Equal("۲۰۰,۰۰۰", vm.Rows[0].UnitCostText);      // ۲۰٬۰۰۰٬۰۰۰ ریال برای ۱۰ عدد = ۲۰۰٬۰۰۰ تومان هر عدد
        Assert.Equal("۲,۰۰۰,۰۰۰", vm.Rows[0].ValueText);
        Assert.Equal("۲,۰۰۰,۰۰۰", vm.Rows[0].BalanceValueText);
        Assert.Equal("۶۰۰,۰۰۰", vm.Rows[1].ValueText);
        Assert.Equal("۲۰۰,۰۰۰", vm.Rows[1].UnitCostText);
        Assert.Equal("۱,۴۰۰,۰۰۰", vm.Rows[1].BalanceValueText);
        Assert.Equal(StockCardText.Unknown, vm.Rows[2].ValueText);
        Assert.Equal(StockCardText.Unknown, vm.Rows[2].UnitCostText);
        Assert.Equal(StockCardText.Unknown, vm.Rows[2].BalanceValueText);
        Assert.Equal(StockCardText.Unknown, vm.ClosingValueText);
        Assert.Contains("۱ حرکت", vm.UnvaluedNoticeText);
        Assert.Contains("مبلغ مانده", vm.UnvaluedNoticeText);
    }

    [Fact]
    public async Task WhenEveryCostIsKnownThereIsNoNotice()
    {
        var (vm, _) = Create(new StockCardPage("برنج", null, "عدد", 0, 10, 0, 10,
            [new StockCardEntry(Now, StockMovementType.OpeningBalance, StockCardDocumentKind.None, null, 10, 0, 10, null, Money.FromRials(20_000_000), Money.FromRials(20_000_000))],
            OpeningValue: Money.Zero, ClosingValue: Money.FromRials(20_000_000), TotalInValue: Money.FromRials(20_000_000)));

        await vm.LoadAsync(Product(), CancellationToken.None);

        Assert.Equal(string.Empty, vm.UnvaluedNoticeText);
        Assert.Equal("۲,۰۰۰,۰۰۰", vm.ClosingValueText);
    }

    [Fact]
    public async Task ANegativeBalanceIsFlaggedSoTheScreenCanWarn()
    {
        var (vm, _) = Create(new StockCardPage("برنج", null, "عدد", 0, 2, 5, -3,
            [Entry(StockMovementType.BackorderSale, 0, 5, -3)]));

        await vm.LoadAsync(Product(), CancellationToken.None);

        Assert.True(vm.Rows[0].IsNegativeBalance);
        Assert.Equal("فروش بیش از موجودی", vm.Rows[0].TypeText);
    }

    [Theory]
    [InlineData(StockCardPeriod.All, null)]
    [InlineData(StockCardPeriod.Today, "2026-09-19T20:30:00.0000000Z")]        // نیمه‌شب تهران (UTC+3:30)
    [InlineData(StockCardPeriod.LastSevenDays, "2026-09-13T20:30:00.0000000Z")]
    [InlineData(StockCardPeriod.LastThirtyDays, "2026-08-21T20:30:00.0000000Z")]
    public async Task ThePeriodChipsAskFromTheRightMidnight(StockCardPeriod period, string? expectedFromUtc)
    {
        var (vm, handler) = Create(new StockCardPage("برنج", null, "عدد", 0, 0, 0, 0, []));
        await vm.LoadAsync(Product(), CancellationToken.None);

        await vm.SelectPeriodCommand.ExecuteAsync(period);

        var expected = expectedFromUtc is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(expectedFromUtc, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, handler.LastQuery!.FromUtc);
        Assert.Equal(period, vm.SelectedPeriod);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task AMissingProductShowsAPlainMessage()
    {
        var (vm, _) = Create(null);

        await vm.LoadAsync(Product(), CancellationToken.None);

        Assert.Contains("پیدا نشد", vm.ErrorMessage);
    }

    private sealed class FakeHandler(StockCardPage? page) : IGetStockCardHandler
    {
        public StockCardQuery? LastQuery { get; private set; }

        public Task<StockCardPage?> ExecuteAsync(StockCardQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(page);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
