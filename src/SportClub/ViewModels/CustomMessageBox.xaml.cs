using System.Windows;
using System.Windows.Input;

namespace SportClubWPF
{
    public partial class CustomMessageBox : Window
    {
        public CustomMessageBox(string message, string title = "Повідомлення", bool isConfirmation = false)
        {
            InitializeComponent();
            MessageText.Text = message;
            TitleText.Text = title;

            if (isConfirmation)
            {
                BtnOk.Visibility = Visibility.Collapsed;
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
            }
            else
            {
                BtnOk.Visibility = Visibility.Visible;
                BtnYes.Visibility = Visibility.Collapsed;
                BtnNo.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; 
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; 
            this.Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
    }
}