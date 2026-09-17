using ERP.Application.Cashiering;
using ERP.Application.Common;
using ERP.Domain.Cashiering;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Presentation.Features.Cashiering;

namespace ERP.Desktop.Tests.Features.Cashiering;

public sealed class CashShiftViewModelTests
{
    [Fact]
    public async Task LoadingWithNoOpenShiftShowsTheOpeningForm()
    {
        var (backend, viewModel) = Create();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.IsOpen);
        Assert.Equal(0, backend.OpenCalls);
    }

    [Fact]
    public async Task OpeningWithABlankAmountShowsAnErrorWithoutCallingTheHandler()
    {
        var (backend, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.OpeningCashInput = "چیز عجیب";

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.OpenError);
        Assert.Equal(0, backend.OpenCalls);
    }

    [Fact]
    public async Task OpeningWithAValidAmountSwitchesToTheOpenState()
    {
        var (backend, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.OpeningCashInput = "200,000";

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsOpen);
        Assert.Equal(1, backend.OpenCalls);
        Assert.Null(viewModel.OpenError);
        Assert.False(string.IsNullOrEmpty(viewModel.OpeningCashText));
    }

    [Fact]
    public async Task ClosingWithGibberishInsteadOfANumberShowsAnErrorAndStaysOpen()
    {
        var (_, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.OpeningCashInput = "200,000";
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.CountedCashInput = "خیلی زیاد";

        await viewModel.CloseCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.CloseError);
        Assert.True(viewModel.IsOpen);
        Assert.False(viewModel.IsCloseSummaryOpen);
    }

    [Fact]
    public async Task ClosingWithAValidAmountShowsTheSummaryAndReturnsToTheOpeningForm()
    {
        var (_, viewModel) = Create();
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.OpeningCashInput = "200,000";
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.CountedCashInput = "200,000";

        await viewModel.CloseCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsCloseSummaryOpen);
        Assert.False(viewModel.IsOpen);
        Assert.Contains("دقیقاً برابر انتظار بود", viewModel.CloseSummaryText, StringComparison.Ordinal);
    }

    private static (InMemoryCashiering Backend, CashShiftViewModel ViewModel) Create()
    {
        var backend = new InMemoryCashiering();
        var warehouseId = WarehouseId.New();
        return (backend, new CashShiftViewModel(backend, backend, backend, warehouseId));
    }

    private sealed class InMemoryCashiering : IOpenCashShiftHandler, ICloseCashShiftHandler, IGetCashShiftStatusHandler
    {
        private CashShift? _shift;

        public int OpenCalls { get; private set; }

        public Task<Result<CashShiftId>> ExecuteAsync(OpenCashShiftCommand command, CancellationToken cancellationToken)
        {
            OpenCalls++;
            _shift = CashShift.Open(command.WarehouseId, Domain.Identity.UserId.New(), Money.FromTomans(command.OpeningCashTomans), DateTimeOffset.UtcNow);
            return Task.FromResult(Result.Success(_shift.Id));
        }

        public Task<Result<CashShiftCloseSummary>> ExecuteAsync(CloseCashShiftCommand command, CancellationToken cancellationToken)
        {
            if (_shift is null || _shift.Id != command.ShiftId)
            {
                return Task.FromResult(Result.Failure<CashShiftCloseSummary>("cashiering.shift.not-found", "شیفت صندوق پیدا نشد."));
            }

            _shift.Close(Money.FromTomans(command.CountedCashTomans), Money.Zero, Domain.Identity.UserId.New(), DateTimeOffset.UtcNow, command.Note);
            var summary = new CashShiftCloseSummary(
                _shift.OpeningCash, Money.Zero, _shift.ExpectedCash!.Value, _shift.CountedCash!.Value, _shift.Variance!.Value);
            return Task.FromResult(Result.Success(summary));
        }

        public Task<CashShiftStatusView> ExecuteAsync(GetCashShiftStatusQuery query, CancellationToken cancellationToken)
        {
            if (_shift is null || _shift.Status == CashShiftStatus.Closed)
            {
                return Task.FromResult(new CashShiftStatusView(false, null, null, null, null, null));
            }

            return Task.FromResult(new CashShiftStatusView(
                true, _shift.Id, _shift.OpenedAtUtc, _shift.OpeningCash, Money.Zero, _shift.OpeningCash));
        }
    }
}
