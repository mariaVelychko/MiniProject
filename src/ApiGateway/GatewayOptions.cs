namespace ApiGateway;

/// <summary>Налаштування шлюзу (секція "Gateway" в appsettings.json / змінних середовища).</summary>
public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public TimeoutOptions Timeouts { get; set; } = new();
    public CacheOptions Cache { get; set; } = new();
    public RateLimitOptions RateLimits { get; set; } = new();
}

/// <summary>Таймаути всіх вихідних викликів шлюзу (ЛР3, 3.2).</summary>
public sealed class TimeoutOptions
{
    /// <summary>Виклики складеного ендпоінта до Auth / Catalog / Schedule. Короткий: екран не має «висіти».</summary>
    public int ClientSeconds { get; set; } = 2;

    /// <summary>Проксіювання звичайних маршрутів через Ocelot. Більший, бо тут бувають записи в БД.</summary>
    public int ProxySeconds { get; set; } = 5;

    /// <summary>Опитування /health сусідніх сервісів.</summary>
    public int HealthSeconds { get; set; } = 1;
}

public sealed class CacheOptions
{
    /// <summary>Час життя запису кешу каталогу (TTL). Каталог змінюється рідко, адмін-зміни скидають кеш одразу.</summary>
    public int CatalogTtlSeconds { get; set; } = 60;
}

/// <summary>
/// Ліміти запитів. Анонімні — за IP (фіксоване вікно), авторизовані — за користувачем (token bucket: дозволяє сплески),
/// вхід і реєстрація — жорсткіше й за IP (захист від підбору паролів).
/// </summary>
public sealed class RateLimitOptions
{
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Анонімні: запитів на вікно з одного IP.</summary>
    public int AnonymousPerWindow { get; set; } = 100;

    /// <summary>Авторизовані: місткість відра (короткий сплеск).</summary>
    public int AuthenticatedBurst { get; set; } = 100;

    /// <summary>Авторизовані: скільки жетонів повертається за вікно.</summary>
    public int AuthenticatedPerWindow { get; set; } = 120;

    /// <summary>Вхід/реєстрація: спроб на вікно з одного IP.</summary>
    public int LoginPerWindow { get; set; } = 5;
}
