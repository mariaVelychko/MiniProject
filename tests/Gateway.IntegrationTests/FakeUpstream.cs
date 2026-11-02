using System.Collections.Concurrent;

namespace Gateway.IntegrationTests;

/// <summary>
/// Справжній HTTP-сервер у тестовому процесі, що грає роль Auth, Catalog, Schedule і моноліту разом.
/// Шлюз звертається до нього по мережі, як до справжніх сервісів, — тож перевіряються і Ocelot-маршрути, і типізовані клієнти.
/// Він рахує виклики (для перевірки кешу й «у сервіси нічого не пішло») і вміє ламатися: помилка, затримка.
/// </summary>
public sealed class FakeUpstream : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, int> _calls = new();
    private readonly ConcurrentDictionary<string, int> _delays = new();
    private readonly ConcurrentDictionary<string, bool> _failures = new();
    private readonly ConcurrentDictionary<string, string> _seen = new();
    private readonly ConcurrentDictionary<string, bool> _bookingKeys = new();
    private readonly ConcurrentDictionary<(int Workout, int Client), bool> _booked = new();

    public int Port { get; private set; }

    public FakeUpstream()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        MapEndpoints();
    }

    public async Task StartAsync()
    {
        await _app.StartAsync();
        Port = new Uri(_app.Urls.First()).Port;
    }

    // ---------- керування з тестів ----------

    /// <summary>Скинути лічильники й збої перед кожним тестом.</summary>
    public FakeUpstream Reset()
    {
        _calls.Clear();
        _delays.Clear();
        _failures.Clear();
        _seen.Clear();
        _bookingKeys.Clear();
        _booked.Clear();
        _booked[(1, 501)] = true;   // у клієнтки «Анна» (id 501 у довіднику моноліту) уже є запис на тренування 1
        return this;
    }

    public int Calls(string name) => _calls.GetValueOrDefault(name);

    public int TotalCalls => _calls.Where(c => c.Key != "health").Sum(c => c.Value);

    public void Fail(string name, bool fail = true) => _failures[name] = fail;

    public void Delay(string name, int milliseconds) => _delays[name] = milliseconds;

    /// <summary>Додати запис клієнта на тренування (для підготовки сценарію).</summary>
    public void Seed(int workoutId, int clientId) => _booked[(workoutId, clientId)] = true;

    /// <summary>Прибрати всі записи.</summary>
    public void Clear() => _booked.Clear();

    public string Seen(string key) => _seen.GetValueOrDefault(key, string.Empty);

    // ---------- «сервіси» ----------

    private async Task<bool> EnterAsync(string name)
    {
        _calls.AddOrUpdate(name, 1, (_, v) => v + 1);

        if (_delays.TryGetValue(name, out var delay) && delay > 0)
        {
            await Task.Delay(delay);
        }

        return _failures.GetValueOrDefault(name);
    }

    private void MapEndpoints()
    {
        _app.MapGet("/health", async () => await EnterAsync("health") ? Results.StatusCode(500) : Results.Ok());

        // Catalog
        _app.MapGet("/api/v1/catalog/items/{id:int}", async (int id) =>
        {
            if (await EnterAsync("catalog"))
            {
                return Results.StatusCode(500);
            }

            return id == 404
                ? Results.NotFound()
                : Results.Json(new
                {
                    id, title = "Йога для початківців", description = "Базовий рівень", price = 250.00m, durationMin = 60, capacity = 15,
                    isActive = true, createdAt = "2026-10-26T15:13:39", categoryId = 1, categoryName = "Йога",
                });
        });

        _app.MapGet("/api/v1/catalog/items", async () =>
            await EnterAsync("catalog-list") ? Results.StatusCode(500) : Results.Json(new { items = new[] { 1, 2 }, total = 2 }));

        _app.MapPut("/api/v1/catalog/items/{id:int}", async (int id) =>
        {
            await EnterAsync("catalog-put");
            return Results.NoContent();
        });

        _app.MapPost("/api/v1/catalog/items", async () =>
        {
            await EnterAsync("catalog-post");
            return Results.Created("/api/v1/catalog/items/9", new { id = 9 });
        });

        // Auth
        _app.MapPost("/api/v1/auth/login", async () =>
        {
            await EnterAsync("login");
            return Results.Ok(new { accessToken = "x" });
        });

        _app.MapGet("/api/v1/users/me", async (HttpRequest request) =>
        {
            var failed = await EnterAsync("auth");
            _seen["me:X-User-Id"] = request.Headers["X-User-Id"].ToString();

            if (failed)
            {
                return Results.StatusCode(500);
            }

            var token = request.Headers.Authorization.ToString().Replace("Bearer ", string.Empty);
            var (userId, displayName, role) = TestTokens.Read(token);

            return Results.Json(new { id = userId, email = $"{displayName.ToLowerInvariant()}@test.ua", displayName, createdAt = "2026-10-01T10:00:00", roles = new[] { role } });
        });

        // Schedule (і заодно моноліт: у шлюзі це різні маршрути, у стенді — один сервер)
        _app.MapGet("/api/workouts", async (HttpRequest request) =>
        {
            if (await EnterAsync("schedule"))
            {
                return Results.StatusCode(503);
            }

            // Для позиції каталогу 2 тренувань немає: сервіс ВІДПОВІДАЄ порожнім списком (це не збій).
            if (request.Query["catalogItemId"] == "2")
            {
                return Results.Json(Array.Empty<object>());
            }

            return Results.Json(new[]
            {
                new
                {
                    id = 1, catalogItemId = 1, type = "Йога для початківців", time = "2026-12-01T18:00:00", durationMin = 60, hall = "Зал 1",
                    coachId = 2, coachName = "Іван Ковальчук", participants = 2, maxParticipants = 10, participantIds = new[] { 3, 4 },
                },
            });
        });

        // Моноліт: довідник людей. Id в довіднику (501) НЕ збігається з id в Auth (7) — як у справжній системі.
        _app.MapGet("/api/users", async (HttpRequest request) =>
        {
            if (await EnterAsync("users"))
            {
                return Results.StatusCode(503);
            }

            return Results.Json(new[]
            {
                new { id = 501, fullName = "Анна", phone = "0501112233", email = "анна@test.ua", role = "client", roleInfo = "Клієнт", specialization = (string?)null },
            });
        });

        // Schedule: записи клієнта
        _app.MapGet("/api/bookings/client/{clientId:int}", async (int clientId) =>
        {
            _seen["bookings:clientId"] = clientId.ToString();

            if (await EnterAsync("bookings-list"))
            {
                return Results.StatusCode(503);
            }

            var mine = _booked.Where(b => b.Key.Client == clientId).Select(b => b.Key.Workout).Order().ToList();

            return Results.Json(mine.Select(w => new
            {
                id = w, catalogItemId = 1, type = "Йога для початківців", time = w == 1 ? "2099-01-01T18:00:00" : "2020-01-01T18:00:00",
                durationMin = 60, hall = "Зал 1", coachId = 2, coachName = "Іван Ковальчук", participants = 2, maxParticipants = 10,
                participantIds = new[] { 3, 4 },
            }));
        });

        _app.MapPost("/api/bookings", async (HttpRequest request) =>
        {
            var body = await request.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var workoutId = body.GetProperty("workoutId").GetInt32();
            var clientId = body.GetProperty("clientId").GetInt32();
            var key = request.Headers["Idempotency-Key"].ToString();

            _seen["book:clientId"] = clientId.ToString();
            _seen["book:key"] = key;
            _calls.AddOrUpdate("book", 1, (_, v) => v + 1);

            if (_failures.GetValueOrDefault("book"))
            {
                return Results.StatusCode(503);
            }

            if (_bookingKeys.ContainsKey(key))
            {
                request.HttpContext.Response.Headers["Idempotency-Replayed"] = "true";
                return Results.Json(new { id = workoutId });
            }

            if (workoutId == 404)
            {
                return Results.Json(new { message = "Тренування 404 не знайдено" }, statusCode: 404);
            }

            if (workoutId == 409)
            {
                return Results.Json(new { message = "Вільних місць немає" }, statusCode: 409);
            }

            _bookingKeys[key] = true;
            _booked[(workoutId, clientId)] = true;
            _calls.AddOrUpdate("book-created", 1, (_, v) => v + 1);
            return Results.Json(new { id = workoutId });
        });

        _app.MapDelete("/api/bookings/{workoutId:int}/{clientId:int}", async (int workoutId, int clientId) =>
        {
            _seen["cancel:clientId"] = clientId.ToString();

            if (await EnterAsync("cancel"))
            {
                return Results.StatusCode(503);
            }

            return _booked.TryRemove((workoutId, clientId), out _)
                ? Results.NoContent()
                : Results.Json(new { message = "Клієнт не записаний на це тренування" }, statusCode: 404);
        });

        // Допоміжні маршрути для перевірки проксі (йдуть через резервний маршрут /api/{everything})
        _app.MapGet("/api/echo", (HttpRequest request) =>
        {
            _calls.AddOrUpdate("echo", 1, (_, v) => v + 1);
            return Results.Json(new { userId = request.Headers["X-User-Id"].ToString(), roles = request.Headers["X-User-Roles"].ToString() });
        });

        _app.MapGet("/api/slow", async () =>
        {
            await EnterAsync("slow");
            await Task.Delay(TimeSpan.FromSeconds(4));
            return Results.Ok("пізно");
        });

        _app.MapGet("/api/boom", async () =>
        {
            await EnterAsync("boom");
            return Results.StatusCode(500);
        });

        _app.MapGet("/api/own-error", async () =>
        {
            await EnterAsync("own-error");
            return Results.Json(new { message = "Вільних місць немає" }, statusCode: 409);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
