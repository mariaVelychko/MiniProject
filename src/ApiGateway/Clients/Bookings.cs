using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ApiGateway.Clients;

// ---------- Довідник людей (моноліт) ----------

/// <summary>Людина з довідника моноліту (лише поля, потрібні шлюзу).</summary>
public sealed record DirectoryPerson(int Id, string FullName, string Email, string Role);

public interface IPersonDirectory
{
    /// <summary>
    /// Знайти клієнта за email облікового запису Auth. Ідентифікатори Auth і моноліту — різні числа (ADR-0004), тож
    /// «хто це» шлюз визначає за email із перевіреного токена, а не за числом, яке прислав клієнт.
    /// </summary>
    Task<Upstream<DirectoryPerson>> FindClientByEmailAsync(string email, CancellationToken ct);
}

public sealed class PersonDirectory(HttpClient http, ILogger<PersonDirectory> log) : IPersonDirectory
{
    public async Task<Upstream<DirectoryPerson>> FindClientByEmailAsync(string email, CancellationToken ct)
    {
        var all = await UpstreamHttp.GetAsync<List<DirectoryPerson>>(http, "api/users?role=client", log, "Monolith", ct);

        if (!all.IsOk)
        {
            return Upstream<DirectoryPerson>.Unavailable();
        }

        var person = all.Value!.FirstOrDefault(p => string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase));
        return person is null ? Upstream<DirectoryPerson>.NotFound() : Upstream<DirectoryPerson>.Ok(person);
    }
}

// ---------- Записи (Schedule) ----------

/// <summary>Тренування, на яке записаний клієнт (відповідь Schedule «як є»; зайве ігнорується).</summary>
public sealed record BookedWorkout(int Id, int? CatalogItemId, string Type, DateTime Time, int DurationMin, string Hall,
    string CoachName, int Participants, int MaxParticipants);

public enum BookingOutcome
{
    Ok,
    NotFound,
    Conflict,
    Rejected,
    Unavailable,
}

/// <summary>Результат запису/скасування: вид, текст помилки сервісу (для користувача) і ознака повтору ідемпотентного запиту.</summary>
public sealed record BookingResult(BookingOutcome Outcome, string? Message = null, bool Replayed = false);

public interface IBookingsClient
{
    Task<Upstream<List<BookedWorkout>>> GetByClientAsync(int clientId, CancellationToken ct);

    Task<BookingResult> BookAsync(int workoutId, int clientId, string idempotencyKey, CancellationToken ct);

    Task<BookingResult> CancelAsync(int workoutId, int clientId, CancellationToken ct);
}

/// <summary>
/// Клієнт записів. Idempotency-Key, згенерований клієнтом застосунку, шлюз передає в Schedule без змін: повтор того
/// самого запиту (після мережевої помилки) не створює другого запису. Дедлайн — як у проксі (записи ходять ще й у моноліт).
/// </summary>
public sealed class BookingsClient(HttpClient http, ILogger<BookingsClient> log) : IBookingsClient
{
    public Task<Upstream<List<BookedWorkout>>> GetByClientAsync(int clientId, CancellationToken ct) =>
        UpstreamHttp.GetAsync<List<BookedWorkout>>(http, $"api/bookings/client/{clientId}", log, "Schedule", ct);

    public async Task<BookingResult> BookAsync(int workoutId, int clientId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/bookings")
            {
                Content = JsonContent.Create(new { workoutId, clientId }),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

            using var response = await http.SendAsync(request, ct);
            return await ToResultAsync(response, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            log.LogWarning("Schedule: не вдалося виконати POST api/bookings: {Reason}", ex.GetType().Name);
            return new BookingResult(BookingOutcome.Unavailable);
        }
    }

    public async Task<BookingResult> CancelAsync(int workoutId, int clientId, CancellationToken ct)
    {
        try
        {
            using var response = await http.DeleteAsync($"api/bookings/{workoutId}/{clientId}", ct);
            return await ToResultAsync(response, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            log.LogWarning("Schedule: не вдалося виконати DELETE api/bookings: {Reason}", ex.GetType().Name);
            return new BookingResult(BookingOutcome.Unavailable);
        }
    }

    private async Task<BookingResult> ToResultAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var replayed = response.Headers.Contains("Idempotency-Replayed");

        if (response.IsSuccessStatusCode)
        {
            return new BookingResult(BookingOutcome.Ok, Replayed: replayed);
        }

        var message = await ReadMessageAsync(response, ct);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return new BookingResult(BookingOutcome.NotFound, message);
            case HttpStatusCode.Conflict:
                return new BookingResult(BookingOutcome.Conflict, message);
            case HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity:
                return new BookingResult(BookingOutcome.Rejected, message);
            default:
                log.LogWarning("Schedule: неочікувана відповідь {Status} на запис", (int)response.StatusCode);
                return new BookingResult(BookingOutcome.Unavailable, message);
        }
    }

    /// <summary>Текст помилки бізнес-логіки Schedule: {"message": "..."} або ProblemDetails {"title"/"detail"}.</summary>
    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            foreach (var name in new[] { "message", "detail", "title" })
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or NotSupportedException)
        {
            // тіла немає або воно не JSON — повідомлення додасть шлюз
        }

        return null;
    }
}
