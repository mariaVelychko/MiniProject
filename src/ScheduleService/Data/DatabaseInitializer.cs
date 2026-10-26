using Microsoft.EntityFrameworkCore;

namespace ScheduleService.Data;

public static class DatabaseInitializer
{
    public const string ResetVariable = "RESET_DB";

    /// <summary>
    /// База створюється з нуля однією дією: застосовуються міграції з репозиторію
    /// (<c>dotnet ef migrations add InitialCreate</c>); якщо міграцій ще немає — EnsureCreated.
    /// </summary>
    public static async Task EnsureReadyAsync(ScheduleDbContext db, CancellationToken ct = default)
    {
        if (Environment.GetEnvironmentVariable(ResetVariable) == "1")
        {
            await db.Database.EnsureDeletedAsync(ct);
        }

        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(ct);
        }
    }
}
