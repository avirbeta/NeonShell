using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NeonShell.Core;
using NeonShell.Services;

namespace NeonShell.ControlCenter
{
    /// <summary>Неоновый центр управления: звук, батарея, сеть, питание.</summary>
    public partial class ControlCenterWindow : Window
    {
        private readonly SystemServices.VolumeService _volume = new();
        private readonly SystemServices.NetworkService _network = new();
        private readonly DispatcherTimer _timer;
        private bool _syncingSliders;
        private bool _closing;

        public ControlCenterWindow()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += (_, _) => RefreshState();
        }

        public void OpenMenu()
        {
            _closing = false;
            Left = -10000;
            Top = -10000;
            Show();
            Position();
            RefreshState();
            Activate();
            _timer.Start();
            if (App.AnimationsEnabled)
            {
                Opacity = 0;
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            }
        }

        public void CloseMenu()
        {
            _timer.Stop();
            if (_closing || !IsVisible) { Hide(); return; }
            _closing = true;
            if (!App.AnimationsEnabled)
            {
                Hide();
                _closing = false;
                return;
            }
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(90));
            fade.Completed += (_, _) => { Hide(); _closing = false; Opacity = 1; };
            BeginAnimation(OpacityProperty, fade);
        }

        public void UpdatePosition() => Position();

        private void Position()
        {
            try
            {
                var settings = App.Settings.Data;
                bool top = settings.TaskbarPosition == "Top";
                double screenH = SystemParameters.PrimaryScreenHeight;
                double screenW = SystemParameters.PrimaryScreenWidth;
                var wa = SystemParameters.WorkArea;
                double right = settings.HideSystemTaskbar ? screenW : wa.Right;
                Left = right - Width - 10;
                if (top) Top = (settings.HideSystemTaskbar ? 0 : wa.Top) + settings.TaskbarHeight + 10;
                else Top = (settings.HideSystemTaskbar ? screenH : wa.Bottom) - settings.TaskbarHeight - ActualHeight - 10;
            }
            catch (Exception ex) { App.Log?.Error("CC Position", ex); }
        }

        private void RefreshState()
        {
            try
            {
                _syncingSliders = true;
                double v = _volume.GetVolume();
                bool mute = _volume.GetMute();
                VolumeSlider.Value = mute ? 0 : v * 100;
                VolumeGlyph.Text = mute || v <= 0.001 ? Glyphs.VolumeMute
                    : v < 0.34 ? Glyphs.VolumeLow : Glyphs.Volume;
                VolumeValue.Text = mute ? "Выкл" : $"{(int)(v * 100)}%";
                _syncingSliders = false;

                if (SystemServices.BatteryService.IsPresent)
                {
                    BatteryCard.Visibility = Visibility.Visible;
                    int p = SystemServices.BatteryService.Percent;
                    bool charging = SystemServices.BatteryService.IsCharging;
                    BatteryPercent.Text = $"{p}%";
                    BatteryGlyph.Text = charging ? "\uE85A" : ((char)(0xE850 + Math.Clamp((int)Math.Round(p / 10.0), 0, 9))).ToString();
                    BatteryStatus.Text = charging ? "Заряжается" : "От батареи";
                }
                else BatteryCard.Visibility = Visibility.Collapsed;

                _network.Refresh();
                NetworkName.Text = _network.InterfaceName;
                NetworkIp.Text = _network.IpAddress;
                NetworkDown.Text = $"↓ {_network.DownMbps:0.##} Мбит/с";
                NetworkUp.Text = $"↑ {_network.UpMbps:0.##} Мбит/с";
            }
            catch (Exception ex) { App.Log?.Error("CC RefreshState", ex); }
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncingSliders) return;
            _volume.SetMute(false);
            _volume.SetVolume(e.NewValue / 100.0);
            VolumeValue.Text = $"{(int)e.NewValue}%";
            VolumeGlyph.Text = e.NewValue <= 0.001 ? Glyphs.VolumeMute
                : e.NewValue < 34 ? Glyphs.VolumeLow : Glyphs.Volume;
        }

        private void OnQuickAction(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is string uri)
                AppLauncher.OpenSystemSettings(uri);
        }

        private void OnOpenWifiSettings(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            App.WindowManager?.OpenSettings("wifi");
        }

        private void OnOpenSettings(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            App.WindowManager?.OpenSettings();
        }

        private void OnOpenWindowsSettings(object sender, RoutedEventArgs e)
            => AppLauncher.OpenSystemSettings("ms-settings:");

        private void OnShutdown(object sender, RoutedEventArgs e)
        {
            if (Confirm("Завершить работу компьютера?")) SystemServices.PowerService.Shutdown();
        }

        private void OnRestart(object sender, RoutedEventArgs e)
        {
            if (Confirm("Перезагрузить компьютер?")) SystemServices.PowerService.Restart();
        }

        private void OnSleep(object sender, RoutedEventArgs e) => SystemServices.PowerService.Sleep();

        private void OnExit(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            App.WindowManager?.ExitShell();
        }

        private static bool Confirm(string text)
            => MessageBox.Show(text, "Neon Shell",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        private void OnDeactivated(object sender, EventArgs e) => CloseMenu();

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { CloseMenu(); e.Handled = true; }
        }
    }
}
