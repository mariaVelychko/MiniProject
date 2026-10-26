using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScheduleService.Clients;

public class CatalogUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
public class UsersUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Спільна логіка виклику сусіднього сервісу: дедлайн, 404 → null, усе інше → «недоступний».</summary>
internal static class Downstream
{
    public static TimeSpan Timeout(IConfiguration cfg, string section) =>
        TimeSpan.FromSeconds(cfg.GetValue($"Services:{section}:TimeoutSeconds", 2.0));

    public static async Task<T?> GetJsonOrNullAsync<T>(
        HttpClient http, string url, TimeSpan deadline, CancellationToken ct,
        Func<string, Exception, Exception> unavailable) where T : class
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(deadline);

        try
        {
            using var response = await http.GetAsync(url, cts.Token);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<T>(cts.Token)
                   ?? throw unavailable("Порожня відповідь сусіднього сервісу", new InvalidOperationException());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // клієнт сам скасував запит — це не збій сусіда
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            // OperationCanceledException без ct.IsCancellationRequested == спрацював наш дедлайн
            throw unavailable($"Сусідній сервіс недоступний або відповів надто довго ({url})", ex);
        }
    }

    public static async Task<bool> PingAsync(HttpClient http, TimeSpan deadline, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(deadline);

        try
        {
            using var response = await http.GetAsync("health", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }
}
