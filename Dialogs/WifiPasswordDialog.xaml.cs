using System.Windows;

namespace NeonShell.Dialogs
{
    public partial class WifiPasswordDialog : Window
    {
        public string Password => PasswordInput.Password;

        public WifiPasswordDialog(string ssid)
        {
            InitializeComponent();
            NetworkNameText.Text = ssid;
            PasswordInput.Focus();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (Password.Length < 8)
            {
                ErrorText.Text = "Пароль слишком короткий (минимум 8 символов).";
                return;
            }
            DialogResult = true;
        }
    }
}
