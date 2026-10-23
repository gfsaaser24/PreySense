using System.Runtime.InteropServices;
using DtsCnt;

namespace PreySense.Core.Audio
{
    public sealed partial class DtsAudioManager
    {
        public bool TryGetCustomSettings(string endpointId, out DtsCustomAudioSettings? settings, out string? error)
        {
            settings = null;
            if (!EnsureSystem(out error))
                return false;

            try
            {
                if (!TryOpenCustom(endpointId, requireActive: false, out IDTSOperatingMode? custom, out bool active, out error))
                    return false;
                settings = ReadCustom(custom!, active);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogger.Error($"DTS custom read: {ex.Message}");
                return false;
            }
        }

        public bool TrySetStereo(string endpointId, DtsStereoMode mode, out string? error)
            => TryWrite(endpointId, DtsCustomAudioSettings.ControlStereo, (int)mode, out error);

        public bool TrySetTrebleEnabled(string endpointId, bool enabled, out string? error)
            => TryWrite(endpointId, DtsCustomAudioSettings.ControlTrebleEnable, enabled ? 1 : 0, out error);

        public bool TrySetTrebleLevel(string endpointId, int level, out string? error)
            => TryWrite(endpointId, DtsCustomAudioSettings.ControlTrebleLevel, level, out error);

        public bool TrySetBassEnabled(string endpointId, bool enabled, out string? error)
            => TryWrite(endpointId, DtsCustomAudioSettings.ControlBassEnable, enabled ? 1 : 0, out error);

        public bool TrySetBassLevel(string endpointId, int level, out string? error)
            => TryWrite(endpointId, DtsCustomAudioSettings.ControlBassLevel, level, out error);

        public bool TrySetDialogEnabled(string endpointId, bool enabled, out string? error)
            => TryWrite(endpointId, DtsCustomAudioSettings.ControlDialogEnable, enabled ? 1 : 0, out error);

        public bool TrySetDialogLevel(string endpointId, int level, out string? error)
            => TryWrite(endpointId, DtsCustomAudioSettings.ControlDialogLevel, level, out error);

        public bool TrySetVolumeSmoothing(string endpointId, bool enabled, out string? error)
            => TryWrite(endpointId, DtsCustomAudioSettings.ControlLoudnessEnable, enabled ? 1 : 0, out error);

        public bool TryGetEqualizer(string endpointId, out DtsEqualizerSettings? settings, out string? error)
        {
            settings = null;
            if (!EnsureSystem(out error))
                return false;

            try
            {
                if (!TryOpenCustom(endpointId, requireActive: false, out IDTSOperatingMode? custom, out bool active, out error))
                    return false;
                settings = ReadEqualizer(custom!, active);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogger.Error($"DTS equalizer read: {ex.Message}");
                return false;
            }
        }

        public bool TrySetEqualizerEnabled(string endpointId, bool enabled, out string? error)
            => TryWrite(endpointId, DtsEqualizerSettings.ControlEnable, enabled ? 1 : 0, out error);

        public bool TrySetEqualizerGain(string endpointId, int band, int comGain, out string? error)
        {
            if (band < 0 || band >= DtsEqualizerSettings.BandCount)
            {
                error = "That equalizer band is not available.";
                return false;
            }

            return TryWrite(endpointId, DtsEqualizerSettings.GainControl(band), comGain, out error);
        }

