using System.Security.Cryptography;
using ERP.Domain.Common;

namespace ERP.Domain.Identity;

/// <summary>
/// «رمز عبور Hash می‌شود» (milestone-1 §10 امنیت و مجوز). PBKDF2-HMAC-SHA256
/// with a random salt per password and the iteration count OWASP recommends
/// for it (2023 guidance) — .NET's own <see cref="Rfc2898DeriveBytes"/>, no
/// extra package. The encoded form is self-describing (iterations.salt.hash,
/// all Base64 except the iteration count) so a future increase to the
/// iteration count does not break verifying passwords hashed under the old
/// one — each stored hash carries the count it was made with.
/// </summary>
public readonly record struct PasswordHash
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 210_000;
    private const int MinimumPlainPasswordLength = 8;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    private PasswordHash(string encoded)
    {
        Encoded = encoded;
    }

    /// <summary>Opaque — never the plain password. Safe to persist and to log (it is not the secret itself).</summary>
    public string Encoded { get; }

    public static PasswordHash Create(string plainPassword)
    {
        if (string.IsNullOrWhiteSpace(plainPassword) || plainPassword.Length < MinimumPlainPasswordLength)
        {
            throw new DomainException("رمز عبور باید حداقل ۸ نویسه باشد.");
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(plainPassword, salt, Iterations, Algorithm, HashSizeBytes);
        return new PasswordHash(Encode(Iterations, salt, hash));
    }

    /// <summary>Reconstructs a hash already stored in the database — never re-derives it from a plain password.</summary>
    public static PasswordHash FromEncoded(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new DomainException("رمز عبور ذخیره‌شده معتبر نیست.");
        }

        return new PasswordHash(encoded);
    }

    /// <summary>
    /// Constant-time comparison (<see cref="CryptographicOperations.FixedTimeEquals"/>)
    /// so a login attempt cannot be timed to learn how much of the hash matched.
    /// </summary>
    public bool Matches(string? plainPassword)
    {
        if (string.IsNullOrEmpty(plainPassword))
        {
            return false;
        }

        var parts = Encoded.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(plainPassword, salt, iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string Encode(int iterations, byte[] salt, byte[] hash) =>
        $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
}
