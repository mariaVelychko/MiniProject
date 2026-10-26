using Microsoft.EntityFrameworkCore;

namespace Shared.Data
{
    public static class DatabaseInitializer
    {
        public const string ResetVariable = "RESET_DB";

        public static async Task EnsureReadyAsync(DbContext db)
        {
            if (Environment.GetEnvironmentVariable(ResetVariable) == "1")
            {
                await db.Database.EnsureDeletedAsync();
            }

            await db.Database.EnsureCreatedAsync();
        }
    }
}
