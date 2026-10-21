using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Acpi;
using PreySense.Frontend.Views;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        private const int ModeSwitchDebounceMilliseconds = 1000;
        private readonly object _modeSwitchDebounceLock = new();
        private readonly SemaphoreSlim _modeSwitchGate = new(1, 1);
        private CancellationTokenSource? _modeSwitchDebounceCts;
        private byte _pendingHardwareMode = 0xFF;

        // Power Profiles (0=Quiet, 1=Balanced, 4=Performance, 5=Turbo, 6=Eco)
        [ObservableProperty] private byte _activePowerMode = 0x01; // Balanced
        [ObservableProperty] private string _powerModeName = "Balanced";
        [ObservableProperty] private string _activeModeWattageInfo = "PL1: 55W | PL2: 115W (Factory EC)";

        public bool IsEcoActive => ActivePowerMode == 6;
        public bool IsQuietActive => ActivePowerMode == 0;
        public bool IsBalancedActive => ActivePowerMode == 1;
        public bool IsPerformActive => ActivePowerMode == 4;
        public bool IsTurboActive => ActivePowerMode == 5;
        public bool IsEcoOrQuietActive => IsAcConnected ? IsQuietActive : (IsEcoActive || IsQuietActive);
        public string EcoOrQuietModeName => IsAcConnected ? "Quiet" : "Eco";
        public string EcoOrQuietModeParam => IsAcConnected ? "0" : "6";

        // CPU Boost Mode (0=Disabled, 1=Enabled)
        [ObservableProperty] private int _cpuBoostMode = 1; // Enabled default
        [ObservableProperty] private string _cpuBoostModeName = "Enabled";

        [RelayCommand(AllowConcurrentExecutions = true)]
        public async Task SetModeAsync(object? modeParam)
        {
            byte mode = modeParam switch
            {
                byte b => b,
                int i => (byte)i,
                string s when byte.TryParse(s, out byte parsed) => parsed,
                _ => 0xFF
            };
            if (mode == 0xFF) return;
            if (mode == PeekPendingOrActiveMode())
                return;

            ActivePowerMode = mode;
            UpdateModeName(mode);
            NotifyModeSelectionsChanged();
            StatusMessage = $"Switching to {GetModeTitle(mode)}...";

            CancellationToken debounceToken;
            lock (_modeSwitchDebounceLock)
            {
                _pendingHardwareMode = mode;
                _modeSwitchDebounceCts?.Cancel();
                _modeSwitchDebounceCts?.Dispose();
                _modeSwitchDebounceCts = new CancellationTokenSource();
                debounceToken = _modeSwitchDebounceCts.Token;
            }

            try
            {
                await Task.Delay(ModeSwitchDebounceMilliseconds, debounceToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await _modeSwitchGate.WaitAsync();
            try
            {
                byte toApply;
                lock (_modeSwitchDebounceLock)
                    toApply = _pendingHardwareMode;
                if (toApply == 0xFF)
                    return;
                await ApplyModeSwitchAsync(toApply);
                lock (_modeSwitchDebounceLock)
                {
                    if (_pendingHardwareMode == toApply)
                    {
                        _pendingHardwareMode = 0xFF;
                        _modeSwitchDebounceCts?.Dispose();
                        _modeSwitchDebounceCts = null;
                    }
                }
            }
            finally
            {
                _modeSwitchGate.Release();
            }
        }

        internal bool IsModeSelectionPending()
        {
            lock (_modeSwitchDebounceLock)
                return _pendingHardwareMode != 0xFF;
        }

        internal byte PeekPendingOrActiveMode()
        {
            lock (_modeSwitchDebounceLock)
                return _pendingHardwareMode != 0xFF ? _pendingHardwareMode : ActivePowerMode;
        }

        internal byte CyclePerformanceMode(byte current)
        {
            if (!IsAcConnected)
            {
                return current == 0x06 ? (byte)0x01 : (byte)0x06;
            }

            return current switch
            {
                0x00 => 0x01,
                0x01 => 0x04,
                0x04 => 0x05,
                0x05 => 0x00,
                0x06 => 0x01,
                _ => 0x01
            };
        }

        internal async Task ApplyModeSwitchAsync(byte mode)
        {
            ActivePowerMode = mode;
            UpdateModeName(mode);
            NotifyModeSelectionsChanged();
            StatusMessage = $"Switching to {GetModeTitle(mode)}...";

            int modePl1 = LimitPl1ToPlatform(mode switch
            {
                0x05 => CustomTurboPl1,
                0x04 => CustomPerfPl1,
                0x01 => CustomBalancedPl1,
                _ => CustomQuietPl1
            });
            int modePl2 = LimitPl2(modePl1, mode switch
            {
                0x05 => CustomTurboPl2,
                0x04 => CustomPerfPl2,
                0x01 => CustomBalancedPl2,
                _ => CustomQuietPl2
            });
            int modeGpu = mode switch
            {
                0x05 => CustomTurboGpuWatts,
                0x04 => CustomPerfGpuWatts,
                0x01 => CustomBalancedGpuWatts,
                _ => CustomQuietGpuWatts
            };
            int modeTrip = mode switch
            {
                0x05 => CustomTurboGpuTripPoint,
                0x04 => CustomPerfGpuTripPoint,
                0x01 => CustomBalancedGpuTripPoint,
                _ => CustomQuietGpuTripPoint
            };
            Pl1Watts = modePl1;
            Pl2Watts = modePl2;
            GpuPowerTargetWatts = modeGpu;
            GpuThermalTripPointCelsius = modeTrip;
            ActiveGpuPowerCeiling = $"GPU Power Ceiling: {GpuPowerTargetWatts}W (ACPI Max: {GpuPowerTargetMax}W, Trip: {GpuThermalTripPointCelsius}°C)";

            IsBusy = true;
            try
            {
                await Task.Run(() =>
                {
                    if (_hw.CurrentPowerMode != mode)
                        _hw.SetPowerMode(mode);
                    PowerLimitController.SetCpuPowerLimits(modePl1, modePl2, true, TauSeconds);
                    PreySense.Core.Hardware.PreySenseWmiDeviceClient.SetGpuPowerLimit((uint)modeGpu);
                    PreySense.Core.Hardware.PreySenseWmiDeviceClient.SetGpuThermalTripPoint((uint)modeTrip);
                });
                StatusMessage = $"{PowerModeName} Mode Activated";
                ModeToastWindow.ShowConfirmedMode(mode, GetModeTitle(mode));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                PersistSessionSettings();
            }
        }

        private void NotifyModeSelectionsChanged()
        {
            OnPropertyChanged(nameof(IsEcoActive));
            OnPropertyChanged(nameof(IsQuietActive));
            OnPropertyChanged(nameof(IsBalancedActive));
            OnPropertyChanged(nameof(IsPerformActive));
            OnPropertyChanged(nameof(IsTurboActive));
            OnPropertyChanged(nameof(IsEcoOrQuietActive));
            OnPropertyChanged(nameof(EcoOrQuietModeName));
            OnPropertyChanged(nameof(EcoOrQuietModeParam));
        }

        private void UpdateModeName(byte mode)
        {
            PowerModeName = GetModeTitle(mode);
            ActiveModeWattageInfo = mode switch
            {
                0x00 => "PL1: 45W | PL2: 140W (Quiet AC Profile)",
                0x01 => "PL1: 65W | PL2: 140W (Balanced AC Profile)",
                0x04 => "PL1: 75W | PL2: 140W (Performance AC Profile)",
                0x05 => "PL1: 85W | PL2: 140W (Turbo AC Profile)",
                _ => "PL1/PL2 Managed by EC Firmware"
            };
        }

        [RelayCommand]
        public void SetCpuBoost(string boostModeStr)
        {
            if (!int.TryParse(boostModeStr, out int boostVal))
                return;

            CpuBoostMode = Math.Clamp(boostVal, 0, 6);
        }

        internal static string GetCpuBoostModeName(int boostMode)
        {
            return boostMode switch
            {
                0 => "Disabled",
                2 => "Aggressive",
                3 => "Efficient Enabled",
                4 => "Efficient Aggressive",
                5 => "Aggressive at Guaranteed",
                6 => "Efficient Aggressive at Guaranteed",
                _ => "Enabled"
            };
        }

        partial void OnCpuBoostModeChanged(int value)
        {
            int boost = Math.Clamp(value, 0, 6);
            CpuBoostModeName = GetCpuBoostModeName(boost);
            _ = Task.Run(() => PowerLimitController.SetCpuBoost(boost));
            StatusMessage = $"CPU Boost set to: {CpuBoostModeName}";
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense\AdvancedSettings");
                key?.SetValue("CpuBoost", boost);
            }
            catch { }
        }
    }
}
