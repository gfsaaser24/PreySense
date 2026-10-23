using PreySense.Core.Audio;

namespace PreySense.Tests
{
    [TestClass]
    public class DtsAudioPresetTests
    {
        [TestMethod]
        public void ModeName_KeepsOutputWhenPresetChanges()
        {
            Assert.IsTrue(DtsAudioPreset.TryParseModeName(
                "ApoOpMode:APO4-Music/APO4-Internal Stereo",
                out DtsPreset preset,
                out string output));
            Assert.AreEqual(DtsPreset.Music, preset);
            Assert.AreEqual("Internal Stereo", output);
            Assert.AreEqual(
                "APO4-Movie/APO4-Internal Stereo",
                DtsAudioPreset.FormatModeFragment(DtsPreset.Movie, output));
        }

        [TestMethod]
        public void ModeName_HeadphoneStaysHeadphone()
        {
            Assert.IsTrue(DtsAudioPreset.TryParseModeName(
                "ApoOpMode:APO4-Music/APO4-Headphone",
                out DtsPreset preset,
                out string output));
            Assert.AreEqual(DtsPreset.Music, preset);
            Assert.AreEqual(
                "APO4-Game1/APO4-Headphone",
                DtsAudioPreset.FormatModeFragment(DtsPreset.Game1, output));
            Assert.AreEqual("Game - Immersive", DtsAudioPreset.Display(DtsPreset.Game1));
            Assert.AreEqual("Game - Competitive", DtsAudioPreset.Display(DtsPreset.Game2));
            Assert.AreEqual("Game - Entertainment", DtsAudioPreset.Display(DtsPreset.Game3));
            Assert.AreEqual("Custom", DtsAudioPreset.Display(DtsPreset.Custom));
        }

        [TestMethod]
        public void ModeName_CustomAndOffRoundTrip()
        {
            Assert.IsTrue(DtsAudioPreset.TryParseModeName(
                "ApoOpMode:APO4-Custom Audio/APO4-External Stereo",
                out DtsPreset custom,
                out string output));
            Assert.AreEqual(DtsPreset.Custom, custom);
            Assert.AreEqual("External Stereo", output);
            Assert.AreEqual(
                "APO4-Off/APO4-External Stereo",
                DtsAudioPreset.FormatModeFragment(DtsPreset.Off, output));
        }

        [TestMethod]
        public void Menu_AutoIsFirstAndFixedPresetsFollow()
        {
            Assert.AreEqual("Auto", DtsAudioPreset.Menu[0]);
            Assert.AreEqual(0, DtsAudioPreset.MenuIndex(DtsPreset.Music, automatic: true));
            Assert.AreEqual(1, DtsAudioPreset.MenuIndex(DtsPreset.Music, automatic: false));
            Assert.IsTrue(DtsAudioPreset.TryFromMenuIndex(0, out bool automatic, out _));
            Assert.IsTrue(automatic);
            int customIndex = DtsAudioPreset.MenuIndex(DtsPreset.Custom, automatic: false);
            Assert.IsTrue(DtsAudioPreset.TryFromMenuIndex(customIndex, out bool customAutomatic, out DtsPreset custom));
            Assert.IsFalse(customAutomatic);
            Assert.AreEqual(DtsPreset.Custom, custom);
            Assert.AreEqual("Game - Immersive", DtsAudioPreset.Menu[DtsAudioPreset.MenuIndex(DtsPreset.Game1, automatic: false)]);
        }

        [TestMethod]
        public void Equalizer_DecibelsUseTenthDbSteps()
        {
            Assert.AreEqual(DtsEqualizerSettings.BandCount, DtsEqualizerSettings.Labels.Length);
            Assert.AreEqual("32 Hz", DtsEqualizerSettings.Labels[0]);
            Assert.AreEqual("16 kHz", DtsEqualizerSettings.Labels[9]);
            Assert.AreEqual("MFX:Eagle-GEQ Band0 Gain", DtsEqualizerSettings.GainControl(0));
            Assert.AreEqual(100, DtsEqualizerSettings.ToCom(10));
            Assert.AreEqual(-100, DtsEqualizerSettings.ToCom(-10));
            Assert.AreEqual(10, DtsEqualizerSettings.FromCom(100));
            Assert.AreEqual(-2, DtsEqualizerSettings.FromCom(-15));
        }

