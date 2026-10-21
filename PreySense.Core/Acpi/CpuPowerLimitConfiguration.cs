using System;

namespace PreySense.Core.Acpi
{
    public class CpuPowerLimitConfiguration
    {
        private const double RaplTimeUnitSeconds = 1.0 / 1024.0;
        public const int InfiniteTauSliderValue = 449;
        public const byte InfiniteTauEncodedValue = 0x7F;

        public static byte EncodeTauSelection(int sliderValue) =>
            sliderValue >= InfiniteTauSliderValue ? InfiniteTauEncodedValue : EncodeTauSeconds(sliderValue);

        public static string FormatTauSelection(int sliderValue) =>
            sliderValue >= InfiniteTauSliderValue ? "∞" : $"{sliderValue} s";

        public static byte EncodeTauSeconds(int seconds)
        {
            if (seconds <= 0)
                return 0;

            double target = Math.Clamp(seconds, 1, 65535);
            byte best = 0;
            double bestError = double.MaxValue;
            for (int fraction = 0; fraction < 4; fraction++)
            {
                for (int exponent = 0; exponent < 32; exponent++)
                {
                    double candidate = Math.Pow(2, exponent) * (1.0 + fraction / 4.0) * RaplTimeUnitSeconds;
                    double error = Math.Abs(candidate - target);
                    if (error < bestError)
                    {
                        bestError = error;
                        best = (byte)((fraction << 5) | exponent);
                    }
                }
            }
            return best;
        }

        public static int DecodeTauSeconds(byte encoded)
        {
            int exponent = encoded & 0x1F;
            int fraction = (encoded >> 5) & 0x03;
            return (int)Math.Round(Math.Pow(2, exponent) * (1.0 + fraction / 4.0) * RaplTimeUnitSeconds);
        }
        // Factory AC Hardware Constants from PHN16S-71 ERM4 EC Table (0xFE8004A0)
        // Quiet:    0x2D (45W)  / 0x8C (140W)
        // Balanced: 0x41 (65W)  / 0x8C (140W)
        // Perf:     0x4B (75W)  / 0x8C (140W)
        // Turbo:    0x55 (85W)  / 0x8C (140W)
        // Tau:      0x38 (56s)
        public const int FactoryQuietPl1 = 45;
        public const int FactoryQuietPl2 = 140;

        public const int FactoryBalancedPl1 = 65;
        public const int FactoryBalancedPl2 = 140;

        public const int FactoryPerformancePl1 = 75;
        public const int FactoryPerformancePl2 = 140;

        public const int FactoryTurboPl1 = 85;
        public const int FactoryTurboPl2 = 140;

        public const int FactoryTauSeconds = 56;
        public const int MaxPowerLimitWatts = 999;

        // Custom Target Limits (Watts)
        public int QuietPl1 { get; set; } = FactoryQuietPl1;
        public int QuietPl2 { get; set; } = FactoryQuietPl2;

        public int BalancedPl1 { get; set; } = FactoryBalancedPl1;
        public int BalancedPl2 { get; set; } = FactoryBalancedPl2;

        public int PerformancePl1 { get; set; } = FactoryPerformancePl1;
        public int PerformancePl2 { get; set; } = FactoryPerformancePl2;

        public int TurboPl1 { get; set; } = FactoryTurboPl1;
        public int TurboPl2 { get; set; } = FactoryTurboPl2;

        public int TauSeconds { get; set; } = FactoryTauSeconds;

        /// <summary>
        /// Validates values against Intel Raptor Lake / Arrow Lake mobile thermal specifications
        /// and Acer PHN16S-71 chassis limits (up to 999W max, PL1 <= PL2).
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            if (QuietPl1 < 10 || QuietPl1 > MaxPowerLimitWatts || QuietPl2 < QuietPl1 || QuietPl2 > MaxPowerLimitWatts)
            {
                errorMessage = $"Quiet limits out of range (PL1: 10-{MaxPowerLimitWatts}W, PL2: PL1-{MaxPowerLimitWatts}W).";
                return false;
            }
            if (BalancedPl1 < 10 || BalancedPl1 > MaxPowerLimitWatts || BalancedPl2 < BalancedPl1 || BalancedPl2 > MaxPowerLimitWatts)
            {
                errorMessage = $"Balanced limits out of range (PL1: 10-{MaxPowerLimitWatts}W, PL2: PL1-{MaxPowerLimitWatts}W).";
                return false;
            }
            if (PerformancePl1 < 10 || PerformancePl1 > MaxPowerLimitWatts || PerformancePl2 < PerformancePl1 || PerformancePl2 > MaxPowerLimitWatts)
            {
                errorMessage = $"Performance limits out of range (PL1: 10-{MaxPowerLimitWatts}W, PL2: PL1-{MaxPowerLimitWatts}W).";
                return false;
            }
            if (TurboPl1 < 10 || TurboPl1 > MaxPowerLimitWatts || TurboPl2 < TurboPl1 || TurboPl2 > MaxPowerLimitWatts)
            {
                errorMessage = $"Turbo limits out of range (PL1: 10-{MaxPowerLimitWatts}W, PL2: PL1-{MaxPowerLimitWatts}W).";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public void ResetToDefaults()
        {
            QuietPl1 = FactoryQuietPl1;
            QuietPl2 = FactoryQuietPl2;
            BalancedPl1 = FactoryBalancedPl1;
            BalancedPl2 = FactoryBalancedPl2;
            PerformancePl1 = FactoryPerformancePl1;
            PerformancePl2 = FactoryPerformancePl2;
            TurboPl1 = FactoryTurboPl1;
            TurboPl2 = FactoryTurboPl2;
            TauSeconds = FactoryTauSeconds;
        }
    }
}
