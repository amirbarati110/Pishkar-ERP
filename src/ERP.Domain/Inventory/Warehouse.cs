using ERP.Domain.Common;

namespace ERP.Domain.Inventory;

/// <summary>
/// A physical stock location (checklist «س»): what «لیست انبارها» lists, and what every sale,
/// cash shift, opening-stock receipt and stock movement is recorded against. Until this existed,
/// the whole app pointed at one hard-coded id with no name behind it (`FirebirdDatabaseBootstrapper`'s
/// seeded «فروشگاه مرکزی» row is what a fresh install still gets — this only makes it a real,
/// manageable row instead of a constant nobody could see or add to).
/// </summary>
public sealed class Warehouse : Entity<WarehouseId>
{
    private const int MaximumNameLength = 120;
    private const int MaximumAddressLength = 500;

    private Warehouse(WarehouseId id, string name, string? address)
        : base(id)
    {
        Name = name;
        Address = address;
        Status = WarehouseStatus.Active;
    }

    public string Name { get; private set; }

    public string? Address { get; private set; }

    public WarehouseStatus Status { get; private set; }

    public static Warehouse Create(string name, string? address) =>
        new(WarehouseId.New(), NormalizeName(name), NormalizeAddress(address));

    internal static Warehouse Rehydrate(WarehouseId id, string name, string? address, WarehouseStatus status) =>
        new(id, name, address)
        {
            Status = status,
        };

    public void Update(string name, string? address)
    {
        // both checked before either is assigned, so a rejected edit leaves the warehouse as it was
        var normalizedName = NormalizeName(name);
        var normalizedAddress = NormalizeAddress(address);
        Name = normalizedName;
        Address = normalizedAddress;
    }

    public void Archive() => Status = WarehouseStatus.Archived;

    /// <summary>The name exactly as it will be stored; throws the same message <see cref="Create"/> would.</summary>
    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("نام انبار را وارد کنید.");
        }

        var normalized = name.Trim();
        if (normalized.Length > MaximumNameLength)
        {
            throw new DomainException("نام انبار نمی‌تواند بیشتر از ۱۲۰ نویسه باشد.");
        }

        return normalized;
    }

    private static string? NormalizeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var normalized = address.Trim();
        if (normalized.Length > MaximumAddressLength)
        {
            throw new DomainException("نشانی انبار نمی‌تواند بیشتر از ۵۰۰ نویسه باشد.");
        }

        return normalized;
    }
}
