namespace PreySense.Core.Audio
{
    /// <summary>
    /// Stereo choices on the DTS:X Ultra Custom Audio page.
    /// Values are the <c>Eagle-Stereo Mode</c> enum, read from the control.
    /// </summary>
    public enum DtsStereoMode
    {
        Front = 1,
        Wide = 2,
        Traditional = 3
    }

    /// <summary>
    /// Saved Custom Audio profile for one endpoint.
    /// Control names were read from <c>dtscnt64.dll</c> and matched to the DTS app
    /// (Front / Wide / Traditional, Treble Enhance, Bass Boost, Dialogue Clarity, Volume Smoothing).
    /// </summary>
    public sealed class DtsCustomAudioSettings
    {
        public const string ControlStereo = "Eagle-Stereo Mode";
        public const string ControlTrebleEnable = "MFX:Eagle-Treble Enable";
        public const string ControlTrebleLevel = "MFX:Eagle-Treble Level";
        public const string ControlBassEnable = "MFX:Eagle-TBHDX Enable";
        public const string ControlBassLevel = "MFX:Eagle-TBHDX Bass Level";
        public const string ControlDialogEnable = "SFX:Eagle-DE Enable";
        public const string ControlDialogLevel = "SFX:Eagle-DE Level";
        public const string ControlLoudnessEnable = "SFX:Eagle-LC Enable";

        public DtsCustomAudioSettings(
            bool isCustomActive,
            DtsStereoMode? stereo,
            bool trebleEnabled,
            int trebleLevel,
            bool bassEnabled,
            int bassLevel,
            bool dialogEnabled,
            int dialogLevel,
            bool volumeSmoothing)
        {
            IsCustomActive = isCustomActive;
            Stereo = stereo;
            TrebleEnabled = trebleEnabled;
            TrebleLevel = trebleLevel;
            BassEnabled = bassEnabled;
            BassLevel = bassLevel;
            DialogEnabled = dialogEnabled;
            DialogLevel = dialogLevel;
            VolumeSmoothing = volumeSmoothing;
        }

        /// <summary>True when this endpoint is currently playing the Custom preset.</summary>
        public bool IsCustomActive { get; }

        /// <summary>Null when DTS reports a stereo value the Custom page does not offer.</summary>
        public DtsStereoMode? Stereo { get; }

        public bool TrebleEnabled { get; }
        public int TrebleLevel { get; }
        public bool BassEnabled { get; }
        public int BassLevel { get; }
        public bool DialogEnabled { get; }
        public int DialogLevel { get; }
        public bool VolumeSmoothing { get; }
    }
}
