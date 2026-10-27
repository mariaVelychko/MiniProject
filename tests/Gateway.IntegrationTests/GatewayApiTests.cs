using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApiGateway;

namespace Gateway.IntegrationTests;

/// <summary>
/// Інтеграційні тести ЛР3. Шлюз піднімається цілком, його «сервіси» — справжній HTTP-сервер у тесті (FakeUpstream).
/// Кожен тест перевіряє і код статусу, і структуру тіла, і (де треба) те, що до сервісів дійшло — або не дійшло.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class GatewayApiTests
{
    private readonly FakeUpstream _fake;

    public GatewayApiTests(UpstreamFixture upstream) => _fake = upstream.Fake.Reset();

    // ---------- допоміжне ----------

    private static HttpClient ClientFor(GatewayFactory gateway, int? userId = null, string name = "Анна", string role = "Client")
    {
        var client = gateway.CreateClient();

        if (userId is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId.Value, name, role));
        }

        return client;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    /// <summary>ProblemDetails (RFC 9457): тип вмісту, поле status і непорожній title.</summary>
    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await JsonOf(response);
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));

        return body;
    }

    // ---------- 1–2. Складений ендпоінт ----------

    [Fact]
    public async Task Screen_AllServicesAvailable_ReturnsComposedScreen()
    {
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway, userId: 7, name: "Анна");

        var response = await client.GetAsync("/api/v1/screens/training/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Partial-Response"));

        var body = await JsonOf(response);
        var item = body.GetProperty("item");
        Assert.Equal(1, item.GetProperty("id").GetInt32());
        Assert.Equal("Йога для початківців", item.GetProperty("title").GetString());
        Assert.Equal(250m, item.GetProperty("price").GetDecimal());
        Assert.Equal("Йога", item.GetProperty("categoryName").GetString());

        var sessions = body.GetProperty("sessions");
        Assert.Equal(1, sessions.GetArrayLength());
        Assert.Equal("Іван Ковальчук", sessions[0].GetProperty("coachName").GetString());
        Assert.Equal(2, sessions[0].GetProperty("participants").GetInt32());
        Assert.Equal(10, sessions[0].GetProperty("maxParticipants").GetInt32());

        var me = body.GetProperty("me");
        Assert.Equal(7, me.GetProperty("id").GetInt32());
        Assert.Equal("Анна", me.GetProperty("displayName").GetString());

        Assert.False(body.GetProperty("partial").GetBoolean());
        Assert.Equal(0, body.GetProperty("unavailable").GetArrayLength());
    }

    [Fact]
    public async Task Screen_CallsServicesInParallel_NotOneByOne()
    {
        // Кожен із трьох сервісів відповідає за 600 мс: послідовно було б ≥ 1800 мс, паралельно — близько 600.
        _fake.Delay("catalog", 600);
        _fake.Delay("schedule", 600);
        _fake.Delay("auth", 600);

        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway, userId: 7);

        var timer = Stopwatch.StartNew();
        var response = await client.GetAsync("/api/v1/screens/training/1");
        timer.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.InRange(timer.ElapsedMilliseconds, 500, 1500);
    }

    // ---------- 3. Один сервіс не відповів ----------

    [Fact]
    public async Task Screen_ScheduleDown_ReturnsPartialResponse_WithExplicitMarker()
    {
        _fake.Fail("schedule");

        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway, userId: 7);

        var response = await client.GetAsync("/api/v1/screens/training/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", response.Headers.GetValues("X-Partial-Response").Single());

        var body = await JsonOf(response);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sessions").ValueKind);   // не [] і не пропущено: саме null...
        Assert.True(body.GetProperty("partial").GetBoolean());                      // ...і клієнт бачить, чому
        Assert.Equal("sessions", body.GetProperty("unavailable")[0].GetString());
        Assert.Equal("Йога для початківців", body.GetProperty("item").GetProperty("title").GetString());
        Assert.Equal("Анна", body.GetProperty("me").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Screen_NoSessions_IsEmptyList_NotUnavailable()
    {
        // Сервіс відповів порожнім списком («тренувань немає») — це НЕ те саме, що «сервіс недоступний».
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway, userId: 7);

        var body = await JsonOf(await client.GetAsync("/api/v1/screens/training/2"));

        Assert.Equal(JsonValueKind.Array, body.GetProperty("sessions").ValueKind);
        Assert.Equal(0, body.GetProperty("sessions").GetArrayLength());
        Assert.False(body.GetProperty("partial").GetBoolean());
        Assert.Equal(0, body.GetProperty("unavailable").GetArrayLength());
    }

    [Fact]
    public async Task Screen_AuthDown_ReturnsPartialResponse_WithoutMe()
    {
        _fake.Fail("auth");

        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway, userId: 7);

        var body = await JsonOf(await client.GetAsync("/api/v1/screens/training/1"));

        Assert.Equal(JsonValueKind.Null, body.GetProperty("me").ValueKind);
        Assert.Equal("me", body.GetProperty("unavailable")[0].GetString());
        Assert.Equal(1, body.GetProperty("sessions").GetArrayLength());
    }

    [Fact]
    public async Task Screen_SlowService_IsCutOffByClientTimeout_AndScreenStillArrives()
    {
        _fake.Delay("schedule", 3000);

        using var gateway = new GatewayFactory(o => o.Timeouts.ClientSeconds = 1);
        var client = ClientFor(gateway, userId: 7);

        var timer = Stopwatch.StartNew();
        var response = await client.GetAsync("/api/v1/screens/training/1");
        timer.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.InRange(timer.ElapsedMilliseconds, 900, 2500);   // дедлайн 1 с, а не 3 с очікування сервісу

        var body = await JsonOf(response);
        Assert.Equal("sessions", body.GetProperty("unavailable")[0].GetString());
    }

    [Fact]
    public async Task Screen_CatalogDown_ReturnsProblem503_ForWholeRequest()
    {
        _fake.Fail("catalog");

        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway, userId: 7);

        var problem = await AssertProblem(await client.GetAsync("/api/v1/screens/training/1"), HttpStatusCode.ServiceUnavailable);

        Assert.Equal("item", problem.GetProperty("unavailable")[0].GetString());
    }

    [Fact]
    public async Task Screen_UnknownItem_ReturnsProblem404()
    {
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway, userId: 7);

        await AssertProblem(await client.GetAsync("/api/v1/screens/training/404"), HttpStatusCode.NotFound);
    }

    // ---------- 4. Автентифікація на шлюзі ----------

    [Fact]
    public async Task Screen_WithoutToken_Returns401Problem_AndNothingReachesServices()
    {
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway);

        var response = await client.GetAsync("/api/v1/screens/training/1");

        await AssertProblem(response, HttpStatusCode.Unauthorized);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal(0, _fake.TotalCalls);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("expired")]
    [InlineData("wrong-signature")]
    public async Task Screen_InvalidToken_Returns401Problem_AndNothingReachesServices(string kind)
    {
        using var gateway = new GatewayFactory();
        var client = gateway.CreateClient();

        var token = kind switch
        {
            "expired" => TestTokens.Create(7, "Анна", expired: true),
            "wrong-signature" => TestTokens.Create(7, "Анна", signingKey: "some-other-key-some-other-key-some-other-key"),
            _ => "not.a.jwt",
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await AssertProblem(await client.GetAsync("/api/v1/screens/training/1"), HttpStatusCode.Unauthorized);
        Assert.Equal(0, _fake.TotalCalls);
    }

    [Fact]
    public async Task ProtectedProxyRoutes_WithoutToken_Return401Problem_AndAreNotForwarded()
    {
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway);

        await AssertProblem(await client.GetAsync("/api/v1/users/me"), HttpStatusCode.Unauthorized);
        await AssertProblem(await client.PutAsJsonAsync("/api/v1/catalog/items/1", new { title = "x" }), HttpStatusCode.Unauthorized);

        Assert.Equal(0, _fake.Calls("auth"));
        Assert.Equal(0, _fake.Calls("catalog-put"));
    }

    [Fact]
    public async Task PublicRoutes_StayAnonymous_BehaviourOfExistingRoutesUnchanged()
    {
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway);

        var catalog = await client.GetAsync("/api/v1/catalog/items/1");
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        Assert.Equal("Йога для початківців", (await JsonOf(catalog)).GetProperty("title").GetString());

        var workouts = await client.GetAsync("/api/workouts");
        Assert.Equal(HttpStatusCode.OK, workouts.StatusCode);

        var list = await JsonOf(workouts);
        Assert.Equal(2, list[0].GetProperty("participants").GetInt32());
        Assert.Equal("Зал 1", list[0].GetProperty("hall").GetString());
    }

    [Fact]
    public async Task ServiceOwnErrors_PassThroughUnchanged_NotReplacedWithProblem()
    {
        // Відповідь самого сервісу (409 з {"message": ...}) шлюз не чіпає: поведінка маршрутів не змінюється.
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway);

        var response = await client.GetAsync("/api/own-error");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Вільних місць немає", (await JsonOf(response)).GetProperty("message").GetString());
    }

    // ---------- 4а. Ідентифікатор користувача лише з токена ----------

    [Fact]
    public async Task UserIdHeader_FromClient_IsIgnored_ValueComesFromToken()
    {
        using var gateway = new GatewayFactory();

        // анонім намагається підставити чужий ідентифікатор і роль
        var anonymous = ClientFor(gateway);
        anonymous.DefaultRequestHeaders.Add("X-User-Id", "999");
        anonymous.DefaultRequestHeaders.Add("X-User-Roles", "Admin");
        var anonymousEcho = await JsonOf(await anonymous.GetAsync("/api/echo"));
        Assert.Equal(string.Empty, anonymousEcho.GetProperty("userId").GetString());
        Assert.Equal(string.Empty, anonymousEcho.GetProperty("roles").GetString());

        // авторизований (user 7) теж не може підмінити себе на 999
        var authorized = ClientFor(gateway, userId: 7, role: "Client");
        authorized.DefaultRequestHeaders.Add("X-User-Id", "999");
        authorized.DefaultRequestHeaders.Add("X-User-Roles", "Admin");
        var authorizedEcho = await JsonOf(await authorized.GetAsync("/api/echo"));
        Assert.Equal("7", authorizedEcho.GetProperty("userId").GetString());
        Assert.Equal("Client", authorizedEcho.GetProperty("roles").GetString());
    }

    // ---------- 5. Кеш ----------

    [Fact]
    public async Task Catalog_RepeatedRequest_IsServedFromCache_ServiceCalledOnce()
    {
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway);

        var first = await client.GetAsync("/api/v1/catalog/items/1");
        var second = await client.GetAsync("/api/v1/catalog/items/1");

        Assert.Equal("MISS", first.Headers.GetValues("X-Cache").Single());
        Assert.Equal("HIT", second.Headers.GetValues("X-Cache").Single());
        Assert.Equal(1, _fake.Calls("catalog"));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Catalog_CacheKey_ContainsQueryParameters()
    {
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway);

        await client.GetAsync("/api/v1/catalog/items?page=1");
        await client.GetAsync("/api/v1/catalog/items?page=2");   // інша сторінка — інший ключ, інший виклик сервісу
        await client.GetAsync("/api/v1/catalog/items?page=1");   // повтор — з кешу

        Assert.Equal(2, _fake.Calls("catalog-list"));
    }

    [Fact]
    public async Task Catalog_AfterSuccessfulChangeThroughGateway_CacheIsInvalidated()
    {
        using var gateway = new GatewayFactory();
        var anonymous = ClientFor(gateway);
        var admin = ClientFor(gateway, userId: 1, name: "Адмін", role: "Admin");

        await anonymous.GetAsync("/api/v1/catalog/items/1");
        await anonymous.GetAsync("/api/v1/catalog/items/1");
        Assert.Equal(1, _fake.Calls("catalog"));

        var change = await admin.PutAsJsonAsync("/api/v1/catalog/items/1", new { title = "Нова назва" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var afterChange = await anonymous.GetAsync("/api/v1/catalog/items/1");
        Assert.Equal("MISS", afterChange.Headers.GetValues("X-Cache").Single());
        Assert.Equal(2, _fake.Calls("catalog"));   // клієнт одразу бачить нове, а не чекає закінчення TTL
    }

    [Fact]
    public async Task Screen_CachesSharedCatalogData_ButNeverPersonalData()
    {
        using var gateway = new GatewayFactory();
        var anna = ClientFor(gateway, userId: 7, name: "Анна");
        var bohdan = ClientFor(gateway, userId: 8, name: "Богдан");

        var annaScreen = await JsonOf(await anna.GetAsync("/api/v1/screens/training/1"));
        var bohdanScreen = await JsonOf(await bohdan.GetAsync("/api/v1/screens/training/1"));

        // кожен отримав СВОЇ персональні дані...
        Assert.Equal("Анна", annaScreen.GetProperty("me").GetProperty("displayName").GetString());
        Assert.Equal(7, annaScreen.GetProperty("me").GetProperty("id").GetInt32());
        Assert.Equal("Богдан", bohdanScreen.GetProperty("me").GetProperty("displayName").GetString());
        Assert.Equal(8, bohdanScreen.GetProperty("me").GetProperty("id").GetInt32());

        // ...спільний каталог узято з кешу (1 виклик на двох), а персональні й розклад — свіжі щоразу
        Assert.Equal(1, _fake.Calls("catalog"));
        Assert.Equal(2, _fake.Calls("auth"));
        Assert.Equal(2, _fake.Calls("schedule"));
    }

    [Fact]
    public async Task PersonalRoutes_AreNotCached()
    {
        using var gateway = new GatewayFactory();
        var anna = ClientFor(gateway, userId: 7, name: "Анна");
        var bohdan = ClientFor(gateway, userId: 8, name: "Богдан");

        var first = await JsonOf(await anna.GetAsync("/api/v1/users/me"));
        var second = await JsonOf(await bohdan.GetAsync("/api/v1/users/me"));

        Assert.Equal("Анна", first.GetProperty("displayName").GetString());
        Assert.Equal("Богдан", second.GetProperty("displayName").GetString());
        Assert.Equal(2, _fake.Calls("auth"));
    }

    // ---------- 6. Обмеження частоти ----------

    [Fact]
    public async Task RateLimit_AnonymousOverLimit_Returns429_WithRetryAfterAndProblem()
    {
        using var gateway = new GatewayFactory(o => o.RateLimits.AnonymousPerWindow = 3);
        var client = ClientFor(gateway);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/catalog/items/1")).StatusCode);
        }

        var limited = await client.GetAsync("/api/v1/catalog/items/1");

        var problem = await AssertProblem(limited, HttpStatusCode.TooManyRequests);
        var retryAfter = int.Parse(limited.Headers.GetValues("Retry-After").Single());
        Assert.InRange(retryAfter, 1, 60);
        Assert.Equal(retryAfter, problem.GetProperty("retryAfterSeconds").GetInt32());
    }

    [Fact]
    public async Task RateLimit_Login_IsStricterThanReading()
    {
        using var gateway = new GatewayFactory(o => o.RateLimits.LoginPerWindow = 2);
        var client = ClientFor(gateway);

        static JsonContent Credentials() => JsonContent.Create(new { email = "a@b.c", password = "x" });

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/login", Credentials())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/login", Credentials())).StatusCode);

        var limited = await client.PostAsync("/api/v1/auth/login", Credentials());
        await AssertProblem(limited, HttpStatusCode.TooManyRequests);
        Assert.True(limited.Headers.Contains("Retry-After"));

        // читання каталогу тим самим клієнтом лишається доступним — ліміти розділені
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/catalog/items/1")).StatusCode);
        Assert.Equal(2, _fake.Calls("login"));   // третя спроба до Auth навіть не дійшла
    }

    [Fact]
    public async Task RateLimit_Authorized_IsCountedPerUser_NotPerAddress()
    {
        using var gateway = new GatewayFactory(o =>
        {
            o.RateLimits.AuthenticatedBurst = 2;
            o.RateLimits.AuthenticatedPerWindow = 2;
        });

        var anna = ClientFor(gateway, userId: 7, name: "Анна");
        var bohdan = ClientFor(gateway, userId: 8, name: "Богдан");

        Assert.Equal(HttpStatusCode.OK, (await anna.GetAsync("/api/echo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anna.GetAsync("/api/echo")).StatusCode);
        await AssertProblem(await anna.GetAsync("/api/echo"), HttpStatusCode.TooManyRequests);

        // Анна вичерпала свій ліміт — Богдан із тієї самої адреси його не відчуває
        Assert.Equal(HttpStatusCode.OK, (await bohdan.GetAsync("/api/echo")).StatusCode);
    }

    // ---------- 7. Таймаути проксі, health, документація ----------

    [Fact]
    public async Task Proxy_ServiceTooSlow_Returns504Problem_AfterTimeout_NotAfterServiceFinishes()
    {
        using var gateway = new GatewayFactory(o => o.Timeouts.ProxySeconds = 1);   // сервіс «думає» 4 с
        var client = ClientFor(gateway);

        var timer = Stopwatch.StartNew();
        var response = await client.GetAsync("/api/slow");
        timer.Stop();

        await AssertProblem(response, HttpStatusCode.GatewayTimeout);
        Assert.InRange(timer.ElapsedMilliseconds, 900, 3000);
    }

    [Fact]
    public async Task Proxy_ServiceFailsWithoutBody_BecomesProblem503()
    {
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway);

        await AssertProblem(await client.GetAsync("/api/boom"), HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Health_AllServicesUp_ReturnsOk_WithDependencies()
    {
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway);

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("ok", body.GetProperty("status").GetString());

        var dependencies = body.GetProperty("dependencies");
        Assert.Equal("ok", dependencies.GetProperty("auth").GetString());
        Assert.Equal("ok", dependencies.GetProperty("catalog").GetString());
        Assert.Equal("ok", dependencies.GetProperty("monolith").GetString());
        Assert.Equal("ok", dependencies.GetProperty("schedule").GetString());
    }

    [Fact]
    public async Task Health_ServiceDown_Returns503_Degraded()
    {
        _fake.Fail("health");

        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway);

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("degraded", body.GetProperty("status").GetString());
        Assert.Equal("down", body.GetProperty("dependencies").GetProperty("catalog").GetString());
    }

    [Fact]
    public async Task Swagger_DescribesCompositeEndpoint_WithBearerScheme()
    {
        using var gateway = new GatewayFactory();
        var client = ClientFor(gateway);

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var spec = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/screens/training/{itemId}", spec);
        Assert.Contains("\"bearer\"", spec);
        Assert.Contains("X-Partial-Response", spec);
        Assert.Contains("429", spec);
        Assert.Contains("503", spec);
    }
}
