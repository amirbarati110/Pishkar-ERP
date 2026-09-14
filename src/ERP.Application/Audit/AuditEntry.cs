namespace ERP.Application.Audit;

public sealed record AuditEntry(
    Guid Id,
    Guid ActorUserId,
    string Action,
    string EntityType,
    string EntityId,
    string? OldValue,
    string? NewValue,
    DateTimeOffset OccurredAtUtc);