        [TestMethod]
        public void Equalizer_ReadsCustomBandsWithoutChangingPreset()
        {
            string engine = Path.Combine(DtsAudioManager.ApoDirectory, "dtscnt64.dll");
            if (!File.Exists(engine))
                return;

            using DtsAudioManager manager = new();
            Assert.IsTrue(manager.TryGetEndpoints(out IReadOnlyList<DtsAudioEndpoint> endpoints, out string? error), error);
            DtsAudioEndpoint speakers = endpoints.First(endpoint => endpoint.Title == "Speakers");
            Assert.IsTrue(manager.TryGetEqualizer(speakers.Id, out DtsEqualizerSettings? equalizer, out error), error);
            Assert.IsNotNull(equalizer);
            Assert.AreEqual(DtsEqualizerSettings.BandCount, equalizer!.ComGains.Count);
            Assert.AreEqual(speakers.Preset == DtsPreset.Custom, equalizer.IsCustomActive);

            if (speakers.Preset != DtsPreset.Custom)
            {
                Assert.IsFalse(manager.TrySetEqualizerGain(speakers.Id, 0, equalizer.ComGains[0], out _));
                return;
            }

            if (equalizer.Enabled)
                return;

            int original = equalizer.ComGains[0];
            int next = original == 0 ? DtsEqualizerSettings.ToCom(1) : 0;
            try
            {
                Assert.IsTrue(manager.TrySetEqualizerGain(speakers.Id, 0, next, out error), error);
                Assert.IsTrue(manager.TryGetEqualizer(speakers.Id, out DtsEqualizerSettings? updated, out error), error);
                Assert.AreEqual(next, updated!.ComGains[0]);
                for (int band = 1; band < updated.ComGains.Count; band++)
                    Assert.AreEqual(equalizer.ComGains[band], updated.ComGains[band]);
                Assert.IsFalse(updated.Enabled);
                Assert.IsTrue(manager.TryGetEndpoints(out IReadOnlyList<DtsAudioEndpoint> after, out error), error);
                Assert.AreEqual(speakers.Preset, after.First(endpoint => endpoint.Id == speakers.Id).Preset);
            }
            finally
            {
                Assert.IsTrue(manager.TrySetEqualizerGain(speakers.Id, 0, original, out string? restoreError), restoreError);
            }
        }

        [TestMethod]
        public void Manager_ListsEndpointsWhenDtsIsInstalled()
        {
            string engine = Path.Combine(DtsAudioManager.ApoDirectory, "dtscnt64.dll");
            if (!File.Exists(engine))
                return;

            using DtsAudioManager manager = new();
            Assert.IsTrue(manager.TryGetEndpoints(out IReadOnlyList<DtsAudioEndpoint> endpoints, out string? error), error);
            Assert.IsTrue(endpoints.Any(endpoint => endpoint.Title == "Speakers"));
            Assert.IsTrue(endpoints.All(endpoint => endpoint.ProcessingEnabled));
            Assert.IsTrue(endpoints.All(endpoint => endpoint.OutputKind.Length > 0));
        }

        [TestMethod]
        public void Automatic_SetsFlagWithoutChangingPreset()
        {
            string engine = Path.Combine(DtsAudioManager.ApoDirectory, "dtscnt64.dll");
            if (!File.Exists(engine))
                return;

            using DtsAudioManager manager = new();
            Assert.IsTrue(manager.TryGetEndpoints(out IReadOnlyList<DtsAudioEndpoint> endpoints, out string? error), error);
            DtsAudioEndpoint? headphones = endpoints.FirstOrDefault(endpoint => endpoint.Title == "Headphones");
            if (headphones == null || headphones.Automatic || headphones.Preset == DtsPreset.Off)
                return;

            DtsPreset preset = headphones.Preset;
            string output = headphones.OutputKind;
            try
            {
                Assert.IsTrue(manager.TrySetAutomatic(headphones.Id, out error), error);
                Assert.IsTrue(manager.TryGetEndpoints(out IReadOnlyList<DtsAudioEndpoint> after, out error), error);
                DtsAudioEndpoint updated = after.First(endpoint => endpoint.Id == headphones.Id);
                Assert.IsTrue(updated.Automatic);
                Assert.AreEqual(preset, updated.Preset);
                Assert.AreEqual(output, updated.OutputKind);
                foreach (DtsAudioEndpoint other in after.Where(endpoint => endpoint.Id != headphones.Id))
                {
                    DtsAudioEndpoint before = endpoints.First(endpoint => endpoint.Id == other.Id);
                    Assert.AreEqual(before.Preset, other.Preset);
                    Assert.AreEqual(before.Automatic, other.Automatic);
                }
            }
            finally
            {
                Assert.IsTrue(manager.TrySetPreset(headphones.Id, preset, out string? restoreError), restoreError);
            }

            Assert.IsTrue(manager.TryGetEndpoints(out IReadOnlyList<DtsAudioEndpoint> restored, out error), error);
            DtsAudioEndpoint done = restored.First(endpoint => endpoint.Id == headphones.Id);
            Assert.IsFalse(done.Automatic);
            Assert.AreEqual(preset, done.Preset);
        }

