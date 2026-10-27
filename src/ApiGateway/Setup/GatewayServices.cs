using ApiGateway.Clients;
using ApiGateway.Infrastructure;
using Microsoft.Extensions.Options;

namespace ApiGateway.Setup;

public static class GatewayServices
{
    /// <summary>Усе, що не залежить від Ocelot, JwtBearer і Swagger: опції, кеш, клієнти, помилки, ліміти.</summary>
    public static IServiceCollection AddGateway(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<GatewayOptions>(config.GetSection(GatewayOptions.SectionName));
        services.AddSingleton<ServiceUrls>();

        services.AddMemoryCache();
        services.AddSingleton<CatalogCacheRegion>();
        services.AddSingleton<CacheStats>();

        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdHandler>();

        // Таймаут кожного вихідного виклику — GatewayOptions.Timeouts.ClientSeconds (читається ліниво).
        services.AddHttpClient<ICatalogClient, CatalogClient>(Configure("Catalog", 5002)).AddHttpMessageHandler<CorrelationIdHandler>();
        services.AddHttpClient<IScheduleClient, ScheduleClient>(Configure("Schedule", 5102)).AddHttpMessageHandler<CorrelationIdHandler>();
        services.AddHttpClient<IAuthClient, AuthClient>(Configure("Auth", 5001)).AddHttpMessageHandler<CorrelationIdHandler>();
        services.AddHttpClient("health");

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddGatewayRateLimiting();

        return services;
    }

    private static Action<IServiceProvider, HttpClient> Configure(string service, int defaultPort) => (sp, client) =>
    {
        client.BaseAddress = sp.GetRequiredService<ServiceUrls>().Build(service, defaultPort);
        client.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<GatewayOptions>>().Value.Timeouts.ClientSeconds);
    };
}
