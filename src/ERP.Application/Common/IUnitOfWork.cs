namespace ERP.Application.Common;

public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken);
}