        [TestMethod]
        public void CustomSettings_RoundTripLevelWithoutChangingPreset()
        {
            string engine = Path.Combine(DtsAudioManager.ApoDirectory, "dtscnt64.dll");
            if (!File.Exists(engine))
                return;

            using DtsAudioManager manager = new();
            Assert.IsTrue(manager.TryGetEndpoints(out IReadOnlyList<DtsAudioEndpoint> endpoints, out string? error), error);
            Dictionary<string, DtsPreset> presets = endpoints.ToDictionary(endpoint => endpoint.Id, endpoint => endpoint.Preset);
            DtsAudioEndpoint speakers = endpoints.First(endpoint => endpoint.Title == "Speakers");
            Assert.IsTrue(manager.TryGetCustomSettings(speakers.Id, out DtsCustomAudioSettings? speakersCustom, out error), error);
            Assert.IsNotNull(speakersCustom);

            DtsAudioEndpoint? other = endpoints.FirstOrDefault(endpoint => endpoint.Id != speakers.Id);
            DtsCustomAudioSettings? otherCustom = null;
            if (other != null)
            {
                Assert.IsTrue(manager.TryGetCustomSettings(other.Id, out otherCustom, out error), error);
                if (other.Preset != DtsPreset.Custom)
                {
                    int probe = otherCustom!.TrebleLevel == 0 ? 1 : 0;
                    Assert.IsFalse(manager.TrySetTrebleLevel(other.Id, probe, out _));
                }
            }

            AssertSamePresets(manager, presets);

            if (speakers.Preset != DtsPreset.Custom || speakersCustom!.TrebleEnabled)
                return;

            int original = speakersCustom.TrebleLevel;
            int next = original >= 100 ? original - 1 : original + 1;
            try
            {
                Assert.IsTrue(manager.TrySetTrebleLevel(speakers.Id, next, out error), error);
                Assert.IsTrue(manager.TryGetCustomSettings(speakers.Id, out DtsCustomAudioSettings? updated, out error), error);
                Assert.AreEqual(next, updated!.TrebleLevel);
                Assert.AreEqual(speakersCustom.Stereo, updated.Stereo);
                Assert.AreEqual(speakersCustom.BassLevel, updated.BassLevel);
                Assert.IsFalse(updated.TrebleEnabled);
            }
            finally
            {
                Assert.IsTrue(manager.TrySetTrebleLevel(speakers.Id, original, out string? restoreError), restoreError);
            }

            Assert.IsTrue(manager.TryGetCustomSettings(speakers.Id, out DtsCustomAudioSettings? restored, out error), error);
            Assert.AreEqual(original, restored!.TrebleLevel);
            if (other != null && otherCustom != null)
            {
                Assert.IsTrue(manager.TryGetCustomSettings(other.Id, out DtsCustomAudioSettings? otherAfter, out error), error);
                Assert.AreEqual(otherCustom.TrebleLevel, otherAfter!.TrebleLevel);
                Assert.AreEqual(otherCustom.BassEnabled, otherAfter.BassEnabled);
                Assert.AreEqual(otherCustom.Stereo, otherAfter.Stereo);
            }

            AssertSamePresets(manager, presets);
        }

        private static void AssertSamePresets(DtsAudioManager manager, Dictionary<string, DtsPreset> presets)
        {
            Assert.IsTrue(manager.TryGetEndpoints(out IReadOnlyList<DtsAudioEndpoint> endpoints, out string? error), error);
            foreach (DtsAudioEndpoint endpoint in endpoints)
                Assert.AreEqual(presets[endpoint.Id], endpoint.Preset);
        }
    }
}
