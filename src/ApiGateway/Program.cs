using System.Diagnostics;
using ApiGateway.Infrastructure;
using ApiGateway.Screens;
using ApiGateway.Setup;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

const string CorrelationHeader = Correlation.Header;

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

// ЛР3: складений ендпоінт, кеш, ліміти, таймаути (GatewayServices), перевірка JWT на шлюзі та Swagger.
builder.Services.AddGateway(builder.Configuration);
builder.Services.AddGatewayAuthentication();
builder.Services.AddGatewaySwagger();

var app = builder.Build();
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gateway");

app.UseCors();

// Кореляційний id + журнал запиту. Токени й тіла запитів у журнал не потрапляють.
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

app.UseExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "ApiGateway v1"));

app.UseRouting();
app.UseAuthentication();     // токен перевіряється один раз, тут
app.UseRateLimiter();        // після автентифікації: авторизованих рахуємо за користувачем, анонімів — за IP
app.UseAuthorization();
app.UseMiddleware<IdentityHeadersMiddleware>();   // X-User-Id / X-User-Roles лише з токена; клієнтські — видаляються

// Власні ендпоінти шлюзу (складені екрани, health, метрики кешу).
app.MapTrainingScreen();
app.MapGatewayEndpoints();

// Усе, що не є власним ендпоінтом шлюзу, проксіюється Ocelot: захист маршрутів → кеш каталогу → таймаут → Ocelot.
app.UseWhen(context => context.GetEndpoint() is null, branch =>
{
    branch.UseMiddleware<ProtectedRoutesMiddleware>();
    branch.UseMiddleware<CatalogCacheMiddleware>();
    branch.UseMiddleware<ProxyTimeoutMiddleware>();
    branch.UseOcelot().Wait();
});

await app.RunAsync();

// Потрібен інтеграційним тестам (WebApplicationFactory<Program>).
public partial class Program;
