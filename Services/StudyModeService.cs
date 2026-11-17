using System;

namespace NeonShell.Services
{
    /// <summary>
    /// Режим учёбы без PIN: игры полностью заблокированы, пока режим включён
    /// вручную или действует расписание (например, с 18:00 до 21:00).
    /// </summary>
    public static class StudyModeService
    {
        public static bool IsEnabled => App.Settings?.Data.StudyModeEnabled ?? false;

        public static bool ScheduleEnabled => App.Settings?.Data.StudyScheduleEnabled ?? false;

        public static string StartTime
        {
            get => App.Settings?.Data.StudyStart ?? "18:00";
            set => App.Settings.Data.StudyStart = value;
        }

        public static string EndTime
        {
            get => App.Settings?.Data.StudyEnd ?? "21:00";
            set => App.Settings.Data.StudyEnd = value;
        }

        /// <summary>Сейчас игры заблокированы?</summary>
        public static bool IsActive
        {
            get
            {
                if (IsEnabled) return true;
                if (!ScheduleEnabled) return false;
                return IsWithinSchedule(DateTime.Now);
            }
        }

        public static bool IsWithinSchedule(DateTime now)
        {
            if (!TimeSpan.TryParse(StartTime, out var start)) return false;
            if (!TimeSpan.TryParse(EndTime, out var end)) return false;
            if (start == end) return false;
            var current = now.TimeOfDay;
            if (start < end) return current >= start && current < end;
            // интервал через полночь
            return current >= start || current < end;
        }

        public static void SetEnabled(bool enabled)
        {
            App.Settings.Data.StudyModeEnabled = enabled;
            App.Settings.MarkDirty();
            App.WindowManager?.ApplySettings();
            App.WindowManager?.RescanGames();
        }

        public static void SetSchedule(bool enabled, string start, string end)
        {
            App.Settings.Data.StudyScheduleEnabled = enabled;
            if (TimeSpan.TryParse(start, out _)) App.Settings.Data.StudyStart = start;
            if (TimeSpan.TryParse(end, out _)) App.Settings.Data.StudyEnd = end;
            App.Settings.MarkDirty();
            App.WindowManager?.ApplySettings();
            App.WindowManager?.RescanGames();
        }

        /// <summary>Описание текущего состояния (для индикаторов).</summary>
        public static string StatusText
        {
            get
            {
                if (IsEnabled) return "Режим учёбы включён — игры заблокированы";
                if (ScheduleEnabled)
                    return $"Расписание: {StartTime}–{EndTime}" +
                           (IsWithinSchedule(DateTime.Now) ? " (сейчас заблокировано)" : "");
                return "Режим учёбы выключен";
            }
        }
    }
}
