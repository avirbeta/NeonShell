using System.Collections.Generic;

namespace NeonShell.Models
{
    public class PinnedApp
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Args { get; set; } = "";
        public string BuiltInId { get; set; } = "";
        public bool IsBuiltIn => !string.IsNullOrEmpty(BuiltInId);
    }

    public class DesktopIconPos
    {
        public string Id { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class AppSettings
    {
        // Неон
        public string AccentHex { get; set; } = "#00E5FF";
        public string AccentSecondaryHex { get; set; } = "#FF2EA6";
        public bool AnimationsEnabled { get; set; } = true;

        // Рабочий стол
        public string Wallpaper { get; set; } = "neon://grid";
        public int DesktopIconSize { get; set; } = 76;
        public List<DesktopIconPos> DesktopIconPositions { get; set; } = new();
        public List<string> HiddenBuiltinIcons { get; set; } = new();

        // Панель задач
        public string TaskbarPosition { get; set; } = "Bottom";
        public int TaskbarHeight { get; set; } = 56;
        public bool TaskbarAutoHide { get; set; } = false;
        public double TaskbarOpacity { get; set; } = 0.88;
        public bool HideSystemTaskbar { get; set; } = true;
        public bool PinnedInitialized { get; set; } = false;
        public List<PinnedApp> PinnedApps { get; set; } = new();
        public List<string> RecentApps { get; set; } = new();

        // Уведомления
        public bool NotificationsEnabled { get; set; } = true;
        public int NotificationDurationSec { get; set; } = 5;

        // Игры
        public List<string> HiddenGameIds { get; set; } = new();
        public List<ManualGame> ManualGames { get; set; } = new();

        // Режим учёбы (без PIN: ручной + расписание)
        public bool StudyModeEnabled { get; set; } = false;
        public bool StudyScheduleEnabled { get; set; } = false;
        public string StudyStart { get; set; } = "18:00";
        public string StudyEnd { get; set; } = "21:00";

        // Система
        public bool Autostart { get; set; } = false;
    }

    public class ManualGame
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Args { get; set; } = "";
    }
}
