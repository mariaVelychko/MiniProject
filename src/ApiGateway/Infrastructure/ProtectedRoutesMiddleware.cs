using Microsoft.AspNetCore.Authentication;

namespace ApiGateway.Infrastructure;

/// <summary>
/// Маршрути, які шлюз пропускає лише з дійсним токеном. Без токена запит до сервісу не йде взагалі —
/// клієнт одразу отримує 401 у форматі ProblemDetails.
///
/// Інші маршрути (розклад, записи, читання каталогу) лишаються такими, якими були: правило ЛР2/ЛР3 —
/// «поведінка наявних маршрутів не змінюється». Перевірку ролей (Admin) лишено сервісам, бо це їхні дані.
/// </summary>
public sealed class ProtectedRoutesMiddleware(RequestDelegate next)
{
    private static readonly string[] WriteMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (RequiresToken(ctx.Request) && ctx.User.Identity?.IsAuthenticated != true)
        {
            await ctx.ChallengeAsync("Bearer");
            return;
        }

        await next(ctx);
    }

    public static bool RequiresToken(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/api/v1/users", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return request.Path.StartsWithSegments("/api/v1/catalog", StringComparison.OrdinalIgnoreCase)
            && WriteMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase);
    }
}
