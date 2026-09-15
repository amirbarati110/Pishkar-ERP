using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Sales;
using ERP.Presentation.Features.Sales;

namespace ERP.Desktop.Tests.Features.Sales;

public sealed class SalesWorkspaceViewModelTests
{
    [Fact]
    public async Task OpeningWithNothingHeldStartsOneEmptyCashInvoice()
    {
        var (backend, viewModel) = Create();

        await viewModel.LoadAsync(CancellationToken.None);

        var tab = Assert.Single(viewModel.Tabs);
        Assert.Same(tab, viewModel.CurrentTab);
        Assert.True(tab.IsEmpty);
        Assert.Equal("مشتری نقدی", tab.Title);
        Assert.Equal(["همه", "مواد غذایی"], viewModel.Categories.Select(chip => chip.Name));
        Assert.Equal(2, viewModel.Products.Count);
        Assert.Single(backend.Sales);
    }

    [Fact]
    public async Task HeldInvoicesComeBackAsTabsWhenTheScreenOpens()
    {
        var (backend, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products[0]);

        var reopened = new SalesWorkspaceViewModel(backend.Build(), backend.Warehouse);
        await reopened.LoadAsync(CancellationToken.None);

        var tab = Assert.Single(reopened.Tabs);
        Assert.Single(tab.Lines);
    }

    [Fact]
    public async Task AddingAProductShowsItsRowAndTheFooterTheServerComputed()
    {
        var (_, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single(product => product.Name == "روغن حیوانی"));

        var tab = viewModel.CurrentTab!;
        var row = Assert.Single(tab.Lines);
        Assert.Equal("روغن حیوانی", row.Name);
        Assert.Equal("کد OIL-۱", row.CodeText);
        Assert.Equal("۸۵۰,۰۰۰", row.LineTotalText);
        Assert.Equal("۸۵۰,۰۰۰", tab.SubtotalText);
        Assert.Equal("۷۶,۵۰۰", tab.TaxText);     // ۹٪
        Assert.Equal("۹۲۶,۵۰۰", tab.TotalText);
        Assert.Equal("مشتری نقدی · ۱", tab.Title);
    }

    [Fact]
    public async Task ScanningAnExactBarcodeAddsTheProductAndClearsTheBox()
    {
        var (_, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.ProductSearchText = "6260000009001";
        await viewModel.SubmitProductSearchCommand.ExecuteAsync(null);

        Assert.Equal("برنج ایرانی", Assert.Single(viewModel.CurrentTab!.Lines).Name);
        Assert.Equal(string.Empty, viewModel.ProductSearchText);
    }

    [Fact]
    public async Task AnUnknownScanSaysSoAndAddsNothing()
    {
        var (_, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.ProductSearchText = "0000";
        await viewModel.SubmitProductSearchCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.CurrentTab!.Lines);
        Assert.True(viewModel.NoticeIsError);
        Assert.Contains("پیدا نشد", viewModel.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MinusOnTheLastUnitRemovesTheRow()
    {
        var (_, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products[0]);
        await viewModel.IncreaseLineCommand.ExecuteAsync(viewModel.CurrentTab!.Lines[0]);
        Assert.Equal("۲", viewModel.CurrentTab.Lines[0].QuantityText);

        await viewModel.DecreaseLineCommand.ExecuteAsync(viewModel.CurrentTab.Lines[0]);
        await viewModel.DecreaseLineCommand.ExecuteAsync(viewModel.CurrentTab.Lines[0]);

        Assert.True(viewModel.CurrentTab.IsEmpty);
    }

    [Fact]
    public async Task ADiscountTypedAsAPercentIsSavedAsAnAmountOfTheGoods()
    {
        var (backend, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single(product => product.Name == "روغن حیوانی"));

        viewModel.CurrentTab!.DiscountPercentInput = "۱۰";
        await viewModel.CommitChargesCommand.ExecuteAsync("discount-percent");

        Assert.Equal(85_000, backend.Sales.Single().Discount.ToTomansExact());
        Assert.Equal("۸۵,۰۰۰", viewModel.CurrentTab.DiscountInput);
        Assert.Equal("۸۳۳,۸۵۰", viewModel.CurrentTab.TotalText); // (۸۵۰٬۰۰۰ − ۸۵٬۰۰۰) × ۱.۰۹
    }

    [Fact]
    public async Task CashPaymentCompletesShowsTheNumberAndThenStartsTheNextInvoice()
    {
        var (backend, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products[0]);

        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);
        Assert.True(viewModel.IsPaymentOpen);
        await viewModel.ConfirmPaymentCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsPaymentOpen);
        Assert.True(viewModel.IsCompletedSummaryOpen);
        Assert.Equal("۱۲۵۸", viewModel.CompletedNumberText);
        Assert.Equal(SaleStatus.Completed, backend.Sales[0].Status);

        await viewModel.StartNextAfterSummaryCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentTab!.IsEmpty);
        Assert.NotEqual(backend.Sales[0].Id, viewModel.CurrentTab.SaleId);
    }

