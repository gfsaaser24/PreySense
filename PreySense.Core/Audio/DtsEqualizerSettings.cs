namespace PreySense.Core.Audio
{
    /// <summary>
    /// Graphic EQ on the Custom Audio operating mode.
    /// Band labels and the ±10 dB slider come from the DTS:X Ultra app resources.
    /// The COM gain is 0.1 dB per step. The control itself allows −15.0 to +15.0 dB;
    /// the app slider stops at ±10 dB.
    /// </summary>
    public sealed class DtsEqualizerSettings
    {
        public const int BandCount = 10;
        public const int MinimumDecibels = -10;
        public const int MaximumDecibels = 10;
        public const string ControlEnable = "MFX:Eagle-GEQ Enable";

        public static readonly string[] Labels =
        {
            "32 Hz",
            "64 Hz",
            "125 Hz",
            "250 Hz",
            "500 Hz",
            "1 kHz",
            "2 kHz",
            "4 kHz",
            "8 kHz",
            "16 kHz"
        };

        public DtsEqualizerSettings(bool isCustomActive, bool enabled, IReadOnlyList<int> comGains)
        {
            if (comGains.Count != BandCount)
                throw new ArgumentException("DTS equalizer has 10 bands.", nameof(comGains));

            IsCustomActive = isCustomActive;
            Enabled = enabled;
            ComGains = comGains.ToArray();
        }

        public bool IsCustomActive { get; }

        public bool Enabled { get; }

        /// <summary>Raw <c>MFX:Eagle-GEQ BandN Gain</c> values. One unit is 0.1 dB.</summary>
        public IReadOnlyList<int> ComGains { get; }

        public static string GainControl(int band) => $"MFX:Eagle-GEQ Band{band} Gain";

        public static int ToCom(int decibels) => decibels * 10;

        public static int FromCom(int comGain) => (int)Math.Round(comGain / 10.0, MidpointRounding.AwayFromZero);
    }
}
