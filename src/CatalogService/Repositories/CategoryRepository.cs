using CatalogService.Data;
using CatalogService.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Data;

namespace CatalogService.Repositories
{
    public class CategoryRepository : BaseRepository<Category>, ICategoryRepository
    {
        private readonly CatalogDbContext _db;

        public CategoryRepository(CatalogDbContext db) : base(db)
        {
            _db = db;
        }

        public Task<List<Category>> GetAllAsync() => _db.Categories.OrderBy(c => c.Name).ToListAsync();

        public Task<bool> ExistsAsync(int id) => _db.Categories.AnyAsync(c => c.Id == id);
    }
}
