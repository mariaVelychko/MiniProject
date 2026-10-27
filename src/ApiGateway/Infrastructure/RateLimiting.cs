using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ApiGateway.Infrastructure;

/// <summary>
/// Обмеження частоти запитів (ЛР3, 3.5). Один глобальний обмежувач із трьома «розділами»:
///
///  • вхід і реєстрація — fixed window, жорстко (5 / хв) і за IP: саме цей ендпоінт підбирають паролями;
///  • авторизований користувач — token bucket за ідентифікатором користувача: екран клієнта лише на старті
///    відправляє кілька запитів поспіль, тож короткий сплеск має проходити, а стабільний потік — ні;
///  • анонім — fixed window за IP: ідентифікатора в нього немає, IP — єдина відмінна ознака.
///
/// Чому для авторизованих — користувач, а не IP: за одним IP (офіс, мобільний оператор) сидять тисячі людей,
/// і один спамер не повинен блокувати решту; а IP анонімів — єдине, чим їх можна розрізнити.
/// /health і Swagger лімітом не обмежуються.
/// </summary>
public static class RateLimiting
{
    private static readonly (string Method, string Path)[] LoginRoutes =
    [
        ("POST", "/api/v1/auth/login"),
        ("POST", "/api/v1/auth/register"),
        ("POST", "/api/auth/login"),
    ];

    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });

        // Налаштування читаються лише під час першого запиту (IOptions) — тести можуть підмінити ліміти.
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<GatewayOptions>>((limiter, gateway) =>
        {
            var rl = gateway.Value.RateLimits;
            var window = TimeSpan.FromSeconds(rl.WindowSeconds);

            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var path = ctx.Request.Path;

                if (path.StartsWithSegments("/health") || path.StartsWithSegments("/swagger"))
                {
                    return RateLimitPartition.GetNoLimiter("exempt");
                }

                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                if (IsLogin(ctx.Request))
                {
                    return RateLimitPartition.GetFixedWindowLimiter($"login:{ip}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rl.LoginPerWindow,
                        Window = window,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
                }

                var userId = ctx.User.Identity?.IsAuthenticated == true
                    ? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.User.FindFirstValue("sub")
                    : null;

                if (userId is not null)
                {
                    return RateLimitPartition.GetTokenBucketLimiter($"user:{userId}", _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = rl.AuthenticatedBurst,
                        TokensPerPeriod = rl.AuthenticatedPerWindow,
                        ReplenishmentPeriod = window,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
                }

                return RateLimitPartition.GetFixedWindowLimiter($"ip:{ip}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rl.AnonymousPerWindow,
                    Window = window,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });

            limiter.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;

                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
                    ? Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds))
                    : rl.WindowSeconds;

                http.Response.Headers["Retry-After"] = retryAfter.ToString(CultureInfo.InvariantCulture);

                await Problems.WriteAsync(http, StatusCodes.Status429TooManyRequests, "Забагато запитів",
                    $"Перевищено ліміт запитів. Повторіть спробу через {retryAfter} с.",
                    new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfter }, ct);
            };
        });

        return services;
    }

    private static bool IsLogin(HttpRequest request) =>
        LoginRoutes.Any(r => HttpMethods.Equals(request.Method, r.Method)
            && request.Path.Equals(r.Path, StringComparison.OrdinalIgnoreCase));
}
