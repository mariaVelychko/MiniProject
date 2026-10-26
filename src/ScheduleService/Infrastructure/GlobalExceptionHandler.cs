using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ScheduleService.Infrastructure;

/// <summary>Єдина обробка необроблених винятків: клієнт отримує ProblemDetails без stack trace.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            return true; // клієнт розірвав з'єднання — відповідати нікому
        }

        log.LogError(exception, "Необроблений виняток під час {Method} {Path}",
            context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Внутрішня помилка сервера",
            Detail = "Сталася непередбачена помилка. Спробуйте пізніше.",
        }, options: null, contentType: "application/problem+json", cancellationToken: ct);

        return true;
    }
}
