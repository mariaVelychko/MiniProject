using ApiGateway.Clients;
using ApiGateway.Infrastructure;
using Microsoft.Extensions.Options;

namespace ApiGateway.Screens;

/// <summary>Службові ендпоінти шлюзу: /health (з урахуванням сервісів) та /gateway/cache (метрики кешу).</summary>
public static class GatewayEndpoints
{
    public static IEndpointRouteBuilder MapGatewayEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", HealthAsync)
            .WithTags("Gateway")
            .WithSummary("Стан шлюзу та доступність сервісів")
            .WithDescription("200 — усі сервіси відповідають на /health; 503 — хоча б один недоступний (деталі в `dependencies`). " +
                             "Приклад 503: `{\"status\":\"degraded\",\"service\":\"ApiGateway\",\"dependencies\":{\"auth\":\"ok\",\"catalog\":\"down\",\"monolith\":\"ok\",\"schedule\":\"ok\"}}`")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/gateway/cache", (CacheStats stats) => Results.Ok(stats.Snapshot()))
            .WithTags("Gateway")
            .WithSummary("Лічильники кешу: влучання, промахи, скидання")
            .Produces<CacheStatsSnapshot>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> HealthAsync(IHttpClientFactory factory, ServiceUrls urls,
        IOptions<GatewayOptions> options, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(options.Value.Timeouts.HealthSeconds);

        var targets = new (string Name, Uri Url)[]
        {
            ("auth", new Uri(urls.Auth, "health")),
            ("catalog", new Uri(urls.Catalog, "health")),
            ("monolith", new Uri(urls.Monolith, "health")),
            ("schedule", new Uri(urls.Schedule, "health")),
        };

        var checks = await Task.WhenAll(targets.Select(async t =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(timeout);

            try
            {
                var http = factory.CreateClient("health");
                using var response = await http.GetAsync(t.Url, deadline.Token);
                return (t.Name, Up: response.IsSuccessStatusCode);
            }
            catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !ct.IsCancellationRequested)
            {
                return (t.Name, Up: false);
            }
        }));

        var ok = checks.All(c => c.Up);

        return Results.Json(new
        {
            status = ok ? "ok" : "degraded",
            service = "ApiGateway",
            dependencies = checks.ToDictionary(c => c.Name, c => c.Up ? "ok" : "down"),
        }, statusCode: ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }
}
