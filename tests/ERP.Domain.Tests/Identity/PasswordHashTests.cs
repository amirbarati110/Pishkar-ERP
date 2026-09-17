using ERP.Domain.Common;
using ERP.Domain.Identity;

namespace ERP.Domain.Tests.Identity;

public sealed class PasswordHashTests
{
    [Fact]
    public void TheRightPasswordMatchesAndTheWrongOneDoesNot()
    {
        var hash = PasswordHash.Create("درست‌رمز۱۲۳۴");

        Assert.True(hash.Matches("درست‌رمز۱۲۳۴"));
        Assert.False(hash.Matches("غلط‌رمز"));
        Assert.False(hash.Matches(null));
        Assert.False(hash.Matches(string.Empty));
    }

    [Fact]
    public void TwoHashesOfTheSamePasswordAreDifferentEncodedStringsButBothMatch()
    {
        var first = PasswordHash.Create("یک‌رمز۱۲۳۴۵۶");
        var second = PasswordHash.Create("یک‌رمز۱۲۳۴۵۶");

        Assert.NotEqual(first.Encoded, second.Encoded); // random salt per hash
        Assert.True(first.Matches("یک‌رمز۱۲۳۴۵۶"));
        Assert.True(second.Matches("یک‌رمز۱۲۳۴۵۶"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("کوتاه1")] // under 8 characters
    public void TooShortOrBlankIsRejected(string password)
    {
        Assert.Throws<DomainException>(() => PasswordHash.Create(password));
    }

    [Fact]
    public void FromEncodedReconstructsAStoredHashWithoutNeedingThePlainPasswordAgain()
    {
        var original = PasswordHash.Create("نگهداری‌شده۱۲۳۴");

        var reloaded = PasswordHash.FromEncoded(original.Encoded);

        Assert.True(reloaded.Matches("نگهداری‌شده۱۲۳۴"));
    }

    [Fact]
    public void GarbledEncodedTextNeverMatchesAndNeverThrows()
    {
        var hash = PasswordHash.FromEncoded("این یک هش معتبر نیست");

        Assert.False(hash.Matches("هر رمزی"));
    }
}
