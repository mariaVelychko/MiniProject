using System.Net;
using System.Net.Http.Json;

namespace SportClub.Api.Services;

public record CatalogItemInfo(int Id, string Title, decimal Price, int DurationMin, int Capacity, bool IsActive);

public class CatalogUnavailableException : Exception
{
    public CatalogUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}

public interface ICatalogClient
{
    /// <returns>Позиція каталогу, або null, якщо такої немає (404).</returns>
    /// <exception cref="CatalogUnavailableException"
    Task<CatalogItemInfo?> GetItemAsync(int id, CancellationToken cancellationToken = default);
}

public class CatalogClient : ICatalogClient
{
    private readonly HttpClient _http;

    public CatalogClient(HttpClient http) => _http = http;

    public async Task<CatalogItemInfo?> GetItemAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"api/v1/catalog/items/{id}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<CatalogItemInfo>(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new CatalogUnavailableException("CatalogService недоступний", ex);
        }
    }
}
