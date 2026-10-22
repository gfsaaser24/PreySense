using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using PreySense.Gpu;
using PreySense.Helpers;
using System.Runtime.InteropServices;
using PreySense.Core.Services;

namespace PreySense.Overlay
{
    public static class HardwareControl
    {
        public static Func<WmiController?>? WmiProvider { get; set; }
        private static WmiController? _fallbackWmi;
        public static IGpuControl? GpuControl;
        public static string CpuName { get; private set; } = "";
        public static string CpuShortName { get; private set; } = "";
        public static string GpuShortName { get; private set; } = "";

        public static float? cpuTemp = -1;
        public static float? gpuTemp = -1;

        public static float? cpuPower;
        public static float? gpuPower;

        public static int? cpuFanRPM;
        public static int? gpuFanRPM;

        public static int? cpuUsage;
        public static int? gpuUsage;
        public static int? vramUsage;
        public static int? ramUsage;
        public static int? vramUsedMb;
        public static int? ramUsedMb;

        // Extra metrics
        public static int?   cpuMhz;
        public static int?   gpuMhz;
        public static float? cpuVoltage;
        public static float? gpuVoltage;
        public static int?   ramSpeedMhz;
        public static int?   vramSpeedMhz;

        private static readonly PdhCpuFrequencyMonitor _pdhMonitor = new();
        private static readonly PdhCpuPowerMonitor _pdhPowerMonitor = new();

        private static readonly object _lhmLock = new();
        private static readonly LibreHardwareMonitor.Hardware.Computer _lhm = new()
        {
            IsCpuEnabled = true,
            IsGpuEnabled = false,
            IsNetworkEnabled = false,
            IsStorageEnabled = false,
            IsMotherboardEnabled = false,
            IsControllerEnabled = false,
            IsMemoryEnabled = false
        };
        private static bool _lhmOpened = false;
        private static float? _lhmCpuTemp;
        private static float? _lhmCpuPower;
        private static float _lhmCpuClock;
        private static int? _lastCpuUsage;
        private static long _lastSlowSensorTick;
        private const int SlowSensorIntervalMs = 1000;

        private static void EnsureLhmOpen()
        {
            if (_lhmOpened) return;
            lock (_lhmLock)
            {
                if (_lhmOpened) return;
                try
                {
                    if (!LhmDriverLocator.RelocateBeforeOpen())
                    {
                        AppLogger.Log("LibreHardwareMonitor Open skipped: WinRing0 device is not ready. Not writing PreySense.sys next to the exe.");
                        LhmDriverLocator.DeleteSidecarIfPresent();
                        return;
                    }

                    _lhm.Open();
                    _lhmOpened = true;
                    LhmDriverLocator.DeleteSidecarIfPresent();
                }
                catch (Exception ex)
                {
                    AppLogger.Log("LibreHardwareMonitor Open failed: " + ex.Message);
                }
            }
        }

        static HardwareControl()
        {
            try
            {
                GpuControl = NvidiaGpuControl.Shared;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"HardwareControl GpuControl init failed: {ex.Message}");
            }

            CpuName = ReadCpuName();
            CpuShortName = ShortCpuName(CpuName);
            RefreshGpuName();
        }

        public static void RefreshGpuName() => GpuShortName = ShortGpuName(GpuControl?.FullName);

        private static string ReadCpuName()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string ShortGpuName(string? full)
        {
            if (string.IsNullOrEmpty(full)) return "";
            foreach (string tag in new[] { "RTX", "GTX", "RX", "Arc" })
            {
                int i = full.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (i < 0) continue;
                string[] p = full[i..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return p.Length >= 2 ? p[0] + " " + p[1] : p[0];
            }
            return full;
        }

        private static string ShortCpuName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var m = Regex.Match(name, @"i[3579]-\w+");
            if (m.Success) return m.Value;

            m = Regex.Match(name, @"Ultra\s+\d+\s+(\w*\d\w*)");
            if (m.Success) return "Ultra " + m.Groups[1].Value;

            if (name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
            {
                m = Regex.Match(name, @"(?:[A-Z]{2,}\s+)?\d{3,}\w*");
                return m.Success ? "Ryzen " + m.Value : "Ryzen";
            }

            return name.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } t ? t[0] : "";
        }

