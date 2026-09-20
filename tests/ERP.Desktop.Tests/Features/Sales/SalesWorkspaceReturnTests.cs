using ERP.Domain.Sales;
using ERP.Presentation.Features.Sales;

namespace ERP.Desktop.Tests.Features.Sales;

public sealed class SalesWorkspaceReturnTests
{
    /// <summary>یک فاکتور نقدی ۱ عدد برنج ۲۴۵٬۰۰۰ تومانی (شماره ۱۲۵۸) که ثبت شده و آماده‌ی مرجوعی است.</summary>
    private static async Task<(InMemorySalesBackend Backend, SalesWorkspaceViewModel ViewModel)> WithACompletedSaleAsync()
    {
        var backend = new InMemorySalesBackend();
        backend.AddProduct("برنج ایرانی", "RICE-1", "6260000009001", 245_000, stock: 10);
        backend.AddProduct("روغن حیوانی", "OIL-1", "6260000009002", 850_000, stock: 10);
        var viewModel = new SalesWorkspaceViewModel(backend.Build(), backend.Warehouse);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single(product => product.Name == "برنج ایرانی"));
        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);
        await viewModel.ConfirmPaymentCommand.ExecuteAsync(null);
        Assert.Equal(SaleStatus.Completed, backend.Sales[0].Status);

        return (backend, viewModel);
    }

    private static async Task FindTheInvoiceAsync(SalesWorkspaceViewModel viewModel)
    {
        viewModel.OpenReturnCommand.Execute(null);
        viewModel.ReturnNumberInput = "۱۲۵۸";
        await viewModel.FindReturnInvoiceCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task FindingAnInvoiceByItsNumberShowsItsRowsAndThePreviewFollowsTheQuantity()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();

        await FindTheInvoiceAsync(viewModel);

        Assert.True(viewModel.HasReturnInvoice);
        Assert.Null(viewModel.ReturnError);
        var row = Assert.Single(viewModel.ReturnLines);
        Assert.Equal("برنج ایرانی", row.ProductName);
        Assert.Equal("۰", viewModel.ReturnRefundText);

        await viewModel.IncreaseReturnQuantityCommand.ExecuteAsync(row);

        Assert.Equal(1, row.Quantity);
        Assert.Equal("۲۶۹,۵۰۰", viewModel.ReturnRefundText); // ۲۴۵٬۰۰۰ + ۱۰٪ مالیات
        Assert.True(viewModel.HasReturnSelection);

        await viewModel.IncreaseReturnQuantityCommand.ExecuteAsync(row); // بیشتر از فروخته‌شده نمی‌شود
        Assert.Equal(1, row.Quantity);

        await viewModel.DecreaseReturnQuantityCommand.ExecuteAsync(row);
        Assert.Equal("۰", viewModel.ReturnRefundText);
    }

    [Fact]
    public async Task AnUnknownNumberSaysSoAndOpensNothing()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();

        viewModel.OpenReturnCommand.Execute(null);
        viewModel.ReturnNumberInput = "۹۹۹";
        await viewModel.FindReturnInvoiceCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasReturnInvoice);
        Assert.Equal("فاکتور ثبت‌شده‌ای با این شماره پیدا نشد.", viewModel.ReturnError);
    }

    [Fact]
    public async Task ARubbishNumberIsRefusedBeforeAskingTheServer()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();

        viewModel.OpenReturnCommand.Execute(null);
        viewModel.ReturnNumberInput = "abc";
        await viewModel.FindReturnInvoiceCommand.ExecuteAsync(null);

        Assert.Equal("شماره فاکتور را درست وارد کنید.", viewModel.ReturnError);
    }

    [Fact]
    public async Task ConfirmingWithoutAReasonIsRefusedAndNothingIsSaved()
    {
        var (backend, viewModel) = await WithACompletedSaleAsync();
        await FindTheInvoiceAsync(viewModel);
        await viewModel.IncreaseReturnQuantityCommand.ExecuteAsync(viewModel.ReturnLines[0]);

        await viewModel.ConfirmReturnCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsReturnDone);
        Assert.Equal("دلیل مرجوعی را بنویسید.", viewModel.ReturnError);
        Assert.Empty(backend.SaleReturns);
    }

    [Fact]
    public async Task ConfirmingWithNothingPickedIsRefused()
    {
        var (backend, viewModel) = await WithACompletedSaleAsync();
        await FindTheInvoiceAsync(viewModel);
        viewModel.ReturnReasonText = "دلیل";

        await viewModel.ConfirmReturnCommand.ExecuteAsync(null);

        Assert.Equal("هیچ کالایی برای مرجوعی انتخاب نشده است.", viewModel.ReturnError);
        Assert.Empty(backend.SaleReturns);
    }

    [Fact]
    public async Task ARealReturnPutsTheStockBackAndShowsWhatToHandBack()
    {
        var (backend, viewModel) = await WithACompletedSaleAsync();
        var ledger = backend.Ledgers.Single(item => item.ProductId == backend.Products.Single(product => product.Name == "برنج ایرانی").Id);
        Assert.Equal(9, ledger.AvailableQuantity.Value);

        await FindTheInvoiceAsync(viewModel);
        await viewModel.ReturnAllCommand.ExecuteAsync(null);
        viewModel.ReturnReasonText = "ناسازگار";
        await viewModel.ConfirmReturnCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsReturnDone);
        Assert.Null(viewModel.ReturnError);
        Assert.Contains("مرجوعی شماره ۱ ثبت شد", viewModel.ReturnDoneText, StringComparison.Ordinal);
        Assert.Contains("۲۶۹,۵۰۰ تومان", viewModel.ReturnDoneText, StringComparison.Ordinal);
        Assert.Contains("از صندوق", viewModel.ReturnDoneText, StringComparison.Ordinal);
        Assert.False(viewModel.IsReturnEditable); // ثبت‌شده، دیگر قابل ویرایش نیست
        Assert.Equal(10, ledger.AvailableQuantity.Value);
        Assert.Single(backend.SaleReturns);
    }

    [Fact]
    public async Task ReturningAnInvoiceThatIsAlreadyFullyReturnedIsRefusedWithAClearMessage()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();
        await FindTheInvoiceAsync(viewModel);
        await viewModel.ReturnAllCommand.ExecuteAsync(null);
        viewModel.ReturnReasonText = "اول";
        await viewModel.ConfirmReturnCommand.ExecuteAsync(null);

        await FindTheInvoiceAsync(viewModel);
        var row = Assert.Single(viewModel.ReturnLines);

        Assert.False(row.CanReturn);
        Assert.Equal("همه‌اش مرجوع شده", row.RemainingText);
        await viewModel.IncreaseReturnQuantityCommand.ExecuteAsync(row);
        Assert.Equal(0, row.Quantity);
    }

    [Fact]
    public async Task RefundingOffDebtIsNotOfferedForAWalkInCashCustomer()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();
        await FindTheInvoiceAsync(viewModel);

        Assert.False(viewModel.CanRefundToDebt);
        viewModel.SelectReturnRefundMethodCommand.Execute(PaymentMethod.Credit);

        Assert.Equal(PaymentMethod.Cash, viewModel.ReturnRefundMethod);
    }

    [Fact]
    public async Task AnExchangeOpensAFreshInvoiceThatShowsTheDifferenceAtPayment()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();
        await FindTheInvoiceAsync(viewModel);
        await viewModel.ReturnAllCommand.ExecuteAsync(null);
        viewModel.ReturnReasonText = "تعویض با روغن";
        await viewModel.ConfirmReturnCommand.ExecuteAsync(null);

        await viewModel.StartExchangeCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsReturnOpen);
        Assert.True(viewModel.CurrentTab!.IsEmpty);
        Assert.Equal(Domain.Common.Money.FromTomans(269_500).Rials, viewModel.CurrentTab.ExchangeRefundRials);

        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single(product => product.Name == "روغن حیوانی"));
        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);

        Assert.True(viewModel.HasExchangeHint);
        Assert.Contains("مرجوعی شماره ۱", viewModel.ExchangeHintText, StringComparison.Ordinal);
        Assert.Contains("۶۶۵,۵۰۰ تومان از مشتری بگیرید", viewModel.ExchangeHintText, StringComparison.Ordinal); // ۹۳۵٬۰۰۰ − ۲۶۹٬۵۰۰
    }

    [Fact]
    public async Task AnOrdinaryInvoiceShowsNoExchangeHint()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();
        await viewModel.StartNextAfterSummaryCommand.ExecuteAsync(null);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products[0]);

        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);

        Assert.False(viewModel.HasExchangeHint);
    }

    [Fact]
    public async Task EscClosesTheReturnWindow()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();
        viewModel.OpenReturnCommand.Execute(null);
        Assert.True(viewModel.IsReturnOpen);

        viewModel.CloseTopDialogCommand.Execute(null);

        Assert.False(viewModel.IsReturnOpen);
    }
}
