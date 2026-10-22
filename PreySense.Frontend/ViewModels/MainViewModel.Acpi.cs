using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PreySense.Core.Acpi;
using PreySense.Helpers;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        private FactoryPowerProfileSnapshot? _factoryPowerProfile;
        private bool _hasPersistedPowerLimits;
        private const string FactoryProfilesRegistryPath = @"SOFTWARE\PreySense\FactoryProfiles";
        [ObservableProperty] private string _factoryCalibrationStatus = "Not calibrated";
        [ObservableProperty] private bool _isFactoryCalibrationRunning;
        [ObservableProperty] private bool _isPlatformLimitCalibrated;
        [ObservableProperty] private bool _isFactoryCalibrationComplete;
        public bool CanEnableAcpiOverride => IsOpenCoreLoaded && IsFactoryCalibrationComplete;

        [RelayCommand(AllowConcurrentExecutions = false)]
        public async Task CalibrateFactoryProfilesAsync()
        {
            if (IsFactoryCalibrationRunning)
                return;
            if (!IsOpenCoreLoaded)
            {
                FactoryCalibrationStatus = "OpenCore must be loaded before calibration.";
                return;
            }

            byte originalMode = _hw.CurrentPowerMode;
            var cpu = CopyCpuConfiguration(_factoryPowerProfile?.Cpu ?? new CpuPowerLimitConfiguration());
            var gpu = CopyGpuConfiguration(_factoryPowerProfile?.Gpu ?? new GpuPowerLimitConfiguration());
            int calibratedMask = LoadFactoryCalibrationMask();
            int readThisRun = 0;
            var completed = new List<string>();
            var unavailable = new List<string>();
            bool anyCpu = false, anyGpu = false, anyTrip = false, anyTau = false;
            (byte Mode, string Name, int Bit)[] modes =
            {
                (0x06, "Eco", 1), (0x00, "Quiet", 2), (0x01, "Balanced", 4),
                (0x04, "Performance", 8), (0x05, "Turbo", 16)
            };

            IsFactoryCalibrationRunning = true;
            FactoryCalibrationStatus = "Reading Eco...";
            App.SuppressAutoHide = true;
            var dialog = new Views.CustomModalDialog
            {
                Owner = Application.Current.Windows.OfType<Views.AdvancedSettingsWindow>().FirstOrDefault(window => window.IsVisible)
                    ?? Application.Current.MainWindow,
                Title = "PreySense • Factory Power Limits",
                HeaderSubtitle = " • Factory Power Limits",
                Message = "Reading Eco...",
                ConfirmText = "OK",
                ConfirmEnabled = false,
                ShowCancelButton = false
            };
            dialog.Show();
            try
            {
                foreach (var entry in modes)
                {
                    string reading = $"Reading {entry.Name}...";
                    FactoryCalibrationStatus = reading;
                    if (dialog.IsVisible)
                        dialog.Message = reading;
                    bool changed = await Task.Run(() => _hw.SetPowerMode(entry.Mode));
                    if (!changed)
                    {
                        unavailable.Add(entry.Name);
                        continue;
                    }

                    bool confirmed = false;
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        await Task.Delay(150);
                        confirmed = await Task.Run(() => _hw.CurrentPowerMode == entry.Mode);
                        if (confirmed) break;
                    }
                    if (!confirmed)
                    {
                        unavailable.Add(entry.Name);
                        continue;
                    }

                    // Acer reports the selected mode before the EC has necessarily
                    // finished copying that profile into the live power registers.
                    await Task.Delay(2500);
                    FactoryPowerProfileSnapshot sample = await Task.Run(() => FactoryPowerProfileDetector.Capture(entry.Mode, null));
                    if (!sample.CpuReadFromFirmware && !sample.GpuPowerReadFromFirmware && !sample.GpuTripReadFromFirmware && !sample.TauReadFromFirmware)
                    {
                        unavailable.Add(entry.Name);
                        continue;
                    }

                    MergeFactoryMode(cpu, gpu, sample, entry.Mode);
                    anyCpu |= sample.CpuReadFromFirmware;
                    anyGpu |= sample.GpuPowerReadFromFirmware;
                    anyTrip |= sample.GpuTripReadFromFirmware;
                    anyTau |= sample.TauReadFromFirmware;
                    calibratedMask |= entry.Bit;
                    readThisRun |= entry.Bit;
                    completed.Add(entry.Name);
                }

                IsPlatformLimitCalibrated = (calibratedMask & 0x10) != 0;
                ClampCpuPl1ToPlatformLimit();

                _factoryPowerProfile = new FactoryPowerProfileSnapshot(cpu, gpu, originalMode, anyCpu, anyGpu, anyTrip, anyTau);
                IsFactoryCalibrationComplete = IsPlatformLimitCalibrated && calibratedMask == 0x1F;
                SaveFactoryCalibrationToRegistry(calibratedMask);
                string missing = unavailable.Count == 0 ? string.Empty : $" Unavailable: {string.Join(", ", unavailable)}; run again on the other power source.";
                string tau = anyTau ? string.Empty : " Tau is missing from the loaded PREYDEV schema; reinstall the current OpenCore package and reboot.";
                FactoryCalibrationStatus = completed.Count == 0
                    ? "No firmware profiles could be read."
                    : $"Saved: {string.Join(", ", completed)}.{missing}{tau}";
                StatusMessage = FactoryCalibrationStatus;
                if (dialog.IsVisible && _factoryPowerProfile != null)
                {
                    dialog.Message = BuildFactoryLimitsMessage(_factoryPowerProfile, readThisRun, FactoryCalibrationStatus);
                    dialog.ConfirmEnabled = true;
                }
            }
            catch (Exception ex)
            {
                FactoryCalibrationStatus = $"Calibration failed: {ex.Message}";
                AppLogger.Log($"[MainViewModel] Factory calibration failed: {ex}");
                if (dialog.IsVisible)
                {
                    dialog.Message = FactoryCalibrationStatus;
                    dialog.ConfirmEnabled = true;
                }
            }
            finally
            {
                await Task.Run(() => _hw.SetPowerMode(originalMode));
                ActivePowerMode = originalMode;
                UpdateModeName(originalMode);
                NotifyModeSelectionsChanged();
                IsFactoryCalibrationRunning = false;
                if (!dialog.IsVisible)
                    App.SuppressAutoHide = false;
                else
                    dialog.Closed += (_, _) => App.SuppressAutoHide = false;
            }
        }

        private string BuildFactoryLimitsMessage(FactoryPowerProfileSnapshot snapshot, int calibratedMask, string calibrationStatus)
        {
            var cpu = snapshot.Cpu;
            var gpu = snapshot.Gpu;
            var lines = new List<string>();

            AddFactoryLimitLine(lines, calibratedMask, 1, "Eco", cpu.EcoPl1, cpu.EcoPl2, cpu.EcoTauSeconds, gpu.EcoGpuWatts, gpu.EcoGpuTripPointCelsius, snapshot.TauReadFromFirmware);
            AddFactoryLimitLine(lines, calibratedMask, 2, "Quiet", cpu.QuietPl1, cpu.QuietPl2, cpu.QuietTauSeconds, gpu.QuietGpuWatts, gpu.QuietGpuTripPointCelsius, snapshot.TauReadFromFirmware);
            AddFactoryLimitLine(lines, calibratedMask, 4, "Balanced", cpu.BalancedPl1, cpu.BalancedPl2, cpu.BalancedTauSeconds, gpu.BalancedGpuWatts, gpu.BalancedGpuTripPointCelsius, snapshot.TauReadFromFirmware);
            AddFactoryLimitLine(lines, calibratedMask, 8, "Performance", cpu.PerformancePl1, cpu.PerformancePl2, cpu.PerformanceTauSeconds, gpu.PerformanceGpuWatts, gpu.PerformanceGpuTripPointCelsius, snapshot.TauReadFromFirmware);
            AddFactoryLimitLine(lines, calibratedMask, 16, "Turbo", cpu.TurboPl1, cpu.TurboPl2, cpu.TurboTauSeconds, gpu.TurboGpuWatts, gpu.TurboGpuTripPointCelsius, snapshot.TauReadFromFirmware);

            if (!snapshot.TauReadFromFirmware)
            {
                lines.Add(string.Empty);
                lines.Add("Tau: unavailable (loaded PREYDEV schema is outdated)");
            }

            if (lines.Count > 0)
                lines.Add(string.Empty);
            lines.Add(calibrationStatus);
            return string.Join(Environment.NewLine, lines);
        }

        private static void AddFactoryLimitLine(List<string> lines, int calibratedMask, int modeBit, string modeName,
            int pl1, int pl2, int tauSeconds, int gpuWatts, int gpuTripCelsius, bool tauRead)
        {
            if ((calibratedMask & modeBit) == 0)
                return;
            string tau = tauRead ? CpuPowerLimitConfiguration.FormatTauSelection(tauSeconds) : "n/a";
            lines.Add($"{modeName}: CPU {pl1}/{pl2}W  •  Tau {tau}  •  GPU {gpuWatts}W  •  Trip Point {gpuTripCelsius}°C");
        }

        private static CpuPowerLimitConfiguration CopyCpuConfiguration(CpuPowerLimitConfiguration source) => new()
        {
            QuietPl1 = source.QuietPl1, QuietPl2 = source.QuietPl2,
            EcoPl1 = source.EcoPl1, EcoPl2 = source.EcoPl2,
            BalancedPl1 = source.BalancedPl1, BalancedPl2 = source.BalancedPl2,
            PerformancePl1 = source.PerformancePl1, PerformancePl2 = source.PerformancePl2,
            TurboPl1 = source.TurboPl1, TurboPl2 = source.TurboPl2,
            QuietTauSeconds = source.QuietTauSeconds, EcoTauSeconds = source.EcoTauSeconds,
            BalancedTauSeconds = source.BalancedTauSeconds, PerformanceTauSeconds = source.PerformanceTauSeconds,
            TurboTauSeconds = source.TurboTauSeconds, TauSeconds = source.TauSeconds
        };

        private static GpuPowerLimitConfiguration CopyGpuConfiguration(GpuPowerLimitConfiguration source) => new()
        {
            BasePowerWatts = source.BasePowerWatts, MinPowerWatts = source.MinPowerWatts, MaxPowerWatts = source.MaxPowerWatts,
            AcpiTurboDefaultWatts = source.AcpiTurboDefaultWatts,
            QuietGpuWatts = source.QuietGpuWatts, EcoGpuWatts = source.EcoGpuWatts,
            BalancedGpuWatts = source.BalancedGpuWatts, PerformanceGpuWatts = source.PerformanceGpuWatts, TurboGpuWatts = source.TurboGpuWatts,
            QuietGpuTripPointCelsius = source.QuietGpuTripPointCelsius, EcoGpuTripPointCelsius = source.EcoGpuTripPointCelsius,
            BalancedGpuTripPointCelsius = source.BalancedGpuTripPointCelsius, PerformanceGpuTripPointCelsius = source.PerformanceGpuTripPointCelsius,
            TurboGpuTripPointCelsius = source.TurboGpuTripPointCelsius
        };

        private static void MergeFactoryMode(CpuPowerLimitConfiguration cpu, GpuPowerLimitConfiguration gpu, FactoryPowerProfileSnapshot sample, byte mode)
        {
            if (sample.TauReadFromFirmware)
            {
                cpu.TauSeconds = sample.Cpu.TauSeconds;
                switch (mode)
                {
                    case 0x06: cpu.EcoTauSeconds = sample.Cpu.EcoTauSeconds; break;
                    case 0x00: cpu.QuietTauSeconds = sample.Cpu.QuietTauSeconds; break;
                    case 0x01: cpu.BalancedTauSeconds = sample.Cpu.BalancedTauSeconds; break;
                    case 0x04: cpu.PerformanceTauSeconds = sample.Cpu.PerformanceTauSeconds; break;
                    case 0x05: cpu.TurboTauSeconds = sample.Cpu.TurboTauSeconds; break;
                }
            }
            switch (mode)
            {
                case 0x06:
                    if (sample.CpuReadFromFirmware) { cpu.EcoPl1 = sample.Cpu.EcoPl1; cpu.EcoPl2 = sample.Cpu.EcoPl2; }
                    if (sample.GpuPowerReadFromFirmware) gpu.EcoGpuWatts = sample.Gpu.EcoGpuWatts;
                    if (sample.GpuTripReadFromFirmware) gpu.EcoGpuTripPointCelsius = sample.Gpu.EcoGpuTripPointCelsius;
                    break;
                case 0x00:
                    if (sample.CpuReadFromFirmware) { cpu.QuietPl1 = sample.Cpu.QuietPl1; cpu.QuietPl2 = sample.Cpu.QuietPl2; }
                    if (sample.GpuPowerReadFromFirmware) gpu.QuietGpuWatts = sample.Gpu.QuietGpuWatts;
                    if (sample.GpuTripReadFromFirmware) gpu.QuietGpuTripPointCelsius = sample.Gpu.QuietGpuTripPointCelsius;
                    break;
                case 0x01:
                    if (sample.CpuReadFromFirmware) { cpu.BalancedPl1 = sample.Cpu.BalancedPl1; cpu.BalancedPl2 = sample.Cpu.BalancedPl2; }
                    if (sample.GpuPowerReadFromFirmware) gpu.BalancedGpuWatts = sample.Gpu.BalancedGpuWatts;
                    if (sample.GpuTripReadFromFirmware) gpu.BalancedGpuTripPointCelsius = sample.Gpu.BalancedGpuTripPointCelsius;
                    break;
                case 0x04:
                    if (sample.CpuReadFromFirmware) { cpu.PerformancePl1 = sample.Cpu.PerformancePl1; cpu.PerformancePl2 = sample.Cpu.PerformancePl2; }
                    if (sample.GpuPowerReadFromFirmware) gpu.PerformanceGpuWatts = sample.Gpu.PerformanceGpuWatts;
                    if (sample.GpuTripReadFromFirmware) gpu.PerformanceGpuTripPointCelsius = sample.Gpu.PerformanceGpuTripPointCelsius;
                    break;
                case 0x05:
                    if (sample.CpuReadFromFirmware) { cpu.TurboPl1 = sample.Cpu.TurboPl1; cpu.TurboPl2 = sample.Cpu.TurboPl2; }
                    if (sample.GpuPowerReadFromFirmware) gpu.TurboGpuWatts = sample.Gpu.TurboGpuWatts;
                    if (sample.GpuTripReadFromFirmware) gpu.TurboGpuTripPointCelsius = sample.Gpu.TurboGpuTripPointCelsius;
                    break;
            }
        }

        private void ApplyActiveFactoryMode(byte activeMode)
        {
            switch (activeMode)
            {
                case 0x05:
                    Pl1Watts = CustomTurboPl1;
                    Pl2Watts = CustomTurboPl2;
                    GpuPowerTargetWatts = CustomTurboGpuWatts;
                    GpuThermalTripPointCelsius = CustomTurboGpuTripPoint;
                    break;
                case 0x04:
                    Pl1Watts = CustomPerfPl1;
                    Pl2Watts = CustomPerfPl2;
                    GpuPowerTargetWatts = CustomPerfGpuWatts;
                    GpuThermalTripPointCelsius = CustomPerfGpuTripPoint;
                    break;
                case 0x01:
                    Pl1Watts = CustomBalancedPl1;
                    Pl2Watts = CustomBalancedPl2;
                    GpuPowerTargetWatts = CustomBalancedGpuWatts;
                    GpuThermalTripPointCelsius = CustomBalancedGpuTripPoint;
                    break;
                case 0x06:
                    Pl1Watts = CustomEcoPl1;
                    Pl2Watts = CustomEcoPl2;
                    GpuPowerTargetWatts = CustomEcoGpuWatts;
                    GpuThermalTripPointCelsius = CustomEcoGpuTripPoint;
                    break;
                default:
                    Pl1Watts = CustomQuietPl1;
                    Pl2Watts = CustomQuietPl2;
                    GpuPowerTargetWatts = CustomQuietGpuWatts;
                    GpuThermalTripPointCelsius = CustomQuietGpuTripPoint;
                    break;
            }
        }

        private void ApplyCapturedFactoryDefaults()
        {
            var cpu = _factoryPowerProfile?.Cpu ?? new CpuPowerLimitConfiguration();
            var gpu = _factoryPowerProfile?.Gpu ?? new GpuPowerLimitConfiguration();

            CustomQuietPl1 = cpu.QuietPl1;
            CustomQuietPl2 = cpu.QuietPl2;
            CustomEcoPl1 = cpu.EcoPl1;
            CustomEcoPl2 = cpu.EcoPl2;
            CustomBalancedPl1 = cpu.BalancedPl1;
            CustomBalancedPl2 = cpu.BalancedPl2;
            CustomPerfPl1 = cpu.PerformancePl1;
            CustomPerfPl2 = cpu.PerformancePl2;
            CustomTurboPl1 = cpu.TurboPl1;
            CustomTurboPl2 = cpu.TurboPl2;
            CustomQuietTau = cpu.QuietTauSeconds;
            CustomEcoTau = cpu.EcoTauSeconds;
            CustomBalancedTau = cpu.BalancedTauSeconds;
            CustomPerfTau = cpu.PerformanceTauSeconds;
            CustomTurboTau = cpu.TurboTauSeconds;
            TauSeconds = TauForMode(ActivePowerMode);

            GpuBasePowerWatts = gpu.BasePowerWatts;
            GpuPowerTargetMin = gpu.MinPowerWatts;
            GpuPowerTargetMax = gpu.MaxPowerWatts;
            CustomQuietGpuWatts = gpu.QuietGpuWatts;
            CustomEcoGpuWatts = gpu.EcoGpuWatts;
            CustomBalancedGpuWatts = gpu.BalancedGpuWatts;
            CustomPerfGpuWatts = gpu.PerformanceGpuWatts;
            CustomTurboGpuWatts = gpu.TurboGpuWatts;
            CustomQuietGpuTripPoint = gpu.QuietGpuTripPointCelsius;
            CustomEcoGpuTripPoint = gpu.EcoGpuTripPointCelsius;
            CustomBalancedGpuTripPoint = gpu.BalancedGpuTripPointCelsius;
            CustomPerfGpuTripPoint = gpu.PerformanceGpuTripPointCelsius;
            CustomTurboGpuTripPoint = gpu.TurboGpuTripPointCelsius;
        }

        public void ResetModeToCapturedFactoryDefaults(int modeIndex)
        {
            var cpu = _factoryPowerProfile?.Cpu ?? new CpuPowerLimitConfiguration();
            var gpu = _factoryPowerProfile?.Gpu ?? new GpuPowerLimitConfiguration();

            switch (modeIndex)
            {
                case 0:
                    CustomEcoTau = cpu.EcoTauSeconds;
                    CustomEcoPl1 = cpu.EcoPl1;
                    CustomEcoPl2 = cpu.EcoPl2;
                    CustomEcoGpuWatts = gpu.EcoGpuWatts;
                    CustomEcoGpuTripPoint = gpu.EcoGpuTripPointCelsius;
                    CustomEcoCpuBoost = true;
                    break;
                case 1:
                    CustomQuietTau = cpu.QuietTauSeconds;
                    CustomQuietPl1 = cpu.QuietPl1;
                    CustomQuietPl2 = cpu.QuietPl2;
                    CustomQuietGpuWatts = gpu.QuietGpuWatts;
                    CustomQuietGpuTripPoint = gpu.QuietGpuTripPointCelsius;
                    CustomQuietCpuBoost = true;
                    break;
                case 2:
                    CustomBalancedTau = cpu.BalancedTauSeconds;
                    CustomBalancedPl1 = cpu.BalancedPl1;
                    CustomBalancedPl2 = cpu.BalancedPl2;
                    CustomBalancedGpuWatts = gpu.BalancedGpuWatts;
                    CustomBalancedGpuTripPoint = gpu.BalancedGpuTripPointCelsius;
                    CustomBalancedCpuBoost = true;
                    break;
                case 3:
                    CustomPerfTau = cpu.PerformanceTauSeconds;
                    CustomPerfPl1 = cpu.PerformancePl1;
                    CustomPerfPl2 = cpu.PerformancePl2;
                    CustomPerfGpuWatts = gpu.PerformanceGpuWatts;
                    CustomPerfGpuTripPoint = gpu.PerformanceGpuTripPointCelsius;
                    CustomPerfCpuBoost = true;
                    break;
                case 4:
                    CustomTurboTau = cpu.TurboTauSeconds;
                    CustomTurboPl1 = cpu.TurboPl1;
                    CustomTurboPl2 = cpu.TurboPl2;
                    CustomTurboGpuWatts = gpu.TurboGpuWatts;
                    CustomTurboGpuTripPoint = gpu.TurboGpuTripPointCelsius;
                    CustomTurboCpuBoost = true;
                    break;
            }
        }
        // Power Limits (PL1/PL2/Tau - Informational / Mode Target)
        [ObservableProperty] private int _pl1Watts = 55;
        [ObservableProperty] private int _pl2Watts = 130;
        [ObservableProperty] private int _pl1Max = CpuPowerLimitConfiguration.MaxPowerLimitWatts;
        [ObservableProperty] private int _pl2Max = CpuPowerLimitConfiguration.MaxPowerLimitWatts;
        [ObservableProperty] private int _tauSeconds = CpuPowerLimitConfiguration.FactoryTauSeconds; // Active window written to hardware
        [ObservableProperty] private int _customQuietTau = CpuPowerLimitConfiguration.FactoryTauSeconds;
        [ObservableProperty] private int _customEcoTau = CpuPowerLimitConfiguration.FactoryTauSeconds;
        [ObservableProperty] private int _customBalancedTau = CpuPowerLimitConfiguration.FactoryTauSeconds;
        [ObservableProperty] private int _customPerfTau = CpuPowerLimitConfiguration.FactoryTauSeconds;
        [ObservableProperty] private int _customTurboTau = CpuPowerLimitConfiguration.FactoryTauSeconds;
        [ObservableProperty] private bool _customQuietCpuBoost = true;
        [ObservableProperty] private bool _customEcoCpuBoost = true;
        [ObservableProperty] private bool _customBalancedCpuBoost = true;
        [ObservableProperty] private bool _customPerfCpuBoost = true;
        [ObservableProperty] private bool _customTurboCpuBoost = true;

        public bool CpuBoostForMode(byte mode) => mode switch
        {
            0x05 => CustomTurboCpuBoost,
            0x04 => CustomPerfCpuBoost,
            0x01 => CustomBalancedCpuBoost,
            0x06 => CustomEcoCpuBoost,
            _ => CustomQuietCpuBoost
        };

        public bool CpuBoostForModeIndex(int modeIndex) => modeIndex switch
        {
            0 => CustomEcoCpuBoost,
            1 => CustomQuietCpuBoost,
            2 => CustomBalancedCpuBoost,
            3 => CustomPerfCpuBoost,
            _ => CustomTurboCpuBoost
        };

        public void SetCpuBoostForModeIndex(int modeIndex, bool enabled)
        {
            switch (modeIndex)
            {
                case 0: CustomEcoCpuBoost = enabled; break;
                case 1: CustomQuietCpuBoost = enabled; break;
                case 2: CustomBalancedCpuBoost = enabled; break;
                case 3: CustomPerfCpuBoost = enabled; break;
                default: CustomTurboCpuBoost = enabled; break;
            }
        }

        public void ApplyCpuBoostForMode(byte mode)
        {
            int boost = CpuBoostForMode(mode) ? 1 : 0;
            if (CpuBoostMode != boost)
                CpuBoostMode = boost;
            else
                PowerLimitController.SetCpuBoost(boost);
        }

        public int TauForMode(byte mode) => Math.Clamp(mode switch
        {
            0x05 => CustomTurboTau,
            0x04 => CustomPerfTau,
            0x01 => CustomBalancedTau,
            0x06 => CustomEcoTau,
            _ => CustomQuietTau
        }, 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);

        public int TauForModeIndex(int modeIndex) => modeIndex switch
        {
            0 => CustomEcoTau,
            1 => CustomQuietTau,
            2 => CustomBalancedTau,
            3 => CustomPerfTau,
            _ => CustomTurboTau
        };

        public void SetTauForModeIndex(int modeIndex, int tauSeconds)
        {
            int tau = Math.Clamp(tauSeconds, 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
            switch (modeIndex)
            {
                case 0: CustomEcoTau = tau; break;
                case 1: CustomQuietTau = tau; break;
                case 2: CustomBalancedTau = tau; break;
                case 3: CustomPerfTau = tau; break;
                default: CustomTurboTau = tau; break;
            }
            TauSeconds = tau;
        }
        [ObservableProperty] private string _postRebootVerificationText = "Awaiting post-reboot hardware verification...";

        // ACPI Override Enable Policy
        [ObservableProperty] private bool _isAcpiOverrideEnabled = false;
        [ObservableProperty] private bool _isOpenCoreLoaded = false;
        [ObservableProperty] private bool _isOpenCorePartitionPresent;

        partial void OnIsOpenCorePartitionPresentChanged(bool value) =>
            OnPropertyChanged(nameof(BootDriveActionText));

        public Visibility OpenCoreNotLoadedWarningVisibility => IsOpenCoreLoaded ? Visibility.Collapsed : Visibility.Visible;
        public Visibility OpenCoreLoadedBannerVisibility => IsOpenCoreLoaded ? Visibility.Visible : Visibility.Collapsed;

        public Visibility AcpiOverrideDisabledBannerVisibility => 
            (!IsOpenCoreLoaded || !IsAcpiOverrideEnabled) 
                ? Visibility.Visible 
                : Visibility.Collapsed;

        private OpenCoreSecurityValidator.OpenCoreIntegrationState _openCoreIntegrationState =
            OpenCoreSecurityValidator.OpenCoreIntegrationState.NotLoaded;

        public string AcpiOverrideDisabledBannerText
        {
            get
            {
                if (_openCoreIntegrationState == OpenCoreSecurityValidator.OpenCoreIntegrationState.RestartRequired)
                {
                    return Services.LocalizationService.Translate("Restart into the PreySense boot entry to load the installed OpenCore package.");
                }
                if (_openCoreIntegrationState == OpenCoreSecurityValidator.OpenCoreIntegrationState.UpdateRequired)
                {
                    return Services.LocalizationService.Translate("The installed OpenCore package needs to be reinstalled.");
                }
                if (!IsOpenCoreLoaded)
                {
                    return Services.LocalizationService.Translate("OpenCore is not loaded. Boot via OpenCore to unlock ACPI control.");
                }
                return Services.LocalizationService.Translate("ACPI Hardware Override is disabled.");
            }
        }

        // ACPI Boot-Time Override State
        [ObservableProperty] private int _customQuietPl1 = CpuPowerLimitConfiguration.FactoryQuietPl1;
        [ObservableProperty] private int _customQuietPl2 = CpuPowerLimitConfiguration.FactoryQuietPl2;
        [ObservableProperty] private int _customEcoPl1 = CpuPowerLimitConfiguration.FactoryEcoPl1;
        [ObservableProperty] private int _customEcoPl2 = CpuPowerLimitConfiguration.FactoryEcoPl2;
        [ObservableProperty] private int _customBalancedPl1 = CpuPowerLimitConfiguration.FactoryBalancedPl1;
        [ObservableProperty] private int _customBalancedPl2 = CpuPowerLimitConfiguration.FactoryBalancedPl2;
        [ObservableProperty] private int _customPerfPl1 = CpuPowerLimitConfiguration.FactoryPerformancePl1;
        [ObservableProperty] private int _customPerfPl2 = CpuPowerLimitConfiguration.FactoryPerformancePl2;
        [ObservableProperty] private int _customTurboPl1 = CpuPowerLimitConfiguration.FactoryTurboPl1;
        [ObservableProperty] private int _customTurboPl2 = CpuPowerLimitConfiguration.FactoryTurboPl2;
        [ObservableProperty] private string _acpiOverrideStatus = "OpenCore Not Loaded";
        [ObservableProperty] private string _acpiActionMessage = string.Empty;

        public int EffectivePlatformPl1Limit => CpuPowerLimitConfiguration.MaxPowerLimitWatts;

        public void ClampCpuPl1ToPlatformLimit()
        {
            Pl1Max = EffectivePlatformPl1Limit;
            CustomQuietPl1 = LimitPl1ToPlatform(CustomQuietPl1);
            CustomEcoPl1 = LimitPl1ToPlatform(CustomEcoPl1);
            CustomBalancedPl1 = LimitPl1ToPlatform(CustomBalancedPl1);
            CustomPerfPl1 = LimitPl1ToPlatform(CustomPerfPl1);
            CustomTurboPl1 = LimitPl1ToPlatform(CustomTurboPl1);
            Pl1Watts = LimitPl1ToPlatform(Pl1Watts);
            CustomQuietPl2 = LimitPl2(CustomQuietPl1, CustomQuietPl2);
            CustomEcoPl2 = LimitPl2(CustomEcoPl1, CustomEcoPl2);
            CustomBalancedPl2 = LimitPl2(CustomBalancedPl1, CustomBalancedPl2);
            CustomPerfPl2 = LimitPl2(CustomPerfPl1, CustomPerfPl2);
            CustomTurboPl2 = LimitPl2(CustomTurboPl1, CustomTurboPl2);
            Pl2Watts = LimitPl2(Pl1Watts, Pl2Watts);
        }

        public int LimitPl1ToPlatform(int pl1Watts) => Math.Clamp(pl1Watts, 10, EffectivePlatformPl1Limit);

        public int LimitPl2(int pl1Watts, int pl2Watts) => Math.Clamp(pl2Watts, LimitPl1ToPlatform(pl1Watts), CpuPowerLimitConfiguration.MaxPowerLimitWatts);

        // Intel Undervolt & UVP Diagnostics
        [ObservableProperty] private string _overclockingLockStatus = "Locked by Intel UVP (VarStore 0x0003 Offset 0x00F9 = 0x01)";
        [ObservableProperty] private string _setupVarCommand = "setup_var 0x00F9 0x00 -s 0x0003";

        partial void OnIsOpenCoreLoadedChanged(bool value)
        {
            OnPropertyChanged(nameof(OpenCoreNotLoadedWarningVisibility));
            OnPropertyChanged(nameof(OpenCoreLoadedBannerVisibility));
            OnPropertyChanged(nameof(AcpiOverrideDisabledBannerVisibility));
            OnPropertyChanged(nameof(AcpiOverrideDisabledBannerText));
            OnPropertyChanged(nameof(CanEnableAcpiOverride));
            OnPropertyChanged(nameof(BootDriveActionText));
        }

        public string BootDriveActionText => IsOpenCorePartitionPresent ? "Uninstall" : "Install";

        partial void OnIsAcpiOverrideEnabledChanged(bool value)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense\AdvancedSettings");
                key?.SetValue("AcpiOverrideEnabled", value ? 1 : 0);
            }
            catch { }

            OnPropertyChanged(nameof(AcpiOverrideDisabledBannerVisibility));
            OnPropertyChanged(nameof(AcpiOverrideDisabledBannerText));

            StatusMessage = value 
                ? "ACPI Firmware Override: Enabled (PL1/PL2/Tau unlocked)" 
                : "ACPI Firmware Override: Disabled (Controls locked to factory profile)";
        }

        partial void OnIsFactoryCalibrationCompleteChanged(bool value)
        {
            OnPropertyChanged(nameof(CanEnableAcpiOverride));
            if (!value && IsAcpiOverrideEnabled)
                IsAcpiOverrideEnabled = false;
        }

        public void RefreshOpenCoreStatus()
        {
            ApplyOpenCoreIntegrationState(_openCoreIntegrationState);
            RefreshOpenCorePartition();
        }

        private void ApplyOpenCoreIntegrationState(OpenCoreSecurityValidator.OpenCoreIntegrationState state)
        {
            _openCoreIntegrationState = state;
            IsOpenCoreLoaded = state == OpenCoreSecurityValidator.OpenCoreIntegrationState.Active;
            if (!IsOpenCoreLoaded && IsAcpiOverrideEnabled)
                IsAcpiOverrideEnabled = false;
            AcpiOverrideStatus = Services.LocalizationService.Translate(OpenCoreSecurityValidator.GetAcpiOverrideLabel(state));
            OnPropertyChanged(nameof(AcpiOverrideDisabledBannerText));
        }

        private void PromptOpenCoreReinstall()
        {
            if (_disposed)
                return;

            App.SuppressAutoHide = true;
            bool? reinstall = false;
            try
            {
                var dialog = new Views.CustomModalDialog
                {
                    Title = "PreySense • OpenCore",
                    HeaderSubtitle = " • OpenCore",
                    Message = "The installed OpenCore package is out of date. Reinstall it, then restart into the PreySense boot entry.",
                    ConfirmText = "Reinstall OpenCore",
                    CancelText = "Later"
                };
                if (System.Windows.Application.Current?.MainWindow is System.Windows.Window owner)
                    dialog.Owner = owner;
                reinstall = dialog.ShowDialog();
            }
            finally
            {
                App.SuppressAutoHide = false;
            }

            if (reinstall == true)
                _ = InstallOpenCoreToEspAsync();
        }

        private void RefreshOpenCorePartition()
        {
            _ = Task.Run(() =>
            {
                bool present = OpenCoreSecurityValidator.IsPreySensePartitionPresent();
                _dispatcher.InvokeAsync(() => IsOpenCorePartitionPresent = present);
            });
        }

        public void RefreshLocalizedText()
        {
            AcpiOverrideStatus = Services.LocalizationService.Translate(OpenCoreSecurityValidator.GetAcpiOverrideLabel(_openCoreIntegrationState));
            OnPropertyChanged(nameof(AcpiOverrideDisabledBannerText));
        }

        [RelayCommand]
        public async Task ApplyPowerLimitsAsync()
        {
            IsBusy = true;
            StatusMessage = "Applying CPU Power Limits...";
            try
            {
                await Task.Run(() =>
                {
                    int pl1 = LimitPl1ToPlatform(Pl1Watts);
                    int pl2 = LimitPl2(pl1, Pl2Watts);
                    PowerLimitController.SetCpuPowerLimits(pl1, pl2, true, TauSeconds);
                });
                StatusMessage = $"PL1: {Pl1Watts}W | PL2: {Pl2Watts}W | Tau: {CpuPowerLimitConfiguration.FormatTauSelection(TauSeconds)} Applied";
            }
            catch (Exception ex)
            {
                StatusMessage = $"PL error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void ToggleAcpiOverride()
        {
            if (!IsAcpiOverrideEnabled && !CanEnableAcpiOverride)
                return;
            IsAcpiOverrideEnabled = !IsAcpiOverrideEnabled;
        }

        [RelayCommand]
        public void CopySetupVarCommand()
        {
            try
            {
                System.Windows.Clipboard.SetText(SetupVarCommand);
                StatusMessage = "setup_var command copied to clipboard!";
            }
            catch { }
        }

        private static byte[] ReadEmbeddedAcpiPayload(string resourceName)
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new FileNotFoundException($"The embedded ACPI payload '{resourceName}' is missing.");
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        private string PrepareOpenCorePackage()
        {
            string packageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PreySense",
                "OpenCoreStaging");

            var resources = new Dictionary<string, string>
            {
                ["PreySense.OpenCore.BOOT.BOOTX64.EFI"] = Path.Combine("EFI", "BOOT", "BOOTX64.EFI"),
                ["PreySense.OpenCore.OC.config.plist"] = Path.Combine("EFI", "OC", "config.plist"),
                ["PreySense.OpenCore.OC.OpenCore.efi"] = Path.Combine("EFI", "OC", "OpenCore.efi"),
                ["PreySense.OpenCore.OC.Drivers.OpenRuntime.efi"] = Path.Combine("EFI", "OC", "Drivers", "OpenRuntime.efi"),
                ["PreySense.OpenCore.Install-To-ESP.ps1"] = "Install-To-ESP.ps1",
                ["PreySense.OpenCore.Uninstall-From-ESP.ps1"] = "Uninstall-From-ESP.ps1",
                ["PreySense.OpenCore.Repair-PreySenseWmi.ps1"] = "Repair-PreySenseWmi.ps1"
            };

            foreach ((string resourceName, string relativePath) in resources)
            {
                string destination = Path.Combine(packageRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, ReadEmbeddedAcpiPayload(resourceName));
            }

            string acpiDirectory = Path.Combine(packageRoot, "EFI", "OC", "ACPI");
            Directory.CreateDirectory(acpiDirectory);
            File.WriteAllBytes(Path.Combine(acpiDirectory, "SSDT-PREYDEV.aml"), ReadEmbeddedAcpiPayload("PreySense.ACPI.SSDT-PREYDEV.aml"));
            File.WriteAllBytes(Path.Combine(acpiDirectory, "SSDT-PREYDEV.dsl"), ReadEmbeddedAcpiPayload("PreySense.ACPI.SSDT-PREYDEV.dsl"));

            string configPath = Path.Combine(packageRoot, "EFI", "OC", "config.plist");
            if (!OpenCorePowerLimitManager.UpdateConfigPlistPatches(configPath, string.Empty, 0))
            {
                throw new InvalidOperationException("The staged OpenCore config.plist could not be updated.");
            }

            return packageRoot;
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string file in Directory.GetFiles(sourceDirectory))
            {
                File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), true);
            }

            foreach (string directory in Directory.GetDirectories(sourceDirectory))
            {
                CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
            }
        }

        [RelayCommand]
        public async Task InstallOpenCoreToUsbAsync()
        {
            IsBusy = true;
            StatusMessage = "Preparing OpenCore package for USB installation...";
            try
            {
                string packageRoot = await Task.Run(PrepareOpenCorePackage);
                SaveCpuLimitsToRegistry();

                string? removableRoot = DriveInfo.GetDrives()
                    .FirstOrDefault(drive => drive.IsReady && drive.DriveType == DriveType.Removable)?.RootDirectory.FullName;
                var picker = new OpenFolderDialog
                {
                    Title = "Select the root of the USB drive",
                    Multiselect = false
                };
                if (!string.IsNullOrWhiteSpace(removableRoot))
                {
                    picker.InitialDirectory = removableRoot;
                }

                App.SuppressAutoHide = true;
                bool? confirmed = false;
                string targetRoot = string.Empty;
                try
                {
                    bool? selected = picker.ShowDialog(Application.Current.MainWindow);
                    if (selected != true) return;

                    targetRoot = Path.GetFullPath(picker.FolderName);
                    string driveRoot = Path.GetPathRoot(targetRoot) ?? string.Empty;
                    if (!string.Equals(targetRoot.TrimEnd(Path.DirectorySeparatorChar), driveRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Select the USB drive itself, not a folder inside it.");
                    }

                    var drive = new DriveInfo(driveRoot);
                    string warning = drive.DriveType == DriveType.Removable
                        ? $"Install OpenCore to {driveRoot}?\nExisting EFI on that drive will be deleted."
                        : $"{driveRoot} is not reported as removable. Install anyway? Existing EFI will be deleted.";
                    if (!string.Equals(drive.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase))
                    {
                        warning += $"{Environment.NewLine}{Environment.NewLine}The drive uses {drive.DriveFormat}; most UEFI firmware requires FAT32.";
                    }

                    var dialog = new Views.CustomModalDialog
                    {
                        Owner = Application.Current.Windows.OfType<Views.AdvancedSettingsWindow>().FirstOrDefault(window => window.IsVisible)
                            ?? Application.Current.MainWindow,
                        Title = "PreySense • OpenCore USB",
                        HeaderSubtitle = " • OpenCore USB",
                        Message = warning,
                        ConfirmText = "Install"
                    };
                    confirmed = dialog.ShowDialog();
                }
                finally
                {
                    App.SuppressAutoHide = false;
                }

                if (confirmed != true) return;

                string targetEfi = Path.Combine(targetRoot, "EFI");
                await Task.Run(() =>
                {
                    if (Directory.Exists(targetEfi))
                    {
                        Directory.Delete(targetEfi, true);
                    }

                    CopyDirectory(Path.Combine(packageRoot, "EFI"), targetEfi);
                });
                AcpiActionMessage = $"USB installed: {targetEfi}";
                StatusMessage = $"OpenCore installed to {targetEfi}. Reboot and select the USB drive from the F12 boot menu.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"USB installation failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ToggleOpenCoreBootDriveAsync()
        {
            if (IsOpenCorePartitionPresent)
                UninstallOpenCoreFromEsp();
            else
                await InstallOpenCoreToEspAsync();
        }

        public async Task InstallOpenCoreToEspAsync()
        {
            IsBusy = true;
            StatusMessage = "Preparing OpenCore package for internal-disk installation...";
            try
            {
                string packageRoot = await Task.Run(PrepareOpenCorePackage);
                SaveCpuLimitsToRegistry();
                string scriptPath = Path.Combine(packageRoot, "Install-To-ESP.ps1");
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -NoExit -File \"{scriptPath}\"",
                    Verb = "runas",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                StatusMessage = "Prepared the current ACPI settings and launched the hidden-partition installer.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Internal installation failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void UninstallOpenCoreFromEsp()
        {
            try
            {
                string scriptPath = Path.Combine(PrepareOpenCorePackage(), "Uninstall-From-ESP.ps1");

                if (System.IO.File.Exists(scriptPath))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-ExecutionPolicy Bypass -NoExit -File \"{scriptPath}\"",
                        Verb = "runas",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    StatusMessage = "Launched the OpenCore partition uninstaller (RunAs Administrator).";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to launch uninstaller: {ex.Message}";
            }
        }

        [RelayCommand]
        public void ResetAcpiLimits()
        {
            ApplyCapturedFactoryDefaults();
            GpuPowerTargetWatts = CustomTurboGpuWatts;
            GpuThermalTripPointCelsius = CustomTurboGpuTripPoint;

            AcpiActionMessage = "Factory Configuration (Active)";
            StatusMessage = "Reset all CPU & GPU limits to hardware ACPI default values.";
        }

        [RelayCommand]
        public void RollbackAcpiOverride()
        {
            string stagingDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "acpi_staging");
            AcpiOverrideRecovery.PurgeStagedOverrides(stagingDir);
            ResetAcpiLimits();
            AcpiActionMessage = "Factory Configuration (Staging Purged)";
            StatusMessage = "Staged ACPI override files successfully removed.";
        }

        public void LoadCpuLimitsFromRegistry()
        {
            try
            {
                LoadFactoryCalibrationFromRegistry();

                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense\CpuLimits");
                if (key != null)
                {
                    CustomQuietPl1 = Math.Clamp((int)key.GetValue("QuietPl1", CustomQuietPl1), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomQuietPl2 = Math.Clamp((int)key.GetValue("QuietPl2", CustomQuietPl2), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomEcoPl1 = Math.Clamp((int)key.GetValue("EcoPl1", CustomEcoPl1), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomEcoPl2 = Math.Clamp((int)key.GetValue("EcoPl2", CustomEcoPl2), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomBalancedPl1 = Math.Clamp((int)key.GetValue("BalancedPl1", CustomBalancedPl1), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomBalancedPl2 = Math.Clamp((int)key.GetValue("BalancedPl2", CustomBalancedPl2), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomPerfPl1 = Math.Clamp((int)key.GetValue("PerfPl1", CustomPerfPl1), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomPerfPl2 = Math.Clamp((int)key.GetValue("PerfPl2", CustomPerfPl2), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomTurboPl1 = Math.Clamp((int)key.GetValue("TurboPl1", CustomTurboPl1), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomTurboPl2 = Math.Clamp((int)key.GetValue("TurboPl2", CustomTurboPl2), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    int sharedTau = Math.Clamp((int)key.GetValue("TauSeconds", TauSeconds), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
                    CustomQuietTau = Math.Clamp((int)key.GetValue("QuietTau", sharedTau), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
                    CustomEcoTau = Math.Clamp((int)key.GetValue("EcoTau", sharedTau), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
                    CustomBalancedTau = Math.Clamp((int)key.GetValue("BalancedTau", sharedTau), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
                    CustomPerfTau = Math.Clamp((int)key.GetValue("PerfTau", sharedTau), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
                    CustomTurboTau = Math.Clamp((int)key.GetValue("TurboTau", sharedTau), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
                    bool fallbackBoost = CpuBoostMode != 0;
                    CustomQuietCpuBoost = ReadBoostFlag(key, "QuietCpuBoost", fallbackBoost);
                    CustomEcoCpuBoost = ReadBoostFlag(key, "EcoCpuBoost", fallbackBoost);
                    CustomBalancedCpuBoost = ReadBoostFlag(key, "BalancedCpuBoost", fallbackBoost);
                    CustomPerfCpuBoost = ReadBoostFlag(key, "PerfCpuBoost", fallbackBoost);
                    CustomTurboCpuBoost = ReadBoostFlag(key, "TurboCpuBoost", fallbackBoost);
                    TauSeconds = TauForMode(ActivePowerMode);
                    ClampCpuPl1ToPlatformLimit();
                    AppLogger.Log("[MainViewModel] Loaded CPU power limits from registry.");
                }

                using var gpuKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense\GpuLimits");
                if (gpuKey != null)
                {
                    CustomQuietGpuWatts = Math.Max(1, (int)gpuKey.GetValue("QuietGpuWatts", CustomQuietGpuWatts));
                    CustomEcoGpuWatts = Math.Max(1, (int)gpuKey.GetValue("EcoGpuWatts", CustomEcoGpuWatts));
                    CustomBalancedGpuWatts = Math.Max(1, (int)gpuKey.GetValue("BalancedGpuWatts", CustomBalancedGpuWatts));
                    CustomPerfGpuWatts = Math.Max(1, (int)gpuKey.GetValue("PerfGpuWatts", CustomPerfGpuWatts));
                    CustomTurboGpuWatts = Math.Max(1, (int)gpuKey.GetValue("TurboGpuWatts", CustomTurboGpuWatts));
                    GpuPowerTargetWatts = CustomTurboGpuWatts;

                    CustomQuietGpuTripPoint = Math.Clamp((int)gpuKey.GetValue("QuietGpuTripPoint", CustomQuietGpuTripPoint), 70, 87);
                    CustomEcoGpuTripPoint = Math.Clamp((int)gpuKey.GetValue("EcoGpuTripPoint", CustomEcoGpuTripPoint), 70, 87);
                    CustomBalancedGpuTripPoint = Math.Clamp((int)gpuKey.GetValue("BalancedGpuTripPoint", CustomBalancedGpuTripPoint), 70, 87);
                    CustomPerfGpuTripPoint = Math.Clamp((int)gpuKey.GetValue("PerfGpuTripPoint", CustomPerfGpuTripPoint), 70, 87);
                    CustomTurboGpuTripPoint = Math.Clamp((int)gpuKey.GetValue("TurboGpuTripPoint", CustomTurboGpuTripPoint), 70, 87);
                    GpuThermalTripPointCelsius = CustomTurboGpuTripPoint;

                    AppLogger.Log("[MainViewModel] Loaded GPU power & thermal trip limits from registry.");
                }

                _hasPersistedPowerLimits = key != null || gpuKey != null;
                if (!_hasPersistedPowerLimits)
                {
                    // First boot: Initialize from ACPI firmware / app defaults
                    InitializeDefaultsFromAcpi();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[MainViewModel] LoadCpuLimitsFromRegistry error: {ex.Message}");
            }
        }

        private int LoadFactoryCalibrationMask()
        {
            using var key = Registry.CurrentUser.OpenSubKey(FactoryProfilesRegistryPath);
            return key?.GetValue("CalibratedModes") is int mask ? mask : 0;
        }

        private void LoadFactoryCalibrationFromRegistry()
        {
            using var key = Registry.CurrentUser.OpenSubKey(FactoryProfilesRegistryPath);
            if (key == null)
            {
                IsFactoryCalibrationComplete = false;
                IsAcpiOverrideEnabled = false;
                return;
            }

            var cpu = new CpuPowerLimitConfiguration
            {
                EcoPl1 = ReadFactoryInt(key, "EcoPl1", CpuPowerLimitConfiguration.FactoryEcoPl1),
                EcoPl2 = ReadFactoryInt(key, "EcoPl2", CpuPowerLimitConfiguration.FactoryEcoPl2),
                QuietPl1 = ReadFactoryInt(key, "QuietPl1", CpuPowerLimitConfiguration.FactoryQuietPl1),
                QuietPl2 = ReadFactoryInt(key, "QuietPl2", CpuPowerLimitConfiguration.FactoryQuietPl2),
                BalancedPl1 = ReadFactoryInt(key, "BalancedPl1", CpuPowerLimitConfiguration.FactoryBalancedPl1),
                BalancedPl2 = ReadFactoryInt(key, "BalancedPl2", CpuPowerLimitConfiguration.FactoryBalancedPl2),
                PerformancePl1 = ReadFactoryInt(key, "PerformancePl1", CpuPowerLimitConfiguration.FactoryPerformancePl1),
                PerformancePl2 = ReadFactoryInt(key, "PerformancePl2", CpuPowerLimitConfiguration.FactoryPerformancePl2),
                TurboPl1 = ReadFactoryInt(key, "TurboPl1", CpuPowerLimitConfiguration.FactoryTurboPl1),
                TurboPl2 = ReadFactoryInt(key, "TurboPl2", CpuPowerLimitConfiguration.FactoryTurboPl2),
                TauSeconds = Math.Clamp(ReadFactoryInt(key, "TauSeconds", CpuPowerLimitConfiguration.FactoryTauSeconds), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue)
            };
            int sharedFactoryTau = cpu.TauSeconds;
            cpu.QuietTauSeconds = Math.Clamp(ReadFactoryInt(key, "QuietTau", sharedFactoryTau), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
            cpu.EcoTauSeconds = Math.Clamp(ReadFactoryInt(key, "EcoTau", sharedFactoryTau), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
            cpu.BalancedTauSeconds = Math.Clamp(ReadFactoryInt(key, "BalancedTau", sharedFactoryTau), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
            cpu.PerformanceTauSeconds = Math.Clamp(ReadFactoryInt(key, "PerformanceTau", sharedFactoryTau), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
            cpu.TurboTauSeconds = Math.Clamp(ReadFactoryInt(key, "TurboTau", sharedFactoryTau), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
            var gpu = new GpuPowerLimitConfiguration
            {
                EcoGpuWatts = ReadFactoryInt(key, "EcoGpuWatts", 85),
                QuietGpuWatts = ReadFactoryInt(key, "QuietGpuWatts", 85),
                BalancedGpuWatts = ReadFactoryInt(key, "BalancedGpuWatts", 100),
                PerformanceGpuWatts = ReadFactoryInt(key, "PerformanceGpuWatts", 115),
                TurboGpuWatts = ReadFactoryInt(key, "TurboGpuWatts", 130),
                EcoGpuTripPointCelsius = ReadFactoryInt(key, "EcoGpuTrip", GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius),
                QuietGpuTripPointCelsius = ReadFactoryInt(key, "QuietGpuTrip", GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius),
                BalancedGpuTripPointCelsius = ReadFactoryInt(key, "BalancedGpuTrip", GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius),
                PerformanceGpuTripPointCelsius = ReadFactoryInt(key, "PerformanceGpuTrip", GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius),
                TurboGpuTripPointCelsius = ReadFactoryInt(key, "TurboGpuTrip", GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius)
            };
            int mask = LoadFactoryCalibrationMask();
            IsPlatformLimitCalibrated = key.GetValue("PlatformLimitCalibrated") is int platformCalibrated && platformCalibrated != 0;
            IsFactoryCalibrationComplete = IsPlatformLimitCalibrated && mask == 0x1F;
            if (!IsFactoryCalibrationComplete)
                IsAcpiOverrideEnabled = false;
            _factoryPowerProfile = new FactoryPowerProfileSnapshot(cpu, gpu, 0x01, mask != 0, mask != 0, mask != 0, key.GetValue("TauRead") is int tauRead && tauRead != 0);
            FactoryCalibrationStatus = $"Saved calibration loaded ({CountCalibratedModes(mask)}/5 modes).";
        }

        private static int ReadFactoryInt(RegistryKey key, string name, int fallback) =>
            key.GetValue(name) is int value ? value : fallback;

        private static int CountCalibratedModes(int mask)
        {
            int count = 0;
            for (int bit = 0; bit < 5; bit++)
                if ((mask & (1 << bit)) != 0) count++;
            return count;
        }

        private void SaveFactoryCalibrationToRegistry(int calibratedMask)
        {
            if (_factoryPowerProfile == null)
                return;

            using var key = Registry.CurrentUser.CreateSubKey(FactoryProfilesRegistryPath);
            if (key == null)
                return;

            CpuPowerLimitConfiguration cpu = _factoryPowerProfile.Cpu;
            GpuPowerLimitConfiguration gpu = _factoryPowerProfile.Gpu;
            key.SetValue("CalibratedModes", calibratedMask);
            key.SetValue("PlatformLimitCalibrated", IsPlatformLimitCalibrated ? 1 : 0);
            key.SetValue("TauRead", _factoryPowerProfile.TauReadFromFirmware ? 1 : 0);
            key.SetValue("TauSeconds", cpu.TauSeconds);
            key.SetValue("QuietTau", cpu.QuietTauSeconds);
            key.SetValue("EcoTau", cpu.EcoTauSeconds);
            key.SetValue("BalancedTau", cpu.BalancedTauSeconds);
            key.SetValue("PerformanceTau", cpu.PerformanceTauSeconds);
            key.SetValue("TurboTau", cpu.TurboTauSeconds);
            key.SetValue("EcoPl1", cpu.EcoPl1); key.SetValue("EcoPl2", cpu.EcoPl2);
            key.SetValue("QuietPl1", cpu.QuietPl1); key.SetValue("QuietPl2", cpu.QuietPl2);
            key.SetValue("BalancedPl1", cpu.BalancedPl1); key.SetValue("BalancedPl2", cpu.BalancedPl2);
            key.SetValue("PerformancePl1", cpu.PerformancePl1); key.SetValue("PerformancePl2", cpu.PerformancePl2);
            key.SetValue("TurboPl1", cpu.TurboPl1); key.SetValue("TurboPl2", cpu.TurboPl2);
            key.SetValue("EcoGpuWatts", gpu.EcoGpuWatts); key.SetValue("QuietGpuWatts", gpu.QuietGpuWatts);
            key.SetValue("BalancedGpuWatts", gpu.BalancedGpuWatts); key.SetValue("PerformanceGpuWatts", gpu.PerformanceGpuWatts);
            key.SetValue("TurboGpuWatts", gpu.TurboGpuWatts);
            key.SetValue("EcoGpuTrip", gpu.EcoGpuTripPointCelsius); key.SetValue("QuietGpuTrip", gpu.QuietGpuTripPointCelsius);
            key.SetValue("BalancedGpuTrip", gpu.BalancedGpuTripPointCelsius); key.SetValue("PerformanceGpuTrip", gpu.PerformanceGpuTripPointCelsius);
            key.SetValue("TurboGpuTrip", gpu.TurboGpuTripPointCelsius);
        }

        public void InitializeDefaultsFromAcpi()
        {
            try
            {
                ApplyCapturedFactoryDefaults();

                GpuPowerTargetWatts = CustomTurboGpuWatts;
                GpuThermalTripPointCelsius = CustomTurboGpuTripPoint;
                ActiveGpuPowerCeiling = $"GPU Power Ceiling: {GpuPowerTargetWatts}W (ACPI Max: {GpuPowerTargetMax}W, Trip: {GpuThermalTripPointCelsius}°C)";

                AppLogger.Log("[MainViewModel] Initialized CPU & GPU power limits from ACPI defaults.");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[MainViewModel] InitializeDefaultsFromAcpi error: {ex.Message}");
            }
        }

        public void SaveCpuLimitsToRegistry()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense\CpuLimits");
                if (key != null)
                {
                    key.SetValue("QuietPl1", CustomQuietPl1);
                    key.SetValue("QuietPl2", CustomQuietPl2);
                    key.SetValue("EcoPl1", CustomEcoPl1);
                    key.SetValue("EcoPl2", CustomEcoPl2);
                    key.SetValue("BalancedPl1", CustomBalancedPl1);
                    key.SetValue("BalancedPl2", CustomBalancedPl2);
                    key.SetValue("PerfPl1", CustomPerfPl1);
                    key.SetValue("PerfPl2", CustomPerfPl2);
                    key.SetValue("TurboPl1", CustomTurboPl1);
                    key.SetValue("TurboPl2", CustomTurboPl2);
                    key.SetValue("TauSeconds", TauForMode(ActivePowerMode));
                    key.SetValue("QuietTau", CustomQuietTau);
                    key.SetValue("EcoTau", CustomEcoTau);
                    key.SetValue("BalancedTau", CustomBalancedTau);
                    key.SetValue("PerfTau", CustomPerfTau);
                    key.SetValue("TurboTau", CustomTurboTau);
                    key.SetValue("QuietCpuBoost", CustomQuietCpuBoost ? 1 : 0);
                    key.SetValue("EcoCpuBoost", CustomEcoCpuBoost ? 1 : 0);
                    key.SetValue("BalancedCpuBoost", CustomBalancedCpuBoost ? 1 : 0);
                    key.SetValue("PerfCpuBoost", CustomPerfCpuBoost ? 1 : 0);
                    key.SetValue("TurboCpuBoost", CustomTurboCpuBoost ? 1 : 0);
                }

                // Also persist GPU limits
                SaveGpuLimitsToRegistry();

                // Sync with ProfileManager per-mode profiles
                SyncProfile(0x00, CustomQuietPl1, CustomQuietPl2, CustomQuietTau);
                SyncProfile(0x06, CustomEcoPl1, CustomEcoPl2, CustomEcoTau);
                SyncProfile(0x01, CustomBalancedPl1, CustomBalancedPl2, CustomBalancedTau);
                SyncProfile(0x04, CustomPerfPl1, CustomPerfPl2, CustomPerfTau);
                SyncProfile(0x05, CustomTurboPl1, CustomTurboPl2, CustomTurboTau);

                StatusMessage = "Power limits saved to registry.";
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[MainViewModel] SaveCpuLimitsToRegistry error: {ex.Message}");
            }
        }

        private static bool ReadBoostFlag(Microsoft.Win32.RegistryKey key, string name, bool fallback) =>
            key.GetValue(name) is int value ? value != 0 : fallback;

        private void SyncProfile(byte mode, int pl1, int pl2, int tauSeconds)
        {
            try
            {
                var profile = PreySense.Mode.ProfileManager.LoadProfile(mode);
                profile.CpuPl1 = pl1;
                profile.CpuPl2 = pl2;
                profile.TauSeconds = tauSeconds;
                profile.CpuBoost = CpuBoostForMode(mode) ? 1 : 0;
                PreySense.Mode.ProfileManager.SaveProfile(profile);
            }
            catch { }
        }
    }
}
