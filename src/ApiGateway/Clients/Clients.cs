using System.Net;
using ApiGateway.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ApiGateway.Clients;

// ---------- Catalog ----------

/// <summary>Об'єкт каталогу (лише потрібні шлюзу поля; решта відповіді ігнорується).</summary>
public sealed record CatalogItem(int Id, string Title, string Description, decimal Price, int DurationMin, int Capacity,
    bool IsActive, int CategoryId, string CategoryName);

public interface ICatalogClient
{
    Task<Upstream<CatalogItem>> GetItemAsync(int id, CancellationToken ct);
}

/// <summary>
/// Клієнт каталогу з кешем. Об'єкт каталогу спільний для всіх, тому кешується (TTL — GatewayOptions.Cache).
/// Ключ "catalog:item:{id}" — лише ідентифікатор об'єкта; користувача в ключі немає, бо ці дані не персональні.
/// Кешуємо тільки успішні відповіді: «не знайдено» і «недоступно» не повинні «залипати».
/// Запис належить до регіону каталогу: зміна каталогу через шлюз скидає його (див. CatalogCacheMiddleware).
/// </summary>
public sealed class CatalogClient(HttpClient http, IMemoryCache cache, CatalogCacheRegion region, CacheStats stats,
    IOptions<GatewayOptions> options, ILogger<CatalogClient> log) : ICatalogClient
{
    public async Task<Upstream<CatalogItem>> GetItemAsync(int id, CancellationToken ct)
    {
        var key = $"catalog:item:{id}";

        if (cache.TryGetValue(key, out CatalogItem? cached) && cached is not null)
        {
            stats.Hit();
            return Upstream<CatalogItem>.Ok(cached);
        }

        stats.Miss();
        var token = region.GetToken(); // до запиту: див. коментар у CatalogCacheMiddleware

        var result = await UpstreamHttp.GetAsync<CatalogItem>(http, $"api/v1/catalog/items/{id}", log, "Catalog", ct);

        if (result.IsOk)
        {
            var ttl = TimeSpan.FromSeconds(options.Value.Cache.CatalogTtlSeconds);
            cache.Set(key, result.Value!, new MemoryCacheEntryOptions().SetAbsoluteExpiration(ttl).AddExpirationToken(token));
        }

        return result;
    }
}

// ---------- Schedule ----------

/// <summary>Сеанс (тренування) у тому вигляді, в якому його віддає Schedule.</summary>
public sealed record ScheduleSession(int Id, DateTime Time, int DurationMin, string Hall, string CoachName,
    int Participants, int MaxParticipants);

public interface IScheduleClient
{
    Task<Upstream<List<ScheduleSession>>> GetSessionsAsync(int catalogItemId, CancellationToken ct);
}

/// <summary>Розклад не кешується: кількість учасників змінюється з кожним записом.</summary>
public sealed class ScheduleClient(HttpClient http, ILogger<ScheduleClient> log) : IScheduleClient
{
    public Task<Upstream<List<ScheduleSession>>> GetSessionsAsync(int catalogItemId, CancellationToken ct) =>
        UpstreamHttp.GetAsync<List<ScheduleSession>>(http, $"api/workouts?catalogItemId={catalogItemId}", log, "Schedule", ct);
}

// ---------- Auth ----------

public sealed record UserProfile(int Id, string Email, string DisplayName, IReadOnlyList<string> Roles);

public interface IAuthClient
{
    /// <summary>Профіль власника токена. Персональні дані: НЕ кешується.</summary>
    Task<Upstream<UserProfile>> GetMeAsync(string bearerToken, CancellationToken ct);
}

public sealed class AuthClient(HttpClient http, ILogger<AuthClient> log) : IAuthClient
{
    public Task<Upstream<UserProfile>> GetMeAsync(string bearerToken, CancellationToken ct) =>
        UpstreamHttp.GetAsync<UserProfile>(http, "api/v1/users/me", log, "Auth", ct, bearerToken,
            HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
}

// ---------- спільне ----------

/// <summary>Прокидає X-Correlation-Id із вхідного запиту у вихідні виклики до сервісів.</summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var id = accessor.HttpContext?.Request.Headers[Correlation.Header].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(id) && !request.Headers.Contains(Correlation.Header))
        {
            request.Headers.TryAddWithoutValidation(Correlation.Header, id);
        }

        return base.SendAsync(request, ct);
    }
}

/// <summary>Адреси сусідніх сервісів: Services:{Auth|Catalog|Monolith|Schedule}:Host / :Port (є типові значення).</summary>
public sealed class ServiceUrls(IConfiguration config)
{
    public Uri Auth => Build("Auth", 5001);
    public Uri Catalog => Build("Catalog", 5002);
    public Uri Monolith => Build("Monolith", 5100);
    public Uri Schedule => Build("Schedule", 5102);

    public Uri Build(string name, int defaultPort)
    {
        var host = config[$"Services:{name}:Host"] ?? (name == "Monolith" ? config["Monolith:Host"] : null) ?? "localhost";
        var port = config[$"Services:{name}:Port"] ?? (name == "Monolith" ? config["Monolith:Port"] : null) ?? defaultPort.ToString();
        return new Uri($"http://{host}:{port}/");
    }
}
