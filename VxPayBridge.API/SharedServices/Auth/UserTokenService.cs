using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VxPayBridge.API.SharedServices.Auth;

public class UserTokenService
{
    private readonly IConfiguration _configuration;

    public UserTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreateToken(Guid userId, string email)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(GetExpiryMinutes()).ToUnixTimeSeconds();
        var header = new Dictionary<string, string>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };
        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(),
            ["email"] = email,
            ["exp"] = expiresAt
        };

        var headerPart = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadPart = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = Sign($"{headerPart}.{payloadPart}");
        return $"{headerPart}.{payloadPart}.{signature}";
    }

    public bool TryValidateToken(string token, out Guid userId)
    {
        userId = default;
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var expectedSignature = Sign($"{parts[0]}.{parts[1]}");
        if (!FixedTimeEquals(expectedSignature, parts[2]))
        {
            return false;
        }

        try
        {
            var payloadBytes = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payloadBytes);
            if (!doc.RootElement.TryGetProperty("sub", out var sub) ||
                !Guid.TryParse(sub.GetString(), out userId))
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("exp", out var exp) ||
                exp.GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return false;
            }

            return true;
        }
        catch
        {
            userId = default;
            return false;
        }
    }

    private string Sign(string value)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(GetSigningKey()));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private string GetSigningKey()
    {
        var signingKey = _configuration["Auth:JwtSigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            signingKey = _configuration["InternalApiKey"];
        }

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Auth signing key is not configured. Set Auth:JwtSigningKey.");
        }

        return signingKey;
    }

    private int GetExpiryMinutes()
    {
        var minutes = _configuration.GetValue<int?>("Auth:AccessTokenMinutes") ?? 60;
        return Math.Max(5, minutes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }
}
