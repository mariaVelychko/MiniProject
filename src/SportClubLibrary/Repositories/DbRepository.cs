using Microsoft.EntityFrameworkCore;
using SportClubLibrary.Data;
using SportClubLibrary.Interfaces;
using SportClubLibrary.Utilities;

namespace SportClubLibrary.Repositories
{
    public class DbRepository<T> : IAsyncRepository<T> where T : class, IEntity
    {
        private readonly Func<SportClubDbContext> _contextFactory;
        private readonly ILogger _logger;
        private readonly Func<IQueryable<T>, IQueryable<T>> _include;
        private readonly Action<SportClubDbContext, T> _attachRelated;
        private readonly Func<SportClubDbContext, T, T, Task> _applyUpdate;

        public DbRepository(
            Func<SportClubDbContext> contextFactory,
            ILogger logger,
            Func<IQueryable<T>, IQueryable<T>>? include = null,
            Action<SportClubDbContext, T>? attachRelated = null,
            Func<SportClubDbContext, T, T, Task>? applyUpdate = null)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _include = include ?? (q => q);
            _attachRelated = attachRelated ?? ((_, _) => { });
            _applyUpdate = applyUpdate ?? DefaultApplyUpdateAsync;
        }

        private static Task DefaultApplyUpdateAsync(SportClubDbContext db, T existing, T incoming)
        {
            db.Entry(existing).CurrentValues.SetValues(incoming);
            return Task.CompletedTask;
        }

        public Task ReloadAsync() => Task.CompletedTask;

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            using var db = _contextFactory();
            return await _include(db.Set<T>()).AsNoTracking().ToListAsync();
        }

        public async Task<T> GetByIdAsync(int id)
        {
            using var db = _contextFactory();
            return await _include(db.Set<T>()).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task AddAsync(T item)
        {
            using var db = _contextFactory();

            _attachRelated(db, item);
            db.Set<T>().Add(item);
            await db.SaveChangesAsync();

            await _logger.LogAsync($"Додано ID {item.Id}");
        }

        public async Task UpdateAsync(T item)
        {
            using var db = _contextFactory();

            var existing = await _include(db.Set<T>()).FirstOrDefaultAsync(x => x.Id == item.Id);

            if (existing is null)
            {
                await _logger.LogAsync($"Оновлення неіснуючого ID {item.Id}", LogType.Warn);
                return;
            }

            await _applyUpdate(db, existing, item);
            await db.SaveChangesAsync();

            await _logger.LogAsync($"Оновлено ID {item.Id}");
        }

        public async Task DeleteAsync(int id)
        {
            using var db = _contextFactory();

            var existing = await db.Set<T>().FindAsync(id);

            if (existing is null)
            {
                await _logger.LogAsync($"Спроба видалити неіснуючий ID {id}", LogType.Warn);
                return;
            }

            db.Set<T>().Remove(existing);
            await db.SaveChangesAsync();

            await _logger.LogAsync($"Видалено ID {id}");
        }
    }
}
