using ERP.Domain.Common;
using ERP.Domain.Identity;

namespace ERP.Domain.Tests.Identity;

public sealed class UserTests
{
    [Fact]
    public void CreateNormalizesUsernameToLowercaseAndKeepsTheDisplayNameAsTyped()
    {
        var user = User.Create("Admin.One", "مدیر فروشگاه", "رمزدرست۱۲۳۴", UserRole.Admin);

        Assert.Equal("admin.one", user.Username);
        Assert.Equal("مدیر فروشگاه", user.DisplayName);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Theory]
    [InlineData("ab")] // too short
    [InlineData("یوزرنیم")] // non-ASCII
    [InlineData("has space")]
    public void InvalidUsernamesAreRejected(string username)
    {
        Assert.Throws<DomainException>(() => User.Create(username, "کاربر", "رمزدرست۱۲۳۴", UserRole.Cashier));
    }

    [Fact]
    public void VerifyPasswordAcceptsTheRightPasswordForAnActiveUser()
    {
        var user = User.Create("cashier1", "صندوق‌دار یک", "رمزدرست۱۲۳۴", UserRole.Cashier);

        Assert.True(user.VerifyPassword("رمزدرست۱۲۳۴"));
        Assert.False(user.VerifyPassword("رمز غلط"));
    }

    [Fact]
    public void AnArchivedUserCanNeverSignInEvenWithTheRightPassword()
    {
        var user = User.Create("cashier1", "صندوق‌دار یک", "رمزدرست۱۲۳۴", UserRole.Cashier);

        user.Archive();

        Assert.False(user.VerifyPassword("رمزدرست۱۲۳۴"));
        Assert.Equal(UserStatus.Archived, user.Status);
    }

    [Fact]
    public void ReactivateLetsAnArchivedUserSignInAgain()
    {
        var user = User.Create("cashier1", "صندوق‌دار یک", "رمزدرست۱۲۳۴", UserRole.Cashier);
        user.Archive();

        user.Reactivate();

        Assert.True(user.VerifyPassword("رمزدرست۱۲۳۴"));
    }

    [Fact]
    public void ChangePasswordReplacesTheOldOneEntirely()
    {
        var user = User.Create("cashier1", "صندوق‌دار یک", "رمزقدیمی۱۲۳۴", UserRole.Cashier);

        user.ChangePassword("رمزجدید۱۲۳۴۵۶");

        Assert.False(user.VerifyPassword("رمزقدیمی۱۲۳۴"));
        Assert.True(user.VerifyPassword("رمزجدید۱۲۳۴۵۶"));
    }
}
