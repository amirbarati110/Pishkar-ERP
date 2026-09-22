using ERP.Persistence.Database;
using ERP.Persistence.Migrations;
using ERP.Persistence.Services;
using ERP.Persistence.Tests.TestSupport;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Tests.Migrations;

/// <summary>
/// The upgrade path of «لیست مشتریان»: customers created by the old app version (name + mobile
/// only) must come out of V016–V018 with a first name, kind «حقیقی» and their own customer number.
/// </summary>
public sealed class CustomerProfileMigrationTests
{
    [Fact]
    public async Task ExistingCustomersGetTheirNameAsFirstNameAKindAndUniqueCustomerNumbers()
    {
        await using var database = FirebirdTestDatabase.Create();
        var factory = new FirebirdConnectionFactory(database.Options);
        var all = FirebirdDatabaseBootstrapper.Migrations;

        // the database as it was before the customer record grew
        await new MigrationRunner(factory, all.Where(migration => migration.Version <= 15).ToArray())
            .MigrateAsync(CancellationToken.None);

        await using (var connection = await factory.OpenAsync(CancellationToken.None))
        {
            foreach (var (id, name, mobile) in new[]
            {
                ("11111111-1111-1111-1111-111111111111", "محمد رضایی", "09121111111"),
                ("22222222-2222-2222-2222-222222222222", "زهرا کریمی", "09122222222"),
                ("33333333-3333-3333-3333-333333333333", "علی", "09123333333"),
            })
            {
                await using var insert = new FbCommand(
                    "INSERT INTO CUSTOMER (ID, NAME, MOBILE, STATUS) VALUES (@ID, @NAME, @MOBILE, 1)", connection);
                insert.Parameters.Add("@ID", FbDbType.Char).Value = id;
                insert.Parameters.Add("@NAME", FbDbType.VarChar).Value = name;
                insert.Parameters.Add("@MOBILE", FbDbType.Char).Value = mobile;
                await insert.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }

        await new MigrationRunner(factory, all).MigrateAsync(CancellationToken.None);

        await using var read = await factory.OpenAsync(CancellationToken.None);
        await using var select = new FbCommand(
            "SELECT NAME, FIRST_NAME, LAST_NAME, KIND, CODE, NATIONAL_ID FROM CUSTOMER ORDER BY CODE", read);
        await using var reader = await select.ExecuteReaderAsync(CancellationToken.None);

        var codes = new List<long>();
        var names = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            names.Add(reader.GetString(0));
            Assert.Equal(reader.GetString(0), reader.GetString(1)); // the single name became the first name
            Assert.True(reader.IsDBNull(2));
            Assert.Equal(1, reader.GetInt16(3));                     // شخص حقیقی
            Assert.True(reader.IsDBNull(5));
            codes.Add(reader.GetInt64(4));
        }

        Assert.Equal(3, codes.Count);
        Assert.Equal(3, codes.Distinct().Count());
        Assert.Equal([1L, 2L, 3L], codes);
        Assert.Equal(["زهرا کریمی", "علی", "محمد رضایی"], names.Order().ToArray());
    }
}