        public static void Warmup()
        {
            EnsureLhmOpen();
        }

        private static void RefreshLhmCpuSensors()
        {
            EnsureLhmOpen();
            cpuVoltage = null;
            _lhmCpuTemp = null;
            _lhmCpuPower = null;
            _lhmCpuClock = 0;

            if (!_lhmOpened)
            {
                return;
            }

            lock (_lhmLock)
            {
            foreach (var hardware in _lhm.Hardware)
            {
                hardware.Update();
                if (hardware.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Cpu)
                {
                    continue;
                }

                foreach (var sensor in hardware.Sensors)
                {
                    if (!sensor.Value.HasValue)
                    {
                        continue;
                    }

                    if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Clock &&
                        sensor.Name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase) &&
                        sensor.Value.Value > _lhmCpuClock)
                    {
                        _lhmCpuClock = sensor.Value.Value;
                    }
                    else if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Voltage)
                    {
                        if (sensor.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            sensor.Name.IndexOf("Vcore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            sensor.Name.IndexOf("VID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            cpuVoltage == null)
                        {
                            cpuVoltage = sensor.Value.Value;
                        }
                    }
                    else if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature &&
                             (sensor.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 || _lhmCpuTemp == null))
                    {
                        _lhmCpuTemp = sensor.Value.Value;
                    }
                    else if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Power &&
                             (sensor.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 || _lhmCpuPower == null))
                    {
                        _lhmCpuPower = sensor.Value.Value;
                    }
                }
            }
            }
        }

        public static int GetCpuMhz()
        {
            try
            {
                RefreshLhmCpuSensors();
                if (_lhmCpuClock > 0)
                {
                    return (int)Math.Round(_lhmCpuClock);
                }

                if (_pdhMonitor.TrySampleFrequencies(out int pdhMaxMhz, out _, out _) && pdhMaxMhz > 0)
                {
                    return pdhMaxMhz;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Error in LHM GetCpuMhz and Volts: " + ex.Message);
            }
            return 0;
        }

        public static float? GetCPUPower()
        {
            try
            {
                if (_pdhPowerMonitor.IsAvailable)
                {
                    float watts = _pdhPowerMonitor.GetPackagePowerWatts();
                    if (watts > 0f)
                    {
                        return watts;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GetCPUPower PDH failed: " + ex.Message);
            }

            return _lhmCpuPower is > 0f ? _lhmCpuPower : null;
        }

        private static long _cpuLastIdle, _cpuLastKernel, _cpuLastUser, _cpuLastTick;
        private static bool _cpuUsageBaseline;

        private static int? GetCPUUsage()
        {
            if (!NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user)) return null;

            long now = Environment.TickCount64;

            if (!_cpuUsageBaseline || now - _cpuLastTick > 2000)
            {
                _cpuLastIdle = idle; _cpuLastKernel = kernel; _cpuLastUser = user; _cpuLastTick = now;
                _cpuUsageBaseline = true;
                return _lastCpuUsage;
            }

            long deltaIdle = idle - _cpuLastIdle;
            long deltaTotal = (kernel - _cpuLastKernel) + (user - _cpuLastUser);

            _cpuLastIdle = idle; _cpuLastKernel = kernel; _cpuLastUser = user; _cpuLastTick = now;

            if (deltaTotal <= 0) return _lastCpuUsage ?? 0;
            _lastCpuUsage = Math.Clamp((int)Math.Round((1.0 - (double)deltaIdle / deltaTotal) * 100), 0, 100);
            return _lastCpuUsage;
        }

        private static (int percent, int usedMb)? GetRAMInfo()
        {
            var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
            if (!NativeMethods.GlobalMemoryStatusEx(ref status)) return null;
            int usedMb = (int)((status.ullTotalPhys - status.ullAvailPhys) / (1024 * 1024));
            return ((int)status.dwMemoryLoad, usedMb);
        }

        private static float? GetGPUPower()
        {
            try
            {
                float? power = GpuControl?.GetGpuPower();
                if (power is not null) return power.Value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed reading GPU power: " + ex.Message);
            }
            return null;
        }

        public static void ReadSensorsOverlay()
        {
            var wmi = WmiProvider?.Invoke() ?? (_fallbackWmi ??= new WmiController());
            var nvidia = GpuControl as NvidiaGpuControl;
            // NVML init wakes a dGPU in D3/RTD3 and holds it there. Skip it unless the GPU is already in use.
            bool gpuInUse = nvidia is null || nvidia.IsDiscreteGpuInUse();

            long now = Environment.TickCount64;
            bool slowSensors = _lastSlowSensorTick == 0 || now - _lastSlowSensorTick >= SlowSensorIntervalMs;
            if (slowSensors)
            {
                _lastSlowSensorTick = now;
                cpuMhz = GetCpuMhz();
                if (wmi != null)
                {
                    var thermal = wmi.ReadThermalSensors();
                    cpuFanRPM = thermal.CpuRpm;
                    gpuFanRPM = thermal.GpuRpm;
                    cpuTemp = thermal.CpuTemp;
                    if (gpuInUse)
                        gpuTemp = thermal.GpuTemp;
                }
                else
                {
                    cpuFanRPM = null;
                    gpuFanRPM = null;
                    cpuTemp = -1;
                }

                if (cpuTemp <= 0 && _lhmCpuTemp.HasValue && _lhmCpuTemp.Value > 0)
                    cpuTemp = _lhmCpuTemp.Value;

                if (!gpuInUse)
                {
                    int? idleTemp = null;
                    try { idleTemp = nvidia?.ReadCurrentTemperature(); }
                    catch { idleTemp = null; }
                    gpuTemp = idleTemp is > 0 ? idleTemp.Value : 0;
                }
                else
                {
                    if (wmi == null)
                        gpuTemp = -1;

                    int? liveGpuTemp = NvmlHelper.GetGpuTemperature();
                    if (liveGpuTemp is null or <= 0)
                    {
                        try { liveGpuTemp = nvidia?.ReadCurrentTemperature(); }
                        catch { liveGpuTemp = null; }
                    }
                    if (liveGpuTemp is > 0)
                        gpuTemp = liveGpuTemp.Value;
                }
            }

            cpuUsage = GetCPUUsage();

            var ram = GetRAMInfo();
            ramUsage = ram?.percent;
            ramUsedMb = ram?.usedMb;

            if (!gpuInUse)
            {
                gpuUsage = null;
                vramUsedMb = null;
                vramUsage = null;
                gpuPower = 0;
                gpuMhz = null;
                vramSpeedMhz = null;
                gpuVoltage = null;
            }
            else
            {
                try { gpuUsage = GpuControl?.GetGpuUse(); } catch { gpuUsage = null; }

                try
                {
                    if (GpuControl?.GetVramInfo() is { } v && v.totalMb > 0)
                    {
                        vramUsedMb = (int)v.usedMb;
                        vramUsage = (int)Math.Clamp(v.usedMb * 100 / v.totalMb, 0, 100);
                    }
                    else { vramUsedMb = null; vramUsage = null; }
                }
                catch { vramUsedMb = null; vramUsage = null; }

                gpuPower = GetGPUPower() ?? gpuPower;
                try { gpuMhz = GpuControl?.GetGpuClock(); } catch { gpuMhz = null; }
                try { vramSpeedMhz = GpuControl?.GetGpuMemoryClock(); } catch { vramSpeedMhz = null; }
                try { gpuVoltage = GpuControl?.GetGpuVoltage(); } catch { gpuVoltage = null; }
            }

            cpuPower = GetCPUPower();
            ramSpeedMhz = GetRamSpeed();
        }

        private static int? _cachedRamSpeed;
        public static int? GetRamSpeed()
        {
            if (_cachedRamSpeed.HasValue) return _cachedRamSpeed;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var speed = obj["ConfiguredClockSpeed"] ?? obj["Speed"];
                    if (speed != null)
                      {
                          int val = Convert.ToInt32(speed);
                          if (val > 0)
                          {
                              _cachedRamSpeed = val;
                              return val;
                          }
                      }
                  }
              }
              catch { }
              return null;
          }
    }
}
