using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using Microsoft.Win32;
using PreySense.Core.Acpi;
using PreySense.Helpers;

namespace PreySense.Core.Hardware
{
    /// <summary>
    /// Startup check for PreySenseControl. A matching SSDT and a live Tau read clear the
    /// pending flag. A stale class is recompiled. An older SSDT is left unchanged.
    /// </summary>
    public static class PreySenseWmiRepair
    {
        public const string PendingValueName = "WmiRepairPending";
        public const string InterfaceVersionMarker = "PreySenseInterfaceVersion=";
        private const string AppKeyPath = @"SOFTWARE\PreySense";
        private const string AutorecoverKeyPath = @"SOFTWARE\Microsoft\WBEM\CIMOM";
        private const string AutorecoverValueName = "Autorecover MOFs";

        private enum MofCompMode
        {
            Check,
            Compile,
            Delete
        }

        public static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool ShouldRebuild(PreySenseWmiSchemaState state)
        {
            return state is PreySenseWmiSchemaState.Missing or PreySenseWmiSchemaState.Mismatched;
        }

        public static bool TryParseInterfaceVersion(string mofText, out uint version)
        {
            version = 0;
            foreach (string raw in mofText.Split('\n'))
            {
                string line = raw.Trim();
                int marker = line.IndexOf(InterfaceVersionMarker, StringComparison.Ordinal);
                if (marker < 0 || !line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                string token = line[(marker + InterfaceVersionMarker.Length)..].Trim();
                if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    token = token[2..];
                return uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out version);
            }

            return false;
        }

        public static bool IsPending()
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(AppKeyPath);
            return key?.GetValue(PendingValueName) is int value && value != 0;
        }

        public static void ClearPending()
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(AppKeyPath, writable: true);
            key?.DeleteValue(PendingValueName, throwOnMissingValue: false);
        }

        public static bool TryRegister(string mofPath, out string message)
        {
            message = string.Empty;
            try
            {
                if (!File.Exists(mofPath))
                {
                    message = "The current preysense.mof could not be staged.";
                    return false;
                }

                if (!TryParseInterfaceVersion(File.ReadAllText(mofPath), out uint expectedVersion))
                {
                    message = "preysense.mof does not declare PreySenseInterfaceVersion, so the existing class was left unchanged.";
                    return false;
                }

                if (!OpenCoreSecurityValidator.TryGetPreyDevOemRevision(out uint loadedRevision) ||
                    loadedRevision != expectedVersion)
                {
                    message = "Restart into the PreySense boot entry, then open PreySense again. The WMI class was left unchanged.";
                    return false;
                }

                PreySenseWmiSchemaState state = PreySenseWmiDeviceClient.InspectInterface(expectedVersion);
                if (state == PreySenseWmiSchemaState.Ready)
                {
                    ClearPending();
                    message = string.Empty;
                    return true;
                }

                if (!ShouldRebuild(state))
                {
                    message = Describe(state);
                    return false;
                }

                if (!IsAdministrator())
                {
                    message = "PreySense is not running as administrator. Approve the elevation prompt and start PreySense again.";
                    return false;
                }

                if (!RunMofComp(mofPath, MofCompMode.Check, out string checkError))
                {
                    message = "mofcomp -check failed, so the existing class was left unchanged. " + checkError;
                    return false;
                }

                RemoveAutorecoverEntries();
                if (!RunMofComp(WriteDeleteMof(), MofCompMode.Delete, out string deleteError))
                {
                    message = deleteError;
                    return false;
                }

                if (!RunMofComp(mofPath, MofCompMode.Compile, out string compileError))
                {
                    message = compileError;
                    return false;
                }

                state = PreySenseWmiDeviceClient.InspectInterface(expectedVersion);
                if (state != PreySenseWmiSchemaState.Ready)
                {
                    message = "PreySenseControl was recompiled, but the live power read failed. Start PreySense again.";
                    return false;
                }

                RemoveAutorecoverEntries();
                ClearPending();
                message = "PreySense WMI class registered for this boot.";
                AppLogger.Log("[WMI] PreySenseControl registered after OpenCore boot.");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                message = "PreySense is not running as administrator. Approve the elevation prompt and start PreySense again.";
                AppLogger.Log("[WMI] PreySenseControl registration needs administrator privileges.");
                return false;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                AppLogger.Log($"[WMI] PreySenseControl registration failed: {ex.Message}");
                return false;
            }
        }

        private static string Describe(PreySenseWmiSchemaState state)
        {
            return state switch
            {
                PreySenseWmiSchemaState.RuntimeFailure =>
                    "PreySenseControl matches this package, but a live power read failed. The class was left unchanged.",
                PreySenseWmiSchemaState.SsdtVersionDiffers =>
                    "PreySenseControl is present, but its ACPI version does not match this package. The class was left unchanged.",
                _ => "PreySenseControl was recompiled, but verification did not succeed."
            };
        }

        private static string WriteDeleteMof()
        {
            string deleteMof = Path.Combine(Path.GetTempPath(), "PreySenseControl-delete.mof");
            File.WriteAllText(deleteMof, "#pragma deleteclass(\"PreySenseControl\", NOFAIL)\r\n");
            return deleteMof;
        }

        private static void RemoveAutorecoverEntries()
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(AutorecoverKeyPath, writable: true);
            if (key?.GetValue(AutorecoverValueName) is not string[] entries)
                return;

            string[] kept = entries
                .Where(entry => entry.Contains("preysense.mof", StringComparison.OrdinalIgnoreCase) == false)
                .ToArray();
            if (kept.Length == entries.Length)
                return;

            key.SetValue(AutorecoverValueName, kept, RegistryValueKind.MultiString);
        }

        private static bool RunMofComp(string mofPath, MofCompMode mode, out string error)
        {
            string compiler = Path.Combine(Environment.SystemDirectory, "wbem", "mofcomp.exe");
            if (!File.Exists(compiler))
            {
                error = "mofcomp.exe was not found.";
                return false;
            }

            string arguments = mode switch
            {
                MofCompMode.Check => $"-check \"{mofPath}\"",
                MofCompMode.Delete => $"-N:root\\wmi \"{mofPath}\"",
                _ => $"\"{mofPath}\""
            };
            var start = new ProcessStartInfo
            {
                FileName = compiler,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using Process? process = Process.Start(start);
            if (process == null)
            {
                error = "mofcomp.exe could not be started.";
                return false;
            }

            string output = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                error = string.Empty;
                return true;
            }

            string detail = string.IsNullOrWhiteSpace(stderr) ? output.Trim() : stderr.Trim();
            error = string.IsNullOrWhiteSpace(detail)
                ? $"mofcomp failed with exit code {process.ExitCode}."
                : $"mofcomp failed with exit code {process.ExitCode}: {detail}";
            return false;
        }
    }
}
