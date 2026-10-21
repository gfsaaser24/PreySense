using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PreySense.Frontend.Services;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        private const string CustomFanRegistryPath = @"SOFTWARE\PreySense";
        private readonly UiDebouncer _customFanDebouncer = new(TimeSpan.FromMilliseconds(250));
        private bool _loadingCustomFanSpeeds;

        // Fan Control (0 = Auto, 1 = Max, 2 = Custom)
        [ObservableProperty] private int _activeFanMode = 0; // 0=Auto, 1=Max, 2=Custom
        public bool IsAutoFanActive => ActiveFanMode == 0;
        public bool IsMaxFanActive => ActiveFanMode == 1;
        public bool IsCustomFanActive => ActiveFanMode == 2;
        [ObservableProperty] private bool _isFanMaxActive = false;
        [ObservableProperty] private int _customCpuFanPercent = 50;
        [ObservableProperty] private int _customGpuFanPercent = 50;

        private void NotifyFanSelectionsChanged()
        {
            OnPropertyChanged(nameof(IsAutoFanActive));
            OnPropertyChanged(nameof(IsMaxFanActive));
            OnPropertyChanged(nameof(IsCustomFanActive));
        }

        private void LoadCustomFanSpeeds()
        {
            _loadingCustomFanSpeeds = true;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(CustomFanRegistryPath);
                if (key?.GetValue("CustomCpuFanPercent") is int cpu)
                    CustomCpuFanPercent = EcFanPolicy.SnapDuty(cpu);
                if (key?.GetValue("CustomGpuFanPercent") is int gpu)
                    CustomGpuFanPercent = EcFanPolicy.SnapDuty(gpu);
            }
            catch { }
            finally
            {
                _loadingCustomFanSpeeds = false;
            }
        }

        private void SaveCustomFanSpeeds()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(CustomFanRegistryPath);
                key?.SetValue("CustomCpuFanPercent", EcFanPolicy.SnapDuty(CustomCpuFanPercent), RegistryValueKind.DWord);
                key?.SetValue("CustomGpuFanPercent", EcFanPolicy.SnapDuty(CustomGpuFanPercent), RegistryValueKind.DWord);
            }
            catch { }
        }

        partial void OnCustomCpuFanPercentChanged(int value) => OnCustomFanPercentChanged();

        partial void OnCustomGpuFanPercentChanged(int value) => OnCustomFanPercentChanged();

        private void OnCustomFanPercentChanged()
        {
            if (_loadingCustomFanSpeeds)
                return;

            int cpu = EcFanPolicy.SnapDuty(CustomCpuFanPercent);
            int gpu = EcFanPolicy.SnapDuty(CustomGpuFanPercent);
            if (cpu != CustomCpuFanPercent)
            {
                CustomCpuFanPercent = cpu;
                return;
            }
            if (gpu != CustomGpuFanPercent)
            {
                CustomGpuFanPercent = gpu;
                return;
            }

            SaveCustomFanSpeeds();
            if (ActiveFanMode != 2)
                return;

            _customFanDebouncer.Debounce(() =>
            {
                _ = Task.Run(() => _hw.SetFanSpeeds((byte)CustomCpuFanPercent, (byte)CustomGpuFanPercent));
                StatusMessage = $"Custom Fan Speed ({CustomCpuFanPercent}% CPU / {CustomGpuFanPercent}% GPU)";
            });
        }

        [RelayCommand]
        public async Task SetFanModeAsync(object? fanModeParam)
        {
            int fanMode = fanModeParam switch
            {
                int i => i,
                byte b => (int)b,
                string s when int.TryParse(s, out int parsed) => parsed,
                _ => -1
            };
            if (fanMode < 0) return;

            ActiveFanMode = fanMode;
            IsFanMaxActive = fanMode == 1;
            SetTelemetryPollingActive(App.Current?.MainWindow?.IsVisible == true);
            NotifyFanSelectionsChanged();
            byte modeByte = (byte)fanMode;
            StatusMessage = fanMode switch
            {
                1 => "Max Fans Engaged (100% RPM)",
                2 => $"Custom Fan Speed ({CustomCpuFanPercent}% CPU / {CustomGpuFanPercent}% GPU)",
                _ => "Auto Fan Speed (Firmware EC Curve)"
            };

            IsBusy = true;
            try
            {
                if (fanMode == 2)
                {
                    await Task.Run(() => _hw.SetFanSpeeds((byte)CustomCpuFanPercent, (byte)CustomGpuFanPercent));
                }
                else
                {
                    await Task.Run(() => _hw.SetFanMode(modeByte, modeByte));
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Fan error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                PersistSessionSettings();
            }
        }

        [RelayCommand]
        public async Task ToggleMaxFansAsync()
        {
            await SetFanModeAsync(ActiveFanMode == 1 ? "0" : "1");
        }
    }
}
