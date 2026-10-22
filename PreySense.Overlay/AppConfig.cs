using System;
using Microsoft.Win32;

namespace PreySense.Overlay
{
    public static class AppConfig
    {
        private const string RegistryKeyPath = @"SOFTWARE\PreySense";

        public static bool Exists(string name)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                return key?.GetValue(name) != null;
            }
            catch
            {
                return false;
            }
        }

        public static int Get(string name, int empty = -1)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                var val = key?.GetValue(name);
                if (val != null && int.TryParse(val.ToString(), out int result))
                {
                    return result;
                }
            }
            catch { }
            return empty;
        }

        public static bool Is(string name)
        {
            return Get(name) == 1;
        }

        public static string GetString(string name, string empty = "")
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                var val = key?.GetValue(name);
                return val?.ToString() ?? empty;
            }
            catch
            {
                return empty;
            }
        }

        public static void Set(string name, int value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(name, value, RegistryValueKind.DWord);
            }
            catch { }
        }

        public static void Set(string name, string value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(name, value, RegistryValueKind.String);
            }
            catch { }
        }

        public static bool IsOverlay()
        {
            return Is("overlay");
        }

        public static bool IsOverlayGameOnly()
        {
            return Is("overlay_game_only");
        }

        public static bool ShouldRunOverlay()
        {
            return IsOverlay() || IsOverlayGameOnly();
        }

        public static string DefaultOverlayColumn(int oneBasedIndex) => oneBasedIndex switch
        {
            1 => "Usage",
            2 => "Temperature",
            3 => "Voltage",
            4 => "Clock",
            5 => "Power",
            6 => "Memory",
            7 => "Memory Speed",
            8 => "Fan Speed",
            _ => "None"
        };

        public static void EnsureDefaultOverlayColumns()
        {
            if (Get("overlay_col_defaults", 1) >= 2)
                return;

            for (int i = 1; i <= 8; i++)
            {
                string key = $"overlay_col_{i}";
                string current = GetString(key);
                if (string.IsNullOrEmpty(current) || (i >= 7 && current == "None"))
                    Set(key, DefaultOverlayColumn(i));
            }

            Set("overlay_col_defaults", 2);
        }
    }
}
