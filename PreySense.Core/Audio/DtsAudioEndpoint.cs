namespace PreySense.Core.Audio
{
    public sealed class DtsAudioEndpoint
    {
        public DtsAudioEndpoint(string id, string friendlyName, string outputKind, DtsPreset preset, bool processingEnabled, bool automatic)
        {
            Id = id;
            FriendlyName = friendlyName;
            OutputKind = outputKind;
            Preset = preset;
            ProcessingEnabled = processingEnabled;
            Automatic = automatic;
        }

        /// <summary>DTS registry path, for example <c>Software\DTS\APO\{guid}</c>.</summary>
        public string Id { get; }

        public string FriendlyName { get; }

        /// <summary>Internal Stereo, Headphone, or External Stereo.</summary>
        public string OutputKind { get; }

        public DtsPreset Preset { get; }

        public bool ProcessingEnabled { get; }

        /// <summary>True when DTS follows the Windows audio stream instead of staying on <see cref="Preset"/>.</summary>
        public bool Automatic { get; }

        public string Title
        {
            get
            {
                if (FriendlyName.Contains("Speaker", StringComparison.OrdinalIgnoreCase))
                    return "Speakers";
                if (FriendlyName.Contains("Headphone", StringComparison.OrdinalIgnoreCase))
                    return "Headphones";
                return FriendlyName;
            }
        }
    }
}
