using System.ComponentModel.DataAnnotations;

namespace AuthService.Dtos
{
    public class RegisterRequest
    {
        [Required, EmailAddress, MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(8), MaxLength(100)]
        public string Password { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string DisplayName { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class RefreshRequest
    {
        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class LogoutRequest
    {
        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }

    public record TokenResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt);

    public record UserProfileDto(int Id, string Email, string DisplayName, DateTime CreatedAt, IReadOnlyList<string> Roles);

    public record UserListItemDto(int Id, string Email, string DisplayName, bool IsActive, IReadOnlyList<string> Roles);

    public class UpdateRolesRequest
    {
        [Required, MinLength(1)]
        public List<string> Roles { get; set; } = new();
    }
}
