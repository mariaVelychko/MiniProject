using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ApiGateway.Infrastructure;

/// <summary>
/// «Регіон» кешу каталогу: усі записи каталогу прив'язані до одного токена зміни. Після успішної зміни каталогу
/// (POST/PUT/DELETE) шлюз скасовує токен — і всі записи каталогу (сам об'єкт і списки, куди він входить) зникають.
/// Це інвалідизація «напряму», як вимагає методичка; TTL лишається страховкою на випадок змін повз шлюз.
/// </summary>
public sealed class CatalogCacheRegion
{
    private readonly object _lock = new();
    private CancellationTokenSource _source = new();

    public IChangeToken GetToken()
    {
        lock (_lock)
        {
            return new CancellationChangeToken(_source.Token);
        }
    }

    public void Invalidate()
    {
        CancellationTokenSource old;

        lock (_lock)
        {
            old = _source;
            _source = new CancellationTokenSource();
        }

        old.Cancel();
        old.Dispose();
    }
}

/// <summary>Лічильники влучань і промахів кешу (видно через GET /gateway/cache).</summary>
public sealed class CacheStats
{
    private long _hits;
    private long _misses;
    private long _invalidations;

    public void Hit() => Interlocked.Increment(ref _hits);
    public void Miss() => Interlocked.Increment(ref _misses);
    public void Invalidated() => Interlocked.Increment(ref _invalidations);

    public CacheStatsSnapshot Snapshot()
    {
        var hits = Interlocked.Read(ref _hits);
        var misses = Interlocked.Read(ref _misses);
        var total = hits + misses;

        return new CacheStatsSnapshot(hits, misses, Interlocked.Read(ref _invalidations),
            total == 0 ? 0 : Math.Round((double)hits / total, 3));
    }
}

public sealed record CacheStatsSnapshot(long Hits, long Misses, long Invalidations, double HitRatio);

/// <summary>Збережена відповідь каталогу.</summary>
public sealed record CachedResponse(int Status, string? ContentType, byte[] Body);

/// <summary>
/// Кеш відповідей каталогу для проксійованих GET /api/v1/catalog/**.
///
/// Що кешуємо: лише СПІЛЬНІ дані — каталог однаковий для всіх, він доступний анонімам (AllowAnonymous) і не містить
/// нічого персонального. Тому ключ складається лише з шляху й параметрів запиту (фільтри, сторінка, сортування):
/// "catalog:http:{path}{query}". Користувача в ключі немає — а отже, персональні дані сюди потрапити не можуть.
/// Персональні маршрути (/api/v1/users/**, моя операція, мій профіль) сюди не потрапляють взагалі — перелік закритий.
///
/// Інвалідизація: успішна зміна каталогу через шлюз скидає весь регіон.
/// </summary>
public sealed class CatalogCacheMiddleware(RequestDelegate next, IMemoryCache cache, CatalogCacheRegion region,
    CacheStats stats, IOptions<GatewayOptions> options)
{
    private const int MaxBodyBytes = 512 * 1024;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var request = ctx.Request;

        if (!request.Path.StartsWithSegments("/api/v1/catalog", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        if (!HttpMethods.IsGet(request.Method))
        {
            await next(ctx);

            // Зміна пройшла успішно — стара копія каталогу більше не правдива.
            if (ctx.Response.StatusCode is >= 200 and < 300 && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method))
            {
                region.Invalidate();
                stats.Invalidated();
            }

            return;
        }

        var key = $"catalog:http:{request.Path.Value!.ToLowerInvariant()}{request.QueryString.Value}";

        if (cache.TryGetValue(key, out CachedResponse? hit) && hit is not null)
        {
            stats.Hit();
            ctx.Response.StatusCode = hit.Status;
            ctx.Response.ContentType = hit.ContentType;
            ctx.Response.ContentLength = hit.Body.Length;
            ctx.Response.Headers["X-Cache"] = "HIT";
            await ctx.Response.Body.WriteAsync(hit.Body, ctx.RequestAborted);
            return;
        }

        stats.Miss();

        // Токен беремо ДО запиту: якщо під час запиту хтось змінить каталог, застаріла відповідь у кеш не потрапить.
        var token = region.GetToken();
        var original = ctx.Response.Body;
        await using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        try
        {
            await next(ctx);
        }
        finally
        {
            ctx.Response.Body = original;
        }

        var cacheable = ctx.Response.StatusCode == StatusCodes.Status200OK
            && buffer.Length is > 0 and <= MaxBodyBytes
            && ctx.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
            && !ctx.Response.Headers.ContainsKey("Set-Cookie");

        if (cacheable)
        {
            var ttl = TimeSpan.FromSeconds(options.Value.Cache.CatalogTtlSeconds);

            cache.Set(key, new CachedResponse(ctx.Response.StatusCode, ctx.Response.ContentType, buffer.ToArray()),
                new MemoryCacheEntryOptions().SetAbsoluteExpiration(ttl).AddExpirationToken(token));
        }

        ctx.Response.Headers["X-Cache"] = "MISS";
        buffer.Position = 0;
        await buffer.CopyToAsync(original, ctx.RequestAborted);
    }
}
