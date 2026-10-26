using AuthService.Data;
using AuthService.Models;
using AuthService.Repositories;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Shared.Configuration;
using Shared.Data;
using Shared.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("AuthDb")
    ?? throw new InvalidOperationException("Не задано ConnectionStrings:AuthDb.");

builder.Services.AddScoped(_ => new AuthDbContext(connectionString));

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped(sp => new TokenService(sp.GetRequiredService<JwtOptions>()));
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<UserService>();

builder.Services.AddControllers();

builder.Services.AddJwtAuth(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "AuthService", Version = "v1" });

    var xmlFile = Path.Combine(AppContext.BaseDirectory,
        $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");

    if (File.Exists(xmlFile))
    {
        options.IncludeXmlComments(xmlFile);
    }

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Вставте лише сам токен, без слова \"Bearer\" — воно додається автоматично.",
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>(),
    });
});

var app = builder.Build();

app.UseRequestLogging("AuthService");
app.UseApiExceptionHandling();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", async (AuthDbContext db) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync();

        return canConnect
            ? Results.Ok(new { status = "ok", service = "AuthService" })
            : Results.Json(new { status = "degraded", service = "AuthService" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Json(new { status = "down", service = "AuthService" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

if (!await TryInitializeDatabaseAsync(app))
{
    return;
}

await app.RunAsync();

static async Task<bool> TryInitializeDatabaseAsync(WebApplication app)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        await DatabaseInitializer.EnsureReadyAsync(db);

        if (!await db.Users.AnyAsync())
        {
            var admin = new User
            {
                Email = "admin@miniproject.local",
                DisplayName = "Головний Адмін",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
            };

            admin.UserRoles.Add(new UserRole { RoleId = 2 });

            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }

        return true;
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex,
            "AuthService не зміг ініціалізувати БД. Перевірте: 1) MySQL запущено; 2) пароль у " +
            "ConnectionStrings:AuthDb (або запустіть start-all.ps1); 3) якщо база лишилась від невдалої " +
            "спроби — запустіть з RESET_DB=1 (start-all.ps1 -ResetDb).");
        return false;
    }
}
