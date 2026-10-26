using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.Diagnostics;

const string CorrelationHeader = "X-Correlation-Id";

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.SetBasePath(builder.Environment.ContentRootPath).AddOcelot();

// Адреси сусідів можна перекрити конфігурацією: Services:{Auth|Catalog|Monolith|Schedule}:Host / :Port
// (а також старі Monolith:Host / Monolith:Port). Перекривається лише маршрут, чий типовий порт належить цьому сервісу.
var portToService = new Dictionary<string, string>
{
    ["5001"] = "Auth", ["5002"] = "Catalog", ["5100"] = "Monolith", ["5102"] = "Schedule",
};
var overrides = new Dictionary<string, string?>();

for (var i = 0; builder.Configuration[$"Routes:{i}:UpstreamPathTemplate"] is not null; i++)
{
    var defaultPort = builder.Configuration[$"Routes:{i}:DownstreamHostAndPorts:0:Port"];

    if (defaultPort is null || !portToService.TryGetValue(defaultPort, out var service))
    {
        continue;
    }

    var host = builder.Configuration[$"Services:{service}:Host"]
        ?? (service == "Monolith" ? builder.Configuration["Monolith:Host"] : null);
    var port = builder.Configuration[$"Services:{service}:Port"]
        ?? (service == "Monolith" ? builder.Configuration["Monolith:Port"] : null);

    if (host is not null)
    {
        overrides[$"Routes:{i}:DownstreamHostAndPorts:0:Host"] = host;
    }

    if (port is not null)
    {
        overrides[$"Routes:{i}:DownstreamHostAndPorts:0:Port"] = port;
    }
}

builder.Configuration.AddInMemoryCollection(overrides);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["*"])
    .AllowAnyHeader().AllowAnyMethod().WithExposedHeaders(CorrelationHeader)));
builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gateway");

app.UseCors();
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers[CorrelationHeader].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
        context.Request.Headers[CorrelationHeader] = correlationId;
    }

    context.Response.Headers[CorrelationHeader] = correlationId;
    var path = $"{context.Request.Path}{context.Request.QueryString}";
    var timer = Stopwatch.StartNew();
    await next();
    timer.Stop();

    log.LogInformation("{Method} {Path} -> {Target} | {Status} | {Elapsed} ms | {CorrelationId}",
        context.Request.Method, path, context.Items.DownstreamRequest()?.ToUri() ?? "gateway",
        context.Response.StatusCode, timer.ElapsedMilliseconds, correlationId);
});
app.Map("/health", branch => branch.Run(context =>
    context.Response.WriteAsJsonAsync(new { status = "ok", service = "ApiGateway" })));

await app.UseOcelot();
await app.RunAsync();