        /// <summary>
        /// Same sequence as the DTS app's per-device factory reset:
        /// restore every operating mode on this endpoint, then select Music
        /// without changing the output type.
        /// </summary>
        public bool TryRestoreFactory(string endpointId, out string? error)
        {
            if (!EnsureSystem(out error))
                return false;

            try
            {
                if (!RestoreModes(endpointId, out error))
                    return false;
                return TryApply(endpointId, DtsPreset.Music, out error);
            }
            catch (COMException ex) when (ex.HResult == NotInitialized)
            {
                _ready = false;
                if (!EnsureSystem(out error))
                    return false;
                if (!RestoreModes(endpointId, out error))
                    return false;
                return TryApply(endpointId, DtsPreset.Music, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogger.Error($"DTS factory reset: {ex.Message}");
                return false;
            }
        }

        private bool RestoreModes(string endpointId, out string? error)
        {
            error = null;
            IDTSEndpointEnumerator enumerator = (IDTSEndpointEnumerator)_system!.EnumerateDTSEndpoints();
            try
            {
                while (true)
                {
                    IDTSEndpointInfo? endpoint = NextEndpoint(enumerator);
                    if (endpoint == null)
                        break;
                    if (!string.Equals(endpoint.RegistryPath, endpointId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    IDTSAPOOpModeEnumerator modes = (IDTSAPOOpModeEnumerator)endpoint.GetOpModeEnumerator();
                    try
                    {
                        while (true)
                        {
                            IDTSOperatingMode? mode = NextMode(modes);
                            if (mode == null)
                                break;
                            mode.RestoreDefaults();
                        }
                    }
                    finally
                    {
                        Release(modes);
                    }

                    return true;
                }
            }
            finally
            {
                Release(enumerator);
            }

            error = "That audio device is no longer available.";
            return false;
        }

        private bool TryWrite(string endpointId, string controlName, int value, out string? error)
        {
            if (!EnsureSystem(out error))
                return false;

            try
            {
                if (!TryOpenCustom(endpointId, requireActive: true, out IDTSOperatingMode? custom, out _, out error))
                    return false;
                IDTSAPOControl control = Control(custom!, controlName);
                int min = control.MinValue;
                int max = control.MaxValue;
                if (max >= min && (value < min || value > max))
                {
                    error = "That value is outside the range DTS accepts.";
                    return false;
                }

                control.Value = value;
                if (control.Value != value)
                {
                    error = "DTS did not keep that setting.";
                    return false;
                }

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogger.Error($"DTS custom write: {ex.Message}");
                return false;
            }
        }

        private bool TryOpenCustom(
            string endpointId,
            bool requireActive,
            out IDTSOperatingMode? custom,
            out bool active,
            out string? error)
        {
            custom = null;
            active = false;
            error = null;
            IDTSEndpointEnumerator enumerator = (IDTSEndpointEnumerator)_system!.EnumerateDTSEndpoints();
            try
            {
                while (true)
                {
                    IDTSEndpointInfo? endpoint = NextEndpoint(enumerator);
                    if (endpoint == null)
                        break;
                    if (!string.Equals(endpoint.RegistryPath, endpointId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    IDTSOperatingMode current = (IDTSOperatingMode)endpoint.OpMode;
                    if (!DtsAudioPreset.TryParseModeName(current.Name, out DtsPreset preset, out string outputKind))
                    {
                        error = "DTS did not report a preset for this device.";
                        return false;
                    }

                    active = preset == DtsPreset.Custom;
                    if (requireActive && !active)
                    {
                        error = "Select Custom for this device before changing these settings.";
                        return false;
                    }

                    custom = FindMode(endpoint, DtsAudioPreset.FormatModeFragment(DtsPreset.Custom, outputKind));
                    if (custom == null)
                    {
                        error = "Custom Audio is not available for this device.";
                        return false;
                    }

                    return true;
                }
            }
            finally
            {
                Release(enumerator);
            }

            error = "That audio device is no longer available.";
            return false;
        }

        private static DtsCustomAudioSettings ReadCustom(IDTSOperatingMode custom, bool active)
        {
            int stereo = Control(custom, DtsCustomAudioSettings.ControlStereo).Value;
            DtsStereoMode? mode = stereo switch
            {
                (int)DtsStereoMode.Front => DtsStereoMode.Front,
                (int)DtsStereoMode.Wide => DtsStereoMode.Wide,
                (int)DtsStereoMode.Traditional => DtsStereoMode.Traditional,
                _ => null
            };
            return new DtsCustomAudioSettings(
                active,
                mode,
                Flag(custom, DtsCustomAudioSettings.ControlTrebleEnable),
                Control(custom, DtsCustomAudioSettings.ControlTrebleLevel).Value,
                Flag(custom, DtsCustomAudioSettings.ControlBassEnable),
                Control(custom, DtsCustomAudioSettings.ControlBassLevel).Value,
                Flag(custom, DtsCustomAudioSettings.ControlDialogEnable),
                Control(custom, DtsCustomAudioSettings.ControlDialogLevel).Value,
                Flag(custom, DtsCustomAudioSettings.ControlLoudnessEnable));
        }

        private static DtsEqualizerSettings ReadEqualizer(IDTSOperatingMode custom, bool active)
        {
            var gains = new int[DtsEqualizerSettings.BandCount];
            for (int band = 0; band < gains.Length; band++)
                gains[band] = Control(custom, DtsEqualizerSettings.GainControl(band)).Value;
            return new DtsEqualizerSettings(active, Flag(custom, DtsEqualizerSettings.ControlEnable), gains);
        }

        private static bool Flag(IDTSOperatingMode mode, string name) => Control(mode, name).Value != 0;

        private static IDTSAPOControl Control(IDTSOperatingMode mode, string name)
        {
            IDTSAPOControlEnumerator enumerator = mode.get_DTSAPOControlEnumerator(DTSAPOControlType.kDTSAPO_Cont_Public);
            try
            {
                IDTSAPOControlEnumerator2 named = (IDTSAPOControlEnumerator2)enumerator;
                return (IDTSAPOControl)named.GetControlFromName(name);
            }
            finally
            {
                Release(enumerator);
            }
        }
    }
}
