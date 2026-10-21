using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace PreySense.Frontend.Services
{
    public static class StartupTaskService
    {
        public const string TaskName = "PreySense";

        public static bool IsStartupEnabled()
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{TaskName}\" /FO LIST",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                proc?.WaitForExit(3000);
                if (proc?.ExitCode == 0)
                {
                    return true;
                }
            }
            catch { }

            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                return runKey?.GetValue(TaskName) != null;
            }
            catch { return false; }
        }

        public static bool SetStartupEnabled(bool enable)
        {
            string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
                return false;

            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                runKey?.DeleteValue(TaskName, false);
            }
            catch { }

            if (enable)
            {
                string args = $"/Create /F /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --minimized\" /SC ONLOGON /RL HIGHEST";
                return RunSchtasks(args);
            }
            else
            {
                return RunSchtasks($"/Delete /F /TN \"{TaskName}\"");
            }
        }

        private static bool RunSchtasks(string arguments)
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                proc?.WaitForExit(5000);
                return proc?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
