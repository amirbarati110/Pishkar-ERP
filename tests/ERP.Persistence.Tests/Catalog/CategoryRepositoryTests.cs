using ERP.Domain.Catalog;
using ERP.Application.Common;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Catalog;

public sealed class CategoryRepositoryTests
{
    [Fact]
    public async Task AddAndGetPreservePersianNameHierarchyAndVisibility()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await new MigrationRunner(factory, [new V001CreateCatalogAndInventory()])
            .MigrateAsync(CancellationToken.None);
        var parent = Category.Create("مواد غذایی", null, 0);
        var child = Category.Create("خشکبار", parent.Id, 1);
        child.SetVisibility(new CategoryVisibility(true, false, true));

        await using (var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
        {
            var repository = new FirebirdCategoryRepository(unitOfWork);
            await repository.AddAsync(parent, CancellationToken.None);
            await repository.AddAsync(child, CancellationToken.None);
            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var readUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var readRepository = new FirebirdCategoryRepository(readUnitOfWork);
        var restored = await readRepository.GetByIdAsync(child.Id, CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal("خشکبار", restored.Name);
        Assert.Equal(parent.Id, restored.ParentId);
        Assert.True(restored.Visibility.IsVisibleInPos);
        Assert.False(restored.Visibility.IsVisibleOnline);
        Assert.True(restored.Visibility.IsVisibleInPurchasing);
    }

    [Fact]
    public async Task DisposeWithoutCommitRollsBackCategoryInsert()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await new MigrationRunner(factory, [new V001CreateCatalogAndInventory()])
            .MigrateAsync(CancellationToken.None);
        var category = Category.Create("تنقلات", null, 0);

        await using (var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
        {
            var repository = new FirebirdCategoryRepository(unitOfWork);
            await repository.AddAsync(category, CancellationToken.None);
        }

        await using var readUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var readRepository = new FirebirdCategoryRepository(readUnitOfWork);
        var restored = await readRepository.GetByIdAsync(category.Id, CancellationToken.None);

        Assert.Null(restored);
    }

    [Fact]
    public async Task SiblingNameExistsTreatsRootCategoriesAsSameLevel()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await new MigrationRunner(factory, [new V001CreateCatalogAndInventory()])
            .MigrateAsync(CancellationToken.None);

        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var repository = new FirebirdCategoryRepository(unitOfWork);
        await repository.AddAsync(Category.Create("خشکبار", null, 0), CancellationToken.None);

        var exists = await repository.SiblingNameExistsAsync("خشکبار", null, CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task DatabaseDuplicateSiblingNameBecomesFriendlyConflict()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await new MigrationRunner(factory, [new V001CreateCatalogAndInventory()])
            .MigrateAsync(CancellationToken.None);

        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var repository = new FirebirdCategoryRepository(unitOfWork);
        await repository.AddAsync(Category.Create("خشکبار", null, 0), CancellationToken.None);

        var error = await Assert.ThrowsAsync<DataConflictException>(
            () => repository.AddAsync(Category.Create("خشکبار", null, 1), CancellationToken.None));

        Assert.Equal("catalog.category.duplicate-name", error.Code);
        Assert.Equal("در این سطح، دسته‌بندی دیگری با همین نام وجود دارد.", error.Message);
    }
}
