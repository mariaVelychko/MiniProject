using Microsoft.EntityFrameworkCore;

namespace SportClubLibrary.Data
{
   
    public static class DatabaseInitializer
    {
        public const string ResetVariable = "RESET_DB";

        public static async Task EnsureReadyAsync(SportClubDbContext db)
        {
            if (Environment.GetEnvironmentVariable(ResetVariable) == "1")
            {
                await db.Database.EnsureDeletedAsync();
            }

            await db.Database.EnsureCreatedAsync();
            await AddMissingColumnsAsync(db);
        }

        private static async Task AddMissingColumnsAsync(SportClubDbContext db)
        {
            var columnCount = await db.Database
                .SqlQueryRaw<long>(
                    "SELECT COUNT(*) AS `Value` FROM information_schema.COLUMNS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) = 'workouts' AND COLUMN_NAME = 'CatalogItemId'")
                .SingleAsync();

            if (columnCount == 0)
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE `Workouts` ADD COLUMN `CatalogItemId` int NULL");
            }
        }
    }
}
