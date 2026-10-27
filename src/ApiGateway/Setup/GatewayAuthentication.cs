using System.Security.Cryptography;
using System.Text;
using ApiGateway.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.Setup;

public static class GatewayAuthentication
{
    /// <summary>
    /// JWT перевіряється на шлюзі (ЛР3, 3.3). Ключ, видавець і аудиторія — ті самі, що в Auth/Catalog (секція "Jwt").
    /// Ключ не зберігається в репозиторії: його задає змінна середовища Jwt__Key (start-all.ps1 / .env).
    /// Налаштування читаються під час першого запиту, а не на старті.
    /// </summary>
    public static IServiceCollection AddGatewayAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration, ILoggerFactory>((options, config, loggers) =>
            {
                var key = config["Jwt:Key"];

                if (string.IsNullOrWhiteSpace(key))
                {
                    // Публічні маршрути (розклад, читання каталогу) мають працювати й без ключа, тож не падаємо:
                    // випадковий ключ означає, що жоден токен не пройде перевірку (захищені маршрути дадуть 401).
                    loggers.CreateLogger("Gateway").LogWarning(
                        "Jwt:Key не задано — усі токени відхилятимуться. Задайте змінну середовища Jwt__Key (той самий ключ, що в Auth).");
                    key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = config["Jwt:Issuer"],
                    ValidAudience = config["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                options.Events = new JwtBearerEvents
                {
                    // 401 — завжди ProblemDetails, без звернення до сервісів.
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers["WWW-Authenticate"] = "Bearer";

                        var detail = context.AuthenticateFailure is null
                            ? "Потрібен заголовок Authorization: Bearer <токен>."
                            : "Токен недійсний або прострочений.";

                        await Problems.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                            "Потрібна автентифікація", detail, ct: context.HttpContext.RequestAborted);
                    },
                    OnForbidden = context => Problems.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                        "Доступ заборонено", "Недостатньо прав для цієї дії.", ct: context.HttpContext.RequestAborted),
                };
            });

        services.AddAuthorization();

        return services;
    }
}
