using ERP.Domain.Sales;
using ERP.Presentation.Features.Sales;

namespace ERP.Desktop.Tests.Features.Sales;

/// <summary>
/// «خدمات/هزینه‌ی فاکتور هم پس داده شود» in the return window (user decision 1405/07/02):
/// offered only when the invoice has one still to give back, off until the cashier ticks it.
/// </summary>
public sealed class SalesWorkspaceServiceChargeReturnTests
{
    /// <summary>
    /// A cash invoice (number 1258): one rice at 245,000 toman plus 20,000 toman delivery, 10% VAT
    /// = 291,500 toman. The delivery with its VAT is 22,000 toman.
    /// </summary>
    private static async Task<(InMemorySalesBackend Backend, SalesWorkspaceViewModel ViewModel)> WithACompletedSaleAsync(
        string serviceCharge = "۲۰,۰۰۰")
    {
        var backend = new InMemorySalesBackend();
        backend.AddProduct("برنج ایرانی", "RICE-1", "6260000009001", 245_000, stock: 10);
        var viewModel = new SalesWorkspaceViewModel(backend.Build(), backend.Warehouse);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.AddProductCommand.ExecuteAsync(viewModel.Products.Single());
        viewModel.CurrentTab!.ServiceChargeInput = serviceCharge;
        await viewModel.CommitChargesCommand.ExecuteAsync("service");
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
        Assert.True(viewModel.HasReturnInvoice, viewModel.ReturnError);
    }

    [Fact]
    public async Task TheChoiceIsOfferedWithItsAmountButStartsUnticked()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();

        await FindTheInvoiceAsync(viewModel);

        Assert.True(viewModel.CanRefundServiceCharge);
        Assert.Equal("۲۲,۰۰۰ تومان", viewModel.ServiceChargeRefundText);
        Assert.False(viewModel.RefundServiceCharge);
        Assert.False(viewModel.HasReturnSelection);
        Assert.Equal("۰", viewModel.ReturnRefundText);
    }

    [Fact]
    public async Task TickingItAddsItToThePreviewAndAloneIsEnoughToConfirm()
    {
        var (backend, viewModel) = await WithACompletedSaleAsync();
        await FindTheInvoiceAsync(viewModel);

        viewModel.RefundServiceCharge = true;

        Assert.True(viewModel.HasReturnSelection);
        Assert.Equal("۲۲,۰۰۰", viewModel.ReturnRefundText);

        await viewModel.IncreaseReturnQuantityCommand.ExecuteAsync(viewModel.ReturnLines[0]);
        Assert.Equal("۲۹۱,۵۰۰", viewModel.ReturnRefundText); // the whole invoice

        await viewModel.DecreaseReturnQuantityCommand.ExecuteAsync(viewModel.ReturnLines[0]);
        viewModel.ReturnReasonText = "ارسال انجام نشد";
        await viewModel.ConfirmReturnCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsReturnDone, viewModel.ReturnError);
        var saleReturn = Assert.Single(backend.SaleReturns);
        Assert.Empty(saleReturn.Lines);
        Assert.Equal(220_000, saleReturn.RefundTotal.Rials);
    }

    [Fact]
    public async Task OnceGivenBackItIsNoLongerOffered()
    {
        var (_, viewModel) = await WithACompletedSaleAsync();
        await FindTheInvoiceAsync(viewModel);
        viewModel.RefundServiceCharge = true;
        viewModel.ReturnReasonText = "ارسال انجام نشد";
        await viewModel.ConfirmReturnCommand.ExecuteAsync(null);

        await FindTheInvoiceAsync(viewModel);

        Assert.False(viewModel.CanRefundServiceCharge);
        Assert.False(viewModel.RefundServiceCharge);
    }

    [Fact]
    public async Task AnInvoiceWithoutAServiceChargeDoesNotShowTheChoice()
    {
        var (_, viewModel) = await WithACompletedSaleAsync(serviceCharge: "۰");

        await FindTheInvoiceAsync(viewModel);

        Assert.False(viewModel.CanRefundServiceCharge);
        Assert.Equal(string.Empty, viewModel.ServiceChargeRefundText);
    }
}
