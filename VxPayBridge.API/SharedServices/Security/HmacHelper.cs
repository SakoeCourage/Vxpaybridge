using System.Security.Cryptography;
using System.Text;

namespace VxPayBridge.API.SharedServices.Security;

public static class HmacHelper
{
    public static string GenerateSha512Signature(string secret, string payload)
    {
        var encoding = new UTF8Encoding();
        var keyByte = encoding.GetBytes(secret);
        var messageBytes = encoding.GetBytes(payload);

        using var hmacsha512 = new HMACSHA512(keyByte);
        var hashmessage = hmacsha512.ComputeHash(messageBytes);
        return BitConverter.ToString(hashmessage).Replace("-", "").ToLower();
    }

    public static string GenerateSha256Signature(string secret, string payload)
    {
        var encoding = new UTF8Encoding();
        var keyByte = encoding.GetBytes(secret);
        var messageBytes = encoding.GetBytes(payload);

        using var hmacsha256 = new HMACSHA256(keyByte);
        var hashmessage = hmacsha256.ComputeHash(messageBytes);
        return BitConverter.ToString(hashmessage).Replace("-", "").ToLower();
    }

    public static bool ValidateSha512Signature(string secret, string payload, string signature)
    {
        var expectedSignature = GenerateSha512Signature(secret, payload);
        return FixedTimeEquals(expectedSignature, signature);
    }

    public static bool ValidateSha256Signature(string secret, string payload, string signature)
    {
        var expectedSignature = GenerateSha256Signature(secret, payload);
        return FixedTimeEquals(expectedSignature, signature);
    }

    private static bool FixedTimeEquals(string expected, string received)
    {
        if (string.IsNullOrWhiteSpace(received))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected.ToLowerInvariant());
        var receivedBytes = Encoding.UTF8.GetBytes(received.ToLowerInvariant());

        return expectedBytes.Length == receivedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);
    }

    /// <summary>
    /// One-way SHA-256 hash of a plain-text secret.
    /// Store the result for authentication checks.
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
