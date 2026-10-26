using ScheduleService.Clients;

namespace Schedule.IntegrationTests;

/// <summary>
/// Стаб Catalog-сервісу з відомими відповідями:
/// 1 — активна (місткість 12, ціна 250), 2 — вилучена, 3..7 — активні (місткість 5), інші — 404.
/// </summary>
public sealed class StubCatalogClient : ICatalogClient
{
    public volatile bool Down;

    public Task<CatalogItemInfo?> GetItemAsync(int id, CancellationToken ct = default)
    {
        if (Down)
        {
            throw new CatalogUnavailableException("stub: каталог недоступний");
        }

        CatalogItemInfo? item = id switch
        {
            1 => new(1, "Йога для початківців", 250m, 60, 12, true),
            2 => new(2, "Стара програма", 100m, 45, 10, false),
            >= 3 and <= 7 => new(id, $"Програма {id}", 300m, 50, 5, true),
            _ => null,
        };

        return Task.FromResult(item);
    }

    public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(!Down);
}

/// <summary>Стаб довідника користувачів: тренери 1..9, клієнти 10..999, інші — 404.</summary>
public sealed class StubUserDirectoryClient : IUserDirectoryClient
{
    public volatile bool Down;

    public Task<DirectoryUser?> GetUserAsync(int id, CancellationToken ct = default)
    {
        if (Down)
        {
            throw new UsersUnavailableException("stub: моноліт недоступний");
        }

        DirectoryUser? user = id switch
        {
            >= 1 and <= 9 => new(id, $"Тренер {id}", $"coach{id}@club.test", "+380500000000", "trainer", "Тренер: Йога", "Йога"),
            >= 10 and <= 999 => new(id, $"Клієнт {id}", $"client{id}@club.test", "+380501111111", "client", "Клієнт клубу", null),
            _ => null,
        };

        return Task.FromResult(user);
    }

    public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(!Down);
}
