using SportClub.Api.Services;
using SportClubLibrary.Data;
using SportClubLibrary.Models;
using SportClubLibrary.Repositories;
using SportClubLibrary.Services;
using SportClubLibrary.Utilities;
using System.Diagnostics;
using SportClub.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

string connectionString = builder.Configuration.GetConnectionString("SportClub")
    ?? "server=localhost;port=3307;database=sportclub_cs;user=root;";

builder.Services.AddSingleton<global::ILogger, FileLogger>();
builder.Services.AddSingleton<Func<SportClubDbContext>>(_ =>
    () => new SportClubDbContext(connectionString));
builder.Services.AddSingleton<IAsyncRepository<Person>>(sp =>
    RepositoryFactory.CreatePersonRepository(
        sp.GetRequiredService<Func<SportClubDbContext>>(),
        sp.GetRequiredService<global::ILogger>()));
builder.Services.AddSingleton<IAsyncRepository<Workout>>(sp =>
    RepositoryFactory.CreateWorkoutRepository(
        sp.GetRequiredService<Func<SportClubDbContext>>(),
        sp.GetRequiredService<global::ILogger>()));
builder.Services.AddScoped<AuthService>();

builder.Services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Catalog:BaseUrl"] ?? "http://localhost:5002");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient<IScheduleClient, ScheduleClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Schedule:BaseUrl"] ?? "http://localhost:5102");
    client.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;

    if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var timer = Stopwatch.StartNew();
    await next();
    timer.Stop();

    app.Logger.LogInformation("[Monolith] {Method} {Path}{Query} -> {Status} ({Elapsed} ms)",
        context.Request.Method, path, context.Request.QueryString,
        context.Response.StatusCode, timer.ElapsedMilliseconds);
});

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Monolith" }));

if (!await TryInitializeDatabaseAsync(app))
{
    return;
}

await app.RunAsync();

static async Task<bool> TryInitializeDatabaseAsync(WebApplication app)
{
    try
    {
        var contextFactory = app.Services.GetRequiredService<Func<SportClubDbContext>>();

        using (var db = contextFactory())
        {
            await DatabaseInitializer.EnsureReadyAsync(db);
        }

        var users = app.Services.GetRequiredService<IAsyncRepository<Person>>();

        if (!(await users.GetAllAsync()).Any())
        {
            await users.AddAsync(new Admin
            {
                FullName = "Головний Адмін",
                Email = "admin",
                Phone = "+38 (000) 000-00-00",
                Password = PasswordHasher.Hash("admin"),
            });
        }

        return true;
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex,
            "Моноліт не зміг підключитися до БД. Перевірте: 1) MySQL запущено; 2) пароль/назва бази в " +
            "ConnectionStrings:SportClub (або запустіть start-all.ps1, він спитає пароль); 3) якщо база " +
            "лишилась від невдалої спроби — запустіть з RESET_DB=1 (start-all.ps1 -ResetDb).");
        return false;
    }
}
