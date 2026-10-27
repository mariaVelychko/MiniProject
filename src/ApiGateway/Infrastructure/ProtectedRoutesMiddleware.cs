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
            // Схема "Bearer" = JwtBearerDefaults.AuthenticationScheme; відповідь формує OnChallenge (ProblemDetails 401).
            await ctx.ChallengeAsync("Bearer");
            return;
        }

        await next(ctx);
    }

    public static bool RequiresToken(HttpRequest request)
    {
        // Профіль і користувачі — завжди персональні.
        if (request.Path.StartsWithSegments("/api/v1/users", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Каталог: читати може будь-хто, змінювати — лише з токеном (роль Admin перевіряє сам Catalog).
        return request.Path.StartsWithSegments("/api/v1/catalog", StringComparison.OrdinalIgnoreCase)
            && WriteMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase);
    }
}
