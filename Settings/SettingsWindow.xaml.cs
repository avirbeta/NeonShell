using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NeonShell.Core;
using NeonShell.Services;

namespace NeonShell.Settings
{
    /// <summary>Расширенные параметры Neon Shell.</summary>
    public partial class SettingsWindow : Window
    {
        private static readonly (string name, string c1, string c2)[] Schemes =
        {
            ("Циан", "#00E5FF", "#FF2EA6"),
            ("Маджента", "#FF2EA6", "#B537F2"),
            ("Кислота", "#39FF88", "#00E5FF"),
            ("Янтарь", "#FFC233", "#FF2E5B"),
            ("Синева", "#2E6DFF", "#00C2D4"),
            ("Фиолет", "#B537F2", "#2E6DFF"),
        };

        private static readonly (string id, string label)[] BuiltinIcons =
        {
            ("games", "Игры"),
            ("recycle", "Корзина"),
            ("settings", "Параметры Neon"),
            ("exit", "Выйти в Windows"),
        };

        private bool _loading = true;
        private readonly DispatcherTimer _debounce;
        private readonly DispatcherTimer _stateTimer;
        private readonly Dictionary<string, UIElement> _pages = new();
        private readonly SystemServices.VolumeService _volume;
        private readonly WifiService _wifi;

        public SettingsWindow()
        {
            InitializeComponent();
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                App.Settings.Data.DesktopIconSize = (int)IconSizeSlider.Value;
                App.Settings.Data.TaskbarHeight = (int)TaskbarHeightSlider.Value;
                App.Settings.Data.TaskbarOpacity = OpacitySlider.Value / 100.0;
                App.Settings.MarkDirty();
                App.WindowManager?.ApplySettings();
            };

            NavList.ItemsSource = new[]
            {
                new { Id = "appearance", Glyph = "\uE790", Title = "Внешний вид" },
                new { Id = "wallpaper", Glyph = "\uEB9F", Title = "Обои" },
                new { Id = "desktop", Glyph = "\uE7FC", Title = "Рабочий стол" },
                new { Id = "taskbar", Glyph = "\uE7F4", Title = "Панель задач" },
                new { Id = "sound", Glyph = "\uE767", Title = "Звук" },
                new { Id = "battery", Glyph = "\uE850", Title = "Батарея" },
                new { Id = "wifi", Glyph = "\uE701", Title = "Wi-Fi" },
                new { Id = "notifications", Glyph = "\uE7ED", Title = "Уведомления" },
                new { Id = "games", Glyph = "\uE7FC", Title = "Игры" },
                new { Id = "system", Glyph = "\uE713", Title = "Система" },
            };

            _pages["appearance"] = PageAppearance;
            _pages["wallpaper"] = PageWallpaper;
            _pages["desktop"] = PageDesktop;
            _pages["taskbar"] = PageTaskbar;
            _pages["sound"] = PageSound;
            _pages["battery"] = PageBattery;
            _pages["wifi"] = PageWifi;
            _pages["notifications"] = PageNotifications;
            _pages["games"] = PageGames;
            _pages["system"] = PageSystem;

            _volume = new SystemServices.VolumeService();
            _wifi = new WifiService();
            _stateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _stateTimer.Tick += (_, _) =>
            {
                if (!IsVisible) return;
                UpdateBatteryPage();
                UpdateWifiStatusOnly();
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildSchemes();
            BuildWallpapers();
            RefreshAll();
            InitSoundPage();
            InitBatteryPage();
            RefreshWifiPage();
            _loading = false;
            NavList.SelectedIndex = 0;
            _stateTimer.Start();
        }

