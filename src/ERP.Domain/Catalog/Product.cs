using ERP.Domain.Common;

namespace ERP.Domain.Catalog;

public sealed class Product : Entity<ProductId>
{
    private const int MaximumNameLength = 200;
    private const int MaximumSkuLength = 64;
    private readonly List<ProductBarcode> _barcodes = [];

    private Product(
        ProductId id,
        string name,
        string? sku,
        CategoryId categoryId,
        UnitId baseUnitId,
        Money salePrice)
        : base(id)
    {
        Name = name;
        Sku = sku;
        CategoryId = categoryId;
        BaseUnitId = baseUnitId;
        SalePrice = salePrice;
        Status = ProductStatus.Active;
    }

    public string Name { get; private set; }

    public string? Sku { get; private set; }

    public CategoryId CategoryId { get; private set; }

    public UnitId BaseUnitId { get; private set; }

    public Money SalePrice { get; private set; }

    public ProductStatus Status { get; private set; }

    public IReadOnlyList<ProductBarcode> Barcodes => _barcodes.AsReadOnly();

    public static Product Create(
        string name,
        string? sku,
        CategoryId categoryId,
        UnitId baseUnitId,
        Money salePrice)
    {
        return new Product(
            ProductId.New(),
            NormalizeName(name),
            NormalizeSku(sku),
            categoryId,
            baseUnitId,
            salePrice);
    }

    internal static Product Rehydrate(
        ProductId id,
        string name,
        string? sku,
        CategoryId categoryId,
        UnitId baseUnitId,
        Money salePrice,
        ProductStatus status,
        IEnumerable<string> barcodes)
    {
        var product = new Product(
            id,
            NormalizeName(name),
            NormalizeSku(sku),
            categoryId,
            baseUnitId,
            salePrice);
        product.Status = status;

        foreach (var barcode in barcodes)
        {
            product.AddBarcode(barcode);
        }

        return product;
    }

    public void AddBarcode(string barcode)
    {
        var candidate = ProductBarcode.Create(barcode);

        if (_barcodes.Contains(candidate))
        {
            throw new DomainException("این بارکد قبلاً برای کالا ثبت شده است.");
        }

        _barcodes.Add(candidate);
    }

    public void ChangePrice(Money salePrice)
    {
        SalePrice = salePrice;
    }

    /// <summary>
    /// Edits what a user may change on an existing product (checklist «م»). The base
    /// unit and the barcodes are deliberately not here: stock is counted in the base
    /// unit, so changing it under existing layers would silently change quantities,
    /// and barcodes are append-only with their own uniqueness rules.
    /// </summary>
    public void Update(string name, string? sku, CategoryId categoryId, Money salePrice)
    {
        Name = NormalizeName(name);
        Sku = NormalizeSku(sku);
        CategoryId = categoryId;
        SalePrice = salePrice;
    }

    public void Archive()
    {
        Status = ProductStatus.Archived;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("نام کالا را وارد کنید.");
        }

        var normalized = name.Trim();

        if (normalized.Length > MaximumNameLength)
        {
            throw new DomainException("نام کالا نمی‌تواند بیشتر از ۲۰۰ نویسه باشد.");
        }

        return normalized;
    }

    private static string? NormalizeSku(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return null;
        }

        var normalized = sku.Trim().ToUpperInvariant();

        if (normalized.Length > MaximumSkuLength)
        {
            throw new DomainException("کد کالا نمی‌تواند بیشتر از ۶۴ نویسه باشد.");
        }

        return normalized;
    }
}
