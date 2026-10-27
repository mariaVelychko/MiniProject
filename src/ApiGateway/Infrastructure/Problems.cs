using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Infrastructure;

public static class Correlation
{
    public const string Header = "X-Correlation-Id";
}

/// <summary>Єдиний формат помилок шлюзу — ProblemDetails (RFC 9457), зокрема 401, 429, 503, 504.</summary>
public static class Problems
{
    public const string ContentType = "application/problem+json";

    public static ProblemDetails Create(HttpContext ctx, int status, string title, string? detail = null,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
            Instance = ctx.Request.Path.Value,
        };

        if (ctx.Request.Headers.TryGetValue(Correlation.Header, out var correlationId))
        {
            problem.Extensions["correlationId"] = correlationId.ToString();
        }

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        return problem;
    }

    /// <summary>Записати помилку прямо у відповідь (для middleware).</summary>
    public static Task WriteAsync(HttpContext ctx, int status, string title, string? detail = null,
        IDictionary<string, object?>? extensions = null, CancellationToken ct = default)
    {
        // Content-Length міг лишитися від сурогатної відповіді Ocelot — він зламав би запис тіла.
        ctx.Response.ContentLength = null;
        ctx.Response.StatusCode = status;

        return ctx.Response.WriteAsJsonAsync(Create(ctx, status, title, detail, extensions),
            options: null, contentType: ContentType, cancellationToken: ct);
    }

    /// <summary>Те саме для minimal API (повертається з обробника).</summary>
    public static IResult ToResult(HttpContext ctx, int status, string title, string? detail = null,
        IDictionary<string, object?>? extensions = null) =>
        Results.Json(Create(ctx, status, title, detail, extensions), options: null, contentType: ContentType, statusCode: status);
}