        /// <summary>Открыть конкретную вкладку (вызывается из других модулей).</summary>
        public void ShowPage(string id)
        {
            foreach (var item in NavList.Items)
            {
                string itemId = item.GetType().GetProperty("Id")?.GetValue(item) as string;
                if (itemId == id)
                {
                    NavList.SelectedItem = item;
                    return;
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _stateTimer.Stop();
            try { _wifi?.Dispose(); } catch { }
            base.OnClosed(e);
        }

        private void OnNavChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem == null) return;
            // получаем Id выбранного элемента через отражение анонимного типа
            string id = NavList.SelectedItem.GetType().GetProperty("Id")?.GetValue(NavList.SelectedItem) as string;
            foreach (var kv in _pages) kv.Value.Visibility = Visibility.Collapsed;
            if (id != null && _pages.TryGetValue(id, out var page))
                page.Visibility = Visibility.Visible;
        }

        private void RefreshAll()
        {
            _loading = true;
            var s = App.Settings.Data;
            AnimationsChk.IsChecked = s.AnimationsEnabled;
            IconSizeSlider.Value = s.DesktopIconSize;
            TaskbarHeightSlider.Value = s.TaskbarHeight;
            OpacitySlider.Value = s.TaskbarOpacity * 100;
            foreach (ComboBoxItem item in PositionBox.Items)
                if ((string)item.Tag == s.TaskbarPosition) { PositionBox.SelectedItem = item; break; }
            AutoHideChk.IsChecked = s.TaskbarAutoHide;
            SysTrayChk.IsChecked = s.HideSystemTaskbar;
            NotifEnabledChk.IsChecked = s.NotificationsEnabled;
            NotifDurationSlider.Value = s.NotificationDurationSec;
            AutostartChk.IsChecked = AutostartService.IsEnabled();
            IconGamesChk.IsChecked = !s.HiddenBuiltinIcons.Contains("games");
            IconRecycleChk.IsChecked = !s.HiddenBuiltinIcons.Contains("recycle");
            IconSettingsChk.IsChecked = !s.HiddenBuiltinIcons.Contains("settings");
            IconExitChk.IsChecked = !s.HiddenBuiltinIcons.Contains("exit");
            _loading = false;
        }

        // ---------- внешний вид ----------

