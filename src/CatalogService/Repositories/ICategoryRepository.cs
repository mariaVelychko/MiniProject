using CatalogService.Models;
using Shared.Data;

namespace CatalogService.Repositories
{
    public interface ICategoryRepository : IBaseRepository<Category>
    {
        Task<List<Category>> GetAllAsync();
        Task<bool> ExistsAsync(int id);
    }
}
