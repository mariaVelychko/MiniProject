using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gateway.IntegrationTests;

/// <summary>
/// ЛР4: ендпоінти «мої записи» для клієнтського застосунку. Головне, що вони гарантують: клієнт НЕ надсилає
/// ідентифікатор користувача — шлюз бере його з токена, а повтор запиту з тим самим Idempotency-Key не дублює запис.
/// Користувач «Анна» має id 7 в Auth і id 501 у довіднику моноліту: у Schedule має піти саме 501.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class MyBookingsTests
{
    private readonly FakeUpstream _fake;

    public MyBookingsTests(UpstreamFixture upstream) => _fake = upstream.Fake.Reset();

    private static HttpClient Client(GatewayFactory gateway, string name = "Анна", int userId = 7, bool anonymous = false)
    {
        var client = gateway.CreateClient();

        if (!anonymous)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId, name));
        }

        return client;
    }

    private static HttpRequestMessage Post(object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/bookings") { Content = JsonContent.Create(body) };

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return request;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    [Fact]
    public async Task List_UsesIdentityFromToken_AndMarksUpcomingAndPast()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway);
        _fake.Seed(9, 501);

        var response = await client.GetAsync("/api/v1/me/bookings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("501", _fake.Seen("bookings:clientId"));

        var items = await JsonOf(response);
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("past", items[0].GetProperty("status").GetString());
        Assert.False(items[0].GetProperty("canCancel").GetBoolean());
        Assert.Equal("upcoming", items[1].GetProperty("status").GetString());
        Assert.True(items[1].GetProperty("canCancel").GetBoolean());
    }

    [Fact]
    public async Task List_NoBookings_IsEmptyArray_NotAnError()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway);
        _fake.Clear();

        var response = await client.GetAsync("/api/v1/me/bookings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await JsonOf(response)).GetArrayLength());
    }

    [Fact]
    public async Task List_ScheduleDown_Returns503_NotEmptyList()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway);
        _fake.Fail("bookings-list");

        var response = await client.GetAsync("/api/v1/me/bookings");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Endpoints_WithoutToken_Return401_AndNothingReachesServices()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway, anonymous: true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/bookings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Post(new { workoutId = 5 }, "k"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/v1/me/bookings/1")).StatusCode);
        Assert.Equal(0, _fake.TotalCalls);
    }

    [Fact]
    public async Task Create_ClientIdComesFromToken_NotFromRequestBody()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway);

        var response = await client.SendAsync(Post(new { workoutId = 5, clientId = 999, userId = 999 }, "key-1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("501", _fake.Seen("book:clientId"));
        Assert.Equal("key-1", _fake.Seen("book:key"));
    }

    [Fact]
    public async Task Create_SameIdempotencyKeyTwice_CreatesOneBooking()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway);

        var first = await client.SendAsync(Post(new { workoutId = 5 }, "same-key"));
        var second = await client.SendAsync(Post(new { workoutId = 5 }, "same-key"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(first.Headers.Contains("Idempotency-Replayed"));
        Assert.True(second.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(1, _fake.Calls("book-created"));
    }

    [Fact]
    public async Task Create_InvalidRequest_Returns400_WithErrorsPerField_AndDoesNotCallServices()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway);

        var response = await client.SendAsync(Post(new { }, key: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Помилка валідації", body.GetProperty("title").GetString());
        Assert.Equal(1, body.GetProperty("errors").GetProperty("workoutId").GetArrayLength());
        Assert.Equal(1, body.GetProperty("errors").GetProperty("idempotencyKey").GetArrayLength());
        Assert.Equal(0, _fake.TotalCalls);
    }

    [Fact]
    public async Task Create_NoFreePlaces_Returns409_WithMessageOnWorkoutField()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway);

        var response = await client.SendAsync(Post(new { workoutId = 409 }, "k409"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Вільних місць немає", body.GetProperty("errors").GetProperty("workoutId")[0].GetString());
    }

    [Fact]
    public async Task Create_UnknownWorkout_Returns404_WithMessageOnWorkoutField()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway);

        var response = await client.SendAsync(Post(new { workoutId = 404 }, "k404"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, (await JsonOf(response)).GetProperty("errors").GetProperty("workoutId").GetArrayLength());
    }

    [Fact]
    public async Task Create_ScheduleDown_Returns503Problem_AndRetryWithSameKeyIsSafe()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway);
        _fake.Fail("book");

        var failed = await client.SendAsync(Post(new { workoutId = 5 }, "retry-key"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal("application/problem+json", failed.Content.Headers.ContentType?.MediaType);

        _fake.Fail("book", false);
        var retried = await client.SendAsync(Post(new { workoutId = 5 }, "retry-key"));

        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal(1, _fake.Calls("book-created"));
    }

    [Fact]
    public async Task Create_UserWithoutClientProfile_Returns403_WithCode()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway, name: "Борис", userId: 8);

        var response = await client.SendAsync(Post(new { workoutId = 5 }, "k"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("client_profile_missing", (await JsonOf(response)).GetProperty("code").GetString());
        Assert.Equal(0, _fake.Calls("book"));
    }

    [Fact]
    public async Task Cancel_RemovesOwnBooking_AndUsesIdentityFromToken()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway);

        var response = await client.DeleteAsync("/api/v1/me/bookings/1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("501", _fake.Seen("cancel:clientId"));

        var again = await client.DeleteAsync("/api/v1/me/bookings/1");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("application/problem+json", again.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Cors_ExposesHeadersTheBrowserClientNeeds()
    {
        using var gateway = new GatewayFactory();
        var client = Client(gateway, anonymous: true);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://localhost:8080");

        var response = await client.SendAsync(request);

        var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("Retry-After", exposed);
        Assert.Contains("X-Partial-Response", exposed);
        Assert.Contains("Idempotency-Replayed", exposed);
    }
}
