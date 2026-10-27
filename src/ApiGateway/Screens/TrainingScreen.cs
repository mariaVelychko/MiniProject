using ApiGateway.Clients;
using ApiGateway.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Screens;

/// <summary>Об'єкт каталогу на екрані.</summary>
public sealed record ScreenItem(int Id, string Title, string Description, decimal Price, int DurationMin, string CategoryName, bool IsActive);

/// <summary>Найближчі тренування цього типу (дані Schedule «як є»).</summary>
public sealed record ScreenSession(int Id, DateTime Time, int DurationMin, string Hall, string CoachName, int Participants, int MaxParticipants);

/// <summary>Поточний користувач (дані Auth, персональні — не кешуються).</summary>
public sealed record ScreenUser(int Id, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>
/// Формат екрана «Заняття». Він належить шлюзу: це не склейка чужих DTO, а те, що потрібно екрану.
/// <c>sessions</c> і <c>me</c> можуть бути null — але ЛИШЕ разом із записом у <c>unavailable</c> (сервіс не відповів).
/// Порожній масив <c>sessions: []</c> означає «сервіс відповів: тренувань немає».
/// </summary>
public sealed record TrainingScreenResponse(
    ScreenItem Item,
    IReadOnlyList<ScreenSession>? Sessions,
    ScreenUser? Me,
    bool Partial,
    IReadOnlyList<string> Unavailable);

public static class TrainingScreenEndpoint
{
    public const string Route = "/api/v1/screens/training/{itemId:int}";

    public static IEndpointRouteBuilder MapTrainingScreen(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, GetAsync)
            .RequireAuthorization()
            .WithTags("Screens")
            .WithName("GetTrainingScreen")
            .WithSummary("Екран «Заняття»: об'єкт каталогу + розклад + поточний користувач (один запит замість трьох)")
            .WithDescription(
                "Шлюз паралельно (Task.WhenAll) звертається до Catalog, Schedule і Auth та складає одну відповідь.\n\n" +
                "**Головна частина** — `item` (Catalog). Якщо каталог не відповів — повертається 503 (ProblemDetails) на весь запит, " +
                "якщо об'єкта немає — 404.\n\n" +
                "**Допоміжні частини** — `sessions` (Schedule) і `me` (Auth). Якщо сервіс не відповів, шлюз віддає " +
                "**часткову відповідь** (200): відсутня частина = `null`, її назва — у `unavailable`, `partial` = true, " +
                "додається заголовок `X-Partial-Response: true`. `sessions: []` означає «тренувань немає», а `null` — «сервіс недоступний».\n\n" +
                "Приклад часткової відповіді, коли Schedule недоступний:\n\n" +
                "```json\n{\"item\":{\"id\":1,\"title\":\"Йога для початківців\",\"description\":\"Базовий рівень\",\"price\":250.00,\"durationMin\":60,\"categoryName\":\"Йога\",\"isActive\":true},\"sessions\":null,\"me\":{\"id\":2,\"displayName\":\"Марія\",\"roles\":[\"Client\"]},\"partial\":true,\"unavailable\":[\"sessions\"]}\n```\n\n" +
                "Потрібен заголовок `Authorization: Bearer <токен>` (інакше 401 без звернення до сервісів). " +
                "Дані каталогу беруться з кешу шлюзу (TTL 60 с), персональні й розклад — завжди свіжі.")
            .Produces<TrainingScreenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetAsync(int itemId, HttpContext ctx, ICatalogClient catalog,
        IScheduleClient schedule, IAuthClient auth, CancellationToken ct)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : string.Empty;

        // Паралельно: загальний час — за найповільнішим викликом, а не сума трьох. Кожен клієнт сам обробляє збої
        // свого сервісу й повертає Upstream, тож WhenAll не впаде через один недоступний сервіс.
        var itemTask = catalog.GetItemAsync(itemId, ct);
        var sessionsTask = schedule.GetSessionsAsync(itemId, ct);
        var meTask = auth.GetMeAsync(token, ct);

        await Task.WhenAll(itemTask, sessionsTask, meTask);

        var item = await itemTask;
        var sessions = await sessionsTask;
        var me = await meTask;

        if (item.Status == UpstreamStatus.NotFound)
        {
            return Problems.ToResult(ctx, StatusCodes.Status404NotFound, "Заняття не знайдено",
                $"Об'єкта каталогу {itemId} не існує.");
        }

        if (!item.IsOk)
        {
            return Problems.ToResult(ctx, StatusCodes.Status503ServiceUnavailable, "Каталог недоступний",
                "Не вдалося отримати основну частину екрана (каталог). Спробуйте пізніше.",
                new Dictionary<string, object?> { ["unavailable"] = new[] { "item" } });
        }

        var unavailable = new List<string>();

        if (!sessions.IsOk)
        {
            unavailable.Add("sessions");
        }

        if (!me.IsOk)
        {
            unavailable.Add("me");
        }

        var i = item.Value!;

        var response = new TrainingScreenResponse(
            new ScreenItem(i.Id, i.Title, i.Description, i.Price, i.DurationMin, i.CategoryName, i.IsActive),
            sessions.IsOk
                ? sessions.Value!.Select(s => new ScreenSession(s.Id, s.Time, s.DurationMin, s.Hall, s.CoachName, s.Participants, s.MaxParticipants)).ToList()
                : null,
            me.IsOk ? new ScreenUser(me.Value!.Id, me.Value.DisplayName, me.Value.Roles) : null,
            unavailable.Count > 0,
            unavailable);

        if (response.Partial)
        {
            ctx.Response.Headers["X-Partial-Response"] = "true";
        }

        return Results.Ok(response);
    }
}
