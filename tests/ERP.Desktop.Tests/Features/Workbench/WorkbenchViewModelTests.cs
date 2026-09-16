using ERP.Application.Sales;
using ERP.Desktop.Tests.Features.Sales;
using ERP.Domain.Sales;
using ERP.Presentation.Features.Sales;
using ERP.Presentation.Features.Workbench;

namespace ERP.Desktop.Tests.Features.Workbench;

/// <summary>
/// The workbench over the same in-memory backend the sales screen is tested on,
/// so its numbers are the ones the real handlers produce.
/// </summary>
public sealed class WorkbenchViewModelTests
{
    [Fact]
    public async Task AnEmptyDayShowsZeroAndSaysSoInBothLists()
    {
        var (_, workbench) = Create();

        await workbench.LoadAsync(CancellationToken.None);

        Assert.Equal("۰", workbench.TodayTotalText);
        Assert.Equal("۰", workbench.TodayCountText);
        Assert.Equal("۰", workbench.HeldCountText);
        Assert.True(workbench.HasNoRecentInvoices);
        Assert.True(workbench.HasNoHeldInvoices);
        Assert.Equal("چیزی معلق نمانده است", workbench.HeldSummaryText);
        Assert.False(workbench.HasLoadError);
    }

    [Fact]
    public async Task AnInvoiceLeftWithItemsIsCountedAsHeld()
    {
        var (backend, workbench) = Create();
        var sales = new SalesWorkspaceViewModel(backend.Build(), backend.Warehouse);
        await sales.LoadAsync(CancellationToken.None);
        await sales.AddProductCommand.ExecuteAsync(sales.Products[0]);

        await workbench.LoadAsync(CancellationToken.None);

        Assert.Equal("۱", workbench.HeldCountText);
        Assert.False(workbench.HasNoHeldInvoices);
        Assert.Equal("۱ فاکتور در انتظار ادامه", workbench.HeldSummaryText);
        var row = Assert.Single(workbench.HeldInvoices);
        Assert.Equal("مشتری نقدی", row.Customer);
        Assert.Equal("۱ قلم", row.ItemCountText);
    }

    [Fact]
    public async Task ACompletedInvoiceShowsInTodayWithItsNumberAndPaymentMethod()
    {
        var (backend, workbench) = Create();
        var sales = new SalesWorkspaceViewModel(backend.Build(), backend.Warehouse);
        await sales.LoadAsync(CancellationToken.None);
        await sales.AddProductCommand.ExecuteAsync(sales.Products[0]);
        sales.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);
        sales.SelectPaymentMethodCommand.Execute(PaymentMethod.Cash);
        await sales.ConfirmPaymentCommand.ExecuteAsync(null);

        await workbench.LoadAsync(CancellationToken.None);

        Assert.Equal("۱", workbench.TodayCountText);
        Assert.False(workbench.HasNoRecentInvoices);
        Assert.True(workbench.HasPaymentBreakdown);
        Assert.Contains("نقدی", workbench.PaymentBreakdownText, StringComparison.Ordinal);
        var row = Assert.Single(workbench.RecentInvoices);
        Assert.Equal("فاکتور ۱۲۵۸", row.Title);
        Assert.Contains("مشتری نقدی", row.CustomerAndCount, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailureIsReportedInPlainPersianAndLogged()
    {
        var (backend, _) = Create();
        Exception? logged = null;
        var workbench = new WorkbenchViewModel(new ThrowingDay(), backend.Build().HeldSales, backend)
        {
            ReportUnexpectedError = exception => logged = exception,
        };

        await workbench.LoadAsync(CancellationToken.None);

        Assert.True(workbench.HasLoadError);
        Assert.DoesNotContain("Exception", workbench.LoadError!, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(logged);
        Assert.False(workbench.IsLoading);
    }

    private sealed class ThrowingDay : IListSalesOfDayHandler
    {
        public Task<SalesOfDay> ExecuteAsync(ListSalesOfDayQuery query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("database is away");
    }

    private static (InMemorySalesBackend Backend, WorkbenchViewModel Workbench) Create()
    {
        var backend = new InMemorySalesBackend();
        backend.AddProduct("برنج ایرانی", "RICE-1", "6260000009001", 245_000, stock: 10);
        var built = backend.Build();
        return (backend, new WorkbenchViewModel(built.SalesOfDay, built.HeldSales, backend));
    }
}
