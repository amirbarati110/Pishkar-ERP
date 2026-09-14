using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Database;

public sealed class FirebirdConnectionFactory
{
    private readonly FirebirdOptions _options;

    public FirebirdConnectionFactory(FirebirdOptions options)
    {
        _options = options;
    }

    public async Task<FbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new FbConnection(_options.BuildConnectionString());

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

