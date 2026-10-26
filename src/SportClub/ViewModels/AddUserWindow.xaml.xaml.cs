using SportClubLibrary.Models;
using SportClubLibrary.Utilities;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextBox = System.Windows.Controls.TextBox;

namespace SportClubWPF
{
    public partial class AddUserWindow : Window
    {
        private readonly IAsyncRepository<Person> _repository;
        private readonly ILogger _logger;

        private Person _personToEdit;

        public AddUserWindow(
            IAsyncRepository<Person> repository,
            ILogger logger)
        {
            InitializeComponent();
            _repository = repository;
            _logger = logger;
        }

        public AddUserWindow(
            Person personToEdit,
            IAsyncRepository<Person> repository,
            ILogger logger) : this(repository, logger)
        {
            _personToEdit = personToEdit;
            Title = "Редагування користувача";

            NameBox.Text = personToEdit.FullName;
            EmailBox.Text = personToEdit.Email;
            PhoneBox.Text = personToEdit.Phone;

            if (personToEdit is Client) RoleCombo.SelectedIndex = 0;
            else if (personToEdit is Trainer t)
            {
                RoleCombo.SelectedIndex = 1;
                SpecCombo.Text = t.Specialization;
            }
            else if (personToEdit is Admin) RoleCombo.SelectedIndex = 2;

            RoleCombo.IsEnabled = false;
        }


        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) =>
            this.WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e) => this.Close();


        private void PhoneBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void PhoneBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            textBox.TextChanged -= PhoneBox_TextChanged;

            try
            {
                string digits = new string(textBox.Text.Where(char.IsDigit).ToArray());

                if (digits.Length >= 1 && !digits.StartsWith("38"))
                {
                    digits = "38" + digits.TrimStart('0');
                }

                if (digits.Length > 12) digits = digits.Substring(0, 12);

                string formatted = "";
                if (digits.Length > 0) formatted = "+";
                if (digits.Length > 0) formatted += digits.Substring(0, Math.Min(2, digits.Length));
                if (digits.Length > 2) formatted += " (" + digits.Substring(2, Math.Min(3, digits.Length - 2));
                if (digits.Length > 5) formatted += ") " + digits.Substring(5, Math.Min(3, digits.Length - 5));
                if (digits.Length > 8) formatted += "-" + digits.Substring(8, Math.Min(2, digits.Length - 8));
                if (digits.Length > 10) formatted += "-" + digits.Substring(10, Math.Min(2, digits.Length - 10));

                textBox.Text = formatted;
                textBox.CaretIndex = formatted.Length;
            }
            finally
            {
                textBox.TextChanged += PhoneBox_TextChanged;
            }
        }


        private void RoleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpecCombo == null || SpecLabel == null) return;

            var item = RoleCombo.SelectedItem as ComboBoxItem;

            if (item != null && item.Content.ToString() == "Тренер")
            {
                SpecLabel.Visibility = Visibility.Visible;
                SpecCombo.Visibility = Visibility.Visible;
            }
            else
            {
                SpecLabel.Visibility = Visibility.Collapsed;
                SpecCombo.Visibility = Visibility.Collapsed;
            }
        }


        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput()) return;

            if (_personToEdit == null)
            {
                await CreateNewUserAsync();
            }
            else
            {
                await UpdateExistingUserAsync();
            }

            DialogResult = true;
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text) ||
                string.IsNullOrWhiteSpace(EmailBox.Text) ||
                string.IsNullOrWhiteSpace(PhoneBox.Text))
            {
                new CustomMessageBox("Заповніть основні поля!", "Увага").ShowDialog();
                return false;
            }

            if (_personToEdit == null && string.IsNullOrWhiteSpace(PasswordBox.Password))
            {
                new CustomMessageBox("Введіть пароль!", "Помилка").ShowDialog();
                return false;
            }

            if (!string.IsNullOrEmpty(PasswordBox.Password))
            {
                string pass = PasswordBox.Password;

                if (pass.Length < 6 || !pass.Any(char.IsLetter) || !pass.Any(char.IsDigit))
                {
                    new CustomMessageBox("Пароль: мін. 6 символів, літери + цифри.", "Помилка").ShowDialog();
                    return false;
                }
            }

            if (PhoneBox.Text.Length < 19)
            {
                new CustomMessageBox("Введіть повний номер телефону!", "Помилка").ShowDialog();
                return false;
            }

            return true;
        }

        private async Task CreateNewUserAsync()
        {
            Person newPerson = null;
            string role = (RoleCombo.SelectedItem as ComboBoxItem)?.Content.ToString();

            switch (role)
            {
                case "Клієнт":
                    newPerson = new Client();
                    break;
                case "Тренер":
                    newPerson = new Trainer { Specialization = SpecCombo.Text };
                    break;
                case "Адміністратор":
                    newPerson = new Admin();
                    break;
            }

            if (newPerson != null)
            {
                newPerson.FullName = NameBox.Text;
                newPerson.Email = EmailBox.Text;
                newPerson.Phone = PhoneBox.Text;
                newPerson.Password = PasswordHasher.Hash(PasswordBox.Password);

                await _repository.AddAsync(newPerson);
                await _logger.LogAsync($"Створено користувача: {newPerson.Email} ({role})", LogType.Info);

                new CustomMessageBox("Користувача успішно створено!", "Успіх").ShowDialog();
            }
        }

        private async Task UpdateExistingUserAsync()
        {
            _personToEdit.FullName = NameBox.Text;
            _personToEdit.Email = EmailBox.Text;
            _personToEdit.Phone = PhoneBox.Text;

            if (!string.IsNullOrWhiteSpace(PasswordBox.Password))
            {
                _personToEdit.Password = PasswordHasher.Hash(PasswordBox.Password);
            }

            if (_personToEdit is Trainer t)
            {
                t.Specialization = SpecCombo.Text;
            }

            await _repository.UpdateAsync(_personToEdit);
            await _logger.LogAsync($"Оновлено користувача: {_personToEdit.Email}", LogType.Info);

            new CustomMessageBox("Дані успішно оновлено!", "Успіх").ShowDialog();
        }
    }
}
