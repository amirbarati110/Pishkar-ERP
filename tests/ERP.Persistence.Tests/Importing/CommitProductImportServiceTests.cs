using ERP.Application.Common;
using ERP.Application.Importing;
using ERP.Domain.Common;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Importing;

/// <summary>
/// Proves the batch is truly atomic on real Firebird, not just "no CommitAsync
/// call" as the in-memory Application-layer test can show — the in-memory
/// repository has no transaction to roll back, so only a real database can
/// prove the first row of a failed batch does not survive.
/// </summary>
public sealed class CommitProductImportServiceTests
{
    [Fact]
    public async Task AllRowsSurviveTogetherWhenTheWholeBatchIsValid()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (factory, service, defaults) = await SetupAsync(database);

        var rows = new[]
        {
            Row(2, "برنج ایرانی", defaults, quantity: 10, unitCostTomans: 100_000, barcode: "6260001000011"),
            Row(3, "روغن", defaults, quantity: null, unitCostTomans: null),
        };

        var result = await service.ExecuteAsync(new CommitProductImportCommand(rows, defaults.MainWarehouseId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var products = new FirebirdProductRepository(unitOfWork);
        Assert.True(await products.BarcodeExistsAsync("6260001000011", CancellationToken.None));
    }

    [Fact]
    public async Task ADuplicateBarcodeMidBatchLeavesNoRowBehindNotEvenTheEarlierOnes()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (factory, service, defaults) = await SetupAsync(database);

        // یک بارکد از قبل در دیتابیس واقعی هست — انگار یک دستگاه دیگر همین
        // لحظه آن را ثبت کرده، نه چیزی که Preview روی فایل می‌توانست ببیند.
        var first = await service.ExecuteAsync(
            new Application.Catalog.CreateProductCommand(
                "کالای از قبل", null, defaults.GeneralCategoryId, defaults.EachUnitId,
                Money.FromTomans(10_000), ["6260001000099"]),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var rows = new[]
        {
            Row(2, "برنج ایرانی", defaults, quantity: 10, unitCostTomans: 100_000, barcode: "6260001000011"),
            Row(3, "روغن با بارکد تکراری", defaults, quantity: null, unitCostTomans: null, barcode: "6260001000099"),
        };

        var result = await service.ExecuteAsync(new CommitProductImportCommand(rows, defaults.MainWarehouseId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("import.product.duplicate-barcode", result.Error?.Code);

        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var products = new FirebirdProductRepository(unitOfWork);
        // ردیف اول («برنج ایرانی») هم نباید مانده باشد — کل تراکنش رول‌بک شده.
        Assert.False(await products.BarcodeExistsAsync("6260001000011", CancellationToken.None));
    }

    private static PreparedProductImportRow Row(
        int rowNumber,
        string name,
        RetailSetupDefaults defaults,
        decimal? quantity,
        long? unitCostTomans,
        string? barcode = null) =>
        new(
            rowNumber,
            name,
            Sku: null,
            defaults.GeneralCategoryId,
            defaults.EachUnitId,
            Money.FromTomans(50_000),
            barcode is null ? [] : [barcode],
            quantity,
            unitCostTomans is { } cost ? Money.FromTomans(cost) : null);

    private static async Task<(FirebirdConnectionFactory Factory, FirebirdRetailSetupService Service, RetailSetupDefaults Defaults)> SetupAsync(
        FirebirdTestDatabase database)
    {
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var service = new FirebirdRetailSetupService(
            factory,
            new TestUserContext(Guid.NewGuid()),
            new TestClock(new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero)));
        return (factory, service, defaults);
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;
}
