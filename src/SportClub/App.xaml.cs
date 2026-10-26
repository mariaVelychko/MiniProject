using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportClubLibrary.Data;
using SportClubLibrary.Models;
using SportClubLibrary.Repositories;
using SportClubLibrary.Services;
using SportClubLibrary.Utilities;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace SportClubWPF
{
    public partial class App : Application
    {
        private ServiceProvider _serviceProvider;

        public App()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IServiceProvider>(provider => provider);

            services.AddSingleton<ILogger, FileLogger>();

            const string connectionString =
                "server=localhost;port=3307;database=sportclub_cs;user=root;password=1234;";

            services.AddSingleton<Func<SportClubDbContext>>(_ =>
                () => new SportClubDbContext(connectionString));

            services.AddSingleton<IAsyncRepository<Person>>(provider =>
                RepositoryFactory.CreatePersonRepository(
                    provider.GetRequiredService<Func<SportClubDbContext>>(),
                    provider.GetRequiredService<ILogger>()));

            services.AddSingleton<IAsyncRepository<Workout>>(provider =>
                RepositoryFactory.CreateWorkoutRepository(
                    provider.GetRequiredService<Func<SportClubDbContext>>(),
                    provider.GetRequiredService<ILogger>()));

            services.AddTransient<AuthService>();

            services.AddTransient<LoginWindow>();
            services.AddTransient<MainWindow>();
            services.AddTransient<AddWorkoutWindow>();
            services.AddTransient<AddUserWindow>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                var logger = _serviceProvider.GetRequiredService<ILogger>();
                logger.Log("Application Started", LogType.Info);

                var contextFactory = _serviceProvider.GetRequiredService<Func<SportClubDbContext>>();
                using (var db = contextFactory())
                {
                    await db.Database.EnsureCreatedAsync();
                }

                var personRepo = _serviceProvider.GetRequiredService<IAsyncRepository<Person>>();

                logger.Log("Data repositories initialized.", LogType.Info);

                var users = await personRepo.GetAllAsync();

                if (users == null || !users.Any())
                {
                    var admin = new Admin 
                    {
                        FullName = "Головний Адмін",
                        Email = "admin", 
                        Phone = "+38 (000) 000-00-00",
                        Password = PasswordHasher.Hash("admin")
                    };

                    await personRepo.AddAsync(admin);
                    logger.Log("Initial Admin user created.", LogType.Warn);
                }

                var loginWindow = _serviceProvider.GetRequiredService<LoginWindow>();
                this.MainWindow = loginWindow;
                loginWindow.Show();

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Критична помилка");
                Shutdown();
            }
        }

        public AddWorkoutWindow CreateAddWorkoutWindow(Person user)
        {
            return new AddWorkoutWindow(
                user,
                _serviceProvider.GetRequiredService<IAsyncRepository<Workout>>(),
                _serviceProvider.GetRequiredService<IAsyncRepository<Person>>(),
                _serviceProvider.GetRequiredService<ILogger>()
            );
        }

    }
}
