using ERP.Domain.Common;
using ERP.Domain.Inventory;

namespace ERP.Domain.Sales.Events;

/// <summary>
/// Raised when a Sale transitions from Draft to Completed. This is the contract
/// other modules (Accounting, BI, Loyalty) react to instead of reaching into the
/// Sales aggregate directly (source-of-truth rule #19: cross-module interaction
/// uses a Contract/Event). No subscriber exists yet — Accounting/Loyalty are
/// unbuilt — so this event is currently only observable via
/// <see cref="ERP.Domain.Common.Entity{TId}.DomainEvents"/> on the aggregate.
/// </summary>
public sealed record SaleCompleted(
    SaleId SaleId,
    WarehouseId WarehouseId,
    Customers.CustomerId? CustomerId,
    Money Total,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
