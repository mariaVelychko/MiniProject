using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gateway.IntegrationTests;

/// <summary>
/// JWT для тестів. Ті самі issuer/audience/claims, що в справжньому Auth-сервісі, підпис HS256.
/// Токен складається вручну (без зайвих пакетів): header.payload.signature, base64url.
/// </summary>
public static class TestTokens
{
    public const string Key = "test-signing-key-test-signing-key-0123456789";
    public const string Issuer = "MiniProject";
    public const string Audience = "MiniProject.Clients";

    private const string NameIdentifier = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
    private const string Name = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";
    private const string Role = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    public static string Create(int userId, string displayName, string role = "Client", bool expired = false, string? signingKey = null)
    {
        var now = DateTimeOffset.UtcNow;

        var payload = new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = userId.ToString(),
            [NameIdentifier] = userId.ToString(),
            [Name] = displayName,
            [Role] = role,
            ["nbf"] = now.AddMinutes(expired ? -30 : -1).ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(expired ? -20 : 15).ToUnixTimeSeconds(),
        };

        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey ?? Key), Encoding.ASCII.GetBytes($"{header}.{body}")));

        return $"{header}.{body}.{signature}";
    }

    /// <summary>Розбір корисного навантаження БЕЗ перевірки підпису — лише для фейкового Auth-сервісу в тестах.</summary>
    public static (int UserId, string DisplayName, string Role) Read(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
        var root = json.RootElement;

        return (int.Parse(root.GetProperty("sub").GetString()!), root.GetProperty(Name).GetString()!, root.GetProperty(Role).GetString()!);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
