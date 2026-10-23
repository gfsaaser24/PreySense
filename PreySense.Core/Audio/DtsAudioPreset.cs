namespace PreySense.Core.Audio
{
    public enum DtsPreset
    {
        Music = 0,
        Movie = 1,
        Voice = 2,
        Game1 = 3,
        Game2 = 4,
        Game3 = 5,
        Custom = 6,
        Off = 7
    }

    public static class DtsAudioPreset
    {
        public static readonly string[] Displays =
        {
            "Music",
            "Movie",
            "Voice",
            "Game - Immersive",
            "Game - Competitive",
            "Game - Entertainment",
            "Custom",
            "Off"
        };

        public const string AutoDisplay = "Auto";

        public static readonly string[] Menu = new[] { AutoDisplay }.Concat(Displays).ToArray();

        public static string Display(DtsPreset preset) => Displays[(int)preset];

        /// <summary>
        /// Auto is the first menu row. It is a flag on the current mode, not its own mode name.
        /// </summary>
        public static int MenuIndex(DtsPreset preset, bool automatic) => automatic ? 0 : (int)preset + 1;

        public static bool TryFromMenuIndex(int index, out bool automatic, out DtsPreset preset)
        {
            if (index == 0)
            {
                automatic = true;
                preset = DtsPreset.Music;
                return true;
            }

            int presetIndex = index - 1;
            if (presetIndex < 0 || presetIndex >= Displays.Length)
            {
                automatic = false;
                preset = default;
                return false;
            }

            automatic = false;
            preset = (DtsPreset)presetIndex;
            return true;
        }

        public static string Token(DtsPreset preset) => preset switch
        {
            DtsPreset.Game1 => "Game1",
            DtsPreset.Game2 => "Game2",
            DtsPreset.Game3 => "Game3",
            DtsPreset.Custom => "Custom Audio",
            DtsPreset.Off => "Off",
            DtsPreset.Movie => "Movie",
            DtsPreset.Voice => "Voice",
            _ => "Music"
        };

        public static bool TryFromToken(string token, out DtsPreset preset)
        {
            switch (token)
            {
                case "Music": preset = DtsPreset.Music; return true;
                case "Movie": preset = DtsPreset.Movie; return true;
                case "Voice": preset = DtsPreset.Voice; return true;
                case "Game1": preset = DtsPreset.Game1; return true;
                case "Game2": preset = DtsPreset.Game2; return true;
                case "Game3": preset = DtsPreset.Game3; return true;
                case "Custom Audio": preset = DtsPreset.Custom; return true;
                case "Off": preset = DtsPreset.Off; return true;
                default:
                    preset = default;
                    return false;
            }
        }

        /// <summary>
        /// Mode names look like <c>ApoOpMode:APO4-Music/APO4-Internal Stereo</c>.
        /// The part after the slash is the output and must be kept when the preset changes.
        /// </summary>
        public static bool TryParseModeName(string? modeName, out DtsPreset preset, out string outputKind)
        {
            preset = default;
            outputKind = "";
            if (string.IsNullOrWhiteSpace(modeName))
                return false;

            int slash = modeName.LastIndexOf('/');
            if (slash < 0 || slash == modeName.Length - 1)
                return false;

            string output = modeName[(slash + 1)..];
            const string outputPrefix = "APO4-";
            if (!output.StartsWith(outputPrefix, StringComparison.Ordinal))
                return false;
            outputKind = output[outputPrefix.Length..];

            string left = modeName[..slash];
            int presetMarker = left.LastIndexOf("APO4-", StringComparison.Ordinal);
            if (presetMarker < 0)
                return false;
            string token = left[(presetMarker + outputPrefix.Length)..];
            return TryFromToken(token, out preset) && outputKind.Length > 0;
        }

        public static string FormatModeFragment(DtsPreset preset, string outputKind)
            => $"APO4-{Token(preset)}/APO4-{outputKind}";
    }
}
