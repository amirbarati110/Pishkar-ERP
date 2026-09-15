using ERP.Domain.Catalog;
using ERP.Domain.Common;
using ERP.Domain.Inventory;

namespace ERP.Domain.Tests.Inventory;

public sealed class StockLedgerTests
{
    [Fact]
    public void OpeningStockCreatesLayerAndMovement()
    {
        var ledger = CreateEmptyLedger();

        ledger.ReceiveOpeningStock(
            Quantity.Create(10m),
            Money.FromTomans(100_000),
            new DateOnly(2026, 9, 14));

        Assert.Equal(10m, ledger.AvailableQuantity.Value);
        var layer = Assert.Single(ledger.Layers);
        Assert.Equal(10m, layer.RemainingQuantity.Value);
        var movement = Assert.Single(ledger.Movements);
        Assert.Equal(StockMovementType.OpeningBalance, movement.Type);
        Assert.Equal(10m, movement.Quantity.Value);
    }

    [Fact]
    public void ConsumeFifoUsesOldestLayerFirst()
    {
        var ledger = CreateEmptyLedger();
        ledger.ReceiveOpeningStock(Quantity.Create(5m), Money.FromTomans(100_000), new DateOnly(2026, 9, 1));
        ledger.ReceiveOpeningStock(Quantity.Create(4m), Money.FromTomans(120_000), new DateOnly(2026, 9, 10));

        var allocations = ledger.ConsumeFifo(Quantity.Create(6m), "sale-1001");

        Assert.Collection(
            allocations,
            first =>
            {
                Assert.Equal(5m, first.Quantity.Value);
                Assert.Equal(1_000_000, first.UnitCost.Rials);
            },
            second =>
            {
                Assert.Equal(1m, second.Quantity.Value);
                Assert.Equal(1_200_000, second.UnitCost.Rials);
            });
        Assert.Equal(0m, ledger.Layers[0].RemainingQuantity.Value);
        Assert.Equal(3m, ledger.Layers[1].RemainingQuantity.Value);
        Assert.Equal(3m, ledger.AvailableQuantity.Value);
        Assert.Equal(StockMovementType.Sale, ledger.Movements[^1].Type);
    }

    [Fact]
    public void ConsumeMoreThanAvailableLeavesLedgerUnchanged()
    {
        var ledger = CreateEmptyLedger();
        ledger.ReceiveOpeningStock(Quantity.Create(5m), Money.FromTomans(100_000), new DateOnly(2026, 9, 1));

        var exception = Assert.Throws<InsufficientStockException>(
            () => ledger.ConsumeFifo(Quantity.Create(6m), "sale-1002"));

        Assert.Equal("موجودی کالا کافی نیست. موجودی فعلی: ۵", exception.Message);
        Assert.Equal(5m, ledger.AvailableQuantity.Value);
        Assert.Equal(5m, ledger.Layers[0].RemainingQuantity.Value);
        Assert.Single(ledger.Movements);
    }

    [Fact]
    public void ReceiveOpeningStockRejectsFutureDateWithoutChangingLedger()
    {
        var ledger = CreateEmptyLedger();

        var exception = Assert.Throws<DomainException>(() => ledger.ReceiveOpeningStock(
            Quantity.Create(5m),
            Money.FromTomans(100_000),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))));

        Assert.Equal("تاریخ موجودی اولیه نمی‌تواند در آینده باشد.", exception.Message);
        Assert.Empty(ledger.Layers);
        Assert.Empty(ledger.Movements);
    }

    [Fact]
    public void ConsumeRequiresReferenceForTraceability()
    {
        var ledger = CreateEmptyLedger();
        ledger.ReceiveOpeningStock(Quantity.Create(5m), Money.Zero, new DateOnly(2026, 9, 1));

        var exception = Assert.Throws<DomainException>(() => ledger.ConsumeFifo(Quantity.Create(1m), " "));

        Assert.Equal("مرجع گردش موجودی را وارد کنید.", exception.Message);
        Assert.Equal(5m, ledger.AvailableQuantity.Value);
    }

    [Fact]
    public void ConsumeFifoAllowingBackorderCoversWhatItCanAndRecordsTheRestAsBackorder()
    {
        // "بار اومده، فروش رفته، ولی هنوز فاکتور خرید ثبت نشده": 5 in stock,
        // cashier sells 8 anyway with the override on.
        var ledger = CreateEmptyLedger();
        ledger.ReceiveOpeningStock(Quantity.Create(5m), Money.FromTomans(100_000), new DateOnly(2026, 9, 1));

        var allocations = ledger.ConsumeFifoAllowingBackorder(Quantity.Create(8m), "sale-2001");

        var allocation = Assert.Single(allocations);
        Assert.Equal(5m, allocation.Quantity.Value);
        Assert.Equal(0m, ledger.Layers[0].RemainingQuantity.Value);
        Assert.Equal(-3m, ledger.AvailableQuantity.Value);
        Assert.True(ledger.AvailableQuantity.IsNegative);
        Assert.Collection(
            ledger.Movements,
            opening => Assert.Equal(StockMovementType.OpeningBalance, opening.Type),
            sale =>
            {
                Assert.Equal(StockMovementType.Sale, sale.Type);
                Assert.Equal(5m, sale.Quantity.Value);
            },
            backorder =>
            {
                Assert.Equal(StockMovementType.BackorderSale, backorder.Type);
                Assert.Equal(3m, backorder.Quantity.Value);
            });
    }

    [Fact]
    public void ConsumeFifoAllowingBackorderWithNoStockAtAllRecordsAPureBackorder()
    {
        var ledger = CreateEmptyLedger();

        var allocations = ledger.ConsumeFifoAllowingBackorder(Quantity.Create(4m), "sale-2002");

        Assert.Empty(allocations);
        Assert.Equal(-4m, ledger.AvailableQuantity.Value);
        var movement = Assert.Single(ledger.Movements);
        Assert.Equal(StockMovementType.BackorderSale, movement.Type);
        Assert.Equal(4m, movement.Quantity.Value);
    }

    [Fact]
    public void ConsumeFifoAllowingBackorderStacksOnTopOfAnExistingBackorder()
    {
        var ledger = CreateEmptyLedger();
        ledger.ConsumeFifoAllowingBackorder(Quantity.Create(2m), "sale-2003");

        ledger.ConsumeFifoAllowingBackorder(Quantity.Create(1m), "sale-2004");

        Assert.Equal(-3m, ledger.AvailableQuantity.Value);
    }

    private static StockLedger CreateEmptyLedger()
    {
        return StockLedger.Empty(ProductId.New(), WarehouseId.New());
    }
}
