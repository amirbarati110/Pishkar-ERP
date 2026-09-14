using System.Globalization;
using ERP.Application.Audit;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Persistence.Audit;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Transactions;

public sealed class TransactionalUseCaseTests
{
    [Fact]
    public async Task CreateCategoryUseCaseCommitsCategoryAndAuditTogether()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await MigrateAsync(factory);
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        CategoryId categoryId;

        await using (var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
        {
            var handler = new CreateCategoryHandler(
                new FirebirdCategoryRepository(unitOfWork),
                new FirebirdAuditWriter(unitOfWork),
                unitOfWork,
                new TestUserContext(userId),
                new TestClock(now));

            var result = await handler.ExecuteAsync(
                new CreateCategoryCommand("لبنیات", null, 0),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            categoryId = result.Value;
        }

        await using var readUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var category = await new FirebirdCategoryRepository(readUnitOfWork)
            .GetByIdAsync(categoryId, CancellationToken.None);

        Assert.NotNull(category);
        Assert.Equal(1, await CountAuditEntriesAsync(factory));
    }

    [Fact]
    public async Task FailedSecondWriteRollsBackEveryEarlierWrite()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await MigrateAsync(factory);
        var category = Category.Create("شوینده", null, 0);
        var duplicateAudit = new AuditEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "catalog.category.created",
            "Category",
            category.Id.ToString(),
            null,
            category.Name,
            DateTimeOffset.UtcNow);

        await using (var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
        {
            await new FirebirdCategoryRepository(unitOfWork).AddAsync(category, CancellationToken.None);
            var audit = new FirebirdAuditWriter(unitOfWork);
            await audit.WriteAsync(duplicateAudit, CancellationToken.None);

            await Assert.ThrowsAnyAsync<Exception>(
                () => audit.WriteAsync(duplicateAudit, CancellationToken.None));
        }

        await using var readUnitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var restored = await new FirebirdCategoryRepository(readUnitOfWork)
            .GetByIdAsync(category.Id, CancellationToken.None);

        Assert.Null(restored);
        Assert.Equal(0, await CountAuditEntriesAsync(factory));
    }

    private static Task MigrateAsync(FirebirdConnectionFactory factory)
    {
        return new MigrationRunner(factory, [new V001CreateCatalogAndInventory()])
            .MigrateAsync(CancellationToken.None);
    }

    private static async Task<int> CountAuditEntriesAsync(FirebirdConnectionFactory factory)
    {
        await using var connection = await factory.OpenAsync(CancellationToken.None);
        await using var command = new FbCommand("SELECT COUNT(*) FROM AUDIT_ENTRY", connection);
        var result = await command.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;
}
