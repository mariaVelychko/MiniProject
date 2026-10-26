using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScheduleService.Contracts;
using ScheduleService.Data;

namespace Schedule.IntegrationTests;

/// <summary>
/// Характеризаційні/регресійні тести: очікування списані з фактичної поведінки моноліту (tests/baseline).
/// Кожен тест перевіряє і код статусу, і структуру тіла; кожен створює власні дані й не залежить від порядку.
/// </summary>
[Collection(ApiCollection.Name)]
public class ScheduleApiTests(ApiFixture fixture)
{
    private static readonly string[] WorkoutKeys =
    [
        "id", "catalogItemId", "type", "time", "durationMin", "hall",
        "coachId", "coachName", "participants", "maxParticipants", "participantIds",
    ];

    private static readonly string[] UserKeys = ["id", "fullName", "email", "phone", "role", "roleInfo", "specialization"];

    private ApiFactory Api => fixture.Factory;
    private HttpClient NewClient() => Api.CreateClient();

    // ---------- helpers ----------

    private static WorkoutRequest Req(int item = 1, int coach = 1, string hall = "Зал 1", int? max = null,
        DateTime? time = null) => new(item, time ?? new DateTime(2026, 11, 1, 18, 0, 0), hall, coach, null, max);

    private async Task<WorkoutResponse> CreateWorkoutAsync(HttpClient c, WorkoutRequest? r = null)
    {
        var resp = await c.PostAsJsonAsync("/api/workouts", r ?? Req());
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<WorkoutResponse>())!;
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient c, int workoutId, int clientId, string? key = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/bookings")
        {
            Content = JsonContent.Create(new BookingRequest(workoutId, clientId)),
        };

        if (key is not null)
        {
            msg.Headers.Add("Idempotency-Key", key);
        }

        return c.SendAsync(msg);
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static void AssertKeys(JsonElement obj, string[] expected) =>
        Assert.Equal(expected, obj.EnumerateObject().Select(p => p.Name).ToArray());

    private static async Task AssertMessage(HttpResponseMessage r, HttpStatusCode status, string message)
    {
        Assert.Equal(status, r.StatusCode);
        var body = await JsonOf(r);
        AssertKeys(body, ["message"]);
        Assert.Equal(message, body.GetProperty("message").GetString());
    }