        private void BuildSchemes()
        {
            SchemePanel.Children.Clear();
            foreach (var (name, c1, c2) in Schemes)
            {
                var color1 = ThemeManager.ParseColor(c1, Colors.White);
                var color2 = ThemeManager.ParseColor(c2, Colors.White);
                bool current = App.Settings.Data.AccentHex == c1;

                var card = new Border
                {
                    Width = 116,
                    Height = 58,
                    Margin = new Thickness(0, 0, 10, 10),
                    CornerRadius = new CornerRadius(10),
                    Background = new LinearGradientBrush(color1, color2, new Point(0, 0), new Point(1, 1)),
                    BorderThickness = new Thickness(current ? 3 : 1),
                    BorderBrush = current
                        ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                        : (Brush)Application.Current.Resources["BorderBrushSoft"],
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = (name, c1, c2),
                };
                card.MouseLeftButtonUp += (_, _) =>
                {
                    App.Settings.Data.AccentHex = c1;
                    App.Settings.Data.AccentSecondaryHex = c2;
                    App.Settings.MarkDirty();
                    App.WindowManager?.ApplySettings();
                    BuildSchemes();
                };
                card.Child = new TextBlock
                {
                    Text = name,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 6),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8, Color = Colors.Black,
                    },
                };
                SchemePanel.Children.Add(card);
            }
        }

        private void OnVisualChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.Data.AnimationsEnabled = AnimationsChk.IsChecked == true;
            App.Settings.MarkDirty();
            App.WindowManager?.ApplySettings();
        }

        // ---------- обои ----------

        private void BuildWallpapers()
        {
            WallpaperPanel.Children.Clear();
            foreach (var preset in WallpaperService.Presets)
            {
                bool current = App.Settings.Data.Wallpaper == "neon://" + preset;
                var card = new Border
                {
                    Width = 140,
                    Height = 84,
                    Margin = new Thickness(0, 0, 10, 10),
                    CornerRadius = new CornerRadius(10),
                    Background = WallpaperService.GetPresetBrush(preset) ?? (Brush)Application.Current.Resources["SurfaceBrush"],
                    BorderThickness = new Thickness(current ? 3 : 1),
                    BorderBrush = current
                        ? (Brush)Application.Current.Resources["AccentBrush"]
                        : (Brush)Application.Current.Resources["BorderBrushSoft"],
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = preset,
                };
                card.MouseLeftButtonUp += (_, _) =>
                {
                    App.Settings.Data.Wallpaper = "neon://" + preset;
                    App.Settings.MarkDirty();
                    App.WindowManager?.ApplySettings();
                    BuildWallpapers();
                };
                card.Child = new TextBlock
                {
                    Text = WallpaperService.PresetName(preset),
                    Foreground = Brushes.White,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 8),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8, Color = Colors.Black,
                    },
                };
                WallpaperPanel.Children.Add(card);
            }
        }

        private void OnBrowseWallpaper(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите изображение",
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Все файлы|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                App.Settings.Data.Wallpaper = WallpaperService.StoreUserImage(dlg.FileName);
                App.Settings.MarkDirty();
                App.WindowManager?.ApplySettings();
                BuildWallpapers();
            }
        }

        // ---------- рабочий стол ----------

        private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            _debounce.Stop();
            _debounce.Start();
        }

        private void OnBuiltinChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var hidden = App.Settings.Data.HiddenBuiltinIcons;
            hidden.Clear();
            if (IconGamesChk.IsChecked != true) hidden.Add("games");
            if (IconRecycleChk.IsChecked != true) hidden.Add("recycle");
            if (IconSettingsChk.IsChecked != true) hidden.Add("settings");
            if (IconExitChk.IsChecked != true) hidden.Add("exit");
            App.Settings.MarkDirty();
            App.WindowManager?.ApplySettings();
        }

        private void OnResetPositions(object sender, RoutedEventArgs e)
        {
            App.Settings.Data.DesktopIconPositions.Clear();
            App.Settings.MarkDirty();
            App.WindowManager?.ApplySettings();
        }

        private void OnOpenDesktopFolder(object sender, RoutedEventArgs e)
        {
            string dir = App.WindowManager?.DesktopDir
                ?? System.IO.Path.Combine(App.DataDir, "Desktop");
            System.IO.Directory.CreateDirectory(dir);
            AppLauncher.OpenPath(dir);
        }

        // ---------- панель задач ----------

        private void OnPositionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (PositionBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                App.Settings.Data.TaskbarPosition = tag;
                App.Settings.MarkDirty();
                App.WindowManager?.ApplySettings();
            }
        }

        private void OnTaskbarChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.Data.TaskbarAutoHide = AutoHideChk.IsChecked == true;
            App.Settings.MarkDirty();
            App.WindowManager?.ApplySettings();
        }

        private void OnSysTrayChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool hide = SysTrayChk.IsChecked == true;
            if (!hide)
            {
                var res = MessageBox.Show(
                    "Показать системную панель Windows? Neon-панель останется поверх неё.",
                    "Neon Shell", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes)
                {
                    _loading = true;
                    SysTrayChk.IsChecked = true;
                    _loading = false;
                    return;
                }
            }
            App.Settings.Data.HideSystemTaskbar = hide;
            App.Settings.MarkDirty();
            ShellIntegration.HideSystemTaskbar(hide);
            App.WindowManager?.ApplySettings();
        }

        // ---------- уведомления ----------

        private void OnNotifChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.Data.NotificationsEnabled = NotifEnabledChk.IsChecked == true;
            App.Settings.Data.NotificationDurationSec = (int)NotifDurationSlider.Value;
            App.Settings.MarkDirty();
        }

        private void OnClearNotifications(object sender, RoutedEventArgs e)
        {
            App.Notifications?.ClearHistory();
            App.Notifications?.CloseAll();
        }

        // ---------- игры ----------

        private void OnRescanGames(object sender, RoutedEventArgs e)
        {
            App.WindowManager?.RescanGames();
        }

        private void OnRestoreGames(object sender, RoutedEventArgs e)
        {
            App.Settings.Data.HiddenGameIds.Clear();
            App.Settings.MarkDirty();
            App.WindowManager?.RescanGames();
        }

        // ---------- система ----------

        private void OnAutostartChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool enable = AutostartChk.IsChecked == true;
            string exe = Environment.ProcessPath ?? "";
            bool ok = AutostartService.SetEnabled(enable, exe, App.Log);
            App.Settings.Data.Autostart = enable;
            App.Settings.MarkDirty();
            if (!ok)
            {
                _loading = true;
                AutostartChk.IsChecked = AutostartService.IsEnabled();
                _loading = false;
            }
        }

        private void OnSafeMode(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "Neon Shell будет закрыт и запущен заново в безопасном режиме.\n\nПродолжить?",
                "Neon Shell", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? "", "--safemode")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                App.Log?.Error("Безопасный режим", ex);
            }
            App.WindowManager?.ExitShell();
        }

        private void OnExitWindows(object sender, RoutedEventArgs e)
            => App.WindowManager?.ExitShell();

        private void OnOpenLogs(object sender, RoutedEventArgs e)
        {
            if (System.IO.Directory.Exists(App.LogDir))
                AppLauncher.OpenPath(App.LogDir);
        }

        private void OnResetAll(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Сбросить все настройки Neon Shell?",
                "Neon Shell", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
            App.Settings.ResetToDefaults();
            App.WindowManager?.ApplySettings();
            RefreshAll();
            BuildSchemes();
            BuildWallpapers();
        }

        // ---------- звук ----------

        private void InitSoundPage()
        {
            double v = _volume.GetVolume();
            bool mute = _volume.GetMute();
            SoundVolumeSlider.Value = mute ? 0 : v * 100;
            SoundMuteChk.IsChecked = mute;
            UpdateSoundText();
        }

        private void OnSoundVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            _volume.SetMute(false);
            SoundMuteChk.IsChecked = false;
            _volume.SetVolume(e.NewValue / 100.0);
            UpdateSoundText();
        }

        private void OnSoundMuteChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _volume.SetMute(SoundMuteChk.IsChecked == true);
            UpdateSoundText();
        }

        private void UpdateSoundText()
        {
            bool mute = _volume.GetMute();
            double v = _volume.GetVolume();
            SoundPercentText.Text = mute ? "Выключено" : $"{(int)(v * 100)}%";
        }

        private void OnOpenMixer(object sender, RoutedEventArgs e)
            => AppLauncher.OpenSystemSettings("ms-settings:sound");

        // ---------- батарея ----------

        private void InitBatteryPage()
        {
            if (!SystemServices.BatteryService.IsPresent)
            {
                BatteryPercentText.Text = "—";
                BatteryStatusText.Text = "Батарея не обнаружена (стационарный ПК)";
                BatteryHintText.Text = "Режимы энергопотребления доступны и на ПК — они управляют питанием процессора и дисков.";
            }
            LoadPowerPlans();
            UpdateBatteryPage();
        }

        private void UpdateBatteryPage()
        {
            if (SystemServices.BatteryService.IsPresent)
            {
                int p = SystemServices.BatteryService.Percent;
                bool charging = SystemServices.BatteryService.IsCharging;
                BatteryPercentText.Text = p + "%";
                BatteryGlyphText.Text = charging ? "\uE85A" :
                    ((char)(0xE850 + Math.Clamp((int)Math.Round(p / 10.0), 0, 9))).ToString();
                BatteryStatusText.Text = charging ? "Заряжается от сети" : "Работает от батареи";
            }
        }

        private void LoadPowerPlans()
        {
            var plans = PowerPlanService.GetPlans();
            PlanBox.ItemsSource = plans;
            foreach (var plan in plans)
            {
                if (plan.IsActive)
                {
                    PlanBox.SelectedItem = plan;
                    break;
                }
            }
        }

        private void OnApplyPlan(object sender, RoutedEventArgs e)
        {
            if (PlanBox.SelectedItem is PowerPlan plan)
            {
                if (PowerPlanService.SetActive(plan.Guid))
                {
                    App.Notifications.Show("Питание",
                        "Режим включён: " + plan.Name, NotificationKind.Success);
                    LoadPowerPlans();
                }
                else
                {
                    App.Notifications.Show("Питание", "Не удалось применить режим.", NotificationKind.Warning);
                }
            }
        }

        // ---------- wi-fi ----------

        private void RefreshWifiPage()
        {
            try
            {
                if (!_wifi.Available)
                {
                    WifiStatusText.Text = "Wi-Fi недоступен на этом устройстве";
                    NetworksList.ItemsSource = null;
                    SavedList.ItemsSource = null;
                    return;
                }
                var current = _wifi.GetCurrentSsid();
                WifiStatusText.Text = string.IsNullOrEmpty(current)
                    ? "Не подключено"
                    : "Подключено: " + current;

                NetworksList.ItemsSource = _wifi.GetNetworks();
                SavedList.ItemsSource = _wifi.GetSavedProfiles();
            }
            catch (Exception ex)
            {
                App.Log?.Error("Wi-Fi обновление", ex);
            }
        }

        private void UpdateWifiStatusOnly()
        {
            try
            {
                if (!_wifi.Available) return;
                var current = _wifi.GetCurrentSsid();
                WifiStatusText.Text = string.IsNullOrEmpty(current)
                    ? "Не подключено"
                    : "Подключено: " + current;
            }
            catch { }
        }

        private void OnWifiRefresh(object sender, RoutedEventArgs e) => RefreshWifiPage();

        private void OnNetworkSelected(object sender, SelectionChangedEventArgs e)
        {
            if (NetworksList.SelectedItem is not WifiNetwork net) return;
            NetworksList.SelectedIndex = -1;

            if (net.AuthName == "WEP")
            {
                App.Notifications.Show("Wi-Fi",
                    "Сеть использует устаревшее шифрование WEP. Подключитесь через параметры Windows.",
                    NotificationKind.Warning);
                return;
            }

            if (!net.Secure)
            {
                if (_wifi.ConnectOpen(net.Ssid))
                    App.Notifications.Show("Wi-Fi", "Подключение к «" + net.Ssid + "»...", NotificationKind.Info);
                else
                    App.Notifications.Show("Wi-Fi", "Не удалось подключиться к «" + net.Ssid + "».", NotificationKind.Warning);
                return;
            }

            var dlg = new Dialogs.WifiPasswordDialog(net.Ssid) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                if (_wifi.ConnectSecured(net.Ssid, dlg.Password, net.AuthName))
                    App.Notifications.Show("Wi-Fi",
                        "Подключение к «" + net.Ssid + "»... Если пароль неверный, подключение не выполнится.",
                        NotificationKind.Info);
                else
                    App.Notifications.Show("Wi-Fi",
                        "Не удалось подключиться к «" + net.Ssid + "». Проверьте пароль.",
                        NotificationKind.Warning);
            }
        }

        private void OnForgetProfile(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is string profile)
            {
                _wifi.Forget(profile);
                App.Notifications.Show("Wi-Fi", "Сеть забыта: " + profile, NotificationKind.Info);
                RefreshWifiPage();
            }
        }

        private void OnWifiDisconnect(object sender, RoutedEventArgs e)
        {
            _wifi.Disconnect();
            App.Notifications.Show("Wi-Fi", "Отключение от сети...", NotificationKind.Info);
        }
    }
}
