using SportClubLibrary.Models;
using SportClubLibrary.Utilities;

namespace SportClubLibrary.Services
{
    public class AuthService
    {
        private readonly IAsyncRepository<Person> _repository;
        private readonly ILogger _logger;

        public AuthService(IAsyncRepository<Person> repository, ILogger logger)
        {
            _repository = repository;
            _logger = logger;

        }

        private async Task InitializeAdminAsync()
        {
            var users = await _repository.GetAllAsync();

            if (!users.Any())
            {
                var admin = new Admin
                {
                    FullName = "Головний Адмін",
                    Email = "admin",
                    Password = PasswordHasher.Hash("admin")
                };

                await _repository.AddAsync(admin);
                await _logger.LogAsync("Автоматично створено адміністратора");
            }
        }

        public async Task<Person> LoginAsync(string email, string password)
        {
            var users = await _repository.GetAllAsync();
            string passwordHash = PasswordHasher.Hash(password);

            var user = users.FirstOrDefault(u =>
                u.Email == email && u.Password == passwordHash);

            if (user == null)
                await _logger.LogAsync($"Невдала спроба входу: {email}", LogType.Warn);
            else
                await _logger.LogAsync($"Успішний вхід: {email}");

            return user;
        }
    }
}
