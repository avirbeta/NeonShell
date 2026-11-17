using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace NeonShell.Dialogs
{
    public partial class AddGameDialog : Window
    {
        public string GamePath => PathBox.Text.Trim();
        public string GameName => string.IsNullOrWhiteSpace(NameBox.Text)
            ? Path.GetFileNameWithoutExtension(GamePath)
            : NameBox.Text.Trim();
        public string Arguments => ArgsBox.Text;

        public AddGameDialog()
        {
            InitializeComponent();
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите файл игры",
                Filter = "Исполняемые файлы|*.exe|Все файлы|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                PathBox.Text = dlg.FileName;
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                    NameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GamePath) || !File.Exists(GamePath))
            {
                MessageBox.Show("Укажите существующий файл игры.",
                    "Neon Shell", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}
