namespace PreySense.Mode
{
    /// <summary>
    /// Represents the hardware settings associated with a specific performance mode.
    /// Each mode (Quiet/Eco, Balanced, Performance, Turbo) stores its own set of limits.
    /// </summary>
    public class PerformanceProfile
    {
        public byte PowerMode { get; set; }
        public string Name { get; set; } = "Balanced";

        // CPU Power Limits
        public int CpuPl1 { get; set; } = 45;
        public int CpuPl2 { get; set; } = 65;
        public int TauSeconds { get; set; } = 56;
        public int CpuBoost { get; set; } = 1; // 0 = Disabled, 1 = Enabled, 2 = Aggressive, etc.

        // Apply flags — each category can be independently enabled/disabled per mode
        public bool ApplyCpuLimits { get; set; } = false;
        public bool ApplyGpuLimits { get; set; } = false;
        public bool ApplyFanCurve { get; set; } = false;
        public int FanRampUp { get; set; } = 1;

        /// <summary>
        /// Creates the default profile for a given power mode code.
        /// </summary>
        public static PerformanceProfile CreateDefault(byte mode)
        {
            var profile = mode switch
            {
                0x00 or 0x06 => new PerformanceProfile // Quiet / Eco
                {
                    PowerMode = mode,
                    Name = mode == 0x06 ? "Eco" : "Quiet",
                    CpuPl1 = 45,
                    CpuPl2 = 140,
                    TauSeconds = 56,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false, ApplyFanCurve = false
                },
                0x04 => new PerformanceProfile // Performance
                {
                    PowerMode = mode,
                    Name = "Performance",
                    CpuPl1 = 75,
                    CpuPl2 = 140,
                    TauSeconds = 56,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false, ApplyFanCurve = false
                },
                0x05 => new PerformanceProfile // Turbo
                {
                    PowerMode = mode,
                    Name = "Turbo",
                    CpuPl1 = 85,
                    CpuPl2 = 140,
                    TauSeconds = 56,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false, ApplyFanCurve = false
                },
                _ => new PerformanceProfile // Balanced (default)
                {
                    PowerMode = mode,
                    Name = "Balanced",
                    CpuPl1 = 65,
                    CpuPl2 = 140,
                    TauSeconds = 56,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false, ApplyFanCurve = false
                }
            };

            return profile;
        }
    }
}
