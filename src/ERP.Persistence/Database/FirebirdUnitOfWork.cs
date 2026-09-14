using ERP.Application.Common;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Database;

public sealed class FirebirdUnitOfWork : IUnitOfWork, IAsyncDisposable
{
    private bool _committed;
    private bool _disposed;

    private FirebirdUnitOfWork(FbConnection connection, FbTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
    }

    internal FbConnection Connection { get; }

    internal FbTransaction Transaction { get; }

    public static async Task<FirebirdUnitOfWork> CreateAsync(
        FirebirdConnectionFactory connectionFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var transaction = (FbTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            return new FirebirdUnitOfWork(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_committed)
        {
            throw new InvalidOperationException("این تراکنش قبلاً ثبت نهایی شده است.");
        }

        await Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (!_committed)
            {
                await Transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            await Transaction.DisposeAsync().ConfigureAwait(false);
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
