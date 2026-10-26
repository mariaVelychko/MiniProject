using System.Diagnostics;

namespace ScheduleService.Infrastructure;

public static class CorrelationId
{
    public const string Header = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";
}

/// <summary>
/// Бере X-Correlation-Id від шлюзу (або генерує свій), повертає його у відповіді, кладе в scope логів
/// і пише рядок запиту. Паролі, токени й тіла запитів у логи не потрапляють.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[CorrelationId.Header].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString("N");
        }

        context.Items[CorrelationId.ItemKey] = id;
        context.Response.Headers[CorrelationId.Header] = id;

        using var _ = log.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id });
        var timer = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            timer.Stop();

            var path = context.Request.Path.Value ?? string.Empty;

            if (!path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
            {
                log.LogInformation("[ScheduleService] {Method} {Path}{Query} -> {Status} ({Elapsed} ms)",
                    context.Request.Method, path, context.Request.QueryString,
                    context.Response.StatusCode, timer.ElapsedMilliseconds);
            }
        }
    }
}

/// <summary>Передає кореляційний id у виклики до Catalog і моноліту.</summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (accessor.HttpContext?.Items[CorrelationId.ItemKey] is string id)
        {
            request.Headers.TryAddWithoutValidation(CorrelationId.Header, id);
        }

        return base.SendAsync(request, ct);
    }
}
