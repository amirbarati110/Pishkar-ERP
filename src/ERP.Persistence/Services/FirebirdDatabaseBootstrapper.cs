using ERP.Domain.Catalog;
using ERP.Domain.Inventory;
using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Services;

public sealed class FirebirdDatabaseBootstrapper
{
    private static readonly CategoryId GeneralCategoryId =
        CategoryId.From(Guid.Parse("11111111-1111-4111-8111-111111111111"));
    private static readonly UnitId EachUnitId =
        UnitId.From(Guid.Parse("22222222-2222-4222-8222-222222222222"));
    internal static readonly WarehouseId MainWarehouseId =
        WarehouseId.From(Guid.Parse("33333333-3333-4333-8333-333333333333"));

    /// <summary>Exposed so a health check can compare "what the code expects" against "what the database's own <c>SCHEMA_MIGRATIONS</c> table says was applied" without hardcoding the count twice.</summary>
    public static readonly IReadOnlyList<IMigration> Migrations =
    [
        new V001CreateCatalogAndInventory(),
        new V002CreateSales(),
        new V003SaleLineDiscountAndServiceCharge(),
        new V004SaleNumber(),
        new V005CreateCustomers(),
        new V006CustomerAccount(),
        new V007SaleNote(),
        new V008SaleCorrection(),
        new V009CreateAppUser(),
        new V010CreateCashShift(),
        new V011CreateJournalEntry(),
        new V012CreateBackupRecord(),
        new V013SaleLineProductIndex(),
        new V014SaleReturns(),
        new V015CustomerListIndexes(),
        new V016CustomerProfile(),
        new V017CustomerProfileBackfill(),
        new V018CustomerCodeRequired(),
        new V019InternalBarcodeSequence(),
        new V020CreateWarehouse(),
        new V021SeedMainWarehouse(),
        new V022WarehouseForeignKeys(),
    ];

    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdDatabaseBootstrapper(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<RetailSetupDefaults> InitializeAsync(CancellationToken cancellationToken)
    {
        await new MigrationRunner(_connectionFactory, Migrations)
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var unitOfWork = await FirebirdUnitOfWork
            .CreateAsync(_connectionFactory, cancellationToken)
            .ConfigureAwait(false);
        await SeedGeneralCategoryAsync(unitOfWork, cancellationToken).ConfigureAwait(false);
        await SeedEachUnitAsync(unitOfWork, cancellationToken).ConfigureAwait(false);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new RetailSetupDefaults(GeneralCategoryId, EachUnitId, MainWarehouseId);
    }

    private static async Task SeedGeneralCategoryAsync(
        FirebirdUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand(
            """
            INSERT INTO CATEGORY (
                ID, PARENT_ID, PARENT_KEY, NAME, SORT_ORDER, STATUS,
                VIS_POS, VIS_ONLINE, VIS_PURCHASING)
            SELECT
                @ID, NULL, '00000000-0000-0000-0000-000000000000',
                'عمومی', 0, 1, TRUE, TRUE, TRUE
            FROM RDB$DATABASE
            WHERE NOT EXISTS (SELECT 1 FROM CATEGORY WHERE ID = @ID)
            """,
            unitOfWork.Connection,
            unitOfWork.Transaction);
        command.Parameters.Add("@ID", FbDbType.Char).Value = GeneralCategoryId.ToString();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedEachUnitAsync(
        FirebirdUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand(
            """
            INSERT INTO PRODUCT_UNIT (ID, NAME, SYMBOL, ALLOWS_FRACTIONS)
            SELECT @ID, 'عدد', 'عدد', FALSE
            FROM RDB$DATABASE
            WHERE NOT EXISTS (SELECT 1 FROM PRODUCT_UNIT WHERE ID = @ID)
            """,
            unitOfWork.Connection,
            unitOfWork.Transaction);
        command.Parameters.Add("@ID", FbDbType.Char).Value = EachUnitId.ToString();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
