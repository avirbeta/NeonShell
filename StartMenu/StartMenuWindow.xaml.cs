using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NeonShell.Core;
using NeonShell.Core.Helpers;
using NeonShell.Models;
using NeonShell.Services;

namespace NeonShell.StartMenu
{
    /// <summary>
    /// Неоновое меню Neon: вкладки «Игры» (Steam/Epic/вручную, с адаптацией
    /// неонового цвета под обложку) и «Приложения».
    /// </summary>
    public partial class StartMenuWindow : Window
    {
        public ObservableCollection<GameEntry> Games { get; } = new();
        public ObservableCollection<AppEntry> AppsView { get; } = new();

        private List<GameEntry> _allGames = new();
        private List<AppEntry> _allApps = new();
        private string _currentCategory = "Все приложения";
        private bool _closing;

        public StartMenuWindow()
        {
            InitializeComponent();
            GamesItems.ItemsSource = Games;
            AppsItems.ItemsSource = AppsView;

            App.Apps.AppsLoaded += (_, _) => Dispatcher.BeginInvoke(new Action(RebuildApps));
            RebuildApps();
            ScanGames();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SearchBox.Focus();
        }

        // ---------- игры ----------

        /// <summary>Пересканировать библиотеку игр (вызывается из настроек).</summary>
        public void RescanGames() => ScanGames();

        private void ScanGames()
        {
            try
            {
                _allGames = GameScanner.Scan(App.Settings.Data, App.Log)
                    .Where(g => !App.Settings.Data.HiddenGameIds.Contains(g.Id))
                    .ToList();
                ApplyFilters();
                _ = LoadGameColorsAsync(_allGames);
            }
            catch (Exception ex)
            {
                App.Log?.Error("Сканирование игр", ex);
            }
        }

        private async System.Threading.Tasks.Task LoadGameColorsAsync(List<GameEntry> entries)
        {
            foreach (var g in entries)
            {
                try
                {
                    ImageSource source = g.Cover ?? g.Icon;
                    if (source != null && !g.ColorReady)
                    {
                        await System.Windows.Threading.Dispatcher.Yield(
                            System.Windows.Threading.DispatcherPriority.Background);
                        var color = ColorService.DominantColor(source);
                        g.Dominant = color;
                        g.ColorReady = true;
                    }
                }
                catch { }
            }
        }

        private void OnGameClick(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is GameEntry game)
            {
                if (game.IsLocked)
                {
                    App.Notifications.Show("Время учёбы",
                        "Игра «" + game.Name + "» заблокирована. Управление — Параметры Neon → Учёба.",
                        NotificationKind.Warning);
                    return;
                }
                CloseMenu();
                GameScanner.LaunchGame(game, App.Log);
            }
        }

