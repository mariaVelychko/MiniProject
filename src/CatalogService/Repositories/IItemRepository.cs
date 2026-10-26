using CatalogService.Models;
using Shared.Data;

namespace CatalogService.Repositories
{
    public record ItemQuery(int Page, int PageSize, string? Search, int? CategoryId, string? SortBy, bool Desc);

    public interface IItemRepository : IBaseRepository<Item>
    {
        Task<Item?> GetByIdWithCategoryAsync(int id);
        Task<(IReadOnlyList<Item> Items, int TotalCount)> GetPagedAsync(ItemQuery query);
    }
}
