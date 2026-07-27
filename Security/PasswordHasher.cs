using System.Security.Cryptography;

namespace NinjaTrader_Xen.Security;

public static class PasswordHasher
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var derive = new Rfc2898DeriveBytes(
            password, salt, 100_000, HashAlgorithmName.SHA256);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(derive.GetBytes(32))}";
    }

    public static bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);
            using var derive = new Rfc2898DeriveBytes(
                password, salt, 100_000, HashAlgorithmName.SHA256);

            return CryptographicOperations.FixedTimeEquals(
                derive.GetBytes(expected.Length),
                expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
