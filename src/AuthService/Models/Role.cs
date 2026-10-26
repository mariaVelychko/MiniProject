namespace AuthService.Models
{
    public class Role
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public List<UserRole> UserRoles { get; set; } = new();

        public const string User = "User";
        public const string Admin = "Admin";
    }
}
