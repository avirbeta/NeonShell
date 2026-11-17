using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace NeonShell.Services
{
    public class PowerPlan
    {
        public string Guid { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Режимы энергопотребления ноутбука через штатный powercfg.exe
    /// (список схем и переключение активной).
    /// </summary>
    public static class PowerPlanService
    {
        public static List<PowerPlan> GetPlans()
        {
            var plans = new List<PowerPlan>();
            try
            {
                var psi = new ProcessStartInfo("powercfg.exe", "/list")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(866),
                };
                using var process = Process.Start(psi);
                if (process == null) return plans;
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                foreach (Match m in Regex.Matches(output,
                    @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\(([^)]*)\)"))
                {
                    plans.Add(new PowerPlan
                    {
                        Guid = m.Groups[1].Value,
                        Name = m.Groups[2].Value.Trim(),
                        IsActive = output.Substring(0, m.Index).Length >= 0 &&
                                   m.Index + m.Length < output.Length &&
                                   output.Substring(m.Index + m.Length).TrimStart().StartsWith("*"),
                    });
                }
            }
            catch { }
            return plans;
        }

        public static bool SetActive(string guid)
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo("powercfg.exe", "/setactive " + guid)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                process?.WaitForExit(5000);
                return process?.ExitCode == 0;
            }
            catch { return false; }
        }
    }
}
