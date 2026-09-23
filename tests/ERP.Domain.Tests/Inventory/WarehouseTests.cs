using ERP.Domain.Common;
using ERP.Domain.Inventory;

namespace ERP.Domain.Tests.Inventory;

public sealed class WarehouseTests
{
    [Fact]
    public void CreateTrimsNameAndAddressAndStartsActive()
    {
        var warehouse = Warehouse.Create("  انبار شمال  ", "  تهران، خیابان آزادی  ");

        Assert.Equal("انبار شمال", warehouse.Name);
        Assert.Equal("تهران، خیابان آزادی", warehouse.Address);
        Assert.Equal(WarehouseStatus.Active, warehouse.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankAddressIsStoredAsNoAddress(string? address)
    {
        Assert.Null(Warehouse.Create("انبار", address).Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankNameIsRejected(string name)
    {
        var exception = Assert.Throws<DomainException>(() => Warehouse.Create(name, null));

        Assert.Equal("نام انبار را وارد کنید.", exception.Message);
    }

    [Fact]
    public void NameAndAddressHaveTheColumnLengthLimits()
    {
        Assert.Equal(120, Warehouse.Create(new string('ا', 120), new string('ب', 500)).Name.Length);
        Assert.Throws<DomainException>(() => Warehouse.Create(new string('ا', 121), null));
        Assert.Throws<DomainException>(() => Warehouse.Create("انبار", new string('ب', 501)));
    }

    [Fact]
    public void UpdateReplacesNameAndAddressWithTheSameRules()
    {
        var warehouse = Warehouse.Create("انبار", "نشانی قدیم");

        warehouse.Update(" انبار مرکزی ", " ");

        Assert.Equal("انبار مرکزی", warehouse.Name);
        Assert.Null(warehouse.Address);
        Assert.Throws<DomainException>(() => warehouse.Update(" ", null));
        Assert.Equal("انبار مرکزی", warehouse.Name);
    }

    [Fact]
    public void ArchiveTakesItOutOfUse()
    {
        var warehouse = Warehouse.Create("انبار", null);

        warehouse.Archive();

        Assert.Equal(WarehouseStatus.Archived, warehouse.Status);
    }
}
