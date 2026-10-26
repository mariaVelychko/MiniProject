using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ScheduleService.Data;

/// <summary>Потрібна лише для <c>dotnet ef</c>: підключення до БД під час генерації міграцій не відкривається.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<ScheduleDbContext>
{
    public ScheduleDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__ScheduleDb")
                 ?? "server=localhost;port=3307;database=miniproject_schedule;user=root;";

        var options = new DbContextOptionsBuilder<ScheduleDbContext>()
            .UseMySql(cs, new MySqlServerVersion(new Version(8, 0, 39)))
            .Options;

        return new ScheduleDbContext(options);
    }
}
