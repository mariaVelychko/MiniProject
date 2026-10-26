using AuthService.Data;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Data;

namespace AuthService.Repositories
{
    public class UserRepository : BaseRepository<User>, IUserRepository
    {
        private readonly AuthDbContext _db;

        public UserRepository(AuthDbContext db) : base(db)
        {
            _db = db;
        }

        public Task<User?> FindByEmailWithRolesAsync(string email) =>
            _db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Email == email);

        public Task<User?> GetWithRolesAsync(int id) =>
            _db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == id);

        public Task<bool> ExistsByEmailAsync(string email) =>
            _db.Users.AnyAsync(u => u.Email == email);

        public async Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(int page, int pageSize)
        {
            var query = _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).OrderBy(u => u.Id);

            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return (items, total);
        }
    }
}