        private void OnGameRightClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as Button)?.Tag is GameEntry game)
            {
                var menu = new ContextMenu();
                AddItem(menu, "Играть", Glyphs.Play, () => { CloseMenu(); GameScanner.LaunchGame(game, App.Log); });
                if (!game.IsSteamUri && !string.IsNullOrEmpty(game.WorkingDir) &&
                    System.IO.Directory.Exists(game.WorkingDir))
                {
                    AddItem(menu, "Папка игры", Glyphs.Folder, () =>
                        AppLauncher.OpenPath(game.WorkingDir));
                }
                menu.Items.Add(new Separator());
                AddItem(menu, "Скрыть из списка", Glyphs.Close, () =>
                {
                    if (!App.Settings.Data.HiddenGameIds.Contains(game.Id))
                        App.Settings.Data.HiddenGameIds.Add(game.Id);
                    App.Settings.MarkDirty();
                    ScanGames();
                });
                menu.IsOpen = true;
            }
            e.Handled = true;
        }

        private void OnAddGame(object sender, RoutedEventArgs e)
        {
            var dlg = new Dialogs.AddGameDialog { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.GamePath))
            {
                App.Settings.Data.ManualGames.RemoveAll(m =>
                    string.Equals(m.Path, dlg.GamePath, StringComparison.OrdinalIgnoreCase));
                App.Settings.Data.ManualGames.Add(new ManualGame
                {
                    Name = dlg.GameName,
                    Path = dlg.GamePath,
                    Args = dlg.Arguments,
                });
                App.Settings.Data.HiddenGameIds.Remove("manual:" + dlg.GamePath.ToLowerInvariant());
                App.Settings.MarkDirty();
                ScanGames();
            }
        }

        private void OnRescanGames(object sender, RoutedEventArgs e) => ScanGames();

        // ---------- приложения ----------

        private void RebuildApps()
        {
            _allApps = new List<AppEntry>();
            foreach (var b in App.Apps.BuiltIns)
            {
                b.Icon ??= IconService.FromGlyph(BuiltInGlyph(b), BuiltInColor(b));
                _allApps.Add(b);
            }
            if (App.Apps.IsLoaded) _allApps.AddRange(App.Apps.Apps);

            var cats = new List<string> { "Все приложения", "Недавние" };
            cats.AddRange(_allApps.Where(a => !a.IsBuiltIn && !string.IsNullOrEmpty(a.Category))
                .Select(a => a.Category).Distinct()
                .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase));
            string selected = CategoryList.SelectedItem as string ?? "Все приложения";
            CategoryList.ItemsSource = cats;
            CategoryList.SelectedItem = cats.Contains(selected) ? selected : cats[0];
            ApplyFilters();
        }

        private static string BuiltInGlyph(AppEntry b) => b.BuiltInId switch
        {
            "Games" => Glyphs.Gamepad,
            "Settings" => Glyphs.Settings,
            "ControlCenter" => Glyphs.More,
            "Exit" => Glyphs.Home,
            "Restart" => Glyphs.Restart,
            "Shutdown" => Glyphs.Power,
            _ => Glyphs.Apps,
        };

        private static Color BuiltInColor(AppEntry b) => b.BuiltInId switch
        {
            "Games" => ThemeManager.ParseColor("#00E5FF", Colors.White),
            "Settings" => ThemeManager.ParseColor("#FF2EA6", Colors.White),
            "ControlCenter" => ThemeManager.ParseColor("#39FF88", Colors.White),
            "Exit" => ThemeManager.ParseColor("#39FF88", Colors.White),
            "Restart" => ThemeManager.ParseColor("#FFC233", Colors.White),
            "Shutdown" => ThemeManager.ParseColor("#FF2E5B", Colors.White),
            _ => ThemeManager.ParseColor("#7E8AA0", Colors.White),
        };

        private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentCategory = CategoryList.SelectedItem as string ?? "Все приложения";
            ApplyFilters();
        }

        private void OnAppClick(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is AppEntry app)
            {
                CloseMenu();
                InstalledAppsService.Launch(app);
            }
        }

        // ---------- фильтры ----------

        private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            string q = SearchBox.Text?.Trim() ?? "";

            var games = string.IsNullOrEmpty(q)
                ? _allGames
                : _allGames.Where(g => g.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            Games.Clear();
            bool study = StudyModeService.IsActive;
            foreach (var g in games)
            {
                g.IsLocked = study;
                Games.Add(g);
            }

            IEnumerable<AppEntry> apps = _currentCategory switch
            {
                "Недавние" => GetRecent(),
                _ => _allApps.Where(a => a.IsBuiltIn || a.Category == _currentCategory || _currentCategory == "Все приложения"),
            };
            if (!string.IsNullOrEmpty(q))
                apps = apps.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

            AppsView.Clear();
            foreach (var a in apps) AppsView.Add(a);

            GamesEmptyText.Visibility = Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AppsEmptyText.Visibility = AppsView.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // адаптация неонового цвета под первую найденную игру
            if (!string.IsNullOrEmpty(q))
            {
                var first = Games.FirstOrDefault();
                if (first != null && first.ColorReady)
                    ThemeManager.AnimateAccent(first.Dominant, ColorService.ShiftHue(first.Dominant, 48));
                else
                    ThemeManager.Apply(App.Settings);
            }
            else
            {
                ThemeManager.Apply(App.Settings);
            }
        }

        private IEnumerable<AppEntry> GetRecent()
        {
            foreach (var path in App.Settings.Data.RecentApps)
            {
                var known = _allApps.FirstOrDefault(a =>
                    string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
                if (known != null) yield return known;
                else if (!string.IsNullOrEmpty(path))
                    yield return new AppEntry
                    {
                        Name = System.IO.Path.GetFileNameWithoutExtension(path),
                        Path = path,
                        Icon = IconService.ForExecutable(path) ?? IconService.ForPath(path),
                    };
            }
        }

        // ---------- вкладки ----------

        private void OnGamesTab(object sender, RoutedEventArgs e)
        {
            GamesPanel.Visibility = Visibility.Visible;
            AppsPanel.Visibility = Visibility.Collapsed;
            GamesTab.Background = (Brush)Application.Current.Resources["AccentBrushSoft"];
            AppsTab.Background = (Brush)Application.Current.Resources["SurfaceBrush"];
        }

        private void OnAppsTab(object sender, RoutedEventArgs e)
        {
            GamesPanel.Visibility = Visibility.Collapsed;
            AppsPanel.Visibility = Visibility.Visible;
            AppsTab.Background = (Brush)Application.Current.Resources["AccentBrushSoft"];
            GamesTab.Background = (Brush)Application.Current.Resources["SurfaceBrush"];
            ThemeManager.Apply(App.Settings);
        }

        // ---------- низ ----------

        private void OnOpenSettings(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            App.WindowManager?.OpenSettings();
        }

        private void OnOpenCc(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            App.WindowManager?.ToggleControlCenter();
        }

        private void OnExitWindows(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            App.WindowManager?.ExitShell();
        }

        private void OnPowerClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                PlacementTarget = PowerBtn,
            };
            AddItem(menu, "Завершение работы", Glyphs.Power, () => { CloseMenu(); SystemServices.PowerService.Shutdown(); });
            AddItem(menu, "Перезагрузка", Glyphs.Restart, () => { CloseMenu(); SystemServices.PowerService.Restart(); });
            AddItem(menu, "Сон", Glyphs.Clock, () => { CloseMenu(); SystemServices.PowerService.Sleep(); });
            menu.Items.Add(new Separator());
            AddItem(menu, "Блокировка", Glyphs.Lock, () => { CloseMenu(); SystemServices.PowerService.Lock(); });
            menu.Items.Add(new Separator());
            AddItem(menu, "Выйти в Windows", Glyphs.Home, () => { CloseMenu(); App.WindowManager?.ExitShell(); });
            menu.IsOpen = true;
        }

        private void AddItem(ContextMenu menu, string header, string glyph, Action action)
        {
            var item = new MenuItem
            {
                Header = header,
                Icon = glyph == null ? null : new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        // ---------- показ/скрытие ----------

        public void OpenMenu(bool gamesTab)
        {
            _closing = false;
            if (gamesTab) OnGamesTab(null, null);
            else OnAppsTab(null, null);
            Position();
            Show();
            Activate();
            SearchBox.Text = "";
            SearchBox.Focus();
            if (!App.AnimationsEnabled)
            {
                Opacity = 1;
                MenuTransform.Y = 0;
                return;
            }
            Opacity = 0;
            MenuTransform.Y = 14;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            MenuTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }

        public void CloseMenu()
        {
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
                var wa = SystemParameters.WorkArea;
                double taskbarTop = top ? (settings.HideSystemTaskbar ? 0 : wa.Top)
                    : (settings.HideSystemTaskbar ? screenH - settings.TaskbarHeight : wa.Bottom - settings.TaskbarHeight);

                Left = (settings.HideSystemTaskbar ? 0 : wa.Left) + 8;
                if (top) Top = taskbarTop + settings.TaskbarHeight + 8;
                else Top = Math.Max(4, taskbarTop - Height - 8);

                double maxH = screenH - settings.TaskbarHeight - 16;
                if (Height > maxH) Height = maxH;
            }
            catch (Exception ex) { App.Log?.Error("StartMenu Position", ex); }
        }

        private void OnDeactivated(object sender, EventArgs e)
        {
            if (!_closing) CloseMenu();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    CloseMenu();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (Games.Count > 0 && GamesPanel.Visibility == Visibility.Visible)
                    {
                        CloseMenu();
                        GameScanner.LaunchGame(Games[0], App.Log);
                        e.Handled = true;
                    }
                    else if (AppsView.Count > 0)
                    {
                        CloseMenu();
                        InstalledAppsService.Launch(AppsView[0]);
                        e.Handled = true;
                    }
                    break;
            }
        }
    }
}
