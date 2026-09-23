using System.Data;
using ERP.Application.Inventory;
using ERP.Domain.Inventory;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Inventory;

/// <summary>Backs «لیست انبارها»: active warehouses, name and address, half-word search on the name.</summary>
public sealed class FirebirdWarehouseListReader : IWarehouseListReader
{
    private readonly FirebirdConnectionFactory _connectionFactory;

    public FirebirdWarehouseListReader(FirebirdConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<WarehouseListRow>> ListAsync(WarehouseListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var where = "WHERE STATUS = 1";
        if (query.Term is not null)
        {
            where += " AND NAME CONTAINING @TERM";
        }

        await using var command = new FbCommand(
            $"SELECT ID, NAME, ADDRESS FROM WAREHOUSE {where} ORDER BY NAME",
            connection)
        {
            CommandType = CommandType.Text,
        };
        if (query.Term is { } term)
        {
            command.Parameters.Add("@TERM", FbDbType.VarChar).Value = term;
        }

        var rows = new List<WarehouseListRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new WarehouseListRow(
                WarehouseId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return rows;
    }
}
