using AuthService.Models;
using Shared.Data;

namespace AuthService.Repositories
{
    public interface IRefreshTokenRepository : IBaseRepository<RefreshToken>
    {
        Task<RefreshToken?> FindByTokenAsync(string token);
    }
}
