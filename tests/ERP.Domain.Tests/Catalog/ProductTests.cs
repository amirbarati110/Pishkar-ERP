using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Domain.Tests.Catalog;

public sealed class ProductTests
{
    [Fact]
    public void CreateRequiresName()
    {
        var exception = Assert.Throws<DomainException>(() => Product.Create(
            " ",
            null,
            CategoryId.New(),
            UnitId.New(),
            Money.FromTomans(10_000)));

        Assert.Equal("نام کالا را وارد کنید.", exception.Message);
    }

    [Fact]
    public void CreateAllowsMissingSkuAndNormalizesName()
    {
        var product = Product.Create(
            "  گردو ایرانی  ",
            null,
            CategoryId.New(),
            UnitId.New(),
            Money.FromTomans(500_000));

        Assert.Equal("گردو ایرانی", product.Name);
        Assert.Null(product.Sku);
    }

    [Fact]
    public void AddBarcodeRejectsDuplicateAfterNormalization()
    {
        var product = CreateProduct();
        product.AddBarcode(" 6260000000012 ");

        var exception = Assert.Throws<DomainException>(() => product.AddBarcode("6260000000012"));

        Assert.Equal("این بارکد قبلاً برای کالا ثبت شده است.", exception.Message);
    }

    [Fact]
    public void AddBarcodeRejectsWhitespaceInsideCode()
    {
        var product = CreateProduct();

        var exception = Assert.Throws<DomainException>(() => product.AddBarcode("6260 0001"));

        Assert.Equal("بارکد نمی‌تواند فاصله داشته باشد.", exception.Message);
    }

    [Fact]
    public void ChangePriceUsesExactRialValue()
    {
        var product = CreateProduct();

        product.ChangePrice(Money.FromRials(8_900_000));

        Assert.Equal(8_900_000, product.SalePrice.Rials);
    }

    [Fact]
    public void ArchiveDoesNotRemoveIdentityOrBarcodes()
    {
        var product = CreateProduct();
        product.AddBarcode("6260000000012");
        var originalId = product.Id;

        product.Archive();

        Assert.Equal(ProductStatus.Archived, product.Status);
        Assert.Equal(originalId, product.Id);
        Assert.Single(product.Barcodes);
    }

    private static Product CreateProduct() => Product.Create(
        "گردو",
        "SKU-100",
        CategoryId.New(),
        UnitId.New(),
        Money.FromTomans(750_000));
}

