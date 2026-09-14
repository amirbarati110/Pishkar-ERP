using ERP.Application.Audit;
using ERP.Application.Common;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Catalog;

public sealed record CreateCategoryCommand(string Name, CategoryId? ParentId, int SortOrder);

public sealed class CreateCategoryHandler
{
    private readonly ICategoryRepository _categories;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CreateCategoryHandler(
        ICategoryRepository categories,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _categories = categories;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<CategoryId>> ExecuteAsync(
        CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var category = Category.Create(command.Name, command.ParentId, command.SortOrder);

            if (await _categories.SiblingNameExistsAsync(
                    category.Name,
                    category.ParentId,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return Result.Failure<CategoryId>(
                    "catalog.category.duplicate-name",
                    "در این سطح، دسته‌بندی دیگری با همین نام وجود دارد.");
            }

            await _categories.AddAsync(category, cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(
                    Guid.NewGuid(),
                    _userContext.UserId,
                    "catalog.category.created",
                    nameof(Category),
                    category.Id.ToString(),
                    null,
                    category.Name,
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(category.Id);
        }
        catch (DomainException exception)
        {
            return Result.Failure<CategoryId>("catalog.category.invalid", exception.Message);
        }
        catch (DataConflictException exception)
        {
            return Result.Failure<CategoryId>(exception.Code, exception.Message);
        }
    }
}
