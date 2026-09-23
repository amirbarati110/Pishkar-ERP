using ERP.Application.Common;
using ERP.Application.Inventory;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Inventory;

namespace ERP.Application.Tests.Inventory;

public sealed class ManageWarehousesTests
{
    private static Warehouse Existing(ApplicationTestContext context, string name)
    {
        var warehouse = Warehouse.Create(name, null);
        context.Warehouses.Items.Add(warehouse);
        return warehouse;
    }

    private static CreateWarehouseHandler Create(ApplicationTestContext context) =>
        new(context.Warehouses, context.Audit, context.UnitOfWork, context.User, context.Clock);

    private static UpdateWarehouseHandler Update(ApplicationTestContext context) =>
        new(context.Warehouses, context.Audit, context.UnitOfWork, context.User, context.Clock);

    private static ArchiveWarehouseHandler Archive(ApplicationTestContext context, WarehouseId mainWarehouseId) =>
        new(context.Warehouses, context.Audit, context.UnitOfWork, context.User, context.Clock, mainWarehouseId);

    [Fact]
    public async Task CreateSavesTheWarehouseAndWritesAudit()
    {
        var context = new ApplicationTestContext();

        var result = await Create(context).ExecuteAsync(new CreateWarehouseCommand(" انبار شمال ", " تهران "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(context.Warehouses.Items);
        Assert.Equal(result.Value, saved.Id);
        Assert.Equal("انبار شمال", saved.Name);
        Assert.Equal("تهران", saved.Address);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
        Assert.Equal("inventory.warehouse.created", Assert.Single(context.Audit.Entries).Action);
    }

    [Fact]
    public async Task CreateRejectsABlankNameAndSavesNothing()
    {
        var context = new ApplicationTestContext();

        var result = await Create(context).ExecuteAsync(new CreateWarehouseCommand(" ", null), CancellationToken.None);

        Assert.Equal("inventory.warehouse.invalid", result.Error?.Code);
        Assert.Empty(context.Warehouses.Items);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task CreateRejectsTheNameOfAnActiveWarehouse()
    {
        var context = new ApplicationTestContext();
        Existing(context, "انبار شمال");

        var result = await Create(context).ExecuteAsync(new CreateWarehouseCommand("انبار شمال", null), CancellationToken.None);

        Assert.Equal("inventory.warehouse.duplicate-name", result.Error?.Code);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task CreateMayReuseTheNameOfAnArchivedWarehouse()
    {
        // «حذف» said the name goes away from the list; it must not keep blocking a new warehouse.
        var context = new ApplicationTestContext();
        Existing(context, "انبار شمال").Archive();

        var result = await Create(context).ExecuteAsync(new CreateWarehouseCommand("انبار شمال", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, context.Warehouses.Items.Count);
    }

    [Fact]
    public async Task CreateReportsARaceOnTheNameFromTheDatabaseWithoutCommitting()
    {
        var context = new ApplicationTestContext();
        context.Warehouses.ConflictOnAdd = new DataConflictException(
            "inventory.warehouse.duplicate-name", "انباری با همین نام همین حالا ثبت شد.", new InvalidOperationException("ux"));

        var result = await Create(context).ExecuteAsync(new CreateWarehouseCommand("انبار", null), CancellationToken.None);

        Assert.Equal("inventory.warehouse.duplicate-name", result.Error?.Code);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task UpdateChangesNameAndAddressAndAuditsBeforeAndAfter()
    {
        var context = new ApplicationTestContext();
        var warehouse = Existing(context, "انبار");

        var result = await Update(context).ExecuteAsync(
            new UpdateWarehouseCommand(warehouse.Id, "انبار مرکزی", "قم"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("انبار مرکزی", warehouse.Name);
        Assert.Equal("قم", warehouse.Address);
        Assert.Equal(1, context.Warehouses.UpdateCount);
        var audit = Assert.Single(context.Audit.Entries);
        Assert.Equal("inventory.warehouse.updated", audit.Action);
        Assert.Equal("انبار | ", audit.OldValue);
        Assert.Equal("انبار مرکزی | قم", audit.NewValue);
    }

    [Fact]
    public async Task UpdateMayKeepItsOwnNameButNotTakeAnotherActiveWarehousesName()
    {
        var context = new ApplicationTestContext();
        var warehouse = Existing(context, "انبار الف");
        Existing(context, "انبار ب");

        var same = await Update(context).ExecuteAsync(new UpdateWarehouseCommand(warehouse.Id, "انبار الف", "نشانی تازه"), CancellationToken.None);
        var taken = await Update(context).ExecuteAsync(new UpdateWarehouseCommand(warehouse.Id, "انبار ب", null), CancellationToken.None);

        Assert.True(same.IsSuccess);
        Assert.Equal("inventory.warehouse.duplicate-name", taken.Error?.Code);
        Assert.Equal("انبار الف", warehouse.Name); // the refused edit left it untouched
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task UpdateOfAMissingOrArchivedWarehouseFailsClearly()
    {
        var context = new ApplicationTestContext();
        var archived = Existing(context, "انبار");
        archived.Archive();

        var missing = await Update(context).ExecuteAsync(new UpdateWarehouseCommand(WarehouseId.New(), "x", null), CancellationToken.None);
        var gone = await Update(context).ExecuteAsync(new UpdateWarehouseCommand(archived.Id, "x", null), CancellationToken.None);

        Assert.Equal("inventory.warehouse.not-found", missing.Error?.Code);
        Assert.Equal("inventory.warehouse.archived", gone.Error?.Code);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ArchiveTakesAnEmptySecondWarehouseOutOfUseAndAuditsIt()
    {
        var context = new ApplicationTestContext();
        var main = Existing(context, "فروشگاه مرکزی");
        var second = Existing(context, "انبار دوم");

        var result = await Archive(context, main.Id).ExecuteAsync(new ArchiveWarehouseCommand(second.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WarehouseStatus.Archived, second.Status);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
        Assert.Equal("inventory.warehouse.archived", Assert.Single(context.Audit.Entries).Action);
    }

    [Fact]
    public async Task ArchiveRefusesTheMainWarehouseEvenWhenAnotherIsActiveAndItIsEmpty()
    {
        // Every screen still sells and receives stock on the main warehouse; archiving it would
        // send each new sale to a warehouse that no list shows.
        var context = new ApplicationTestContext();
        var main = Existing(context, "فروشگاه مرکزی");
        Existing(context, "انبار دوم");

        var result = await Archive(context, main.Id).ExecuteAsync(new ArchiveWarehouseCommand(main.Id), CancellationToken.None);

        Assert.Equal("inventory.warehouse.main", result.Error?.Code);
        Assert.Contains("فروشگاه مرکزی", result.Error?.Message, StringComparison.Ordinal);
        Assert.Equal(WarehouseStatus.Active, main.Status);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ArchiveRefusesTheLastActiveWarehouse()
    {
        var context = new ApplicationTestContext();
        var only = Existing(context, "تنها انبار");

        var result = await Archive(context, WarehouseId.New()).ExecuteAsync(new ArchiveWarehouseCommand(only.Id), CancellationToken.None);

        Assert.Equal("inventory.warehouse.last-active", result.Error?.Code);
        Assert.Equal(WarehouseStatus.Active, only.Status);
    }

    [Fact]
    public async Task ArchiveRefusesAWarehouseWithStockOrAnOpenShift()
    {
        var context = new ApplicationTestContext();
        var main = Existing(context, "فروشگاه مرکزی");
        var stocked = Existing(context, "انبار پر");
        var tilled = Existing(context, "انبار با شیفت باز");
        context.Warehouses.WithStock.Add(stocked.Id);
        context.Warehouses.WithOpenShift.Add(tilled.Id);
        var handler = Archive(context, main.Id);

        var hasStock = await handler.ExecuteAsync(new ArchiveWarehouseCommand(stocked.Id), CancellationToken.None);
        var hasShift = await handler.ExecuteAsync(new ArchiveWarehouseCommand(tilled.Id), CancellationToken.None);

        Assert.Equal("inventory.warehouse.has-stock", hasStock.Error?.Code);
        Assert.Equal("inventory.warehouse.has-open-shift", hasShift.Error?.Code);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ArchivingTwiceIsNotAnErrorAndAMissingWarehouseIsReported()
    {
        var context = new ApplicationTestContext();
        var main = Existing(context, "فروشگاه مرکزی");
        var second = Existing(context, "انبار دوم");
        second.Archive();

        var again = await Archive(context, main.Id).ExecuteAsync(new ArchiveWarehouseCommand(second.Id), CancellationToken.None);
        var missing = await Archive(context, main.Id).ExecuteAsync(new ArchiveWarehouseCommand(WarehouseId.New()), CancellationToken.None);

        Assert.True(again.IsSuccess);
        Assert.Equal("inventory.warehouse.not-found", missing.Error?.Code);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ListTrimsTheSearchTermAndTreatsBlankAsNoFilter()
    {
        var reader = new RecordingWarehouseListReader();
        var handler = new ListWarehousesHandler(reader);

        await handler.ExecuteAsync(new WarehouseListQuery("  شمال "), CancellationToken.None);
        await handler.ExecuteAsync(new WarehouseListQuery("   "), CancellationToken.None);

        Assert.Equal(["شمال", null], reader.Terms);
    }

    private sealed class RecordingWarehouseListReader : IWarehouseListReader
    {
        public List<string?> Terms { get; } = [];

        public Task<IReadOnlyList<WarehouseListRow>> ListAsync(WarehouseListQuery query, CancellationToken cancellationToken)
        {
            Terms.Add(query.Term);
            return Task.FromResult<IReadOnlyList<WarehouseListRow>>([]);
        }
    }
}
