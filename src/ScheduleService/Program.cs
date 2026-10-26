using Microsoft.EntityFrameworkCore;
using ScheduleService.Clients;
using ScheduleService.Data;
using ScheduleService.Infrastructure;
using ScheduleService.Repositories;
using ScheduleService.Services;
using ScheduleService.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScheduleSwagger();

// Рядок підключення читається під час першого використання DbContext, тож тести можуть підмінити його через конфігурацію.
builder.Services.AddDbContext<ScheduleDbContext>((sp, options) =>
{
    var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("ScheduleDb");

    if (string.IsNullOrWhiteSpace(cs))
    {
        throw new InvalidOperationException(
            "Не задано ConnectionStrings:ScheduleDb. Задайте його через user-secrets або змінну середовища " +
            "ConnectionStrings__ScheduleDb (див. .env.example).");
    }

    options.UseMySql(cs, new MySqlServerVersion(new Version(8, 0, 39)));
});

builder.Services.AddScoped<IWorkoutRepository, WorkoutRepository>();
builder.Services.AddScoped<IIdempotencyStore, IdempotencyStore>();
builder.Services.AddScoped<IWorkoutService, WorkoutService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IdempotencyFilter>();

builder.Services.AddTransient<CorrelationIdHandler>();

builder.Services.AddHttpClient<ICatalogClient, CatalogClient>((sp, client) =>
    {
        var cfg = sp.GetRequiredService<IConfiguration>();
        client.BaseAddress = new Uri(cfg["Services:Catalog:BaseUrl"] ?? "http://localhost:5002");
        client.Timeout = TimeSpan.FromSeconds(10); // «страхувальний» ліміт; робочий дедлайн — у CatalogClient
    })
    .AddHttpMessageHandler<CorrelationIdHandler>();

builder.Services.AddHttpClient<IUserDirectoryClient, UserDirectoryClient>((sp, client) =>
    {
        var cfg = sp.GetRequiredService<IConfiguration>();
        client.BaseAddress = new Uri(cfg["Services:Users:BaseUrl"] ?? "http://localhost:5100");
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddHttpMessageHandler<CorrelationIdHandler>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

// /health перевіряє власну БД і доступність обох сусідів (Catalog, моноліт — довідник користувачів).
app.MapGet("/health", async (ScheduleDbContext db, ICatalogClient catalog, IUserDirectoryClient users, CancellationToken ct) =>
{
    var dbOk = await SafeAsync(() => db.Database.CanConnectAsync(ct));
    var catalogOk = await SafeAsync(() => catalog.PingAsync(ct));
    var usersOk = await SafeAsync(() => users.PingAsync(ct));
    var ok = dbOk && catalogOk && usersOk;

    return Results.Json(new
    {
        status = ok ? "ok" : "degraded",
        service = "ScheduleService",
        dependencies = new
        {
            database = dbOk ? "ok" : "down",
            catalog = catalogOk ? "ok" : "down",
            monolith = usersOk ? "ok" : "down",
        },
    }, statusCode: ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
})
.WithTags("Health");

if (!await TryInitializeDatabaseAsync(app))
{
    return;
}

await app.RunAsync();

static async Task<bool> SafeAsync(Func<Task<bool>> check)
{
    try
    {
        return await check();
    }
    catch
    {
        return false;
    }
}

static async Task<bool> TryInitializeDatabaseAsync(WebApplication app)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
        await DatabaseInitializer.EnsureReadyAsync(db);
        return true;
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex,
            "ScheduleService не зміг ініціалізувати БД. Перевірте: 1) MySQL запущено; 2) ConnectionStrings:ScheduleDb " +
            "(user-secrets / змінна середовища / start-all.ps1); 3) після невдалої спроби запустіть з RESET_DB=1.");
        return false;
    }
}

// Потрібно для WebApplicationFactory<Program> в інтеграційних тестах.
public partial class Program;
