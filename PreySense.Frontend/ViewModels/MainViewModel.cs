using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Core.Acpi;
using PreySense.Core.Services;
using PreySense;
using PreySense.Frontend.Services;
using PreySense.Gpu;
using PreySense.Helpers;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private const double VisibleTelemetryIntervalMs = 1000;
        private readonly IHardwareService _hw;
        private readonly System.Timers.Timer _telemetryTimer;
        private readonly Dispatcher _dispatcher;
        private readonly TurboKeyWatcher _turboKeyWatcher;
        private readonly HidModeKeyWatcher _hidModeKeyWatcher;
        private DateTime _lastPhysicalModeKeyUtc;
        private int _pendingBrightnessDelta;
        private int _telemetryPollInProgress;
        private bool _telemetryPollingActive;
        private bool _disposed;
        private int? _gpuModeDeferredForBattery;

        public IHardwareService HardwareService => _hw;

        // Telemetry Properties
        [ObservableProperty] private int _cpuTemp = 45;
        [ObservableProperty] private int _gpuTemp = 40;
        [ObservableProperty] private int _cpuRpm = 2200;
        [ObservableProperty] private int _gpuRpm = 2000;
        [ObservableProperty] private float _batteryPercent = 85;
        [ObservableProperty] private bool _isAcConnected = true;
        [ObservableProperty] private string _batteryStatusText = "85% (Plugged in)";
        [ObservableProperty] private float _cpuPackagePowerWatts = 0f;

        // Status Feedback
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private bool _isBusy = false;

        private bool? _lastAcState = null;

        public MainViewModel(IHardwareService hw)
        {
            _hw = hw;
            _isLightTheme = ThemeManager.LoadAndApply();
            AttachSessionLifecycleHooks();

            _dispatcher = Dispatcher.CurrentDispatcher;
            _hw.PowerModeChanged += OnPowerModeChanged;
            _turboKeyWatcher = new TurboKeyWatcher();
            _turboKeyWatcher.TurboKeyPressed += OnTurboKeyPressed;
            _turboKeyWatcher.Start();
            _hidModeKeyWatcher = new HidModeKeyWatcher(HandleModeKeyPressed, StepKeyboardBrightness);
            _turboKeyWatcher.BrightnessKeyPressed += OnBrightnessKeyPressed;
            StartGameLaunchWatcher();

            // 1-second telemetry only while a PreySense window is visible.
            _telemetryTimer = new System.Timers.Timer(VisibleTelemetryIntervalMs) { AutoReset = true };
            _telemetryTimer.Elapsed += OnTelemetryTimerElapsed;
            SetTelemetryPollingActive(true);

            try
            {
                IsRunOnStartupEnabled = StartupTaskService.IsStartupEnabled();
            }
            catch { }

            try
            {
                using var advKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense\AdvancedSettings");
                IsLcdOverdriveEnabled = advKey?.GetValue("LcdOverdrive") is int od ? od == 1 : false;
                if (advKey?.GetValue("BlueLightShield") is int bl)
                {
                    BlueLightShieldMode = Math.Clamp(bl, 0, 2);
                }
                if (advKey?.GetValue("AcpiOverrideEnabled") is int ov)
                {
                    IsAcpiOverrideEnabled = ov == 1;
                }
                object? gpuBattery = advKey?.GetValue("GpuBatteryAuto");
                if (gpuBattery is null)
                {
                    using var rootKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense");
                    gpuBattery = rootKey?.GetValue("GpuBatteryAuto");
                }
                if (gpuBattery is int gpuBatteryValue)
                    IsGpuBatteryAutoEnabled = gpuBatteryValue == 1;
                if (advKey?.GetValue("CpuBoost") is int boost)
                {
                    CpuBoostMode = Math.Clamp(boost, 0, 6);
                    CpuBoostModeName = GetCpuBoostModeName(CpuBoostMode);
                }

                bool? savedServicesDebloated = AcerServiceDebloater.GetRegistryState();
                if (savedServicesDebloated.HasValue)
                {
                    IsAcerServicesDebloated = savedServicesDebloated.Value;
                }
                else
                {
                    IsAcerServicesDebloated = AcerServiceDebloater.AreAllServicesDisabled();
                }
                DebloaterStatusText = IsAcerServicesDebloated
                    ? "Acer OEM Services: Stopped & Disabled (~400MB Saved)"
                    : "Acer OEM Services: Active";
            }
            catch
            {
                IsLcdOverdriveEnabled = false;
                IsAcpiOverrideEnabled = false;
            }

            ActiveGpuMuxMode = LoadGpuModePreference();
            try
            {
                if (_hw.IsDdsAvailable && _hw.IsDdsSupported())
                {
                    var ddsStatus = _hw.GetDdsStatus();
                    if (ddsStatus != null && ddsStatus.IsSupported)
                    {
                        int liveDdsMode = ddsStatus.IsAutomatic
                            ? 1
                            : (ddsStatus.ActiveMux == Gpu.DdsActiveMux.dGPU ? 2 : 0);
                        ActiveGpuMuxMode = liveDdsMode;
                        SaveGpuModePreference(liveDdsMode);
                    }
                }
            }
            catch { }
            GpuMuxModeName = GetGpuModeName(ActiveGpuMuxMode);
            try { IsBiosDiscreteGpuActive = _hw.IsBiosDiscreteGpuActive; } catch { }
            InitializeRefreshRates();
            DisplayPanelInfo = $"{CurrentRefreshRate}Hz" + (IsLcdOverdriveEnabled ? " • Overdrive" : "");

            LoadCoverLightingSettings();
            LoadRgbSettings();
            LoadCpuLimitsFromRegistry();
            LoadCustomFanSpeeds();
            LoadSessionPreferences();
            UpdateModeName(ActivePowerMode);

            NotifyModeSelectionsChanged();
            NotifyGpuSelectionsChanged();
            NotifyFanSelectionsChanged();
            NotifyRefreshRateChanged();
            PersistSessionSettings();

            int startupPl1 = ActivePowerMode switch
            {
                0x05 => CustomTurboPl1,
                0x04 => CustomPerfPl1,
                0x01 => CustomBalancedPl1,
                _ => CustomQuietPl1
            };
            int startupPl2 = ActivePowerMode switch
            {
                0x05 => CustomTurboPl2,
                0x04 => CustomPerfPl2,
                0x01 => CustomBalancedPl2,
                _ => CustomQuietPl2
            };
            Pl1Watts = startupPl1;
            Pl2Watts = startupPl2;

            _ = InitializeSessionAsync();
        }

        private void PollTelemetry()
        {
            if (_disposed || !_telemetryPollingActive || Interlocked.Exchange(ref _telemetryPollInProgress, 1) != 0)
                return;

            try
            {
                var telemetry = _hw.GetCurrentTelemetry();
                App.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    if (!_disposed)
                        UpdateTelemetry(telemetry);
                });
            }
            finally
            {
                Volatile.Write(ref _telemetryPollInProgress, 0);
            }
        }

        private void UpdateTelemetry(HardwareTelemetry t)
        {
            CpuTemp = t.CpuTemp;
            GpuTemp = t.GpuTemp;
            CpuRpm = t.CpuRpm;
            GpuRpm = t.GpuRpm;
            CpuPackagePowerWatts = t.CpuPkgPowerWatts;
            BatteryPercent = t.BatteryPercent;
            IsAcConnected = t.IsAcConnected;
            var batteryStatus = $"{Math.Round(t.BatteryPercent)}% ({(t.IsAcConnected ? "AC Attached" : "Discharging")})";
            if (!string.Equals(BatteryStatusText, batteryStatus, StringComparison.Ordinal))
                BatteryStatusText = batteryStatus;

            if (_lastAcState.HasValue && _lastAcState.Value != t.IsAcConnected)
            {
                NotifyModeSelectionsChanged();
                if (IsAutoRefreshRateEnabled)
                {
                    int maxHz = AvailableRefreshRates
                        .Select(r => int.TryParse(r.Replace("Hz", "").Trim(), out int val) ? val : 0)
                        .DefaultIfEmpty(240)
                        .Max();
                    int targetHz = t.IsAcConnected ? maxHz : 60;
                    _ = SetRefreshRateAsync(targetHz);
                }

                ApplyGpuBatteryAuto(t.IsAcConnected);
            }
            else if (!_lastAcState.HasValue)
            {
                ApplyGpuBatteryAuto(t.IsAcConnected);
            }
            _lastAcState = t.IsAcConnected;
        }

        public void SetTelemetryPollingActive(bool isVisible)
        {
            if (_disposed)
                return;

            _telemetryPollingActive = isVisible;
            if (isVisible)
            {
                _telemetryTimer.Interval = VisibleTelemetryIntervalMs;
                if (!_telemetryTimer.Enabled)
                    _telemetryTimer.Start();
            }
            else
            {
                _telemetryTimer.Stop();
            }
        }

        private void OnTelemetryTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e) => PollTelemetry();

        private void OnPowerModeChanged(object? sender, byte mode)
        {
            App.Current?.Dispatcher?.InvokeAsync(() =>
            {
                if (_disposed) return;
                // PreySense owns the chassis Mode / Turbo key. Ignore OEM firmware
                // echoes while a local cycle is pending or just queued.
                if (IsModeSelectionPending()
                    || (DateTime.UtcNow - _lastPhysicalModeKeyUtc).TotalMilliseconds < 1500
                    || ActivePowerMode == mode)
                    return;
                ActivePowerMode = mode;
                UpdateModeName(mode);
                NotifyModeSelectionsChanged();
                PersistSessionSettings();
                Views.ModeToastWindow.ShowConfirmedMode(mode, GetModeTitle(mode));
            });
        }

        private void OnTurboKeyPressed(object? sender, HotKeyEventArgs e) => HandleModeKeyPressed();

        private void OnBrightnessKeyPressed(object? sender, int delta) => StepKeyboardBrightness(delta);

        public void HandleModeKeyPressed()
        {
            _dispatcher.InvokeAsync(async () =>
            {
                if (_disposed) return;
                if ((DateTime.UtcNow - _lastPhysicalModeKeyUtc).TotalMilliseconds < 80)
                    return;
                _lastPhysicalModeKeyUtc = DateTime.UtcNow;
                byte current = PeekPendingOrActiveMode();
                byte next = CyclePerformanceMode(current);
                AppLogger.Log($"TurboKey: queued mode 0x{next:X2} (was 0x{current:X2}); hardware apply debounced.");
                await SetModeAsync(next);
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _telemetryTimer.Stop();
            _telemetryTimer.Elapsed -= OnTelemetryTimerElapsed;
            _telemetryTimer.Dispose();
            PersistSessionSettings();
            DetachSessionLifecycleHooks();
            _hw.PowerModeChanged -= OnPowerModeChanged;
            _turboKeyWatcher.TurboKeyPressed -= OnTurboKeyPressed;
            _turboKeyWatcher.BrightnessKeyPressed -= OnBrightnessKeyPressed;
            _turboKeyWatcher.Dispose();
            _hidModeKeyWatcher.Dispose();
            _gameLaunchWatcher?.Dispose();
            _gameLaunchWatcher = null;
            lock (_modeSwitchDebounceLock)
            {
                _modeSwitchDebounceCts?.Cancel();
                _modeSwitchDebounceCts?.Dispose();
                _modeSwitchDebounceCts = null;
            }
            _modeSwitchGate.Dispose();
            _usbChargingDebouncer.Dispose();
            _customFanDebouncer.Dispose();
            _lightingDebouncer.Dispose();
            if (_coverSyncFrameHooked && _hw is ProductionHardwareService hooked)
            {
                hooked.LightingEngine.FrameUpdated -= OnLightingEngineFrameUpdated;
                _coverSyncFrameHooked = false;
            }
            if (_hw is IDisposable disposableHardware)
                disposableHardware.Dispose();
            GC.SuppressFinalize(this);
        }

        private string GetModeTitle(byte mode) => mode switch
        {
            0x00 => "Quiet",
            0x01 => "Balanced",
            0x04 => "Performance",
            0x05 => "Turbo",
            0x06 => "Eco",
            _ => "Custom"
        };
    }
}
