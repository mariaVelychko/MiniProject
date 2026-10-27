using ApiGateway;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.IntegrationTests;

/// <summary>
/// Один фейковий «бекенд» на всі тести (колекція «Gateway» виконується послідовно). Шлюз знаходить його через змінні
/// середовища Services__{Auth|Catalog|Monolith|Schedule}__Host/Port — це той самий механізм, що в docker-compose.
/// Змінні задаються до створення першого шлюзу, бо Program.cs читає їх під час запуску.
/// </summary>
public sealed class UpstreamFixture : IAsyncLifetime
{
    public FakeUpstream Fake { get; } = new();

    public async Task InitializeAsync()
    {
        await Fake.StartAsync();

        foreach (var service in new[] { "Auth", "Catalog", "Monolith", "Schedule" })
        {
            Environment.SetEnvironmentVariable($"Services__{service}__Host", "127.0.0.1");
            Environment.SetEnvironmentVariable($"Services__{service}__Port", Fake.Port.ToString());
        }
    }

    public async Task DisposeAsync() => await Fake.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class GatewayCollection : ICollectionFixture<UpstreamFixture>
{
    public const string Name = "Gateway";
}

/// <summary>
/// Справжній шлюз (Program.cs, Ocelot, JWT, кеш, ліміти) у тестовому хості. Кожен тест створює СВІЙ шлюз:
/// свій кеш і свої лічильники лімітів, тож тести не залежать ні від порядку, ні від даних одне одного.
/// </summary>
public sealed class GatewayFactory(Action<GatewayOptions>? tune = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = TestTokens.Key,
            ["Jwt:Issuer"] = TestTokens.Issuer,
            ["Jwt:Audience"] = TestTokens.Audience,
        }));

        builder.ConfigureServices(services =>
        {
            if (tune is not null)
            {
                services.PostConfigure(tune);
            }
        });
    }
}
