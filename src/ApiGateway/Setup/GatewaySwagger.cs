using ApiGateway.Infrastructure;
using ApiGateway.Screens;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;


namespace ApiGateway.Setup;

public static class GatewaySwagger
{
    public static IServiceCollection AddGatewaySwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ApiGateway",
                Version = "v1",
                Description =
                    "Єдині вхідні двері системи (ЛР3): усі запити клієнта йдуть через порт 5000. Шлюз збирає екрани з кількох " +
                    "сервісів, перевіряє JWT, кешує спільні дані каталогу, обмежує частоту запитів і ставить таймаути на всі виклики. " +
                    "Бізнес-логіки в шлюзі немає.\n\n" +
                    "**Власні ендпоінти шлюзу** описано нижче. Решта маршрутів лише проксіюється на сервіси (Ocelot) і описана в " +
                    "їхніх Swagger: `/api/v1/auth/**`, `/api/v1/users/**` → Auth; `/api/v1/catalog/**` → Catalog; " +
                    "`/api/workouts/**`, `/api/bookings/**` → Schedule; решта `/api/**` → моноліт.\n\n" +
                    "**Захищено токеном:** `/api/v1/users/**`, зміна каталогу (`POST/PUT/PATCH/DELETE /api/v1/catalog/**`) і екрани.\n\n" +
                    "**Помилки** шлюзу завжди у форматі ProblemDetails (RFC 9457): 401 (немає/недійсний токен), 429 (ліміт, " +
                    "заголовок `Retry-After`), 503 (сервіс недоступний), 504 (сервіс не відповів вчасно).\n\n" +
                    "**Ліміти:** анонім — 100 запитів/хв з IP; авторизований — token bucket за користувачем (100 у відрі, +120/хв); " +
                    "вхід і реєстрація — 5 спроб/хв з IP.",
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Токен із POST /api/v1/auth/login (поле accessToken). Вводьте лише сам токен, без слова Bearer.",
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
            });

            options.SchemaFilter<ExamplesSchemaFilter>();
        });

        return services;
    }
}

/// <summary>Приклади відповідей у Swagger.</summary>
public sealed class ExamplesSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema s)
        {
            return;
        }

        s.Example = context.Type switch
        {
            var t when t == typeof(TrainingScreenResponse) => JsonNode.Parse(
                """{"item":{"id":1,"title":"Йога для початківців","description":"Базовий рівень","price":250.00,"durationMin":60,"categoryName":"Йога","isActive":true},"sessions":[{"id":1,"time":"2026-12-01T18:00:00","durationMin":60,"hall":"Зал 1","coachName":"Іван Ковальчук","participants":2,"maxParticipants":10}],"me":{"id":2,"displayName":"Марія","roles":["Client"]},"partial":false,"unavailable":[]}"""),
            var t when t == typeof(ProblemDetails) => JsonNode.Parse(
                """{"type":"https://httpstatuses.io/429","title":"Забагато запитів","status":429,"detail":"Перевищено ліміт запитів. Повторіть спробу через 42 с.","instance":"/api/v1/screens/training/1","correlationId":"6f1c0b8e2a7d4c9f","retryAfterSeconds":42}"""),
            var t when t == typeof(CacheStatsSnapshot) => JsonNode.Parse("""{"hits":12,"misses":3,"invalidations":1,"hitRatio":0.8}"""),
            _ => s.Example,
        };
    }
}
