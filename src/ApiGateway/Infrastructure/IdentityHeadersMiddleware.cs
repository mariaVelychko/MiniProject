using System.Security.Claims;

namespace ApiGateway.Infrastructure;

/// <summary>
/// Токен перевіряється один раз — тут, на шлюзі (ЛР3, 3.3). Далі вниз іде і сам токен (Authorization), і
/// заголовки з уже перевіреними даними: X-User-Id та X-User-Roles.
///
/// ВАЖЛИВО: заголовки X-User-* від клієнта ЗАВЖДИ видаляються, інакше будь-хто підставив би чужий ідентифікатор.
/// Значення беруться лише з перевіреного токена. Довіряти цим заголовкам сервіс може лише тоді, коли ззовні
/// до нього не достукатися (у docker-compose порти сервісів не публікуються; локально вони слухають лише 127.0.0.1).
/// </summary>
public sealed class IdentityHeadersMiddleware(RequestDelegate next)
{
    public const string UserIdHeader = "X-User-Id";
    public const string UserRolesHeader = "X-User-Roles";

    public Task InvokeAsync(HttpContext ctx)
    {
        ctx.Request.Headers.Remove(UserIdHeader);
        ctx.Request.Headers.Remove(UserRolesHeader);

        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.User.FindFirstValue("sub");

            if (!string.IsNullOrWhiteSpace(userId))
            {
                ctx.Request.Headers[UserIdHeader] = userId;
            }

            var roles = ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

            if (roles.Length > 0)
            {
                ctx.Request.Headers[UserRolesHeader] = string.Join(',', roles);
            }
        }

        return next(ctx);
    }
}
