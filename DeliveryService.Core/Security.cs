using System.Security.Cryptography;

namespace DeliveryService.Core;

/// <summary>PBKDF2-хэширование паролей демо-пользователей. Соль и хэш хранятся в БД.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string stored);
}

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Convert.ToHexString(salt).ToLowerInvariant()}:{Convert.ToHexString(key).ToLowerInvariant()}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2)
            return false;
        try
        {
            byte[] salt = Convert.FromHexString(parts[0]);
            byte[] expected = Convert.FromHexString(parts[1]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
