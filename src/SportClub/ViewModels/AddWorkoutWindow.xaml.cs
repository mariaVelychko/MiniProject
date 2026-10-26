using SportClubLibrary.Models;
using SportClubLibrary.Utilities;
using System.Windows;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;


namespace SportClubWPF
{
    public partial class AddWorkoutWindow : Window
    {
        private readonly IAsyncRepository<Workout> _workoutRepo;
        private readonly IAsyncRepository<Person> _userRepo;
        private readonly ILogger _logger;

        private readonly Person _currentUser;
        private readonly Workout _workoutToEdit;

        public AddWorkoutWindow(
            Person user,
            IAsyncRepository<Workout> workoutRepo,
            IAsyncRepository<Person> userRepo,
            ILogger logger)
        {
            try
            {
                InitializeComponent();

                if (user == null) throw new Exception("Помилка: Користувач (user) == null");
                if (workoutRepo == null) throw new Exception("Помилка: workoutRepo == null");
                if (userRepo == null) throw new Exception("Помилка: userRepo == null");

                _currentUser = user;
                _workoutRepo = workoutRepo;
                _userRepo = userRepo;
                _logger = logger;

                if (DatePick == null) throw new Exception("XAML елемент 'DatePick' не ініціалізовано!");

                DatePick.SelectedDate = DateTime.Today;

                _ = SetupCoachSelectionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"КРИТИЧНА ПОМИЛКА ПРИ ВІДКРИТТІ ВІКНА:\n{ex.ToString()}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }

        public AddWorkoutWindow(
            Person user,
            Workout workoutToEdit,
            IAsyncRepository<Workout> workoutRepo,
            IAsyncRepository<Person> userRepo,
            ILogger logger)
        {
            try
            {
                InitializeComponent();

                _currentUser = user;
                _workoutToEdit = workoutToEdit;
                _workoutRepo = workoutRepo;
                _userRepo = userRepo;
                _logger = logger;

                Title = "Редагування тренування";

                _ = SetupCoachSelectionAsync();
                FillFields();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"КРИТИЧНА ПОМИЛКА (РЕДАГУВАННЯ):\n{ex.ToString()}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }

        private async Task SetupCoachSelectionAsync()
        {
            try
            {
                if (_currentUser is Admin)
                {
                    if (CoachLabel == null || CoachCombo == null) return;

                    CoachLabel.Visibility = Visibility.Visible;
                    CoachCombo.Visibility = Visibility.Visible;

                    var users = await _userRepo.GetAllAsync();
                    
                    if (users == null) throw new Exception("Репозиторій повернув NULL замість списку користувачів");

                    var trainers = users.OfType<Trainer>().ToList();

                    CoachCombo.ItemsSource = trainers;

                    if (trainers.Any() && _workoutToEdit == null)
                        CoachCombo.SelectedIndex = 0;
                }
                else
                {
                    if (CoachLabel != null) CoachLabel.Visibility = Visibility.Collapsed;
                    if (CoachCombo != null) CoachCombo.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                if (_logger != null) await _logger.LogAsync($"Error loading coaches: {ex.Message}", LogType.Error);
                MessageBox.Show($"Не вдалося завантажити список тренерів:\n{ex.Message}");
            }
        }

        private void FillFields()
        {
            try
            {
                if (_workoutToEdit == null) return;

                TypeCombo.Text = _workoutToEdit.Type.Replace(" (Personal)", "");
                DatePick.SelectedDate = _workoutToEdit.Time.Date;
                TimeBox.Text = _workoutToEdit.Time.ToString("HH:mm");
                DurationBox.Text = _workoutToEdit.DurationMin.ToString();
                HallBox.Text = _workoutToEdit.Hall;
                MaxBox.Text = _workoutToEdit.MaxParticipants.ToString();

                if (_workoutToEdit.MaxParticipants == 1)
                    IsPersonalCheck.IsChecked = true;

                if (_currentUser is Admin && _workoutToEdit.Coach != null)
                {
                    var trainers = CoachCombo.ItemsSource as IEnumerable<Trainer>;
                    if (trainers != null)
                    {
                        var currentCoach = trainers.FirstOrDefault(t => t.Id == _workoutToEdit.Coach.Id);
                        CoachCombo.SelectedItem = currentCoach;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка при заповненні полів: {ex.Message}");
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && this.WindowState == WindowState.Normal)
                this.DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e) => this.Close();

        private void IsPersonalCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (MaxBox == null) return; 

            if (IsPersonalCheck.IsChecked == true)
            {
                MaxBox.Text = "1";
                MaxBox.IsEnabled = false;
            }
            else
            {
                MaxBox.Text = "20";
                MaxBox.IsEnabled = true;
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateInput(out TimeSpan timeSpan)) return;

                Trainer selectedCoach = GetSelectedCoach();
                if (selectedCoach == null && _currentUser is Admin) return;

                string finalType = TypeCombo.Text + (IsPersonalCheck.IsChecked == true ? " (Personal)" : "");
                int maxParticipants = int.TryParse(MaxBox.Text, out int m) ? m : 20;
                int durationMin = int.TryParse(DurationBox.Text, out int d) ? d : 60;
                DateTime workoutTime = DatePick.SelectedDate.Value.Add(timeSpan);
                string hallValue = HallBox.Text;

                if (_workoutToEdit == null)
                {
                    await CreateWorkoutAsync(finalType, workoutTime, durationMin, hallValue, maxParticipants, selectedCoach);
                }
                else
                {
                    await UpdateWorkoutAsync(finalType, workoutTime, durationMin, hallValue, maxParticipants, selectedCoach);
                }

                DialogResult = true;
            }
            catch (Exception ex)
            {
                new CustomMessageBox($"Помилка при збереженні: {ex.Message}", "Помилка").ShowDialog();
            }
        }

        private bool ValidateInput(out TimeSpan timeSpan)
        {
            timeSpan = TimeSpan.Zero;

            if (string.IsNullOrWhiteSpace(TypeCombo.Text) || DatePick.SelectedDate == null || string.IsNullOrWhiteSpace(TimeBox.Text))
            {
                new CustomMessageBox("Заповніть основні поля!", "Помилка").ShowDialog();
                return false;
            }

            if (!TimeSpan.TryParse(TimeBox.Text, out timeSpan))
            {
                new CustomMessageBox("Невірний формат часу (наприклад: 14:30)", "Помилка").ShowDialog();
                return false;
            }

            return true;
        }

        private Trainer GetSelectedCoach()
        {
            if (_currentUser is Admin)
            {
                var coach = CoachCombo.SelectedItem as Trainer;
                if (coach == null)
                {
                    new CustomMessageBox("Оберіть тренера!", "Помилка").ShowDialog();
                }
                return coach;
            }

            if (_currentUser is Trainer trainer)
            {
                return trainer;
            }

            return null;
        }

        private async Task CreateWorkoutAsync(string type, DateTime time, int duration, string hall, int maxParticipants, Trainer coach)
        {
            var workout = new Workout
            {
                Type = type,
                Time = time,
                DurationMin = duration,
                Hall = hall,
                MaxParticipants = maxParticipants,
                Coach = coach
            };

            await _workoutRepo.AddAsync(workout);
            await _logger.LogAsync($"Створено тренування: {workout.Type}", LogType.Info);
            new CustomMessageBox($"Тренування '{type}' створено!", "Успіх").ShowDialog();
        }

        private async Task UpdateWorkoutAsync(string type, DateTime time, int duration, string hall, int maxParticipants, Trainer coach)
        {
            _workoutToEdit.Type = type;
            _workoutToEdit.Time = time;
            _workoutToEdit.DurationMin = duration;
            _workoutToEdit.Hall = hall;
            _workoutToEdit.MaxParticipants = maxParticipants;
            _workoutToEdit.Coach = coach;

            await _workoutRepo.UpdateAsync(_workoutToEdit);
            await _logger.LogAsync($"Оновлено тренування ID {_workoutToEdit.Id}", LogType.Info);
        }

    }
}