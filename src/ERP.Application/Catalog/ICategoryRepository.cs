using ERP.Domain.Catalog;

namespace ERP.Application.Catalog;

public interface ICategoryRepository
{
    Task<bool> SiblingNameExistsAsync(
        string name,
        CategoryId? parentId,
        CancellationToken cancellationToken);

    Task AddAsync(Category category, CancellationToken cancellationToken);
}

