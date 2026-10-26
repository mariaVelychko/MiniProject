using CatalogService.Data;
using CatalogService.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Data;

namespace CatalogService.Repositories
{
    public class ItemRepository : BaseRepository<Item>, IItemRepository
    {
        private readonly CatalogDbContext _db;

        public ItemRepository(CatalogDbContext db) : base(db)
        {
            _db = db;
        }

        public Task<Item?> GetByIdWithCategoryAsync(int id) =>
            _db.Items.Include(i => i.Category).FirstOrDefaultAsync(i => i.Id == id);

        public async Task<(IReadOnlyList<Item> Items, int TotalCount)> GetPagedAsync(ItemQuery query)
        {
            IQueryable<Item> items = _db.Items.Include(i => i.Category).Where(i => i.IsActive);

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var pattern = $"%{query.Search}%";
                items = items.Where(i => EF.Functions.Like(i.Title, pattern));
            }

            if (query.CategoryId is not null)
            {
                items = items.Where(i => i.CategoryId == query.CategoryId);
            }

            items = (query.SortBy?.ToLowerInvariant()) switch
            {
                "price" => query.Desc ? items.OrderByDescending(i => i.Price) : items.OrderBy(i => i.Price),
                "createdat" => query.Desc ? items.OrderByDescending(i => i.CreatedAt) : items.OrderBy(i => i.CreatedAt),
                _ => query.Desc ? items.OrderByDescending(i => i.Title) : items.OrderBy(i => i.Title),
            };

            var total = await items.CountAsync();
            var page = await items.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync();

            return (page, total);
        }
    }
}
