using Microsoft.Extensions.DependencyInjection;
using SportClubLibrary.Models;
using SportClubLibrary.Utilities;
using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;

namespace SportClubWPF
{
    public partial class MainWindow : Window
    {
        private readonly IAsyncRepository<Person> _userRepo;
        private readonly IAsyncRepository<Workout> _workoutRepo;
        private readonly ILogger _logger;

        private readonly IServiceProvider _serviceProvider;
        private readonly Person _currentUser;

        private NotifyIcon _notifyIcon = null!;
        private bool _isExit = false;

        public MainWindow(
            Person user,
            IAsyncRepository<Person> userRepo,
            IAsyncRepository<Workout> workoutRepo,
            ILogger logger,
            IServiceProvider serviceProvider)
        {
            InitializeComponent();

            _currentUser = user;
            _userRepo = userRepo;
            _workoutRepo = workoutRepo;
            _logger = logger;
            _serviceProvider = serviceProvider;

            InitTray();
            SetupInterface();
            _ = LoadScheduleAsync();
            _ = LoadUsersAsync();


        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) =>
            this.WindowState = WindowState.Minimized;

        private void InitTray()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "SportClub Manager"
            };

            _notifyIcon.DoubleClick += (s, args) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Відкрити", null, (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
            });
            contextMenu.Items.Add("Вийти", null, (s, e) =>
            {
                _isExit = true;
                this.Close();
            });

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExit)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                _notifyIcon?.Dispose();
                base.OnClosing(e);
            }
        }


        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            BtnNavSchedule.Style = (Style)FindResource("MenuButtonStyle");
            BtnNavProfile.Style = (Style)FindResource("MenuButtonStyle");
            BtnNavAdmin.Style = (Style)FindResource("MenuButtonStyle");

            btn.Style = (Style)FindResource("ActiveMenuButtonStyle");

            if (btn == BtnNavSchedule) MainTabs.SelectedItem = TabSchedule;
            else if (btn == BtnNavProfile) MainTabs.SelectedItem = TabProfile;
            else if (btn == BtnNavAdmin) MainTabs.SelectedItem = TabAdmin;
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            _isExit = true;
            _notifyIcon?.Dispose();

            await _logger.LogAsync($"Користувач {_currentUser.Email} вийшов із системи.", LogType.Info);

            var login = _serviceProvider.GetRequiredService<LoginWindow>();
            login.Show();

            this.Close();
        }


        private void SetupInterface()
        {
            MenuUserName.Text = _currentUser.FullName;
            MenuUserRole.Text = _currentUser.GetRoleInfo();
            ProfileName.Text = _currentUser.FullName;
            ProfilePhone.Text = _currentUser.Phone;
            ProfileRole.Text = _currentUser.GetRoleInfo();

            if (_currentUser is Admin)
            {
                BtnNavAdmin.Visibility = Visibility.Visible;
                BtnNavProfile.Visibility = Visibility.Collapsed;
                Nav_Click(BtnNavAdmin, null);

                BtnAddWorkout.Visibility = Visibility.Visible;
                BtnEditWorkout.Visibility = Visibility.Visible;
                BtnDeleteWorkout.Visibility = Visibility.Visible;
                BtnJoinWorkout.Visibility = Visibility.Collapsed;
            }
            else if (_currentUser is Trainer)
            {
                BtnNavAdmin.Visibility = Visibility.Collapsed;
                Nav_Click(BtnNavSchedule, null);

                BtnAddWorkout.Visibility = Visibility.Visible;
                BtnEditWorkout.Visibility = Visibility.Visible;
                BtnDeleteWorkout.Visibility = Visibility.Visible;
                BtnJoinWorkout.Visibility = Visibility.Collapsed;

                _ = LoadMyWorkoutsAsync();
            }
            else
            {
                BtnNavAdmin.Visibility = Visibility.Collapsed;
                Nav_Click(BtnNavSchedule, null);

                BtnJoinWorkout.Visibility = Visibility.Visible;
                BtnAddWorkout.Visibility = Visibility.Collapsed;
                BtnEditWorkout.Visibility = Visibility.Collapsed;
                BtnDeleteWorkout.Visibility = Visibility.Collapsed;

                _ = LoadMyWorkoutsAsync();
            }
        }


        private async Task LoadScheduleAsync()
        {
            await _workoutRepo.ReloadAsync();

            var allWorkouts = (await _workoutRepo.GetAllAsync())
                .OrderBy(w => w.Time)
                .ToList();

            ScheduleGrid.ItemsSource = allWorkouts;
        }


        private async void RefreshSchedule_Click(object sender, RoutedEventArgs e) =>
            await LoadScheduleAsync();

        private async void AddWorkout_Click(object sender, RoutedEventArgs e)
        {
            var w = ((App)Application.Current).CreateAddWorkoutWindow(_currentUser);
            w.Owner = this;

            if (w.ShowDialog() == true)
            {
                await LoadScheduleAsync();
                await LoadMyWorkoutsAsync(); 
            }
        }



        private void EditWorkout_Click(object sender, RoutedEventArgs e)
        {
            if (ScheduleGrid.SelectedItem is not Workout selectedWorkout)
            {
                new CustomMessageBox("Оберіть тренування зі списку.", "Увага").ShowDialog();
                return;
            }

            if (_currentUser is Trainer t && selectedWorkout.Coach?.Id != t.Id)
            {
                new CustomMessageBox("Ви можете редагувати тільки свої тренування!", "Помилка").ShowDialog();
                return;
            }

            var window = ActivatorUtilities.CreateInstance<AddWorkoutWindow>(
        _serviceProvider, _currentUser, selectedWorkout);

            if (window.ShowDialog() == true)
            {
                new CustomMessageBox("Тренування успішно оновлено!", "Успіх").ShowDialog();

                _ = LoadScheduleAsync();
            }
        }


        private async void DeleteWorkout_Click(object sender, RoutedEventArgs e)
        {
            if (ScheduleGrid.SelectedItem is not Workout workout) return;

            var dialog = new CustomMessageBox($"Видалити '{workout.Type}'?", "Підтвердження", true);
            if (dialog.ShowDialog() != true) return;

            await _workoutRepo.DeleteAsync(workout.Id);
            await _logger.LogAsync($"Видалено тренування ID {workout.Id}", LogType.Warn);
            await LoadScheduleAsync();
        }

        private async Task LoadMyWorkoutsAsync()
        {
            var workouts = await _workoutRepo.GetAllAsync();

            if (_currentUser is Trainer t)
            {
                ProfileGrid.ItemsSource = workouts
                    .Where(w => w.Coach != null && w.Coach.Id == t.Id)
                    .OrderBy(w => w.Time)
                    .ToList();
            }
            else
            {
                ProfileGrid.ItemsSource = workouts
                    .Where(w => w.Participants.Any(p => p.Id == _currentUser.Id))
                    .OrderBy(w => w.Time)
                    .ToList();
            }
        }



        private async Task LoadUsersAsync()
        {
            await _userRepo.ReloadAsync();
            UsersGrid.ItemsSource = await _userRepo.GetAllAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) =>
            await LoadUsersAsync();

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = _serviceProvider.GetRequiredService<AddUserWindow>();
            if (addWindow.ShowDialog() == true)
                _ = LoadUsersAsync();
        }

        private async void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not Person p) return;
            if (p.Id == _currentUser.Id)
            {
                new CustomMessageBox("Не можна видалити себе!", "Помилка").ShowDialog();
                return;
            }

            var dialog = new CustomMessageBox($"Видалити {p.FullName}?", "Увага", true);

            if (dialog.ShowDialog() == true)
            {
                await _userRepo.DeleteAsync(p.Id);
                await _logger.LogAsync($"Адмін видалив {p.Email}", LogType.Warn);
                await LoadUsersAsync();
            }
        }

        private async void JoinWorkout_Click(object sender, RoutedEventArgs e)
        {
            if (ScheduleGrid.SelectedItem is not Workout selectedWorkout) return;
            if (_currentUser is not Client currentClient) return;

            var freshWorkout = (await _workoutRepo.GetAllAsync())
                .FirstOrDefault(w => w.Id == selectedWorkout.Id);

            if (freshWorkout == null) return;

            if (freshWorkout.Participants.Any(p => p.Id == currentClient.Id))
            {
                new CustomMessageBox("Ви вже записані!", "Увага").ShowDialog();
                return;
            }

            if (freshWorkout.Participants.Count >= freshWorkout.MaxParticipants)
            {
                new CustomMessageBox("Місць більше немає.", "Увага").ShowDialog();
                return;
            }

            freshWorkout.Participants.Add(currentClient);
            await _workoutRepo.UpdateAsync(freshWorkout);
            await _logger.LogAsync($"Клієнт {currentClient.FullName} записався на {freshWorkout.Type}");

            await LoadScheduleAsync();

            await LoadMyWorkoutsAsync();

            new CustomMessageBox("Ви успішно записалися!", "Успіх").ShowDialog();
        }
        private async void LeaveWorkout_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not Workout workout) return;

            var freshWorkout = (await _workoutRepo.GetAllAsync())
                .FirstOrDefault(w => w.Id == workout.Id);

            if (freshWorkout == null) return;

            var p = freshWorkout.Participants.FirstOrDefault(x => x.Id == _currentUser.Id);

            if (p != null)
            {
                var dialog = new CustomMessageBox($"Ви впевнені, що хочете скасувати запис на '{freshWorkout.Type}'?", "Підтвердження", true);

                if (dialog.ShowDialog() != true) return;

                freshWorkout.Participants.Remove(p);
                await _workoutRepo.UpdateAsync(freshWorkout);
                await _logger.LogAsync($"Клієнт {_currentUser.FullName} скасував запис");

                await LoadScheduleAsync();     
                await LoadMyWorkoutsAsync();   

                new CustomMessageBox("Запис успішно скасовано.", "Інфо").ShowDialog();
            }
        }

        private void EditUser_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not Person person) return;

            var window = ActivatorUtilities.CreateInstance<AddUserWindow>(
                _serviceProvider, person);

            if (window.ShowDialog() == true)
                _ = LoadUsersAsync();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

    }
}
