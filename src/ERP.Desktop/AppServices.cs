using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Persistence.Catalog;
using ERP.Persistence.Database;
using ERP.Persistence.Sales;
using ERP.Persistence.Services;

namespace ERP.Desktop;

public sealed record AppServices(
    FirebirdRetailSetupService RetailSetup,
    FirebirdSalesService Sales,
    ICatalogLookupReader CatalogLookup,
    ISearchProductsHandler ProductSearch,
    RetailSetupDefaults Defaults)
{
    public static async Task<AppServices> InitializeAsync(CancellationToken cancellationToken)
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
        var userContext = new PilotUserContext(Guid.Parse("44444444-4444-4444-8444-444444444444"));
        var clock = new SystemClock();
        var service = new FirebirdRetailSetupService(factory, userContext, clock);
        var salesService = new FirebirdSalesService(factory, userContext, clock);

        return new AppServices(
            service,
            salesService,
            new FirebirdCatalogLookupReader(factory),
            new SearchProductsHandler(new FirebirdProductSearchReader(factory)),
            defaults);
    }

    private sealed record PilotUserContext(Guid UserId) : IUserContext;

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
