using ERP.Application.Catalog;
using ERP.Application.Tests.TestDoubles;
using ERP.Domain.Catalog;

namespace ERP.Application.Tests.Catalog;

public sealed class CreateCategoryTests
{
    [Fact]
    public async Task ExecuteReturnsPersianErrorForDuplicateSiblingName()
    {
        var context = new ApplicationTestContext();
        context.Categories.Items.Add(Category.Create("خشکبار", null, 0));
        var handler = CreateHandler(context);

        var result = await handler.ExecuteAsync(
            new CreateCategoryCommand("  خشکبار  ", null, 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("در این سطح، دسته‌بندی دیگری با همین نام وجود دارد.", result.Error?.Message);
        Assert.Single(context.Categories.Items);
        Assert.Empty(context.Audit.Entries);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ExecuteSavesNormalizedCategoryAndAuditTogether()
    {
        var context = new ApplicationTestContext();
        var handler = CreateHandler(context);

        var result = await handler.ExecuteAsync(
            new CreateCategoryCommand("  خشکبار  ", null, 2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("خشکبار", Assert.Single(context.Categories.Items).Name);
        var audit = Assert.Single(context.Audit.Entries);
        Assert.Equal("catalog.category.created", audit.Action);
        Assert.Equal(context.User.UserId, audit.ActorUserId);
        Assert.Equal(context.Clock.UtcNow, audit.OccurredAtUtc);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    private static CreateCategoryHandler CreateHandler(ApplicationTestContext context)
    {
        return new CreateCategoryHandler(
            context.Categories,
            context.Audit,
            context.UnitOfWork,
            context.User,
            context.Clock);
    }
}

