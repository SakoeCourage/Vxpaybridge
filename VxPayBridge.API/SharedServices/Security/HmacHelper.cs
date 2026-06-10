using System.Security.Cryptography;
using System.Text;

namespace VxPayBridge.API.SharedServices.Security;

public static class HmacHelper
{
    public static string GenerateSignature(string secret, string payload)
    {
        var encoding = new UTF8Encoding();
        var keyByte = encoding.GetBytes(secret);
        var messageBytes = encoding.GetBytes(payload);

        using var hmacsha512 = new HMACSHA512(keyByte);
        var hashmessage = hmacsha512.ComputeHash(messageBytes);
        return BitConverter.ToString(hashmessage).Replace("-", "").ToLower();
    }

    public static bool ValidateSignature(string secret, string payload, string signature)
    {
        var expectedSignature = GenerateSignature(secret, payload);
        return expectedSignature.Equals(signature, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One-way SHA-256 hash of a plain-text secret.
    /// Store the result; never store the original secret.
    /// </summary>
    public static string HashSecret(string plainTextSecret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainTextSecret));
        return BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }

    /// <summary>
    /// Time-constant comparison of a plain-text secret against a stored hash.
    /// </summary>
    public static bool VerifySecret(string plainTextSecret, string storedHash)
    {
        var hash = HashSecret(plainTextSecret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hash),
            Encoding.UTF8.GetBytes(storedHash));
    }
}
