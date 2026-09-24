using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;

namespace ERP.Persistence.Tests.Services;

public sealed class FirebirdRetailSetupServiceTests
{
    [Fact]
    public async Task SameServiceCanCommitMultipleUserActionsIndependently()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await new MigrationRunner(factory, [new V001CreateCatalogAndInventory()])
            .MigrateAsync(CancellationToken.None);
        var service = new FirebirdRetailSetupService(
            factory,
            new TestUserContext(Guid.NewGuid()),
            new TestClock(new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero)), AllowAllAccess.Instance);

        var first = await service.ExecuteAsync(
            new CreateCategoryCommand("مواد غذایی", null, 0),
            CancellationToken.None);
        var second = await service.ExecuteAsync(
            new CreateCategoryCommand("شوینده", null, 1),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var repository = new FirebirdCategoryRepository(unitOfWork);
        Assert.NotNull(await repository.GetByIdAsync(first.Value, CancellationToken.None));
        Assert.NotNull(await repository.GetByIdAsync(second.Value, CancellationToken.None));
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;
}