    private async Task<T> WithDb<T>(Func<ScheduleDbContext, Task<T>> action)
    {
        using var scope = Api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ScheduleDbContext>());
    }

    // ---------- 1. створення з коректними даними ----------

    [Fact]
    public async Task CreateWorkout_Valid_Returns201_Location_Body_AndDbRow()
    {
        var c = NewClient();
        var resp = await c.PostAsJsonAsync("/api/workouts", Req(item: 1, coach: 2));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await JsonOf(resp);
        AssertKeys(body, WorkoutKeys);
        var id = body.GetProperty("id").GetInt32();
        Assert.EndsWith($"/api/workouts/{id}", resp.Headers.Location!.ToString());
        Assert.Equal("Йога для початківців", body.GetProperty("type").GetString());
        Assert.Equal("2026-11-01T18:00:00", body.GetProperty("time").GetString());
        Assert.Equal(60, body.GetProperty("durationMin").GetInt32());       // з каталогу
        Assert.Equal(12, body.GetProperty("maxParticipants").GetInt32());   // з каталогу
        Assert.Equal(2, body.GetProperty("coachId").GetInt32());
        Assert.Equal("Тренер 2", body.GetProperty("coachName").GetString());
        Assert.Equal(0, body.GetProperty("participants").GetInt32());
        Assert.Empty(body.GetProperty("participantIds").EnumerateArray());

        var row = await WithDb(db => db.Workouts.AsNoTracking().SingleAsync(w => w.Id == id));
        Assert.Equal("Йога для початківців", row.Type);
        Assert.Equal(250m, row.PriceSnapshot);   // знімок ціни на момент створення
        Assert.True(row.CreatedAt > DateTime.UtcNow.AddMinutes(-5));   // час створення — UTC
    }

    // ---------- 2. без токена: моноліт не вимагав токена — поведінка збережена ----------

    [Fact]
    public async Task Book_WithoutToken_OrWithGarbageToken_Works_LikeMonolith()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);

        var noToken = await BookAsync(c, w.Id, 20);
        Assert.Equal(HttpStatusCode.OK, noToken.StatusCode);

        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        var garbage = await BookAsync(c, w.Id, 21);
        Assert.Equal(HttpStatusCode.OK, garbage.StatusCode);
    }

    // ---------- запис: щасливий шлях ----------

    [Fact]
    public async Task Book_Valid_Returns200_WithWorkoutBody()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);

        var resp = await BookAsync(c, w.Id, 30);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await JsonOf(resp);
        AssertKeys(body, WorkoutKeys);
        Assert.Equal(w.Id, body.GetProperty("id").GetInt32());
        Assert.Equal(1, body.GetProperty("participants").GetInt32());
        Assert.Equal(new[] { 30 }, body.GetProperty("participantIds").EnumerateArray().Select(x => x.GetInt32()).ToArray());

        var row = await WithDb(db => db.Bookings.AsNoTracking().SingleAsync(b => b.WorkoutId == w.Id));
        Assert.Equal(30, row.ClientId);
        Assert.Equal("Клієнт 30", row.ClientFullName);   // знімок даних клієнта
    }

    // ---------- 3. неіснуючі об'єкти: коди й тексти як у моноліті ----------

    [Fact]
    public async Task Book_UnknownWorkout_Returns404_WithMessage() =>
        await AssertMessage(await BookAsync(NewClient(), 999999, 30), HttpStatusCode.NotFound, "Тренування 999999 не знайдено");

    [Fact]
    public async Task Book_UnknownClient_Returns404_WithMessage()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);

        await AssertMessage(await BookAsync(c, w.Id, 999999), HttpStatusCode.NotFound, "Клієнта 999999 не знайдено");
    }

    [Fact]
    public async Task Book_TrainerIdAsClient_Returns404()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);

        await AssertMessage(await BookAsync(c, w.Id, 1), HttpStatusCode.NotFound, "Клієнта 1 не знайдено");
    }

    [Fact]
    public async Task CreateWorkout_UnknownCatalogItem_Returns400_WithMessage() =>
        await AssertMessage(await NewClient().PostAsJsonAsync("/api/workouts", Req(item: 999)),
            HttpStatusCode.BadRequest, "Тип тренування 999 не знайдено в каталозі");

    [Fact]
    public async Task CreateWorkout_InactiveCatalogItem_Returns400_WithMessage() =>
        await AssertMessage(await NewClient().PostAsJsonAsync("/api/workouts", Req(item: 2)),
            HttpStatusCode.BadRequest, "Тип тренування \"Стара програма\" вилучено з каталогу");

    [Fact]
    public async Task CreateWorkout_UnknownCoach_Returns400_WithMessage() =>
        await AssertMessage(await NewClient().PostAsJsonAsync("/api/workouts", Req(coach: 999)),
            HttpStatusCode.BadRequest, "Тренера 999 не знайдено");

    // ---------- 4. бізнес-правила ----------

    [Fact]
    public async Task CreateWorkout_BlankHall_Returns400_WithMessage() =>
        await AssertMessage(await NewClient().PostAsJsonAsync("/api/workouts", Req(hall: "   ")),
            HttpStatusCode.BadRequest, "Вкажіть зал (hall)");

    [Fact]
    public async Task CreateWorkout_MissingHall_Returns400_ValidationProblemDetails()
    {
        var resp = await NewClient().PostAsJsonAsync("/api/workouts",
            new { catalogItemId = 1, time = "2026-11-01T18:00:00", coachId = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await JsonOf(resp);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.True(body.GetProperty("errors").TryGetProperty("Hall", out _));
    }

    [Fact]
    public async Task CreateWorkout_NonPositiveCapacity_Returns400_WithMessage() =>
        await AssertMessage(await NewClient().PostAsJsonAsync("/api/workouts", Req(max: 0)),
            HttpStatusCode.BadRequest, "Тривалість і місткість мають бути додатними");

    [Fact]
    public async Task Book_Duplicate_Returns409_WithMessage()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(c, w.Id, 40)).StatusCode);

        await AssertMessage(await BookAsync(c, w.Id, 40), HttpStatusCode.Conflict, "Клієнт уже записаний на це тренування");
    }

    [Fact]
    public async Task Book_WhenFull_Returns409_WithMessage()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c, Req(max: 1));
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(c, w.Id, 41)).StatusCode);

        await AssertMessage(await BookAsync(c, w.Id, 42), HttpStatusCode.Conflict, "Вільних місць немає");
    }

    [Fact]
    public async Task Book_ConcurrentRequests_NeverExceedCapacity()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c, Req(max: 1));

        var results = await Task.WhenAll(Enumerable.Range(100, 6).Select(id => BookAsync(c, w.Id, id)));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await WithDb(db => db.Bookings.CountAsync(b => b.WorkoutId == w.Id)));
    }

    // ---------- 5. ідемпотентність ----------

    [Fact]
    public async Task Book_SameIdempotencyKey_ReturnsSameResponse_AndCreatesOneBooking()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);
        var key = Guid.NewGuid().ToString();

        var first = await BookAsync(c, w.Id, 50, key);
        var second = await BookAsync(c, w.Id, 50, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);   // без ключа тут було б 409
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.True(second.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(1, await WithDb(db => db.Bookings.CountAsync(b => b.WorkoutId == w.Id)));
    }

    [Fact]
    public async Task Book_IdempotencyKeyReusedWithDifferentBody_Returns422()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);
        var key = Guid.NewGuid().ToString();

        Assert.Equal(HttpStatusCode.OK, (await BookAsync(c, w.Id, 51, key)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await BookAsync(c, w.Id, 52, key)).StatusCode);
        Assert.Equal(1, await WithDb(db => db.Bookings.CountAsync(b => b.WorkoutId == w.Id)));
    }

    [Fact]
    public async Task CreateWorkout_SameIdempotencyKey_CreatesOneWorkout_AndKeepsLocation()
    {
        var c = NewClient();
        var key = Guid.NewGuid().ToString();
        var req = Req(hall: $"Зал-{key}");

        HttpRequestMessage Msg() => new(HttpMethod.Post, "/api/workouts")
        {
            Content = JsonContent.Create(req),
            Headers = { { "Idempotency-Key", key } },
        };

        var first = await c.SendAsync(Msg());
        var second = await c.SendAsync(Msg());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(1, await WithDb(db => db.Workouts.CountAsync(w => w.Hall == req.Hall)));
    }

    // ---------- 6/7. читання ----------

    [Fact]
    public async Task GetWorkout_Returns200_WithSameFields_And404ForUnknown()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);

        var ok = await c.GetAsync($"/api/workouts/{w.Id}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        AssertKeys(await JsonOf(ok), WorkoutKeys);

        await AssertMessage(await c.GetAsync("/api/workouts/999999"), HttpStatusCode.NotFound, "Тренування 999999 не знайдено");
    }

    [Fact]
    public async Task GetParticipants_ReturnsUserShape_AndUnknownWorkoutIs404()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);
        await BookAsync(c, w.Id, 60);

        var resp = await c.GetAsync($"/api/bookings/workout/{w.Id}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var arr = (await JsonOf(resp)).EnumerateArray().ToArray();
        var single = Assert.Single(arr);
        AssertKeys(single, UserKeys);
        Assert.Equal("client", single.GetProperty("role").GetString());
        Assert.Equal("Клієнт клубу", single.GetProperty("roleInfo").GetString());
        Assert.Equal(JsonValueKind.Null, single.GetProperty("specialization").ValueKind);

        await AssertMessage(await c.GetAsync("/api/bookings/workout/999999"), HttpStatusCode.NotFound, "Тренування 999999 не знайдено");
    }

    [Fact]
    public async Task GetClientBookings_ReturnsOnlyOwnWorkouts_SortedByTime()
    {
        var c = NewClient();
        var late = await CreateWorkoutAsync(c, Req(time: new DateTime(2026, 12, 2, 10, 0, 0)));
        var early = await CreateWorkoutAsync(c, Req(time: new DateTime(2026, 12, 1, 10, 0, 0)));
        var other = await CreateWorkoutAsync(c);
        await BookAsync(c, late.Id, 70);
        await BookAsync(c, early.Id, 70);
        await BookAsync(c, other.Id, 71);

        var resp = await c.GetAsync("/api/bookings/client/70");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var arr = (await JsonOf(resp)).EnumerateArray().ToArray();
        Assert.All(arr, e => AssertKeys(e, WorkoutKeys));
        Assert.Equal(new[] { early.Id, late.Id }, arr.Select(e => e.GetProperty("id").GetInt32()).ToArray());
    }

    // ---------- 8. скасування ----------

    [Fact]
    public async Task Cancel_RemovesBooking_RepeatReturns404()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);
        await BookAsync(c, w.Id, 80);

        var first = await c.DeleteAsync($"/api/bookings/{w.Id}/80");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Empty(await first.Content.ReadAsStringAsync());

        var after = await JsonOf(await c.GetAsync($"/api/workouts/{w.Id}"));
        Assert.Equal(0, after.GetProperty("participants").GetInt32());

        await AssertMessage(await c.DeleteAsync($"/api/bookings/{w.Id}/80"),
            HttpStatusCode.NotFound, "Клієнт не записаний на це тренування");
        await AssertMessage(await c.DeleteAsync("/api/bookings/999999/80"),
            HttpStatusCode.NotFound, "Тренування 999999 не знайдено");
    }

    // ---------- 9. сусіди недоступні ----------

    [Fact]
    public async Task CatalogDown_CreateReturns503_ServiceStaysAlive()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);
        Api.Catalog.Down = true;

        try
        {
            await AssertMessage(await c.PostAsJsonAsync("/api/workouts", Req()),
                HttpStatusCode.ServiceUnavailable, "Каталог типів тренувань недоступний. Спробуйте пізніше.");

            // читання й записи, що не потребують каталогу, працюють
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/workouts/{w.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await BookAsync(c, w.Id, 90)).StatusCode);

            var health = await c.GetAsync("/health");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
            Assert.Equal("down", (await JsonOf(health)).GetProperty("dependencies").GetProperty("catalog").GetString());
        }
        finally
        {
            Api.Catalog.Down = false;
        }
    }

    [Fact]
    public async Task UsersDown_BookReturns503()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);
        Api.Users.Down = true;

        try
        {
            await AssertMessage(await BookAsync(c, w.Id, 91),
                HttpStatusCode.ServiceUnavailable, "Сервіс користувачів недоступний. Спробуйте пізніше.");
        }
        finally
        {
            Api.Users.Down = false;
        }
    }

    [Fact]
    public async Task Health_AllHealthy_Returns200()
    {
        var resp = await NewClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await JsonOf(resp);
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal("ok", body.GetProperty("dependencies").GetProperty("database").GetString());
    }

    // ---------- 10. список із фільтром і сортуванням ----------

    [Fact]
    public async Task List_FilteredByCoachAndItem_IsSortedByTime()
    {
        var c = NewClient();   // тренер 9 і позиція 7 використовуються лише в цьому тесті
        var t3 = await CreateWorkoutAsync(c, Req(item: 7, coach: 9, time: new DateTime(2027, 1, 3, 9, 0, 0)));
        var t1 = await CreateWorkoutAsync(c, Req(item: 7, coach: 9, time: new DateTime(2027, 1, 1, 9, 0, 0)));
        var t2 = await CreateWorkoutAsync(c, Req(item: 7, coach: 9, time: new DateTime(2027, 1, 2, 9, 0, 0)));
        await CreateWorkoutAsync(c, Req(item: 6, coach: 9));   // інший тип — у вибірку не потрапляє

        var resp = await c.GetAsync("/api/workouts?coachId=9&catalogItemId=7");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var arr = (await JsonOf(resp)).EnumerateArray().ToArray();
        Assert.All(arr, e => AssertKeys(e, WorkoutKeys));
        Assert.Equal(new[] { t1.Id, t2.Id, t3.Id }, arr.Select(e => e.GetProperty("id").GetInt32()).ToArray());
    }

    // ---------- редагування / видалення ----------

    [Fact]
    public async Task Update_Valid_Returns200_And_CapacityBelowBooked_Returns409()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c, Req(max: 3));
        await BookAsync(c, w.Id, 110);
        await BookAsync(c, w.Id, 111);

        var tooSmall = await c.PutAsJsonAsync($"/api/workouts/{w.Id}", Req(max: 1));
        await AssertMessage(tooSmall, HttpStatusCode.Conflict, "Нова місткість менша за кількість записаних клієнтів");

        var ok = await c.PutAsJsonAsync($"/api/workouts/{w.Id}", Req(hall: "Зал 2", max: 2));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await JsonOf(ok);
        AssertKeys(body, WorkoutKeys);
        Assert.Equal("Зал 2", body.GetProperty("hall").GetString());
        Assert.Equal(2, body.GetProperty("participants").GetInt32());

        await AssertMessage(await c.PutAsJsonAsync("/api/workouts/999999", Req()),
            HttpStatusCode.NotFound, "Тренування 999999 не знайдено");
    }

    [Fact]
    public async Task Delete_RemovesWorkout_WithItsBookings()
    {
        var c = NewClient();
        var w = await CreateWorkoutAsync(c);
        await BookAsync(c, w.Id, 120);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/workouts/{w.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/workouts/{w.Id}")).StatusCode);
        Assert.Equal(0, await WithDb(db => db.Bookings.CountAsync(b => b.WorkoutId == w.Id)));

        await AssertMessage(await c.DeleteAsync($"/api/workouts/{w.Id}"), HttpStatusCode.NotFound, $"Тренування {w.Id} не знайдено");
    }
}
