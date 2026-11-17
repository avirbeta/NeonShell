using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NeonShell.Core;
using NeonShell.Core.Helpers;

namespace NeonShell.Services
{
    /// <summary>Неоновые обои рабочего стола (градиенты со свечением).</summary>
    public static class WallpaperService
    {
        public static readonly string[] Presets = { "grid", "cyber", "void", "pulse", "sunset" };

        public static string PresetName(string id)
        {
            switch (id)
            {
                case "grid": return "Неоновая сетка";
                case "cyber": return "Кибер-синь";
                case "void": return "Пустота";
                case "pulse": return "Пульс";
                case "sunset": return "Неоновый закат";
                default: return id;
            }
        }

        public static Brush GetPresetBrush(string preset)
        {
            (Color c1, Color c2, Color glow1, Color glow2, double ox, double oy) = preset switch
            {
                "cyber" => (C("#070B18"), C("#101A38"), C("#004E8A"), C("#5A1FA0"), 0.25, 0.2),
                "void" => (C("#030507"), C("#081012"), C("#00483C"), C("#123B4A"), 0.7, 0.8),
                "pulse" => (C("#0A0514"), C("#180B26"), C("#7A0FA8"), C("#FF2EA6"), 0.5, 0.4),
                "sunset" => (C("#140812"), C("#24101A"), C("#C2185B"), C("#FF7A2E"), 0.8, 0.25),
                _ => (C("#05070D"), C("#0B1222"), C("#005C8A"), C("#6A1FB8"), 0.3, 0.3),
            };
            return BuildBrush(c1, c2, glow1, glow2, ox, oy);
        }

        public static object Resolve(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (value.StartsWith("neon://"))
                return GetPresetBrush(value.Substring("neon://".Length));
            return IconService.LoadImage(value, 0);
        }

        public static string StoreUserImage(string sourcePath)
        {
            string dir = Path.Combine(App.DataDir, "wallpapers");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, Path.GetFileName(sourcePath));
            try { File.Copy(sourcePath, dest, true); }
            catch { dest = sourcePath; }
            return dest;
        }

        private static Color C(string hex) => ThemeManager.ParseColor(hex, Colors.Black);

        private static DrawingBrush BuildBrush(Color base1, Color base2, Color glow1, Color glow2, double ox, double oy)
        {
            var dg = new DrawingGroup();
            var full = new RectangleGeometry(new Rect(0, 0, 1, 1));
            var linear = new LinearGradientBrush(base1, base2, new Point(0, 0), new Point(1, 1));
            linear.Freeze();
            dg.Children.Add(new GeometryDrawing(linear, null, full));

            var radial1 = new RadialGradientBrush
            {
                GradientOrigin = new Point(ox, oy),
                Center = new Point(ox, oy),
                RadiusX = 0.85,
                RadiusY = 0.85,
            };
            radial1.GradientStops.Add(new GradientStop(Glow(glow1, 0.50), 0));
            radial1.GradientStops.Add(new GradientStop(Glow(glow1, 0.0), 1));
            radial1.Freeze();
            dg.Children.Add(new GeometryDrawing(radial1, null, full));

            var radial2 = new RadialGradientBrush
            {
                GradientOrigin = new Point(1 - ox, 1 - oy),
                Center = new Point(1 - ox, 1 - oy),
                RadiusX = 0.9,
                RadiusY = 0.9,
            };
            radial2.GradientStops.Add(new GradientStop(Glow(glow2, 0.45), 0));
            radial2.GradientStops.Add(new GradientStop(Glow(glow2, 0.0), 1));
            radial2.Freeze();
            dg.Children.Add(new GeometryDrawing(radial2, null, full));

            dg.Freeze();
            var brush = new DrawingBrush(dg) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            return brush;
        }

        private static Color Glow(Color c, double alpha)
            => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);
    }
}
