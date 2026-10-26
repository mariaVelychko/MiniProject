using Xunit;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;
using SportClubLibrary.Services;
using SportClubLibrary.Interfaces;
using SportClubLibrary.Models;
using SportClubLibrary.Utilities;

namespace SportClub.Tests
{
    public class AuthServiceTests
    {
        [Fact]
        public async Task LoginAsync_ValidCredentials_ReturnsUser()
        {
            var mockRepo = new Mock<IAsyncRepository<Person>>();
            var mockLogger = new Mock<ILogger>();

            string email = "test@user.com";
            string password = "password123";
            string hash = PasswordHasher.Hash(password);

            var fakeUsers = new List<Person>
            {
                new Client { Id = 1, Email = email, Password = hash, FullName = "Test User" }
            };

            mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(fakeUsers);

            var authService = new AuthService(mockRepo.Object, mockLogger.Object);

            var result = await authService.LoginAsync(email, password);

            Assert.NotNull(result);
            Assert.Equal(email, result.Email);
            Assert.IsType<Client>(result);
        }

        [Fact]
        public async Task LoginAsync_InvalidPassword_ReturnsNull()
        {
            var mockRepo = new Mock<IAsyncRepository<Person>>();
            var mockLogger = new Mock<ILogger>();

            string email = "admin";
            string correctHash = PasswordHasher.Hash("admin");

            var fakeUsers = new List<Person>
            {
                new Admin { Id = 1, Email = email, Password = correctHash }
            };

            mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(fakeUsers);

            var authService = new AuthService(mockRepo.Object, mockLogger.Object);

            var result = await authService.LoginAsync("admin", "wrong_password");

            Assert.Null(result);
        }


    }
}
