using System.Net.Http.Json;
using SportClub.Api.Contracts;

namespace SportClub.Api.Services;

public class ScheduleUnavailableException : Exception
{
    public ScheduleUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>Клієнт до Schedule-сервісу (ЛР2): тренування й записи тепер живуть там, а не в БД моноліту.</summary>
public interface IScheduleClient
{
    Task<IReadOnlyList<WorkoutResponse>> GetByCoachAsync(int coachId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkoutResponse>> GetByClientAsync(int clientId, CancellationToken cancellationToken = default);
}

public class ScheduleClient : IScheduleClient
{
    private readonly HttpClient _http;

    public ScheduleClient(HttpClient http) => _http = http;

    public Task<IReadOnlyList<WorkoutResponse>> GetByCoachAsync(int coachId, CancellationToken cancellationToken = default) =>
        GetListAsync($"api/workouts?coachId={coachId}", cancellationToken);

    public Task<IReadOnlyList<WorkoutResponse>> GetByClientAsync(int clientId, CancellationToken cancellationToken = default) =>
        GetListAsync($"api/bookings/client/{clientId}", cancellationToken);

    private async Task<IReadOnlyList<WorkoutResponse>> GetListAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<WorkoutResponse>>(cancellationToken)
                   ?? new List<WorkoutResponse>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ScheduleUnavailableException("Schedule-сервіс недоступний", ex);
        }
    }
}
