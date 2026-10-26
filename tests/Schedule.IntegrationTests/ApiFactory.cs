using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScheduleService.Clients;
using ScheduleService.Data;

namespace Schedule.IntegrationTests;

public static class TestDb
{
    /// <summary>
    /// Тестова БД — ЗАВЖДИ окрема (назва закінчується на _test): тести видаляють і перестворюють схему.
    /// Рядок підключення: змінна TEST_SCHEDULE_DB, або типовий + пароль із TEST_MYSQL_PASSWORD.
    /// </summary>
    public static string ConnectionString
    {
        get
        {
            var cs = Environment.GetEnvironmentVariable("TEST_SCHEDULE_DB");

            if (string.IsNullOrWhiteSpace(cs))
            {
                var password = Environment.GetEnvironmentVariable("TEST_MYSQL_PASSWORD") ?? string.Empty;
                cs = $"server=localhost;port=3307;database=miniproject_schedule_test;user=root;password={password};";
            }

            var dbName = cs.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .FirstOrDefault(p => p.Length == 2 && p[0].Trim().Equals("database", StringComparison.OrdinalIgnoreCase))?[1]
                .Trim();

            if (dbName is null || !dbName.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Небезпечно: тестова БД має називатися *_test, а в рядку підключення '{dbName}'. Тести стирають схему.");
            }

            return cs;
        }
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public StubCatalogClient Catalog { get; } = new();
    public StubUserDirectoryClient Users { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ScheduleDb", TestDb.ConnectionString);

        builder.ConfigureServices(services =>
        {
            // Мережевих сусідів у тестах немає: підставляємо стаби з відомими відповідями.
            services.RemoveAll<ICatalogClient>();
            services.RemoveAll<IUserDirectoryClient>();
            services.AddSingleton<ICatalogClient>(Catalog);
            services.AddSingleton<IUserDirectoryClient>(Users);
        });
    }
}

/// <summary>Одна фабрика і одна свіжа схема на всі тести (колекція «Api» виконується послідовно).</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public ApiFactory Factory { get; } = new();

    public async Task InitializeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
        await db.Database.EnsureDeletedAsync();
        await DatabaseInitializer.EnsureReadyAsync(db);
    }

    public async Task DisposeAsync() => await Factory.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
