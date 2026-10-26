using Microsoft.Extensions.DependencyInjection;
using SportClubLibrary.Services;
using SportClubLibrary.Utilities;
using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace SportClubWPF
{
    public partial class LoginWindow : Window
    {
        private readonly AuthService _authService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;

        public LoginWindow(
            AuthService authService,
            IServiceProvider serviceProvider,
            ILogger logger)
        {
            InitializeComponent();

            _authService = authService;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public LoginWindow(AuthService authService)
        {
            InitializeComponent();
            _authService = authService;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            string login = LoginBox.Text;
            string pass = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass))
            {
                new CustomMessageBox("Введіть логін та пароль!", "Помилка").ShowDialog();
                return;
            }

            var user = await _authService.LoginAsync(login, pass);

            if (user != null)
            {
                await _logger.LogAsync($"Успішний вхід: {user.FullName}", LogType.Info);
                new CustomMessageBox($"Ласкаво просимо, {user.FullName}!", "Успіх").ShowDialog();

                var main = ActivatorUtilities.CreateInstance<MainWindow>(
                    _serviceProvider, user);

                main.Show();
                this.Close();
            }
            else
            {
                await _logger.LogAsync($"Невдала спроба входу: {login}", LogType.Warn);
                new CustomMessageBox("Невірний логін або пароль. Спробуйте ще раз.", "Помилка входу").ShowDialog();
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
