using AuthService.Dtos;
using AuthService.Models;
using AuthService.Repositories;
using Shared.Common;

namespace AuthService.Services
{
    public class AuthenticationService
    {
        private readonly IUserRepository _users;
        private readonly IRefreshTokenRepository _refreshTokens;
        private readonly TokenService _tokenService;
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(
            IUserRepository users,
            IRefreshTokenRepository refreshTokens,
            TokenService tokenService,
            ILogger<AuthenticationService> logger)
        {
            _users = users;
            _refreshTokens = refreshTokens;
            _tokenService = tokenService;
            _logger = logger;
        }

        public async Task<ServiceResult<TokenResponse>> RegisterAsync(RegisterRequest request)
        {
            if (await _users.ExistsByEmailAsync(request.Email))
            {
                _logger.LogWarning("Реєстрацію відхилено: email {Email} уже зайнятий", request.Email);

                return ServiceResult<TokenResponse>.Failure(
                    ServiceErrorType.Conflict, "Користувач з таким email вже існує.");
            }

            var user = new User
            {
                Email = request.Email,
                DisplayName = request.DisplayName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            };

            user.UserRoles.Add(new UserRole { RoleId = 1 });

            await _users.AddAsync(user);

            var withRoles = await _users.GetWithRolesAsync(user.Id) ?? user;

            _logger.LogInformation("Зареєстровано нового користувача {UserId} ({Email})", user.Id, user.Email);

            return ServiceResult<TokenResponse>.Success(await IssueTokenPairAsync(withRoles));
        }

        public async Task<ServiceResult<TokenResponse>> LoginAsync(LoginRequest request)
        {
            var user = await _users.FindByEmailWithRolesAsync(request.Email);

            if (user is null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                _logger.LogWarning("Невдала спроба входу для {Email}", request.Email);

                return ServiceResult<TokenResponse>.Failure(
                    ServiceErrorType.Unauthorized, "Невірний email або пароль.");
            }

            _logger.LogInformation("Успішний вхід користувача {UserId} ({Email})", user.Id, user.Email);

            return ServiceResult<TokenResponse>.Success(await IssueTokenPairAsync(user));
        }

        public async Task<ServiceResult<TokenResponse>> RefreshAsync(string refreshTokenValue)
        {
            var existing = await _refreshTokens.FindByTokenAsync(refreshTokenValue);

            if (existing is null || !existing.IsActive)
            {
                _logger.LogWarning("Спроба оновлення за недійсним або відкликаним refresh-токеном");

                return ServiceResult<TokenResponse>.Failure(
                    ServiceErrorType.Unauthorized, "Refresh-токен недійсний або прострочений.");
            }

            var pair = await IssueTokenPairAsync(existing.User);

            existing.RevokedAt = DateTime.UtcNow;
            existing.ReplacedByToken = pair.RefreshToken;
            await _refreshTokens.SaveChangesAsync();

            _logger.LogInformation("Пару токенів оновлено для користувача {UserId} (ротація refresh-токена)", existing.UserId);

            return ServiceResult<TokenResponse>.Success(pair);
        }

        public async Task<ServiceResult> LogoutAsync(string refreshTokenValue)
        {
            var existing = await _refreshTokens.FindByTokenAsync(refreshTokenValue);

            if (existing is null || !existing.IsActive)
            {
                _logger.LogWarning("Спроба виходу з недійсним refresh-токеном");

                return ServiceResult.Failure(ServiceErrorType.NotFound, "Refresh-токен не знайдено.");
            }

            existing.RevokedAt = DateTime.UtcNow;
            await _refreshTokens.SaveChangesAsync();

            _logger.LogInformation("Користувач {UserId} вийшов: refresh-токен відкликано", existing.UserId);

            return ServiceResult.Success();
        }

        private async Task<TokenResponse> IssueTokenPairAsync(User user)
        {
            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshTokenValue = _tokenService.GenerateRefreshTokenValue();

            await _refreshTokens.AddAsync(new RefreshToken
            {
                UserId = user.Id,
                Token = refreshTokenValue,
                ExpiresAt = _tokenService.GetRefreshTokenExpiry(),
            });

            return new TokenResponse(accessToken, refreshTokenValue, _tokenService.GetAccessTokenExpiry());
        }
    }
}
