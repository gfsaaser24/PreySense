using PreySense.Core.Acpi;

namespace PreySense.Mode
{
    public sealed class GameProfile
    {
        public string ExeName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public byte PowerMode { get; set; } = 0x04;
        public int CpuPl1 { get; set; } = 75;
        public int CpuPl2 { get; set; } = 140;
        public int TauSeconds { get; set; } = CpuPowerLimitConfiguration.FactoryTauSeconds;
        public int GpuPowerWatts { get; set; } = 130;
        public int GpuThermalTripCelsius { get; set; } = GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius;
        public bool Enabled { get; set; } = true;

        public string ProcessName
        {
            get
            {
                string name = ExeName.Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name = name[..^4];
                return name;
            }
        }

        public string ModeTitle => PowerMode switch
        {
            0x00 => "Quiet",
            0x01 => "Balanced",
            0x04 => "Performance",
            0x05 => "Turbo",
            0x06 => "Eco",
            _ => "Balanced"
        };
    }
}
