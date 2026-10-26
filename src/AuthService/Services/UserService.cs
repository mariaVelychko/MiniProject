using AuthService.Data;
using AuthService.Dtos;
using AuthService.Models;
using AuthService.Repositories;
using Microsoft.EntityFrameworkCore;
using Shared.Common;
using Shared.Dtos;

namespace AuthService.Services
{
    public class UserService
    {
        private const int MaxPageSize = 100;

        private readonly IUserRepository _users;
        private readonly AuthDbContext _db;
        private readonly ILogger<UserService> _logger;

        public UserService(IUserRepository users, AuthDbContext db, ILogger<UserService> logger)
        {
            _users = users;
            _db = db;
            _logger = logger;
        }

        public async Task<ServiceResult<UserProfileDto>> GetMeAsync(int userId)
        {
            var user = await _users.GetWithRolesAsync(userId);

            return user is null
                ? ServiceResult<UserProfileDto>.Failure(ServiceErrorType.NotFound, "Користувача не знайдено.")
                : ServiceResult<UserProfileDto>.Success(ToProfileDto(user));
        }

        public async Task<ServiceResult<PagedResult<UserListItemDto>>> GetUsersPagedAsync(int page, int pageSize)
        {
            if (page < 1)
            {
                return ServiceResult<PagedResult<UserListItemDto>>.Failure(
                    ServiceErrorType.Validation, "page має бути не менше 1.");
            }

            if (pageSize < 1 || pageSize > MaxPageSize)
            {
                return ServiceResult<PagedResult<UserListItemDto>>.Failure(
                    ServiceErrorType.Validation, $"pageSize має бути від 1 до {MaxPageSize}.");
            }

            var (items, total) = await _users.GetPagedAsync(page, pageSize);

            var dto = new PagedResult<UserListItemDto>
            {
                Items = items.Select(ToListItemDto).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
            };

            return ServiceResult<PagedResult<UserListItemDto>>.Success(dto);
        }

        public async Task<ServiceResult> UpdateRolesAsync(int userId, IReadOnlyCollection<string> roleNames)
        {
            var user = await _users.GetWithRolesAsync(userId);

            if (user is null)
            {
                return ServiceResult.Failure(ServiceErrorType.NotFound, "Користувача не знайдено.");
            }

            var distinctNames = roleNames.Distinct().ToList();
            var roles = await _db.Roles.Where(r => distinctNames.Contains(r.Name)).ToListAsync();

            if (roles.Count != distinctNames.Count)
            {
                return ServiceResult.Failure(ServiceErrorType.Validation, "Серед ролей є невідомі значення.");
            }

            var desiredIds = roles.Select(r => r.Id).ToHashSet();

            user.UserRoles.RemoveAll(ur => !desiredIds.Contains(ur.RoleId));

            foreach (var role in roles)
            {
                if (user.UserRoles.All(ur => ur.RoleId != role.Id))
                {
                    user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
                }
            }

            await _users.SaveChangesAsync();

            _logger.LogInformation("Змінено ролі користувача {UserId}: {Roles}", user.Id, string.Join(", ", distinctNames));

            return ServiceResult.Success();
        }

        private static UserProfileDto ToProfileDto(User user) => new(
            user.Id, user.Email, user.DisplayName, user.CreatedAt,
            user.UserRoles.Select(ur => ur.Role.Name).ToList());

        private static UserListItemDto ToListItemDto(User user) => new(
            user.Id, user.Email, user.DisplayName, user.IsActive,
            user.UserRoles.Select(ur => ur.Role.Name).ToList());
    }
}
