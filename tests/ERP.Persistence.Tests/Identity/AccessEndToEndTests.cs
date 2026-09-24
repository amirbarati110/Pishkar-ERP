using System.Globalization;
using ERP.Application.Backups;
using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Customers;
using ERP.Application.Identity;
using ERP.Application.Inventory;
using ERP.Application.Sales;
using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Customers;
using ERP.Domain.Identity;
using ERP.Domain.Sales;
using ERP.Persistence.Backups;
using ERP.Persistence.Customers;
using ERP.Persistence.Database;
using ERP.Persistence.Identity;
using ERP.Persistence.Inventory;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Identity;

/// <summary>
/// Who may do what (user decision 1405/07/02), end to end on a real Firebird with the real
/// <see cref="FirebirdAccessChecker"/>: the manager-only work refused to a cashier, the four
/// switches a manager turns on and off, and a switch turned off biting on the very next call.
/// </summary>
public sealed class AccessEndToEndTests
{
    private const string Password = "PassWord123";

    [Fact]
    public async Task ACashierSellsButTheManagersWorkIsRefusedOnTheServer()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);
        c.Actor.UserId = c.CashierId.Value;

        var product = await c.Setup.ExecuteAsync(
            new CreateProductCommand("روغن", "OIL-1", c.CategoryId, c.Defaults.EachUnitId, Money.FromTomans(100_000), []),
            CancellationToken.None);
        var stock = await c.Setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(c.RiceId, c.Defaults.MainWarehouseId, 5, 2_000_000, new DateOnly(2026, 9, 1)),
            CancellationToken.None);
        var warehouse = await c.Setup.ExecuteAsync(new CreateWarehouseCommand("انبار دوم", null), CancellationToken.None);
        var backup = await c.Backups.ExecuteAsync(new CreateBackupCommand(Path.GetTempPath()), CancellationToken.None);
        var users = await c.Identity.ExecuteAsync(new ListUsersQuery(c.CashierId), CancellationToken.None);

        Assert.Equal("identity.access.denied", product.Error?.Code);
        Assert.Equal("identity.access.denied", stock.Error?.Code);
        Assert.Equal("identity.access.denied", warehouse.Error?.Code);
        Assert.Equal("identity.access.denied", backup.Error?.Code);
        Assert.Equal("identity.access.denied", users.Error?.Code);

        // the till's own work needs no switch
        var sale = await CashSaleAsync(c);
        Assert.NotEqual(default, sale);

        // a sale's price is the cashier's to change; the product's master price is not
        var draft = (await c.Sales.ExecuteAsync(new StartSaleCommand(c.Defaults.MainWarehouseId), CancellationToken.None)).Value;
        await c.Sales.ExecuteAsync(new AddSaleLineCommand(draft, c.RiceId, 1), CancellationToken.None);
        var linePrice = await c.Sales.ExecuteAsync(
            new ChangeSaleLineCommand(draft, c.RiceId, 1, 2_300_000, 0), CancellationToken.None);
        var masterPrice = await c.Sales.ExecuteAsync(
            new ChangeSaleLineCommand(draft, c.RiceId, 1, 2_300_000, 0, UpdateCatalogPrice: true), CancellationToken.None);
        Assert.True(linePrice.IsSuccess, linePrice.Error?.Message);
        Assert.Equal("identity.access.denied", masterPrice.Error?.Code);
        Assert.Equal(2_450_000L, await ScalarAsync<long>(c, "SELECT SALE_PRICE_RIALS FROM PRODUCT WHERE SKU = 'RICE-1'"));
    }

    [Fact]
    public async Task EachSwitchAManagerTurnsOffIsRefusedOnTheNextCall()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);
        c.Actor.UserId = c.CashierId.Value;
        var sale = await CashSaleAsync(c);

        // everything on (the default): returns, corrections, cost and the customer form all work
        Assert.True((await c.Sales.ExecuteAsync(new GetReturnableSaleQuery(sale), CancellationToken.None)).IsSuccess);
        var info = await c.Sales.ExecuteAsync(new GetLineEditInfoQuery(c.Defaults.MainWarehouseId, c.RiceId, null), CancellationToken.None);
        Assert.NotNull(info.CurrentCost);
        Assert.NotNull((await c.StockCard.ExecuteAsync(new StockCardQuery(c.RiceId), CancellationToken.None))!.ClosingValue);
        var first = await c.Customers.ExecuteAsync(FullCustomer("مشتری اول", "09120000001"), CancellationToken.None);
        Assert.True(first.IsSuccess, first.Error?.Message);

        // the manager turns all four off — while the cashier is still signed in
        c.Actor.UserId = c.AdminId.Value;
        var off = await c.Identity.ExecuteAsync(
            new UpdateUserCommand(c.AdminId, c.CashierId, "سارا", UserRole.Cashier, CashierPermissions.None), CancellationToken.None);
        Assert.True(off.IsSuccess, off.Error?.Message);
        c.Actor.UserId = c.CashierId.Value;

        var returnable = await c.Sales.ExecuteAsync(new GetReturnableSaleQuery(sale), CancellationToken.None);
        var saleReturn = await c.Sales.ExecuteAsync(
            new CompleteSaleReturnCommand(sale, [new ReturnLineRequest(c.RiceId, 1, ReturnDisposition.ToStock)], PaymentMethod.Cash, "نخواست"),
            CancellationToken.None);
        var correction = await c.Sales.ExecuteAsync(new StartSaleCorrectionCommand(sale, "اشتباه"), CancellationToken.None);
        var customer = await c.Customers.ExecuteAsync(FullCustomer("مشتری دوم", "09120000002"), CancellationToken.None);
        var quick = await c.Customers.ExecuteAsync(new QuickCreateCustomerCommand("مشتری سریع", "09120000003"), CancellationToken.None);
        var hiddenInfo = await c.Sales.ExecuteAsync(new GetLineEditInfoQuery(c.Defaults.MainWarehouseId, c.RiceId, null), CancellationToken.None);
        var hiddenCard = await c.StockCard.ExecuteAsync(new StockCardQuery(c.RiceId), CancellationToken.None);

        Assert.Equal("identity.access.denied", returnable.Error?.Code);
        Assert.Equal("identity.access.denied", saleReturn.Error?.Code);
        Assert.Equal("identity.access.denied", correction.Error?.Code);
        Assert.Equal("identity.access.denied", customer.Error?.Code);
        Assert.True(quick.IsSuccess, quick.Error?.Message); // a name and mobile at the till is still the till's work
        Assert.Null(hiddenInfo.CurrentCost);
        Assert.Null(hiddenInfo.LastPurchaseCost);
        Assert.Null(hiddenCard!.ClosingValue);
        Assert.All(hiddenCard.Entries, entry => Assert.Null(entry.Value));
        Assert.Equal(1, hiddenCard.Entries.Count(entry => entry.Out > 0)); // quantities stay
        Assert.Equal(0, await ScalarAsync<int>(c, "SELECT COUNT(*) FROM SALE_RETURN"));
    }

    [Fact]
    public async Task PermissionsAndRoleChangesSurviveInTheDatabaseAndAreAudited()
    {
        await using var database = FirebirdTestDatabase.Create();
        var c = await SetupAsync(database);

        await c.Identity.ExecuteAsync(
            new UpdateUserCommand(c.AdminId, c.CashierId, "سارا احمدی", UserRole.Cashier,
                CashierPermissions.ReturnsAndExchange | CashierPermissions.EditCustomers),
            CancellationToken.None);
        await c.Identity.ExecuteAsync(new ResetUserPasswordCommand(c.AdminId, c.CashierId, "NewPass4567"), CancellationToken.None);

        var signedIn = await c.Identity.ExecuteAsync(new SignInCommand("sara", "NewPass4567"), CancellationToken.None);
        Assert.True(signedIn.IsSuccess, signedIn.Error?.Message);
        Assert.Equal(CashierPermissions.ReturnsAndExchange | CashierPermissions.EditCustomers, signedIn.Value!.Permissions);
        Assert.Equal("سارا احمدی", signedIn.Value.DisplayName);
        Assert.False(signedIn.Value.Can(AccessRight.CorrectPostedInvoices));

        var list = await c.Identity.ExecuteAsync(new ListUsersQuery(c.AdminId), CancellationToken.None);
        Assert.Equal(["سارا احمدی", "مدیر فروشگاه"], list.Value!.Select(row => row.DisplayName).ToArray());

        var lastAdmin = await c.Identity.ExecuteAsync(
            new UpdateUserCommand(c.AdminId, c.AdminId, "مدیر فروشگاه", UserRole.Cashier, CashierPermissions.All), CancellationToken.None);
        Assert.Equal("identity.user.last-admin", lastAdmin.Error?.Code);

        var archived = await c.Identity.ExecuteAsync(new ArchiveUserCommand(c.AdminId, c.CashierId), CancellationToken.None);
        Assert.True(archived.IsSuccess, archived.Error?.Message);
        Assert.False((await c.Identity.ExecuteAsync(new SignInCommand("sara", "NewPass4567"), CancellationToken.None)).IsSuccess);

        Assert.Equal(
            ["identity.user.archived", "identity.user.created", "identity.user.password-reset", "identity.user.updated"],
            await ActionsAsync(c));
    }

    private static CreateCustomerCommand FullCustomer(string name, string mobile) => new(
        new CustomerProfileInput(CustomerKind.Individual, name, "رضایی", null, mobile, null, null, null, null, null, null, null, null, null),
        Money.Zero,
        Money.Zero);

    private static async Task<SaleId> CashSaleAsync(Context c)
    {
        var sale = (await c.Sales.ExecuteAsync(new StartSaleCommand(c.Defaults.MainWarehouseId), CancellationToken.None)).Value;
        await c.Sales.ExecuteAsync(new AddSaleLineCommand(sale, c.RiceId, 1), CancellationToken.None);
        var done = await c.Sales.ExecuteAsync(new CompleteSaleCommand(sale, PaymentMethod.Cash, 0, 0), CancellationToken.None);
        Assert.True(done.IsSuccess, done.Error?.Message);
        return sale;
    }

    private static async Task<T> ScalarAsync<T>(Context c, string sql)
    {
        await using var connection = await c.Factory.OpenAsync(CancellationToken.None);
        await using var command = new FbCommand(sql, connection);
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync(CancellationToken.None), typeof(T), CultureInfo.InvariantCulture)!;
    }

    private static async Task<string[]> ActionsAsync(Context c)
    {
        await using var connection = await c.Factory.OpenAsync(CancellationToken.None);
        await using var command = new FbCommand(
            "SELECT DISTINCT ACTION_NAME FROM AUDIT_ENTRY WHERE ACTION_NAME STARTING WITH 'identity.user.' ORDER BY 1", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var actions = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            actions.Add(reader.GetString(0));
        }

        return [.. actions];
    }

    private static async Task<Context> SetupAsync(FirebirdTestDatabase database)
    {
        var factory = new FirebirdConnectionFactory(database.Options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory).InitializeAsync(CancellationToken.None);
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero) };
        var identity = new FirebirdIdentityService(factory, clock);
        var admin = (await identity.ExecuteAsync(new RegisterFirstAdminCommand("owner", "مدیر فروشگاه", Password), CancellationToken.None)).Value;
        var cashier = await identity.ExecuteAsync(
            new CreateUserCommand(admin, "sara", "سارا", Password, UserRole.Cashier), CancellationToken.None);
        Assert.True(cashier.IsSuccess, cashier.Error?.Message);

        var actor = new SwitchableUser { UserId = admin.Value };
        var access = new FirebirdAccessChecker(factory, actor);
        var setup = new FirebirdRetailSetupService(factory, actor, clock, access);
        var category = await setup.ExecuteAsync(new CreateCategoryCommand("مواد غذایی", null, 1), CancellationToken.None);
        var rice = await setup.ExecuteAsync(
            new CreateProductCommand("برنج", "RICE-1", category.Value, defaults.EachUnitId, Money.FromTomans(245_000), []),
            CancellationToken.None);
        var stocked = await setup.ExecuteAsync(
            new ReceiveOpeningStockCommand(rice.Value, defaults.MainWarehouseId, 20, 2_000_000, new DateOnly(2026, 9, 1)),
            CancellationToken.None);
        Assert.True(stocked.IsSuccess, stocked.Error?.Message);

        return new Context(
            factory,
            defaults,
            actor,
            admin,
            cashier.Value,
            category.Value,
            rice.Value,
            identity,
            setup,
            new FirebirdSalesService(factory, actor, clock, access),
            new FirebirdCustomerService(factory, actor, clock, access),
            new FirebirdBackupService(factory, database.Options, actor, clock, access),
            new CostAwareStockCardHandler(new GetStockCardHandler(new FirebirdStockCardReader(factory)), access));
    }

    private sealed record Context(
        FirebirdConnectionFactory Factory,
        RetailSetupDefaults Defaults,
        SwitchableUser Actor,
        UserId AdminId,
        UserId CashierId,
        CategoryId CategoryId,
        ProductId RiceId,
        FirebirdIdentityService Identity,
        FirebirdRetailSetupService Setup,
        FirebirdSalesService Sales,
        FirebirdCustomerService Customers,
        FirebirdBackupService Backups,
        CostAwareStockCardHandler StockCard);

    /// <summary>One PC, two people taking turns at it — the services stay, the signed-in user changes.</summary>
    private sealed class SwitchableUser : IUserContext
    {
        public Guid UserId { get; set; }
    }

    private sealed class TestClock : IClock
    {
        public required DateTimeOffset UtcNow { get; set; }
    }
}
