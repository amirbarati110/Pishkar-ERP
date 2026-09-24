using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Domain.Inventory;
using ERP.Presentation.Features.Inventory;

namespace ERP.Desktop.Tests.Features.Inventory;

public sealed class WarehouseListViewModelTests
{
    private static (FakeWarehouses Backend, WarehouseListViewModel ViewModel) Create()
    {
        var backend = new FakeWarehouses();
        return (backend, new WarehouseListViewModel(backend, backend, backend, backend) { SearchDelay = TimeSpan.Zero });
    }

    [Fact]
    public async Task LoadingListsTheWarehousesWithTheirCount()
    {
        var (backend, viewModel) = Create();
        backend.Rows.Add(new WarehouseListRow(WarehouseId.New(), "فروشگاه مرکزی", null));
        backend.Rows.Add(new WarehouseListRow(WarehouseId.New(), "انبار شمال", "تهران"));

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(2, viewModel.Items.Count);
        Assert.Equal("۲", viewModel.CountText);
        Assert.Equal("انبار شمال، تهران", viewModel.Items[1].AccessibleName);
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task ABlankNameIsCaughtBeforeAskingTheServer()
    {
        var (backend, viewModel) = Create();
        viewModel.NewCommand.Execute(null);
        viewModel.Name = "  ";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("نام انبار را وارد کنید.", viewModel.NameError);
        Assert.True(viewModel.IsEditorOpen);
        Assert.Empty(backend.Created);
    }

    [Fact]
    public async Task SavingANewWarehouseClosesTheFormAndRefreshesTheList()
    {
        var (backend, viewModel) = Create();
        viewModel.NewCommand.Execute(null);
        viewModel.Name = " انبار شمال ";
        viewModel.Address = " ";

        await viewModel.SaveCommand.ExecuteAsync(null);

        var created = Assert.Single(backend.Created);
        Assert.Equal("انبار شمال", created.Name);
        Assert.Null(created.Address);
        Assert.False(viewModel.IsEditorOpen);
        Assert.Equal("«انبار شمال» ساخته شد.", viewModel.Notice);
        Assert.Single(viewModel.Items);
    }

    [Fact]
    public async Task ADuplicateNameIsShownUnderTheNameField()
    {
        var (backend, viewModel) = Create();
        backend.CreateFailure = new ApplicationError("inventory.warehouse.duplicate-name", "انباری با همین نام از قبل وجود دارد.");
        viewModel.NewCommand.Execute(null);
        viewModel.Name = "انبار شمال";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("انباری با همین نام از قبل وجود دارد.", viewModel.NameError);
        Assert.True(viewModel.IsEditorOpen);
    }

    [Fact]
    public async Task DeletingAsksFirstAndARefusalIsShownInPlainWords()
    {
        var (backend, viewModel) = Create();
        backend.Rows.Add(new WarehouseListRow(WarehouseId.New(), "فروشگاه مرکزی", null));
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedItem = viewModel.Items[0];
        backend.ArchiveFailure = new ApplicationError("inventory.warehouse.main", "«فروشگاه مرکزی» انبار اصلی است و حذف نمی‌شود.");

        viewModel.ArchiveCommand.Execute(null);
        Assert.True(viewModel.IsArchiveConfirmOpen);
        Assert.Empty(backend.Archived);

        await viewModel.ConfirmArchiveCommand.ExecuteAsync(null);

        Assert.Single(backend.Archived);
        Assert.Equal("«فروشگاه مرکزی» انبار اصلی است و حذف نمی‌شود.", viewModel.ErrorMessage);
        Assert.Single(viewModel.Items);
    }

    private sealed class FakeWarehouses :
        IListWarehousesHandler, ICreateWarehouseHandler, IUpdateWarehouseHandler, IArchiveWarehouseHandler
    {
        public List<WarehouseListRow> Rows { get; } = [];

        public List<CreateWarehouseCommand> Created { get; } = [];

        public List<ArchiveWarehouseCommand> Archived { get; } = [];

        public ApplicationError? CreateFailure { get; set; }

        public ApplicationError? ArchiveFailure { get; set; }

        public Task<IReadOnlyList<WarehouseListRow>> ExecuteAsync(WarehouseListQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WarehouseListRow>>(Rows.ToList());

        public Task<Result<WarehouseId>> ExecuteAsync(CreateWarehouseCommand command, CancellationToken cancellationToken)
        {
            if (CreateFailure is { } failure)
            {
                return Task.FromResult(Result.Failure<WarehouseId>(failure.Code, failure.Message));
            }

            Created.Add(command);
            Rows.Add(new WarehouseListRow(WarehouseId.New(), command.Name, command.Address));
            return Task.FromResult(Result.Success(Rows[^1].Id));
        }

        public Task<Result<bool>> ExecuteAsync(UpdateWarehouseCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(true));

        public Task<Result<bool>> ExecuteAsync(ArchiveWarehouseCommand command, CancellationToken cancellationToken)
        {
            Archived.Add(command);
            return Task.FromResult(ArchiveFailure is { } failure
                ? Result.Failure<bool>(failure.Code, failure.Message)
                : Result.Success(true));
        }
    }
}
