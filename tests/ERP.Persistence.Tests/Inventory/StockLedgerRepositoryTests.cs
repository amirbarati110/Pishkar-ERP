using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Inventory;
using ERP.Persistence.Migrations;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Inventory;

public sealed class StockLedgerRepositoryTests
{
    [Fact]
    public async Task SaveAndGetPreserveLayersMovementsAndPersianReference()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await MigrateAsync(factory);
        var productId = await SeedProductAsync(factory);
        var warehouseId = WarehouseId.New();
        var ledger = StockLedger.Empty(productId, warehouseId);
        var receivedOn = DateOnly.FromDateTime(DateTime.UtcNow);
        ledger.ReceiveOpeningStock(Quantity.Create(12.5m), Money.FromRials(1_250_000), receivedOn);

        await using (var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
        {
            var repository = new FirebirdStockLedgerRepository(unitOfWork);
            await repository.SaveAsync(ledger, CancellationToken.None);
            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var readUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var readRepository = new FirebirdStockLedgerRepository(readUnitOfWork);
        var restored = await readRepository.GetAsync(productId, warehouseId, CancellationToken.None);

        Assert.NotNull(restored);
        var layer = Assert.Single(restored.Layers);
        Assert.Equal(12.5m, layer.OriginalQuantity.Value);
        Assert.Equal(12.5m, layer.RemainingQuantity.Value);
        Assert.Equal(1_250_000, layer.UnitCost.Rials);
        Assert.Equal(receivedOn, layer.ReceivedOn);
        var movement = Assert.Single(restored.Movements);
        Assert.Equal(StockMovementType.OpeningBalance, movement.Type);
        Assert.StartsWith("opening:", movement.Reference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingLoadedLedgerPersistsNewLayerWithoutDuplicatingOldMovement()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await MigrateAsync(factory);
        var productId = await SeedProductAsync(factory);
        var warehouseId = WarehouseId.New();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ledger = StockLedger.Empty(productId, warehouseId);
        ledger.ReceiveOpeningStock(Quantity.Create(3m), Money.FromRials(100_000), today);

        await SaveAsync(factory, ledger);
        await using (var loadUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
        {
            var repository = new FirebirdStockLedgerRepository(loadUnitOfWork);
            var loaded = await repository.GetAsync(productId, warehouseId, CancellationToken.None);
            Assert.NotNull(loaded);
            loaded.ReceiveOpeningStock(Quantity.Create(2m), Money.FromRials(110_000), today);
            await repository.SaveAsync(loaded, CancellationToken.None);
            await loadUnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var readUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var restored = await new FirebirdStockLedgerRepository(readUnitOfWork)
            .GetAsync(productId, warehouseId, CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal(5m, restored.AvailableQuantity.Value);
        Assert.Equal(2, restored.Layers.Count);
        Assert.Equal(2, restored.Movements.Count);
    }

    [Fact]
    public async Task FifoConsumptionRemainingQuantitiesSurviveRoundTrip()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await MigrateAsync(factory);
        var productId = await SeedProductAsync(factory);
        var warehouseId = WarehouseId.New();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ledger = StockLedger.Empty(productId, warehouseId);
        ledger.ReceiveOpeningStock(Quantity.Create(4m), Money.FromRials(100_000), today.AddDays(-1));
        ledger.ReceiveOpeningStock(Quantity.Create(5m), Money.FromRials(120_000), today);
        ledger.ConsumeFifo(Quantity.Create(6m), "فروش آزمایشی ۱");

        await SaveAsync(factory, ledger);

        await using var readUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var restored = await new FirebirdStockLedgerRepository(readUnitOfWork)
            .GetAsync(productId, warehouseId, CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal(3m, restored.AvailableQuantity.Value);
        Assert.Equal([0m, 3m], restored.Layers.OrderBy(item => item.ReceivedOn).Select(item => item.RemainingQuantity.Value));
        Assert.Contains(restored.Movements, movement => movement.Reference == "فروش آزمایشی ۱");
    }

    private static Task MigrateAsync(FirebirdConnectionFactory factory)
    {
        return new MigrationRunner(factory, [new V001CreateCatalogAndInventory()])
            .MigrateAsync(CancellationToken.None);
    }

    private static async Task SaveAsync(FirebirdConnectionFactory factory, StockLedger ledger)
    {
        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        await new FirebirdStockLedgerRepository(unitOfWork).SaveAsync(ledger, CancellationToken.None);
        await unitOfWork.CommitAsync(CancellationToken.None);
    }

    private static async Task<ProductId> SeedProductAsync(FirebirdConnectionFactory factory)
    {
        var category = Category.Create("مواد غذایی", null, 0);
        var unitId = UnitId.New();
        var product = Product.Create("کالای تست", null, category.Id, unitId, Money.Zero);

        await using (var connection = await factory.OpenAsync(CancellationToken.None))
        await using (var transaction = (FbTransaction)await connection.BeginTransactionAsync(CancellationToken.None))
        await using (var command = new FbCommand(
            """
            INSERT INTO PRODUCT_UNIT (ID, NAME, SYMBOL, ALLOWS_FRACTIONS)
            VALUES (@ID, 'عدد', 'عدد', FALSE)
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@ID", FbDbType.Char).Value = unitId.ToString();
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        await new FirebirdCategoryRepository(unitOfWork).AddAsync(category, CancellationToken.None);
        await new FirebirdProductRepository(unitOfWork).AddAsync(product, CancellationToken.None);
        await unitOfWork.CommitAsync(CancellationToken.None);
        return product.Id;
    }
}
