using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ApiGateway.Clients;

public enum UpstreamStatus
{
    /// <summary>Сервіс відповів, дані є.</summary>
    Ok,

    /// <summary>Сервіс відповів: такого об'єкта немає. Це НЕ те саме, що «сервіс недоступний».</summary>
    NotFound,

    /// <summary>Сервіс не відповів: таймаут, з'єднання, 5xx, нечитабельна відповідь.</summary>
    Unavailable,
}

/// <summary>
/// Результат виклику сусіднього сервісу. Явний статус замість null: клієнт шлюзу має відрізняти
/// «даних немає» від «сервіс недоступний» (ЛР3, 3.2) — інакше користувач без відповіді Schedule вирішить,
/// що в нього немає жодного запису, і створить другий.
/// </summary>
public sealed record Upstream<T>(UpstreamStatus Status, T? Value = default)
{
    public bool IsOk => Status == UpstreamStatus.Ok;

    public static Upstream<T> Ok(T value) => new(UpstreamStatus.Ok, value);

    public static Upstream<T> NotFound() => new(UpstreamStatus.NotFound);

    public static Upstream<T> Unavailable() => new(UpstreamStatus.Unavailable);
}

internal static class UpstreamHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// GET з розбором JSON. Ніколи не кидає через збій сусіда — повертає Upstream.Unavailable.
    /// Виняток — скасування самим клієнтом (ct): його пропускаємо далі.
    /// </summary>
    public static async Task<Upstream<T>> GetAsync<T>(HttpClient http, string url, ILogger log, string service,
        CancellationToken ct, string? bearerToken = null, params HttpStatusCode[] notFoundCodes)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (bearerToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            using var response = await http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
                return value is null ? Upstream<T>.Unavailable() : Upstream<T>.Ok(value);
            }

            HttpStatusCode[] codes = notFoundCodes.Length == 0 ? [HttpStatusCode.NotFound] : notFoundCodes;

            if (codes.Contains(response.StatusCode))
            {
                return Upstream<T>.NotFound();
            }

            log.LogWarning("{Service}: неочікувана відповідь {Status} на GET {Url}", service, (int)response.StatusCode, url);
            return Upstream<T>.Unavailable();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            // OperationCanceledException тут — це таймаут HttpClient (дедлайн), а не скасування клієнтом.
            log.LogWarning("{Service}: не вдалося виконати GET {Url}: {Reason}", service, url, ex.GetType().Name);
            return Upstream<T>.Unavailable();
        }
    }
}
