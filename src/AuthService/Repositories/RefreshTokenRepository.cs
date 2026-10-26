using AuthService.Data;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Data;

namespace AuthService.Repositories
{
    public class RefreshTokenRepository : BaseRepository<RefreshToken>, IRefreshTokenRepository
    {
        private readonly AuthDbContext _db;

        public RefreshTokenRepository(AuthDbContext db) : base(db)
        {
            _db = db;
        }

        public Task<RefreshToken?> FindByTokenAsync(string token) =>
            _db.RefreshTokens.Include(rt => rt.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(rt => rt.Token == token);
    }
}
