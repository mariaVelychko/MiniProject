using AuthService.Models;
using Shared.Data;

namespace AuthService.Repositories
{
    public interface IUserRepository : IBaseRepository<User>
    {
        Task<User?> FindByEmailWithRolesAsync(string email);
        Task<User?> GetWithRolesAsync(int id);
        Task<bool> ExistsByEmailAsync(string email);
        Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(int page, int pageSize);
    }
}
