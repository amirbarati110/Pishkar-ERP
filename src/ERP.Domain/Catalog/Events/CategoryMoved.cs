using ERP.Domain.Common;

namespace ERP.Domain.Catalog.Events;

public sealed record CategoryMoved(
    CategoryId CategoryId,
    CategoryId? OldParentId,
    CategoryId? NewParentId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

