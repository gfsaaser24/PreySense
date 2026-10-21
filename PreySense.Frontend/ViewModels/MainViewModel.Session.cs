using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using PreySense.Core.Acpi;
using PreySense.Frontend.Services;
using PreySense.Helpers;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        private const string SessionRegistryPath = @"SOFTWARE\PreySense\Session";
        private bool _sessionHooksAttached;
        private bool _isRestoringSession;
        private bool _hasSavedPowerMode;
        private DateTime _lastSessionRestoreUtc = DateTime.MinValue;

        private void AttachSessionLifecycleHooks()
        {
            if (_sessionHooksAttached)
                return;

            SystemEvents.PowerModeChanged += OnSystemPowerModeChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
            _sessionHooksAttached = true;
        }

        private void DetachSessionLifecycleHooks()
        {
            if (!_sessionHooksAttached)
                return;

            SystemEvents.PowerModeChanged -= OnSystemPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            _sessionHooksAttached = false;
        }

        private void LoadSessionPreferences()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SessionRegistryPath);
                if (key == null)
                    return;

                if (key.GetValue("PowerMode") is int savedMode)
                {
                    byte mode = (byte)Math.Clamp(savedMode, 0, 6);
                    if (mode is 0 or 1 or 4 or 5 or 6)
                    {
                        _hasSavedPowerMode = true;
                        ActivePowerMode = mode;
                        UpdateModeName(mode);
                    }
                }

                if (key.GetValue("FanMode") is int fanMode)
                    ActiveFanMode = Math.Clamp(fanMode, 0, 2);

                if (key.GetValue("AutoRefreshRate") is int autoHz)
                    IsAutoRefreshRateEnabled = autoHz == 1;

                if (key.GetValue("UsbChargingPercent") is int usb)
                    UsbChargingPercent = usb is 0 or 10 or 20 or 30 ? usb : UsbChargingPercent;

                if (key.GetValue("BatteryLimit") is int batteryLimit)
                    IsBatteryLimitEnabled = batteryLimit == 1;

                if (key.GetValue("AcerServicesDisabled") is int debloated)
                    IsAcerServicesDebloated = debloated == 1;
                else if (key.GetValue("AcerServicesEnabled") is int enabled)
                    IsAcerServicesDebloated = enabled == 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LoadSessionPreferences: {ex.Message}");
            }
        }

        public void PersistSessionSettings()
        {
            if (_disposed || _isRestoringSession)
                return;

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(SessionRegistryPath);
                if (key == null)
                    return;

                key.SetValue("PowerMode", (int)ActivePowerMode, RegistryValueKind.DWord);
                key.SetValue("FanMode", ActiveFanMode, RegistryValueKind.DWord);
                key.SetValue("AutoRefreshRate", IsAutoRefreshRateEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("UsbChargingPercent", UsbChargingPercent, RegistryValueKind.DWord);
                key.SetValue("BatteryLimit", IsBatteryLimitEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("AcerServicesDisabled", IsAcerServicesDebloated ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("AcerServicesEnabled", IsAcerServicesDebloated ? 0 : 1, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"PersistSessionSettings: {ex.Message}");
            }

            SaveRgbSettings();
            SaveCoverLightingSettings();
            SaveCustomFanSpeeds();
        }

        private async Task InitializeSessionAsync()
        {
            await RestoreSessionSettingsAsync("startup");
            if (_disposed)
                return;

            int batteryLimit = IsBatteryLimitEnabled ? 1 : 0;
            int usbLevel = UsbChargingPercent;
            int platformLimit = OpenCorePowerLimitManager.FactoryIntelPlatformLimitWatts;
            bool openCore = false;

            await Task.Run(() =>
            {
                try { batteryLimit = _hw.GetBatteryChargeLimit(); } catch { }
                try
                {
                    if (usbLevel == 0)
                        usbLevel = _hw.GetUsbChargingLevel();
                }
                catch { }
                try { platformLimit = OpenCorePowerLimitManager.DetectIntelPlatformPowerLimitWatts(); } catch { }
                try { openCore = OpenCoreSecurityValidator.DetectIsOpenCoreLoaded(); } catch { }
            });

            if (_disposed)
                return;

            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed)
                    return;

                IsBatteryLimitEnabled = batteryLimit == 1;
                if (UsbChargingPercent == 0 && usbLevel is 10 or 20 or 30)
                    UsbChargingPercent = usbLevel;
                IntelPlatformPowerLimitMinimumWatts = platformLimit;
                if (PcccCeilingWatts < platformLimit)
                    PcccCeilingWatts = platformLimit;
                ClampCpuPl1ToPlatformLimit();
                IsOpenCoreLoaded = openCore;
                AcpiOverrideStatus = OpenCoreSecurityValidator.GetAcpiOverrideLabel(openCore);
            });
        }

        public async Task RestoreSessionSettingsAsync(string reason)
        {
            if (_disposed)
                return;

            if ((DateTime.UtcNow - _lastSessionRestoreUtc).TotalSeconds < 3 &&
                !string.Equals(reason, "startup", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastSessionRestoreUtc = DateTime.UtcNow;
            _isRestoringSession = true;
            bool gateHeld = false;
            try
            {
                await _modeSwitchGate.WaitAsync();
                gateHeld = true;
                AppLogger.Log($"Restoring PreySense session settings ({reason}).");
                byte mode = ActivePowerMode;
                await Task.Run(() =>
                {
                    if (!_hasSavedPowerMode)
                    {
                        try
                        {
                            byte live = _hw.CurrentPowerMode;
                            if (live is 0 or 1 or 4 or 5 or 6)
                                mode = live;
                        }
                        catch { }
                    }

                    ApplyPersistedHardwareSettings(mode);
                });

                if (string.Equals(reason, "resume", StringComparison.OrdinalIgnoreCase) && OverlayBridge.ShouldRun)
                    _ = App.Current?.Dispatcher.InvokeAsync(OverlayBridge.StartIfNeeded);

                _ = App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed)
                        return;
                    if (!_hasSavedPowerMode)
                    {
                        ActivePowerMode = mode;
                        UpdateModeName(mode);
                    }
                    NotifyModeSelectionsChanged();
                    NotifyFanSelectionsChanged();
                    StatusMessage = reason.Equals("startup", StringComparison.OrdinalIgnoreCase)
                        ? "Saved settings applied."
                        : "Saved settings reapplied after wake.";
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"RestoreSessionSettingsAsync ({reason}): {ex.Message}");
            }
            finally
            {
                _isRestoringSession = false;
                if (gateHeld)
                {
                    try { _modeSwitchGate.Release(); }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        private void ApplyPersistedHardwareSettings(byte? modeOverride = null)
        {
            byte mode = modeOverride ?? ActivePowerMode;
            ApplyKeyboardLightingNow();
            ApplyCoverLighting(saveSettings: false);
            _hw.SetPowerMode(mode);

            int pl1 = mode switch
            {
                0x05 => CustomTurboPl1,
                0x04 => CustomPerfPl1,
                0x01 => CustomBalancedPl1,
                _ => CustomQuietPl1
            };
            int pl2 = mode switch
            {
                0x05 => CustomTurboPl2,
                0x04 => CustomPerfPl2,
                0x01 => CustomBalancedPl2,
                _ => CustomQuietPl2
            };
            int gpuWatts = mode switch
            {
                0x05 => CustomTurboGpuWatts,
                0x04 => CustomPerfGpuWatts,
                0x01 => CustomBalancedGpuWatts,
                _ => CustomQuietGpuWatts
            };
            int gpuTrip = mode switch
            {
                0x05 => CustomTurboGpuTripPoint,
                0x04 => CustomPerfGpuTripPoint,
                0x01 => CustomBalancedGpuTripPoint,
                _ => CustomQuietGpuTripPoint
            };

            int limitedPl1 = LimitPl1ToPlatform(pl1);
            PowerLimitController.SetCpuPowerLimits(limitedPl1, LimitPl2(limitedPl1, pl2), true, TauSeconds);
            PowerLimitController.SetCpuBoost(CpuBoostMode);
            PreySense.Core.Hardware.PreySenseWmiDeviceClient.SetGpuPowerLimit((uint)gpuWatts);
            PreySense.Core.Hardware.PreySenseWmiDeviceClient.SetGpuThermalTripPoint((uint)gpuTrip);

            if (ActiveFanMode == 2)
                _hw.SetFanSpeeds((byte)CustomCpuFanPercent, (byte)CustomGpuFanPercent);
            else
                _hw.SetFanMode((byte)ActiveFanMode, (byte)ActiveFanMode);

            if (IsLcdOverdriveEnabled)
                _hw.SetLcdOverdrive(true);
            _hw.SetBlueLightShield(BlueLightShieldMode);
            _hw.SetUsbChargingLevel(UsbChargingPercent);
            _hw.SetBatteryChargeLimit(IsBatteryLimitEnabled ? 1 : 0);
            _hw.SetKbdBacklightTimeoutSeconds(KbdTimeoutSeconds);

            if (IsAutoRefreshRateEnabled)
            {
                int maxHz = AvailableRefreshRates
                    .Select(r => int.TryParse(r.Replace("Hz", "", StringComparison.OrdinalIgnoreCase).Trim(), out int val) ? val : 0)
                    .DefaultIfEmpty(240)
                    .Max();
                int targetHz = IsAcConnected ? maxHz : 60;
                _hw.SetDisplayRefreshRate(targetHz);
            }
        }

        private void OnSystemPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume)
                return;

            _ = App.Current?.Dispatcher.InvokeAsync(async () =>
            {
                if (_disposed)
                    return;
                await RestoreSessionSettingsAsync("resume");
            });
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            PersistSessionSettings();
        }
    }
}
