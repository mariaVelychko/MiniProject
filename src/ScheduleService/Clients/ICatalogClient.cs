namespace ScheduleService.Clients;

public sealed record CatalogItemInfo(int Id, string Title, decimal Price, int DurationMin, int Capacity, bool IsActive);

public interface ICatalogClient
{
    /// <returns>Позиція каталогу або null, якщо її немає (404).</returns>
    /// <exception cref="CatalogUnavailableException">Каталог недоступний або не відповів у межах дедлайну.</exception>
    Task<CatalogItemInfo?> GetItemAsync(int id, CancellationToken ct = default);

    Task<bool> PingAsync(CancellationToken ct = default);
}

public sealed class CatalogClient(HttpClient http, IConfiguration cfg) : ICatalogClient
{
    private readonly TimeSpan _deadline = Downstream.Timeout(cfg, "Catalog");

    public Task<CatalogItemInfo?> GetItemAsync(int id, CancellationToken ct = default) =>
        Downstream.GetJsonOrNullAsync<CatalogItemInfo>(
            http, $"api/v1/catalog/items/{id}", _deadline, ct,
            (m, e) => new CatalogUnavailableException(m, e));

    public Task<bool> PingAsync(CancellationToken ct = default) => Downstream.PingAsync(http, _deadline, ct);
}
