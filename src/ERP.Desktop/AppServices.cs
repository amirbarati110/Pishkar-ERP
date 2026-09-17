using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Importing;
using ERP.Infrastructure.Spreadsheets;
using ERP.Persistence.Accounting;
using ERP.Persistence.Backups;
using ERP.Persistence.Cashiering;
using ERP.Persistence.Catalog;
using ERP.Persistence.Customers;
using ERP.Persistence.Database;
using ERP.Persistence.Identity;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;
using ERP.Presentation.Features.Sales;

namespace ERP.Desktop;

public sealed record AppServices(
    FirebirdRetailSetupService RetailSetup,
    FirebirdSalesService Sales,
    ICatalogLookupReader CatalogLookup,
    ISearchProductsHandler ProductSearch,
    SalesBackend SalesBackend,
    RetailSetupDefaults Defaults,
    ISpreadsheetReader SpreadsheetReader,
    FirebirdCashieringService Cashiering,
    FirebirdBackupService Backups,
    string BackupDirectory)
{
    /// <summary>
    /// Everything the app needs before anyone has signed in: the database
    /// connection and migrations, plus the Identity service that decides
    /// «راه‌اندازی اولیه» vs. «ورود» (milestone-1 build order step 4). Split
    /// out from <see cref="Build"/> because that decision — and the sign-in
    /// itself — has to happen before a real <see cref="IUserContext"/> exists
    /// to build the rest of the app with.
    /// </summary>
    public sealed record DatabaseContext(
        FirebirdConnectionFactory Factory,
        FirebirdOptions Options,
        RetailSetupDefaults Defaults,
        IClock Clock,
        FirebirdIdentityService Identity);

    public static async Task<DatabaseContext> InitializeDatabaseAsync(CancellationToken cancellationToken)
    {
        var settings = AppSettings.LoadOrCreateDefault();

        if (!File.Exists(settings.DatabasePath))
        {
            throw new FileNotFoundException(
                "دیتابیس برنامه آماده نشده است. ابزار راه‌اندازی را اجرا کنید.",
                settings.DatabasePath);
        }

        var options = new FirebirdOptions(
            settings.DatabasePath,
            settings.ClientLibraryPath,
            settings.UserName,
            settings.Password);
        var factory = new FirebirdConnectionFactory(options);
        var defaults = await new FirebirdDatabaseBootstrapper(factory)
            .InitializeAsync(cancellationToken);
        var clock = new SystemClock();
        var identity = new FirebirdIdentityService(factory);

        return new DatabaseContext(factory, options, defaults, clock, identity);
    }

    /// <summary>The rest of the app, now that a real signed-in user (§3, §15.3 — no shared/placeholder identity) is known.</summary>
    public static AppServices Build(DatabaseContext database, IUserContext userContext)
    {
        var factory = database.Factory;
        var clock = database.Clock;
        var service = new FirebirdRetailSetupService(factory, userContext, clock);
        var salesService = new FirebirdSalesService(factory, userContext, clock);
        var customerService = new FirebirdCustomerService(factory, userContext, clock);
        var catalogLookup = new FirebirdCatalogLookupReader(factory);
        var productSearch = new SearchProductsHandler(new FirebirdProductSearchReader(factory));

        var salesBackend = new SalesBackend(
            StartSale: salesService,
            AddLine: salesService,
            ChangeLine: salesService,
            RemoveLine: salesService,
            SetCharges: salesService,
            SetCustomer: salesService,
            SetNote: salesService,
            Complete: salesService,
            StartCorrection: salesService,
            CompleteCorrection: salesService,
            Cancel: salesService,
            Details: salesService,
            HeldSales: salesService,
            SalesOfDay: salesService,
            BrowseProducts: salesService,
            ReadProducts: salesService,
            LineEditInfo: salesService,
            SearchProducts: productSearch,
            CatalogLookup: catalogLookup,
            SearchCustomers: customerService,
            QuickCreateCustomer: customerService,
            CustomerAccount: customerService,
            ReceivePayment: customerService,
            VerifyAdminCredential: database.Identity,
            JournalEntry: new FirebirdAccountingService(factory),
            Clock: clock);

        var backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PishkarERP",
            "backups");

        return new AppServices(
            service,
            salesService,
            catalogLookup,
            productSearch,
            salesBackend,
            database.Defaults,
            new OpenXmlSpreadsheetReader(),
            new FirebirdCashieringService(factory, userContext, clock),
            new FirebirdBackupService(factory, database.Options, userContext, clock),
            backupDirectory);
    }

    /// <summary>Who is signed in, for every Application-layer handler's <see cref="IUserContext"/>.</summary>
    public sealed record SignedInUserContext(Guid UserId) : IUserContext;

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
