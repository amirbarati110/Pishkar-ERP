using ERP.Domain.Sales;
using ERP.Presentation.Features.Sales;

namespace ERP.Desktop.Tests.Features.Sales;

/// <summary>«دیدن فاکتور»: a completed invoice opened by its number (from a row of a product's کاردکس).</summary>
public sealed class SalesWorkspaceInvoiceViewTests
{
    private static async Task<(InMemorySalesBackend Backend, SalesWorkspaceViewModel ViewModel)> WithACompletedSaleAsync()
    {
        var backend = new InMemorySalesBackend();
        backend.AddProduct("برنج ایرانی", "RICE-1", "6260000009001", 245_000, stock: 10);
        var viewModel = new SalesWorkspaceViewModel(backend.Build(), backend.Warehouse);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single(product => product.Name == "برنج ایرانی"));
        viewModel.OpenPaymentCommand.Execute(CompletionFollowUp.ShowSummary);
        await viewModel.ConfirmPaymentCommand.ExecuteAsync(null);
        Assert.Equal(SaleStatus.Completed, backend.Sales[0].Status);
        return (backend, viewModel);
    }

    [Fact]
    public async Task OpeningACompletedInvoiceByNumberShowsItsLinesTotalsAndActions()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();

        await viewModel.OpenInvoiceByNumberCommand.ExecuteAsync(1258);

        Assert.True(viewModel.IsInvoiceViewOpen);
        Assert.Null(viewModel.InvoiceViewError);
        Assert.Equal("فاکتور ۱۲۵۸", viewModel.InvoiceViewTitleText);
        var line = Assert.Single(viewModel.InvoiceViewLines);
        Assert.Equal("برنج ایرانی", line.Name);
        Assert.Equal("۲۴۵,۰۰۰", viewModel.InvoiceViewSubtotalText);
        Assert.Equal("۲۶۹,۵۰۰", viewModel.InvoiceViewTotalText); // با ۱۰٪ مالیات
        Assert.True(viewModel.HasInvoiceViewTax);
        Assert.False(viewModel.HasInvoiceViewDiscount);
        Assert.True(viewModel.HasInvoiceViewActions);
        Assert.Equal("فاکتور ۱۲۵۸", "فاکتور " + viewModel.ViewedInvoice!.NumberText);
        Assert.Equal(backendSaleId(viewModel), viewModel.ViewedInvoice.Item.SaleId);
    }

    [Fact]
    public async Task AnUnknownNumberShowsAPlainMessageAndNoActions()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();

        await viewModel.OpenInvoiceByNumberCommand.ExecuteAsync(999_999);

        Assert.True(viewModel.IsInvoiceViewOpen);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.InvoiceViewError));
        Assert.False(viewModel.HasInvoiceViewActions);
        Assert.Empty(viewModel.InvoiceViewLines);
    }

    [Fact]
    public async Task EscClosesTheInvoiceView()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();
        await viewModel.OpenInvoiceByNumberCommand.ExecuteAsync(1258);

        viewModel.CloseTopDialogCommand.Execute(null);

        Assert.False(viewModel.IsInvoiceViewOpen);
    }

    [Fact]
    public async Task TheCorrectionActionWorksOnTheOpenedInvoiceLikeOnAListRow()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();
        await viewModel.OpenInvoiceByNumberCommand.ExecuteAsync(1258);

        viewModel.CloseInvoiceViewCommand.Execute(null);
        viewModel.OpenCorrectionPromptCommand.Execute(viewModel.ViewedInvoice);

        Assert.True(viewModel.IsStartingCorrectionOpen);
        Assert.Equal("۱۲۵۸", viewModel.CorrectionOriginalNumberText);
    }

    private static SaleId backendSaleId(SalesWorkspaceViewModel viewModel) => viewModel.ViewedInvoice!.Item.SaleId;
}
