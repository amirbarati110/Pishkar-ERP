using ERP.Application.Audit;
using ERP.Persistence.Audit;
using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Audit;

public sealed class FirebirdAuditWriterTests
{
    [Fact]
    public async Task WritePreservesPersianValuesAndUtcTimestamp()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        await new MigrationRunner(factory, [new V001CreateCatalogAndInventory()])
            .MigrateAsync(CancellationToken.None);
        var occurredAt = new DateTimeOffset(2026, 9, 14, 7, 30, 0, TimeSpan.Zero);
        var entry = new AuditEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "catalog.product.price-changed",
            "Product",
            Guid.NewGuid().ToString("D"),
            "قیمت قبلی: ۱۰۰٬۰۰۰",
            "قیمت جدید: ۱۲۰٬۰۰۰",
            occurredAt);

        await using (var unitOfWork = await FirebirdUnitOfWork.CreateAsync(factory, CancellationToken.None))
        {
            await new FirebirdAuditWriter(unitOfWork).WriteAsync(entry, CancellationToken.None);
            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var connection = await factory.OpenAsync(CancellationToken.None);
        await using var command = new FbCommand(
            "SELECT OLD_VALUE, NEW_VALUE, OCCURRED_AT_UTC FROM AUDIT_ENTRY WHERE ID = @ID",
            connection);
        command.Parameters.Add("@ID", FbDbType.Char).Value = entry.Id.ToString("D");
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(entry.OldValue, reader.GetString(0));
        Assert.Equal(entry.NewValue, reader.GetString(1));
        Assert.Equal(occurredAt.UtcDateTime, DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc));
    }
}
