using ERP.Application.Catalog;
using ERP.Application.Common;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Tests.Catalog;

public sealed class CreateProductTests
{
    [Fact]
    public async Task ExecuteRejectsBarcodeAlreadyAssignedToAnotherProduct()
    {
        var context = new ApplicationTestContext();
        context.Products.ExistingBarcodes.Add("6260000000012");
        var handler = CreateHandler(context);

        var result = await handler.ExecuteAsync(
            new CreateProductCommand(
                "گردو",
                null,
                CategoryId.New(),
                UnitId.New(),
                Money.FromTomans(750_000),
                [" 6260000000012 "]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("بارکد 6260000000012 قبلاً برای کالای دیگری ثبت شده است.", result.Error?.Message);
        Assert.Empty(context.Products.Items);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ExecuteReturnsFriendlyConflictWhenDatabaseDetectsDuplicate()
    {
        var context = new ApplicationTestContext();
        context.Products.ConflictOnAdd = new DataConflictException(
            "catalog.product.duplicate-barcode",
            "این بارکد قبلاً برای کالای دیگری ثبت شده است.",
            new InvalidOperationException("database constraint"));
        var handler = CreateHandler(context);

        var result = await handler.ExecuteAsync(
            new CreateProductCommand(
                "گردو",
                null,
                CategoryId.New(),
                UnitId.New(),
                Money.FromTomans(750_000),
                ["6260000000013"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("catalog.product.duplicate-barcode", result.Error?.Code);
        Assert.Equal("این بارکد قبلاً برای کالای دیگری ثبت شده است.", result.Error?.Message);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    private static CreateProductHandler CreateHandler(ApplicationTestContext context)
    {
        return new CreateProductHandler(
            context.Products,
            context.Audit,
            context.UnitOfWork,
            context.User,
            context.Clock);
    }
}