    [Fact]
    public async Task SaveAndNextSkipsTheSummary()
    {
        var (_, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products[0]);

        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.NextInvoice);
        await viewModel.ConfirmPaymentCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsCompletedSummaryOpen);
        Assert.True(viewModel.CurrentTab!.IsEmpty);
        Assert.Contains("۱۲۵۸", viewModel.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CashShortOfTheTotalIsRefusedInsteadOfBecomingASilentDebt()
    {
        var (backend, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products[0]);

        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);
        viewModel.CashReceivedText = "۱۰۰";
        await viewModel.ConfirmPaymentCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsPaymentOpen);
        Assert.NotNull(viewModel.PaymentError);
        Assert.Equal(0, backend.CompleteCalls);
    }

    [Fact]
    public async Task CashAboveTheTotalShowsTheChangeToGiveBack()
    {
        var (_, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single(product => product.Name == "روغن حیوانی"));

        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);
        viewModel.CashReceivedText = "۱,۰۰۰,۰۰۰";

        Assert.Equal("۷۳,۵۰۰", viewModel.CashChangeText); // ۱٬۰۰۰٬۰۰۰ − ۹۲۶٬۵۰۰
    }

    [Fact]
    public async Task ACreditSaleWithoutACustomerExplainsWhatToDo()
    {
        var (_, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products[0]);

        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);
        viewModel.SelectPaymentMethodCommand.Execute(PaymentMethod.Credit);
        await viewModel.ConfirmPaymentCommand.ExecuteAsync(null);

        Assert.Equal("برای فروش نسیه یا چکی، اول مشتری را انتخاب کنید.", viewModel.PaymentError);
        Assert.False(viewModel.NeedsCreditApproval);
    }

    [Fact]
    public async Task OverTheCreditLimitAsksForApprovalAndCompletesOnceApproved()
    {
        var (backend, viewModel) = Create();
        var customer = Customer.QuickCreate("محمد رضایی", "09123456789");
        customer.SetCreditLimit(Money.FromTomans(800_000));
        backend.Customers.Add(customer);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single(product => product.Name == "روغن حیوانی"));
        await viewModel.ChooseCustomerCommand.ExecuteAsync(new Application.Customers.CustomerSearchResult(customer.Id, customer.Name, customer.Mobile));
        Assert.Equal("محمد رضایی · ۱", viewModel.CurrentTab!.Title);

        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);
        viewModel.SelectPaymentMethodCommand.Execute(PaymentMethod.Credit);
        await viewModel.ConfirmPaymentCommand.ExecuteAsync(null);

        Assert.True(viewModel.NeedsCreditApproval);
        Assert.Contains("تأیید مدیر", viewModel.PaymentError, StringComparison.Ordinal);

        await viewModel.ApproveCreditAndCompleteCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsCompletedSummaryOpen);
        Assert.Contains(backend.Audit, entry => entry.Action == "sales.credit.over-limit-approved");
    }

    [Fact]
    public async Task TheCustomerBannerShowsWhatTheyAlreadyOwe()
    {
        var (backend, viewModel) = Create();
        var customer = Customer.QuickCreate("محمد رضایی", "09123456789");
        backend.Customers.Add(customer);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single(product => product.Name == "روغن حیوانی"));
        await viewModel.ChooseCustomerCommand.ExecuteAsync(new Application.Customers.CustomerSearchResult(customer.Id, customer.Name, customer.Mobile));
        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.NextInvoice);
        viewModel.SelectPaymentMethodCommand.Execute(PaymentMethod.Credit);
        await viewModel.ConfirmPaymentCommand.ExecuteAsync(null);

        await viewModel.ChooseCustomerCommand.ExecuteAsync(new Application.Customers.CustomerSearchResult(customer.Id, customer.Name, customer.Mobile));

        Assert.Equal("مانده قبلی: ۹۲۶,۵۰۰ تومان بدهکار · ۱ فاکتور باز", viewModel.CurrentTab!.BalanceText);
        Assert.Equal("۰۹۱۲۳۴۵۶۷۸۹", viewModel.CurrentTab.CustomerMobileText);
    }

    [Fact]
    public async Task ShortStockOffersTheShortfallOption()
    {
        var (backend, viewModel) = Create();
        backend.AddProduct("زعفران", "SAF-1", "6260000009003", 4_200_000, stock: 0);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single(product => product.Name == "زعفران"));
        Assert.Contains("بیش از موجودی", viewModel.CurrentTab!.Lines[0].WarningText, StringComparison.Ordinal);

        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);
        await viewModel.ConfirmPaymentCommand.ExecuteAsync(null);
        Assert.Contains("اجازه فروش با کسری موجودی", viewModel.PaymentError, StringComparison.Ordinal);

        viewModel.AllowNegativeStock = true;
        await viewModel.ConfirmPaymentCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsCompletedSummaryOpen);
    }

    [Fact]
    public async Task ClosingATabWithItemsHoldsItAndTheListBringsItBack()
    {
        var (_, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products[0]);
        var held = viewModel.CurrentTab!;

        await viewModel.CloseTabCommand.ExecuteAsync(held);

        Assert.DoesNotContain(held, viewModel.Tabs);
        Assert.Single(viewModel.Tabs); // a fresh invoice took its place
        Assert.Contains("معلق", viewModel.Notice, StringComparison.Ordinal);

        await viewModel.OpenInvoiceListCommand.ExecuteAsync(null);
        var row = Assert.Single(viewModel.HeldInvoices);
        Assert.Equal("پیش‌نویس", row.NumberText);

        await viewModel.ResumeHeldInvoiceCommand.ExecuteAsync(row);

        Assert.Equal(2, viewModel.Tabs.Count);
        Assert.Equal(held.SaleId, viewModel.CurrentTab!.SaleId);
        Assert.False(viewModel.IsInvoiceListOpen);
    }

    [Fact]
    public async Task RemovingAnInvoiceAsksFirstAndThenCancelsIt()
    {
        var (backend, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products[0]);

        viewModel.AskCancelInvoiceCommand.Execute(null);
        Assert.True(viewModel.IsCancelConfirmOpen);
        Assert.Equal(SaleStatus.Draft, backend.Sales[0].Status); // nothing yet

        await viewModel.ConfirmCancelInvoiceCommand.ExecuteAsync(null);

        Assert.Equal(SaleStatus.Cancelled, backend.Sales[0].Status);
        Assert.True(viewModel.CurrentTab!.IsEmpty);
    }

    [Fact]
    public async Task TodaysListTotalsByPaymentMethod()
    {
        var (_, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        foreach (var method in new[] { PaymentMethod.Cash, PaymentMethod.Card })
        {
            await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single(product => product.Name == "روغن حیوانی"));
            viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.NextInvoice);
            viewModel.SelectPaymentMethodCommand.Execute(method);
            await viewModel.ConfirmPaymentCommand.ExecuteAsync(null);
        }

        await viewModel.OpenInvoiceListCommand.ExecuteAsync(null);

        Assert.Equal(["۱۲۵۹", "۱۲۵۸"], viewModel.TodayInvoices.Select(row => row.NumberText));
        Assert.Contains("۲ فاکتور", viewModel.TodaySummaryText, StringComparison.Ordinal);
        Assert.Contains("نقدی ۹۲۶,۵۰۰", viewModel.TodaySummaryText, StringComparison.Ordinal);
        Assert.Contains("کارتخوان ۹۲۶,۵۰۰", viewModel.TodaySummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewCustomerCarriesOverAMobileTypedInTheCustomerBoxAndRejectsADuplicate()
    {
        var (backend, viewModel) = Create();
        backend.Customers.Add(Customer.QuickCreate("محمد رضایی", "09123456789"));
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.CustomerSearchText = "۰۹۱۲ ۳۴۵ ۶۷۸۹";
        viewModel.OpenNewCustomerCommand.Execute(null);
        Assert.Equal("۰۹۱۲ ۳۴۵ ۶۷۸۹", viewModel.NewCustomerMobile);
        Assert.Equal(string.Empty, viewModel.NewCustomerName);

        viewModel.NewCustomerName = "رضایی";
        await viewModel.SaveNewCustomerCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsNewCustomerOpen);
        Assert.Contains("محمد رضایی", viewModel.NewCustomerError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingARowSyncsPercentAndAmountAndSavesThePriceForThisInvoiceOnly()
    {
        var (backend, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single(product => product.Name == "روغن حیوانی"));

        viewModel.OpenEditLineCommand.Execute(viewModel.CurrentTab!.Lines[0]);
        viewModel.EditUnitPriceText = "۸۰۰,۰۰۰";
        viewModel.EditDiscountPercentText = "۵";
        Assert.Equal("۴۰,۰۰۰", viewModel.EditDiscountText);

        await viewModel.SaveEditLineCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEditLineOpen);
        var row = viewModel.CurrentTab.Lines[0];
        Assert.Equal("۷۶۰,۰۰۰", row.LineTotalText);
        Assert.Contains("قیمت این فاکتور تغییر کرده", row.WarningText, StringComparison.Ordinal);
        Assert.Equal(850_000, backend.Products.Single(product => product.Name == "روغن حیوانی").SalePrice.ToTomansExact());
    }

    [Fact]
    public async Task OnlyOneActionRunsAtATime()
    {
        var (backend, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products[0]);
        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.NextInvoice);

        var first = viewModel.ConfirmPaymentCommand.ExecuteAsync(null);
        var second = viewModel.ConfirmPaymentCommand.ExecuteAsync(null);
        await Task.WhenAll(first, second);

        Assert.Single(backend.Sales, sale => sale.Status == SaleStatus.Completed);
    }

    private static (InMemorySalesBackend Backend, SalesWorkspaceViewModel ViewModel) Create()
    {
        var backend = new InMemorySalesBackend();
        backend.AddProduct("برنج ایرانی", "RICE-1", "6260000009001", 245_000, stock: 10);
        backend.AddProduct("روغن حیوانی", "OIL-1", "6260000009002", 850_000, stock: 10);
        return (backend, new SalesWorkspaceViewModel(backend.Build(), backend.Warehouse));
    }
}
