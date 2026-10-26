using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using ScheduleService.Contracts;

namespace ScheduleService.Swagger;

public static class SwaggerSetup
{
    public static IServiceCollection AddScheduleSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ScheduleService",
                Version = "v1",
                Description =
                    "Розклад тренувань і записи клієнтів (винесено з моноліту SportClub, ЛР2). " +
                    "Маршрути та формат відповідей збігаються з монолітом. Як і в монолітi, ендпоїнти " +
                    "не вимагають токена (див. ADR-0003). Помилки бізнес-логіки — {\"message\": \"...\"}; " +
                    "помилки валідації моделі — ProblemDetails (RFC 9457).",
            });

            var xml = Path.Combine(AppContext.BaseDirectory, $"{typeof(SwaggerSetup).Assembly.GetName().Name}.xml");

            if (File.Exists(xml))
            {
                options.IncludeXmlComments(xml);
            }

            options.SchemaFilter<ExamplesSchemaFilter>();
        });

        return services;
    }
}

/// <summary>Приклади запитів і відповідей у Swagger.</summary>
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
            var t when t == typeof(WorkoutRequest) => JsonNode.Parse(
                """{"catalogItemId":1,"time":"2026-11-01T18:00:00","hall":"Зал 1","coachId":2,"durationMin":60,"maxParticipants":10}"""),
            var t when t == typeof(BookingRequest) => JsonNode.Parse("""{"workoutId":1,"clientId":3}"""),
            var t when t == typeof(WorkoutResponse) => JsonNode.Parse(
                """{"id":1,"catalogItemId":1,"type":"Йога для початківців","time":"2026-11-01T18:00:00","durationMin":60,"hall":"Зал 1","coachId":2,"coachName":"Іван Ковальчук","participants":1,"maxParticipants":10,"participantIds":[3]}"""),
            var t when t == typeof(ErrorResponse) => JsonNode.Parse("""{"message":"Вільних місць немає"}"""),
            _ => s.Example,
        };
    }
}
