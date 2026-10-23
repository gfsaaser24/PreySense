using System;
using System.Diagnostics;
using System.IO;
using PreySense.Helpers;

namespace PreySense.Mode
{
    /// <summary>
    /// Governs CPU Boost and hardware power limits.
    /// Operates 100% driverless using Windows powercfg policies and ACPI firmware limits,
    /// avoiding unsigned kernel drivers and HVCI memory integrity conflicts.
    /// </summary>
    public class PowerLimitController
    {
        /// <summary>
        /// Applies custom PL1/PL2 power limits directly to Intel MCHBAR registers via
        /// the dedicated PreySense ACPI-WMI Device (\_SB.PRYW.WM01).
        /// Operates 100% driverless through standard Microsoft ACPI-WMI (PNP0C14).
        /// </summary>
        public static bool SetCpuPowerLimits(
            int pl1Watts,
            int pl2Watts,
            bool pl2Enable = true,
            int tauSeconds = PreySense.Core.Acpi.CpuPowerLimitConfiguration.FactoryTauSeconds)
        {
            uint pl1Mw = (uint)(Math.Max(5, pl1Watts) * 1000);
            uint pl2Mw = (uint)(Math.Max(5, pl2Watts) * 1000);

            byte tauTimeWindow = PreySense.Core.Acpi.CpuPowerLimitConfiguration.EncodeTauSelection(tauSeconds);
            bool success = PreySense.Core.Hardware.PreySenseWmiDeviceClient.SetCpuPowerLimits(pl1Mw, pl2Mw, tauTimeWindow);
            string tauDisplay = PreySense.Core.Acpi.CpuPowerLimitConfiguration.FormatTauSelection(tauSeconds);
            AppLogger.Log($"[CPU Power Limits] SetCpuPowerLimits(PL1={pl1Watts}W, PL2={pl2Watts}W, Tau={tauDisplay}/0x{tauTimeWindow:X2}) via ACPI-WMI -> {(success ? "Success" : "Failed/NotDiscovered")}");
            return success;
        }

        public static string GetCpuPowerLimitDiagnostic(
            int pl1Watts,
            int pl2Watts,
            bool pl2Enable = true)
        {
            return $"CPU Power Limits: PL1={pl1Watts}W, PL2={pl2Watts}W (Driverless ACPI / Power Plan mode)";
        }

        /// <summary>
        /// Toggles CPU Turbo Boost using hidden Windows Power Configuration attributes (PerfBoostMode).
        /// This is the exact method G-Helper uses to disable CPU Boost and drastically save battery.
        /// 0 = Disabled, 1 = Enabled, 2 = Aggressive, 3 = Efficient Enabled, 4 = Efficient Aggressive
        /// </summary>
        public static int EnergySaverBatteryThreshold(bool enabled) => enabled ? 100 : 0;

        /// <summary>
        /// Windows applies this DC threshold only while the laptop is on battery.
        /// 100 turns Energy Saver on for the whole battery session. 0 leaves it off.
        /// USB-C and the AC adapter both count as plugged in, so this stays off there.
        /// </summary>
        public static void SetEnergySaverOnBattery(bool enabled)
        {
            int threshold = EnergySaverBatteryThreshold(enabled);
            RunPowerCfg($"-setdcvalueindex scheme_current sub_energysaver esbattthreshold {threshold}");
            RunPowerCfg("-setactive scheme_current");
            AppLogger.Log($"Windows Energy Saver on battery: {(enabled ? "on" : "off")} (threshold {threshold}).");
        }

        public static void SetCpuBoost(int value)
        {
            RunPowerCfg($"-setacvalueindex scheme_current sub_processor perfboostmode {value}");
            RunPowerCfg($"-setdcvalueindex scheme_current sub_processor perfboostmode {value}");
            RunPowerCfg("-setactive scheme_current");
        }

        private static void RunPowerCfg(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"RunPowerCfg failed: {ex.Message}");
            }
        }
    }
}
