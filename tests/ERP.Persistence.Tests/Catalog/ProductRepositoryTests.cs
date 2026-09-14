using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Application.Common;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Catalog;

public sealed class ProductRepositoryTests
{
    [Fact]
    public async Task AddAndGetPreservePersianProductAndAllBarcodes()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await MigrateAsync(factory);
        var (categoryId, unitId) = await SeedPrerequisitesAsync(factory);
        var product = Product.Create(
            "برنج ایرانی ممتاز",
            "rice-001",
            categoryId,
            unitId,
            Money.FromTomans(245_000));
        product.AddBarcode("6260001000011");
        product.AddBarcode("RICE-BULK-01");

        await using (var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
        {
            var repository = new FirebirdProductRepository(unitOfWork);
            await repository.AddAsync(product, CancellationToken.None);
            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var readUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var readRepository = new FirebirdProductRepository(readUnitOfWork);
        var restored = await readRepository.GetByIdAsync(product.Id, CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal("برنج ایرانی ممتاز", restored.Name);
        Assert.Equal("RICE-001", restored.Sku);
        Assert.Equal(categoryId, restored.CategoryId);
        Assert.Equal(unitId, restored.BaseUnitId);
        Assert.Equal(2_450_000, restored.SalePrice.Rials);
        Assert.Equal(
            ["6260001000011", "RICE-BULK-01"],
            restored.Barcodes.Select(item => item.Value).Order().ToArray());
    }

    [Fact]
    public async Task BarcodeExistsFindsBarcodeRegardlessOfLetterCase()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await MigrateAsync(factory);
        var (categoryId, unitId) = await SeedPrerequisitesAsync(factory);
        var product = Product.Create("نوشابه", null, categoryId, unitId, Money.FromTomans(35_000));
        product.AddBarcode("Cola-001");

        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var repository = new FirebirdProductRepository(unitOfWork);
        await repository.AddAsync(product, CancellationToken.None);

        var exists = await repository.BarcodeExistsAsync("cola-001", CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task DisposeWithoutCommitRollsBackProductAndBarcodes()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await MigrateAsync(factory);
        var (categoryId, unitId) = await SeedPrerequisitesAsync(factory);
        var product = Product.Create("رب گوجه", null, categoryId, unitId, Money.FromTomans(79_000));
        product.AddBarcode("6260001999999");

        await using (var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
        {
            var repository = new FirebirdProductRepository(unitOfWork);
            await repository.AddAsync(product, CancellationToken.None);
        }

        await using var readUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var readRepository = new FirebirdProductRepository(readUnitOfWork);

        Assert.Null(await readRepository.GetByIdAsync(product.Id, CancellationToken.None));
        Assert.False(await readRepository.BarcodeExistsAsync("6260001999999", CancellationToken.None));
    }

    [Fact]
    public async Task DatabaseDuplicateBarcodeBecomesFriendlyConflict()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await MigrateAsync(factory);
        var (categoryId, unitId) = await SeedPrerequisitesAsync(factory);
        var first = Product.Create("کالای اول", null, categoryId, unitId, Money.Zero);
        var second = Product.Create("کالای دوم", null, categoryId, unitId, Money.Zero);
        first.AddBarcode("SHARED-001");
        second.AddBarcode("SHARED-001");

        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var repository = new FirebirdProductRepository(unitOfWork);
        await repository.AddAsync(first, CancellationToken.None);

        var error = await Assert.ThrowsAsync<DataConflictException>(
            () => repository.AddAsync(second, CancellationToken.None));

        Assert.Equal("catalog.product.duplicate-barcode", error.Code);
        Assert.Equal("این بارکد قبلاً برای کالای دیگری ثبت شده است.", error.Message);
    }

    private static Task MigrateAsync(FirebirdConnectionFactory factory)
    {
        return new MigrationRunner(factory, [new V001CreateCatalogAndInventory()])
            .MigrateAsync(CancellationToken.None);
    }

    private static async Task<(CategoryId CategoryId, UnitId UnitId)> SeedPrerequisitesAsync(
        FirebirdConnectionFactory factory)
    {
        var categoryId = CategoryId.New();
        var unitId = UnitId.New();
        await using var connection = await factory.OpenAsync(CancellationToken.None);
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync(CancellationToken.None);

        await using (var categoryCommand = new FbCommand(
            """
            INSERT INTO CATEGORY (
                ID, PARENT_ID, PARENT_KEY, NAME, SORT_ORDER, STATUS,
                VIS_POS, VIS_ONLINE, VIS_PURCHASING)
            VALUES (
                @ID, NULL, '00000000-0000-0000-0000-000000000000',
                'مواد غذایی', 0, 1, TRUE, TRUE, TRUE)
            """,
            connection,
            transaction))
        {
            categoryCommand.Parameters.Add("@ID", FbDbType.Char).Value = categoryId.ToString();
            await categoryCommand.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using (var unitCommand = new FbCommand(
            """
            INSERT INTO PRODUCT_UNIT (ID, NAME, SYMBOL, ALLOWS_FRACTIONS)
            VALUES (@ID, 'عدد', 'عدد', FALSE)
            """,
            connection,
            transaction))
        {
            unitCommand.Parameters.Add("@ID", FbDbType.Char).Value = unitId.ToString();
            await unitCommand.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await transaction.CommitAsync(CancellationToken.None);
        return (categoryId, unitId);
    }
}
