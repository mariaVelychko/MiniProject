using System.Text.Json;
using ApiGateway.Clients;
using ApiGateway.Infrastructure;
using Microsoft.Extensions.Options;

namespace ApiGateway.Screens;

/// <summary>Запис користувача на тренування — у вигляді, зручному для екрана «Мої записи».</summary>
/// <param name="Status">"upcoming" — ще не почалось, "past" — уже минуло.</param>
public sealed record MyBooking(int WorkoutId, int? CatalogItemId, string Title, DateTime Time, int DurationMin,
    string Hall, string CoachName, int Participants, int MaxParticipants, string Status, bool CanCancel);

public sealed record CreateBookingRequest(int? WorkoutId);

public sealed record CreatedBooking(int WorkoutId, bool Replayed);

/// <summary>
/// «Мої записи» (ЛР4): клієнтський застосунок НЕ надсилає ідентифікатор користувача. Шлюз бере «хто це» з перевіреного
/// токена (профіль Auth → email → клієнт у довіднику моноліту) і сам підставляє clientId у виклики Schedule.
/// Чужий запис скасувати чи створити неможливо: clientId завжди належить власнику токена.
/// </summary>
public static class MyBookingsEndpoints
{
    public const string Route = "/api/v1/me/bookings";

    public static IEndpointRouteBuilder MapMyBookings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route).RequireAuthorization().WithTags("My bookings");

        group.MapGet("", ListAsync)
            .WithName("ListMyBookings")
            .WithSummary("Мої записи на тренування (зі статусом і ознакою «можна скасувати»)")
            .WithDescription("Користувач визначається за токеном. `status`: `upcoming` або `past`. Порожній масив означає «записів немає», " +
                             "а 503 — «не вдалося завантажити» (не плутати).")
            .Produces<List<MyBooking>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("", CreateAsync)
            .WithName("CreateMyBooking")
            .WithSummary("Записатися на тренування (тіло: {\"workoutId\": 5}; заголовок Idempotency-Key обов'язковий)")
            .WithDescription("Ідентифікатор клієнта не передається — береться з токена. Повтор із тим самим `Idempotency-Key` " +
                             "не створює другого запису (відповідь має заголовок `Idempotency-Replayed: true`). " +
                             "Помилки по полях — у `errors`: `{\"errors\":{\"workoutId\":[\"...\"]}}`.")
            .Produces<CreatedBooking>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapDelete("{workoutId:int}", CancelAsync)
            .WithName("CancelMyBooking")
            .WithSummary("Скасувати власний запис")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> ListAsync(HttpContext ctx, IAuthClient auth, IPersonDirectory people,
        IBookingsClient bookings, IOptions<GatewayOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var (person, error) = await ResolveClientAsync(ctx, auth, people, ct);

        if (error is not null)
        {
            return error;
        }

        var list = await bookings.GetByClientAsync(person!.Id, ct);

        if (!list.IsOk)
        {
            return Problems.ToResult(ctx, StatusCodes.Status503ServiceUnavailable, "Розклад недоступний",
                "Не вдалося завантажити ваші записи. Це не означає, що їх немає — спробуйте ще раз.");
        }

        var localNow = clock.GetUtcNow().UtcDateTime.AddHours(options.Value.LocalUtcOffsetHours);

        var items = list.Value!
            .OrderBy(w => w.Time)
            .Select(w =>
            {
                var upcoming = w.Time >= localNow;
                return new MyBooking(w.Id, w.CatalogItemId, w.Type, w.Time, w.DurationMin, w.Hall, w.CoachName,
                    w.Participants, w.MaxParticipants, upcoming ? "upcoming" : "past", upcoming);
            })
            .ToList();

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(HttpContext ctx, IAuthClient auth, IPersonDirectory people,
        IBookingsClient bookings, CancellationToken ct)
    {
        var workoutId = 0;

        try
        {
            var body = await ctx.Request.ReadFromJsonAsync<CreateBookingRequest>(ct);
            workoutId = body?.WorkoutId ?? 0;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
        }

        var key = ctx.Request.Headers["Idempotency-Key"].ToString().Trim();
        var errors = new Dictionary<string, string[]>();

        if (workoutId <= 0)
        {
            errors["workoutId"] = ["Оберіть тренування."];
        }

        if (key.Length == 0)
        {
            errors["idempotencyKey"] = ["Потрібен заголовок Idempotency-Key."];
        }
        else if (key.Length > 128)
        {
            errors["idempotencyKey"] = ["Idempotency-Key не може бути довшим за 128 символів."];
        }

        if (errors.Count > 0)
        {
            return Problems.ToResult(ctx, StatusCodes.Status400BadRequest, "Помилка валідації",
                extensions: new Dictionary<string, object?> { ["errors"] = errors });
        }

        var (person, error) = await ResolveClientAsync(ctx, auth, people, ct);

        if (error is not null)
        {
            return error;
        }

        var result = await bookings.BookAsync(workoutId, person!.Id, key, ct);

        switch (result.Outcome)
        {
            case BookingOutcome.Ok:
                if (result.Replayed)
                {
                    ctx.Response.Headers["Idempotency-Replayed"] = "true";
                }

                return Results.Json(new CreatedBooking(workoutId, result.Replayed), statusCode: StatusCodes.Status201Created);

            case BookingOutcome.NotFound:
                return FieldProblem(ctx, StatusCodes.Status404NotFound, "Тренування не знайдено",
                    result.Message ?? "Такого тренування немає.");

            case BookingOutcome.Conflict:
                return FieldProblem(ctx, StatusCodes.Status409Conflict, "Записатися не вдалося",
                    result.Message ?? "Запис неможливий.");

            case BookingOutcome.Rejected:
                return Problems.ToResult(ctx, StatusCodes.Status422UnprocessableEntity, "Запит відхилено",
                    result.Message ?? "Сервіс відхилив запит.");

            default:
                return Problems.ToResult(ctx, StatusCodes.Status503ServiceUnavailable, "Запис тимчасово недоступний",
                    "Сервіс не відповів. Спробуйте ще раз — повторний запит не створить дубль.");
        }
    }

    private static async Task<IResult> CancelAsync(int workoutId, HttpContext ctx, IAuthClient auth,
        IPersonDirectory people, IBookingsClient bookings, CancellationToken ct)
    {
        var (person, error) = await ResolveClientAsync(ctx, auth, people, ct);

        if (error is not null)
        {
            return error;
        }

        var result = await bookings.CancelAsync(workoutId, person!.Id, ct);

        return result.Outcome switch
        {
            BookingOutcome.Ok => Results.NoContent(),
            BookingOutcome.NotFound => Problems.ToResult(ctx, StatusCodes.Status404NotFound, "Запис не знайдено",
                result.Message ?? "Ви не записані на це тренування."),
            _ => Problems.ToResult(ctx, StatusCodes.Status503ServiceUnavailable, "Скасування тимчасово недоступне",
                "Сервіс не відповів. Спробуйте ще раз."),
        };
    }

    /// <summary>Хто робить запит: токен → профіль Auth (email) → клієнт у довіднику. Помилки — готові ProblemDetails.</summary>
    private static async Task<(DirectoryPerson? Person, IResult? Error)> ResolveClientAsync(HttpContext ctx,
        IAuthClient auth, IPersonDirectory people, CancellationToken ct)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : string.Empty;

        var me = await auth.GetMeAsync(token, ct);

        if (me.Status == UpstreamStatus.NotFound)
        {
            return (null, Problems.ToResult(ctx, StatusCodes.Status401Unauthorized, "Обліковий запис недоступний",
                "Токен більше не відповідає активному обліковому запису. Увійдіть знову."));
        }

        if (!me.IsOk)
        {
            return (null, Problems.ToResult(ctx, StatusCodes.Status503ServiceUnavailable, "Сервіс автентифікації недоступний",
                "Не вдалося визначити, хто ви. Спробуйте ще раз."));
        }

        var person = await people.FindClientByEmailAsync(me.Value!.Email, ct);

        if (person.Status == UpstreamStatus.NotFound)
        {
            return (null, Problems.ToResult(ctx, StatusCodes.Status403Forbidden, "Профіль клієнта не знайдено",
                "Для вашого облікового запису ще не створено профіль клієнта. Зверніться до адміністратора клубу.",
                new Dictionary<string, object?> { ["code"] = "client_profile_missing" }));
        }

        if (!person.IsOk)
        {
            return (null, Problems.ToResult(ctx, StatusCodes.Status503ServiceUnavailable, "Довідник клієнтів недоступний",
                "Не вдалося знайти ваш профіль клієнта. Спробуйте ще раз."));
        }

        return (person.Value, null);
    }

    /// <summary>Помилка, прив'язана до поля форми: `errors.workoutId` — клієнт покаже її біля списку тренувань.</summary>
    private static IResult FieldProblem(HttpContext ctx, int status, string title, string message) =>
        Problems.ToResult(ctx, status, title, message,
            new Dictionary<string, object?> { ["errors"] = new Dictionary<string, string[]> { ["workoutId"] = [message] } });
}
