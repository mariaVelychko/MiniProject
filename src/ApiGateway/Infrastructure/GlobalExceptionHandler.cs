using Microsoft.AspNetCore.Diagnostics;

namespace ApiGateway.Infrastructure;

/// <summary>Єдина обробка необроблених винятків: клієнт отримує ProblemDetails, але ніколи — stack trace.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && ctx.RequestAborted.IsCancellationRequested)
        {
            log.LogInformation("Клієнт скасував запит {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            return true;
        }

        log.LogError(exception, "Необроблений виняток під час {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

        await Problems.WriteAsync(ctx, StatusCodes.Status500InternalServerError, "Внутрішня помилка шлюзу",
            "Сталася непередбачена помилка. Спробуйте пізніше.", ct: ct);

        return true;
    }
}
