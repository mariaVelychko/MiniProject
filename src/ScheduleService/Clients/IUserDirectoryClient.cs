namespace ScheduleService.Clients;

public sealed record DirectoryUser(
    int Id, string FullName, string Email, string Phone, string Role, string RoleInfo, string? Specialization);

/// <summary>Довідник користувачів (їх досі веде моноліт). Читаємо лише по мережі, у свою БД не копіюємо.</summary>
public interface IUserDirectoryClient
{
    /// <returns>Користувач або null, якщо його немає (404).</returns>
    /// <exception cref="UsersUnavailableException">Сервіс користувачів недоступний.</exception>
    Task<DirectoryUser?> GetUserAsync(int id, CancellationToken ct = default);

    Task<bool> PingAsync(CancellationToken ct = default);
}

public sealed class UserDirectoryClient(HttpClient http, IConfiguration cfg) : IUserDirectoryClient
{
    private readonly TimeSpan _deadline = Downstream.Timeout(cfg, "Users");

    public Task<DirectoryUser?> GetUserAsync(int id, CancellationToken ct = default) =>
        Downstream.GetJsonOrNullAsync<DirectoryUser>(
            http, $"api/users/{id}", _deadline, ct,
            (m, e) => new UsersUnavailableException(m, e));

    public Task<bool> PingAsync(CancellationToken ct = default) => Downstream.PingAsync(http, _deadline, ct);
}
