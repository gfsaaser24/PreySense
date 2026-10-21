using System;
using PreySense.Core.Hardware;

namespace PreySense.Core.Acpi
{
    public sealed record FactoryPowerProfileSnapshot(
        CpuPowerLimitConfiguration Cpu,
        GpuPowerLimitConfiguration Gpu,
        byte ActiveMode,
        bool CpuReadFromFirmware,
        bool GpuPowerReadFromFirmware,
        bool GpuTripReadFromFirmware,
        bool TauReadFromFirmware);

    /// <summary>
    /// Captures the active firmware power profile before application settings are applied.
    /// Firmware-known defaults remain in place for inactive modes because the ACPI-WMI
    /// interface exposes only the currently selected profile.
    /// </summary>
    public static class FactoryPowerProfileDetector
    {
        public static FactoryPowerProfileSnapshot Capture(byte activeMode, byte[]? dsdtBytes)
        {
            var cpu = new CpuPowerLimitConfiguration();
            var gpu = GpuPowerLimitConfiguration.DetectFromHardware(dsdtBytes);

            bool cpuRead = false;
            bool tauRead = false;
            var live = PreySenseWmiDeviceClient.GetPowerSnapshot();
            if (live.Pl1Milliwatts > 0 && live.Pl2Milliwatts > 0)
            {
                int pl1 = (int)Math.Round(live.Pl1Milliwatts / 1000.0);
                int pl2 = (int)Math.Round(live.Pl2Milliwatts / 1000.0);
                if (pl1 is >= 5 and <= CpuPowerLimitConfiguration.MaxPowerLimitWatts &&
                    pl2 >= pl1 && pl2 <= CpuPowerLimitConfiguration.MaxPowerLimitWatts)
                {
                    SetCpuMode(cpu, activeMode, pl1, pl2);
                    cpuRead = true;
                }

                if (live.TauEncoded.HasValue)
                {
                    cpu.TauSeconds = live.TauEncoded.Value == CpuPowerLimitConfiguration.InfiniteTauEncodedValue
                        ? CpuPowerLimitConfiguration.InfiniteTauSliderValue
                        : CpuPowerLimitConfiguration.DecodeTauSeconds(live.TauEncoded.Value);
                    tauRead = true;
                }
            }

            bool gpuPowerRead = false;
            if (live.GpuTargetWatts >= GpuPowerLimitConfiguration.MinimumPowerLimitWatts)
            {
                SetGpuModePower(gpu, activeMode, (int)live.GpuTargetWatts);
                gpuPowerRead = true;
            }

            bool gpuTripRead = false;
            if (live.GpuTripCelsius is >= 70 and <= 89)
            {
                SetGpuModeTrip(gpu, activeMode, (int)live.GpuTripCelsius);
                gpuTripRead = true;
            }

            return new FactoryPowerProfileSnapshot(cpu, gpu, activeMode, cpuRead, gpuPowerRead, gpuTripRead, tauRead);
        }

        private static void SetCpuMode(CpuPowerLimitConfiguration config, byte mode, int pl1, int pl2)
        {
            switch (mode)
            {
                case 0x05: config.TurboPl1 = pl1; config.TurboPl2 = pl2; break;
                case 0x04: config.PerformancePl1 = pl1; config.PerformancePl2 = pl2; break;
                case 0x01: config.BalancedPl1 = pl1; config.BalancedPl2 = pl2; break;
                default: config.QuietPl1 = pl1; config.QuietPl2 = pl2; break;
            }
        }

        private static void SetGpuModePower(GpuPowerLimitConfiguration config, byte mode, int watts)
        {
            switch (mode)
            {
                case 0x05: config.TurboGpuWatts = watts; break;
                case 0x04: config.PerformanceGpuWatts = watts; break;
                case 0x01: config.BalancedGpuWatts = watts; break;
                default: config.QuietGpuWatts = watts; break;
            }
        }

        private static void SetGpuModeTrip(GpuPowerLimitConfiguration config, byte mode, int temp)
        {
            switch (mode)
            {
                case 0x05: config.TurboGpuTripPointCelsius = temp; break;
                case 0x04: config.PerformanceGpuTripPointCelsius = temp; break;
                case 0x01: config.BalancedGpuTripPointCelsius = temp; break;
                default: config.QuietGpuTripPointCelsius = temp; break;
            }
        }
    }
}
