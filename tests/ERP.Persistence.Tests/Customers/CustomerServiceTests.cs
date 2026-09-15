using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Domain.Customers;
using ERP.Persistence.Customers;
using ERP.Persistence.Database;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Customers;

public sealed class CustomerServiceTests
{
    [Fact]
    public async Task QuickCreatedCustomerSurvivesARoundTripWithItsMobileNormalized()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (factory, service) = await SetupAsync(database);

        var created = await service.ExecuteAsync(
            new QuickCreateCustomerCommand("محمد رضایی", "۰۹۱۲ ۳۴۵ ۶۷۸۹"),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        await using var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var reloaded = await new FirebirdCustomerRepository(unitOfWork)
            .GetByIdAsync(created.Value, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal("محمد رضایی", reloaded!.Name);
        Assert.Equal("09123456789", reloaded.Mobile);
        Assert.Equal(CustomerStatus.Active, reloaded.Status);
    }

    [Fact]
    public async Task TheSameMobileCannotBeRegisteredTwice()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (_, service) = await SetupAsync(database);
        await service.ExecuteAsync(
            new QuickCreateCustomerCommand("محمد رضایی", "09123456789"),
            CancellationToken.None);

        var duplicate = await service.ExecuteAsync(
            new QuickCreateCustomerCommand("رضایی", "۰۹۱۲۳۴۵۶۷۸۹"),
            CancellationToken.None);

        Assert.False(duplicate.IsSuccess);
        Assert.Equal("customers.customer.duplicate-mobile", duplicate.Error?.Code);
    }

    [Fact]
    public async Task TheDatabaseConstraintStillCatchesADuplicateThatSlipsPastTheCheck()
    {
        // Simulates two tills: both pass the "does this number exist?" check,
        // then both insert. The second insert must fail as a clean conflict.
        await using var database = FirebirdTestDatabase.Create();
        var (factory, _) = await SetupAsync(database);

        await using (var first = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
        {
            await new FirebirdCustomerRepository(first).AddAsync(
                Customer.QuickCreate("اولی", "09120000000"), CancellationToken.None);
            await first.CommitAsync(CancellationToken.None);
        }

        await using var second = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None);
        var conflict = await Assert.ThrowsAsync<DataConflictException>(() =>
            new FirebirdCustomerRepository(second).AddAsync(
                Customer.QuickCreate("دومی", "09120000000"), CancellationToken.None));

        Assert.Equal("customers.customer.duplicate-mobile", conflict.Code);
    }

    [Fact]
    public async Task SearchRanksPrefixMatchesFirstAndFindsByPersianMobileDigits()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (_, service) = await SetupAsync(database);
        await service.ExecuteAsync(new QuickCreateCustomerCommand("حسین علیزاده", "09120000001"), CancellationToken.None);
        await service.ExecuteAsync(new QuickCreateCustomerCommand("علی محمدی", "09350000002"), CancellationToken.None);
        await service.ExecuteAsync(new QuickCreateCustomerCommand("مریم کریمی", "09121234567"), CancellationToken.None);

        var byName = await service.ExecuteAsync(new SearchCustomersQuery("علی"), CancellationToken.None);
        Assert.Equal(["علی محمدی", "حسین علیزاده"], byName.Select(result => result.Name));

        var byMobile = await service.ExecuteAsync(new SearchCustomersQuery("۱۲۳۴"), CancellationToken.None);
        Assert.Equal("مریم کریمی", Assert.Single(byMobile).Name);
    }

    [Fact]
    public async Task SearchLeavesOutArchivedCustomers()
    {
        await using var database = FirebirdTestDatabase.Create();
        var (factory, service) = await SetupAsync(database);
        var created = await service.ExecuteAsync(
            new QuickCreateCustomerCommand("علی بایگانی", "09129999999"),
            CancellationToken.None);

        await using (var connection = await factory.OpenAsync(CancellationToken.None))
        await using (var archive = new FbCommand("UPDATE CUSTOMER SET STATUS = 2 WHERE ID = @ID", connection))
        {
            archive.Parameters.AddWithValue("@ID", created.Value.ToString());
            await archive.ExecuteNonQueryAsync();
        }

        var results = await service.ExecuteAsync(new SearchCustomersQuery("علی"), CancellationToken.None);

        Assert.Empty(results);
    }

    private static async Task<(FirebirdConnectionFactory Factory, FirebirdCustomerService Service)> SetupAsync(
        FirebirdTestDatabase database)
    {
        var factory = new FirebirdConnectionFactory(database.Options);
        await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var service = new FirebirdCustomerService(
            factory,
            new TestUserContext(Guid.NewGuid()),
            new TestClock(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero)));
        return (factory, service);
    }

    private sealed record TestUserContext(Guid UserId) : IUserContext;

    private sealed record TestClock(DateTimeOffset UtcNow) : IClock;
}
