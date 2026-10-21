using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using PreySense.Helpers;

namespace PreySense.Overlay
{
    /// <summary>
    /// LibreHardwareMonitor extracts WinRing0 next to the exe as PreySense.sys
    /// whenever \\.\WinRing0_1_2_0 is not already open. Host that driver in
    /// ProgramData and open the device first so Computer.Open() never writes
    /// a sidecar into dist.
    /// </summary>
    internal static class LhmDriverLocator
    {
        private const string DevicePath = @"\\.\WinRing0_1_2_0";
        private const uint GenericReadWrite = 0xC0000000;
        private const uint OpenExisting = 3;

        public static bool RelocateBeforeOpen()
        {
            try
            {
                string? processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(processPath))
                {
                    return IsDeviceReady();
                }

                string serviceName = "R0" + Path.GetFileNameWithoutExtension(processPath);
                string sidecarPath = Path.ChangeExtension(processPath, ".sys");
                string dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "PreySense");
                string hostedPath = Path.Combine(dataDir, "WinRing0x64.sys");

                Directory.CreateDirectory(dataDir);
                if (!EnsureDriverFile(hostedPath, sidecarPath))
                {
                    TryDelete(sidecarPath);
                    return IsDeviceReady();
                }

                BindService(serviceName, hostedPath);
                WaitForDevice(1500);
                TryDelete(sidecarPath);
                return IsDeviceReady();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LhmDriverLocator: {ex.Message}");
                return IsDeviceReady();
            }
        }

        public static void DeleteSidecarIfPresent()
        {
            try
            {
                string? processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(processPath))
                {
                    return;
                }

                TryDelete(Path.ChangeExtension(processPath, ".sys"));
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LhmDriverLocator sidecar cleanup: {ex.Message}");
            }
        }

        public static bool IsDeviceReady()
        {
            using SafeFileHandle handle = CreateFile(DevicePath, GenericReadWrite, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            return handle is { IsInvalid: false };
        }

        private static bool EnsureDriverFile(string hostedPath, string sidecarPath)
        {
            if (ExtractFromLibreHardwareMonitor(hostedPath))
            {
                return true;
            }

            if (File.Exists(hostedPath) && new FileInfo(hostedPath).Length > 0)
            {
                return true;
            }

            if (File.Exists(sidecarPath))
            {
                File.Copy(sidecarPath, hostedPath, true);
                return true;
            }

            AppLogger.Log("LhmDriverLocator: WinRing0 driver image was not found.");
            return false;
        }

        private static bool ExtractFromLibreHardwareMonitor(string destPath)
        {
            var asm = typeof(LibreHardwareMonitor.Hardware.Computer).Assembly;
            string? resource = asm.GetManifestResourceNames().FirstOrDefault(name =>
                name.Contains("WinRing0x64", StringComparison.OrdinalIgnoreCase));
            if (resource == null)
            {
                return false;
            }

            try
            {
                using Stream? src = asm.GetManifestResourceStream(resource);
                if (src == null)
                {
                    return false;
                }

                // LHM stores a dummy first byte ahead of the gzip payload.
                if (src.CanSeek && src.Length > 1)
                {
                    src.Position = 1;
                }

                string tempPath = destPath + ".tmp";
                using (var gzip = new GZipStream(src, CompressionMode.Decompress))
                using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    gzip.CopyTo(dst);
                }

                File.Copy(tempPath, destPath, true);
                TryDelete(tempPath);
                AppLogger.Log($"LhmDriverLocator: extracted {resource} to {destPath}.");
                return File.Exists(destPath) && new FileInfo(destPath).Length > 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LhmDriverLocator extract failed: {ex.Message}");
                return false;
            }
        }

        private static void BindService(string serviceName, string hostedPath)
        {
            string? current = ReadImagePath(serviceName);
            string expected = @"\??\" + hostedPath;
            if (string.Equals(Normalize(current), Normalize(hostedPath), StringComparison.OrdinalIgnoreCase)
                || string.Equals(Normalize(current), Normalize(expected), StringComparison.OrdinalIgnoreCase))
            {
                RunSc($"start \"{serviceName}\"");
                return;
            }

            RunSc($"stop \"{serviceName}\"");
            RunSc($"delete \"{serviceName}\"");
            for (int i = 0; i < 8 && ReadImagePath(serviceName) != null; i++)
            {
                System.Threading.Thread.Sleep(150);
            }

            RunSc($"create \"{serviceName}\" type= kernel start= demand binPath= \"{hostedPath}\"");
            RunSc($"start \"{serviceName}\"");
            AppLogger.Log($"LhmDriverLocator: {serviceName} bound to {hostedPath}.");
        }

        private static void WaitForDevice(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (IsDeviceReady())
                {
                    return;
                }

                System.Threading.Thread.Sleep(50);
            }
        }

        private static string? ReadImagePath(string serviceName)
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key?.GetValue("ImagePath") as string;
        }

        private static string Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string trimmed = path.Trim().Trim('"');
            if (trimmed.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[4..];
            }

            return trimmed;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LhmDriverLocator: could not delete {path}: {ex.Message}");
            }
        }

        private static void RunSc(string args)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("sc.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                process?.WaitForExit(4000);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LhmDriverLocator: sc {args} failed: {ex.Message}");
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);
    }
}
