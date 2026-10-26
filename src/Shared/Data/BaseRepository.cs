using Microsoft.EntityFrameworkCore;

namespace Shared.Data
{
    public class BaseRepository<T> : IBaseRepository<T> where T : class
    {
        protected readonly DbContext Db;
        protected readonly DbSet<T> Set;

        public BaseRepository(DbContext db)
        {
            Db = db;
            Set = db.Set<T>();
        }

        public async Task<T?> GetByIdAsync(int id) => await Set.FindAsync(id);

        public async Task AddAsync(T entity)
        {
            await Set.AddAsync(entity);
            await SaveChangesAsync();
        }

        public async Task UpdateAsync(T entity)
        {
            Set.Update(entity);
            await SaveChangesAsync();
        }

        public async Task DeleteAsync(T entity)
        {
            Set.Remove(entity);
            await SaveChangesAsync();
        }

        public Task SaveChangesAsync() => Db.SaveChangesAsync();
    }
}
