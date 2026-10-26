using CatalogService.Data;
using CatalogService.Models;
using CatalogService.Repositories;
using CatalogService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Shared.Data;
using Shared.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("CatalogDb")
    ?? throw new InvalidOperationException("Не задано ConnectionStrings:CatalogDb.");

builder.Services.AddScoped(_ => new CatalogDbContext(connectionString));

builder.Services.AddScoped<IItemRepository, ItemRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<CatalogItemService>();

builder.Services.AddControllers();

builder.Services.AddJwtAuth(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "CatalogService", Version = "v1" });

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
        Description = "Вставте JWT, виданий AuthService (без слова \"Bearer\").",
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>(),
    });
});

var app = builder.Build();

app.UseRequestLogging("CatalogService");
app.UseApiExceptionHandling();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", async (CatalogDbContext db) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync();

        return canConnect
            ? Results.Ok(new { status = "ok", service = "CatalogService" })
            : Results.Json(new { status = "degraded", service = "CatalogService" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Json(new { status = "down", service = "CatalogService" }, statusCode: StatusCodes.Status503ServiceUnavailable);
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
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        await DatabaseInitializer.EnsureReadyAsync(db);

        if (!await db.Items.AnyAsync())
        {
            db.Items.AddRange(
                NewItem("Йога для початківців", "Базовий рівень, без попередньої підготовки.", 250, 60, 15, 1),
                NewItem("Хатха-йога", "Спокійний темп, акцент на розтяжці.", 260, 75, 15, 1),
                NewItem("Ранкова йога", "Бадьорий старт дня, 45 хвилин.", 220, 45, 20, 1),
                NewItem("Йога для спини", "Зміцнення м'язів спини й корпусу.", 270, 60, 12, 1),
                NewItem("Кросфіт WOD", "Інтенсивне групове тренування дня.", 300, 45, 12, 2),
                NewItem("Кросфіт для початківців", "Вивчення базових рухів зі штангою.", 280, 60, 10, 2),
                NewItem("Кросфіт: гімнастика", "Підтягування, віджимання, стійки.", 310, 50, 10, 2),
                NewItem("Функціональний кросфіт", "Кардіо + силові комплекси.", 320, 55, 12, 2),
                NewItem("Бокс: техніка ударів", "Постановка удару, робота на лапах.", 350, 60, 10, 3),
                NewItem("Бокс для початківців", "Стійка, захист, базові комбінації.", 330, 60, 12, 3),
                NewItem("Спаринг-клас", "Умовний спаринг у захисному спорядженні.", 380, 75, 8, 3),
                NewItem("Бокс: кардіо", "Бокс як інтервальне кардіо.", 340, 45, 15, 3));

            await db.SaveChangesAsync();
        }

        return true;
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex,
            "CatalogService не зміг ініціалізувати БД. Перевірте: 1) MySQL запущено; 2) пароль у " +
            "ConnectionStrings:CatalogDb (або запустіть start-all.ps1); 3) якщо база лишилась від невдалої " +
            "спроби — запустіть з RESET_DB=1 (start-all.ps1 -ResetDb).");
        return false;
    }
}

static Item NewItem(string title, string description, decimal price, int durationMin, int capacity, int categoryId) =>
    new()
    {
        Title = title,
        Description = description,
        Price = price,
        DurationMin = durationMin,
        Capacity = capacity,
        CategoryId = categoryId,
    };
