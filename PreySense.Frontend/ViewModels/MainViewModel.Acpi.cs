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
        private int _factoryCaptureStarted;

        public async Task CaptureFactoryPowerProfileAsync()
        {
            if (System.Threading.Interlocked.Exchange(ref _factoryCaptureStarted, 1) != 0)
                return;

            try
            {
                FactoryPowerProfileSnapshot snapshot = await Task.Run(() =>
                {
                    byte activeMode = _hw.CurrentPowerMode;
                    return FactoryPowerProfileDetector.Capture(activeMode, null);
                });

                if (_disposed)
                    return;

                _factoryPowerProfile = snapshot;
                if (!_hasPersistedPowerLimits)
                {
                    ApplyCapturedFactoryDefaults();
                    ApplyActiveFactoryMode(snapshot.ActiveMode);
                }

                byte activeMode = snapshot.ActiveMode;
                AppLogger.Log($"[MainViewModel] Launch factory snapshot for mode 0x{activeMode:X2}: " +
                    $"CPU={(snapshot.CpuReadFromFirmware ? "firmware" : "fallback")}, " +
                    $"GPU={(snapshot.GpuPowerReadFromFirmware ? "firmware" : "fallback")}, " +
                    $"Trip={(snapshot.GpuTripReadFromFirmware ? "firmware" : "fallback")}, " +
                    $"Tau={(snapshot.TauReadFromFirmware ? "firmware" : "fallback")}.");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[MainViewModel] Factory power snapshot failed: {ex.Message}");
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
            CustomBalancedPl1 = cpu.BalancedPl1;
            CustomBalancedPl2 = cpu.BalancedPl2;
            CustomPerfPl1 = cpu.PerformancePl1;
            CustomPerfPl2 = cpu.PerformancePl2;
            CustomTurboPl1 = cpu.TurboPl1;
            CustomTurboPl2 = cpu.TurboPl2;
            TauSeconds = cpu.TauSeconds;

            GpuBasePowerWatts = gpu.BasePowerWatts;
            GpuPowerTargetMin = gpu.MinPowerWatts;
            GpuPowerTargetMax = gpu.MaxPowerWatts;
            CustomQuietGpuWatts = gpu.QuietGpuWatts;
            CustomBalancedGpuWatts = gpu.BalancedGpuWatts;
            CustomPerfGpuWatts = gpu.PerformanceGpuWatts;
            CustomTurboGpuWatts = gpu.TurboGpuWatts;
            CustomQuietGpuTripPoint = gpu.QuietGpuTripPointCelsius;
            CustomBalancedGpuTripPoint = gpu.BalancedGpuTripPointCelsius;
            CustomPerfGpuTripPoint = gpu.PerformanceGpuTripPointCelsius;
            CustomTurboGpuTripPoint = gpu.TurboGpuTripPointCelsius;
        }

        public void ResetModeToCapturedFactoryDefaults(int modeIndex)
        {
            var cpu = _factoryPowerProfile?.Cpu ?? new CpuPowerLimitConfiguration();
            var gpu = _factoryPowerProfile?.Gpu ?? new GpuPowerLimitConfiguration();
            TauSeconds = cpu.TauSeconds;

            switch (modeIndex)
            {
                case 0:
                    CustomQuietPl1 = cpu.QuietPl1;
                    CustomQuietPl2 = cpu.QuietPl2;
                    CustomQuietGpuWatts = gpu.QuietGpuWatts;
                    CustomQuietGpuTripPoint = gpu.QuietGpuTripPointCelsius;
                    break;
                case 1:
                    CustomBalancedPl1 = cpu.BalancedPl1;
                    CustomBalancedPl2 = cpu.BalancedPl2;
                    CustomBalancedGpuWatts = gpu.BalancedGpuWatts;
                    CustomBalancedGpuTripPoint = gpu.BalancedGpuTripPointCelsius;
                    break;
                case 2:
                    CustomPerfPl1 = cpu.PerformancePl1;
                    CustomPerfPl2 = cpu.PerformancePl2;
                    CustomPerfGpuWatts = gpu.PerformanceGpuWatts;
                    CustomPerfGpuTripPoint = gpu.PerformanceGpuTripPointCelsius;
                    break;
                case 3:
                    CustomTurboPl1 = cpu.TurboPl1;
                    CustomTurboPl2 = cpu.TurboPl2;
                    CustomTurboGpuWatts = gpu.TurboGpuWatts;
                    CustomTurboGpuTripPoint = gpu.TurboGpuTripPointCelsius;
                    break;
            }
        }
        // Power Limits (PL1/PL2/Tau - Informational / Mode Target)
        [ObservableProperty] private int _pl1Watts = 55;
        [ObservableProperty] private int _pl2Watts = 130;
        [ObservableProperty] private int _pl1Max = OpenCorePowerLimitManager.FactoryIntelPlatformLimitWatts;
        [ObservableProperty] private int _pl2Max = CpuPowerLimitConfiguration.MaxPowerLimitWatts;
        [ObservableProperty] private int _tauSeconds = CpuPowerLimitConfiguration.FactoryTauSeconds; // Standard 56s, up to 448s / infinite
        [ObservableProperty] private int _pcccCeilingWatts = OpenCorePowerLimitManager.FactoryIntelPlatformLimitWatts;
        [ObservableProperty] private int _intelPlatformPowerLimitMinimumWatts = OpenCorePowerLimitManager.FactoryIntelPlatformLimitWatts;
        [ObservableProperty] private string _intelPlatformPowerLimitDescription = "Detecting the platform minimum from ACPI firmware...";
        [ObservableProperty] private string _postRebootVerificationText = "Awaiting post-reboot hardware verification...";

        // ACPI Override Enable Policy
        [ObservableProperty] private bool _isAcpiOverrideEnabled = false;
        [ObservableProperty] private bool _isOpenCoreLoaded = false;

        public Visibility OpenCoreNotLoadedWarningVisibility => IsOpenCoreLoaded ? Visibility.Collapsed : Visibility.Visible;
        public Visibility OpenCoreLoadedBannerVisibility => IsOpenCoreLoaded ? Visibility.Visible : Visibility.Collapsed;

        public Visibility AcpiOverrideDisabledBannerVisibility => 
            (!IsOpenCoreLoaded || !IsAcpiOverrideEnabled) 
                ? Visibility.Visible 
                : Visibility.Collapsed;

        public string AcpiOverrideDisabledBannerText
        {
            get
            {
                if (!IsOpenCoreLoaded)
                {
                    return Services.LocalizationService.Translate("OpenCore is not loaded. Boot via OpenCore to unlock ACPI control.");
                }
                return Services.LocalizationService.Translate("ACPI Hardware Override is disabled.");
            }
        }

        public bool IsAcpiTuningAllowed => IsOpenCoreLoaded && IsAcpiOverrideEnabled;

        // ACPI Boot-Time Override State
        [ObservableProperty] private int _customQuietPl1 = CpuPowerLimitConfiguration.FactoryQuietPl1;
        [ObservableProperty] private int _customQuietPl2 = CpuPowerLimitConfiguration.FactoryQuietPl2;
        [ObservableProperty] private int _customBalancedPl1 = CpuPowerLimitConfiguration.FactoryBalancedPl1;
        [ObservableProperty] private int _customBalancedPl2 = CpuPowerLimitConfiguration.FactoryBalancedPl2;
        [ObservableProperty] private int _customPerfPl1 = CpuPowerLimitConfiguration.FactoryPerformancePl1;
        [ObservableProperty] private int _customPerfPl2 = CpuPowerLimitConfiguration.FactoryPerformancePl2;
        [ObservableProperty] private int _customTurboPl1 = CpuPowerLimitConfiguration.FactoryTurboPl1;
        [ObservableProperty] private int _customTurboPl2 = CpuPowerLimitConfiguration.FactoryTurboPl2;
        [ObservableProperty] private string _acpiOverrideStatus = "OpenCore Not Loaded";
        [ObservableProperty] private string _acpiActionMessage = string.Empty;

        public int EffectivePlatformPl1Limit =>
            Math.Clamp(PcccCeilingWatts, IntelPlatformPowerLimitMinimumWatts, CpuPowerLimitConfiguration.MaxPowerLimitWatts);

        partial void OnPcccCeilingWattsChanged(int value)
        {
            int clamped = Math.Clamp(value, 1, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
            if (clamped != value)
            {
                PcccCeilingWatts = clamped;
                return;
            }

            OnPropertyChanged(nameof(EffectivePlatformPl1Limit));
            ClampCpuPl1ToPlatformLimit();
        }

        public void ClampCpuPl1ToPlatformLimit()
        {
            Pl1Max = EffectivePlatformPl1Limit;
            CustomQuietPl1 = LimitPl1ToPlatform(CustomQuietPl1);
            CustomBalancedPl1 = LimitPl1ToPlatform(CustomBalancedPl1);
            CustomPerfPl1 = LimitPl1ToPlatform(CustomPerfPl1);
            CustomTurboPl1 = LimitPl1ToPlatform(CustomTurboPl1);
            Pl1Watts = LimitPl1ToPlatform(Pl1Watts);
            CustomQuietPl2 = LimitPl2(CustomQuietPl1, CustomQuietPl2);
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
            OnPropertyChanged(nameof(IsAcpiTuningAllowed));
        }

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
            OnPropertyChanged(nameof(IsAcpiTuningAllowed));

            StatusMessage = value 
                ? "ACPI Firmware Override: Enabled (PL1/PL2/Tau unlocked)" 
                : "ACPI Firmware Override: Disabled (Controls locked to factory profile)";
        }

        public void RefreshOpenCoreStatus()
        {
            IsOpenCoreLoaded = OpenCoreSecurityValidator.DetectIsOpenCoreLoaded();
            AcpiOverrideStatus = Services.LocalizationService.Translate(OpenCoreSecurityValidator.GetAcpiOverrideLabel(IsOpenCoreLoaded));
        }

        public void RefreshLocalizedText()
        {
            AcpiOverrideStatus = Services.LocalizationService.Translate(OpenCoreSecurityValidator.GetAcpiOverrideLabel(IsOpenCoreLoaded));
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
                ["PreySense.OpenCore.Uninstall-From-ESP.ps1"] = "Uninstall-From-ESP.ps1"
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
            if (!OpenCorePowerLimitManager.UpdateConfigPlistPatches(configPath, string.Empty, 0, PcccCeilingWatts))
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

        [RelayCommand]
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
            PcccCeilingWatts = IntelPlatformPowerLimitMinimumWatts;
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
                IntelPlatformPowerLimitMinimumWatts = OpenCorePowerLimitManager.FactoryIntelPlatformLimitWatts;
                PcccCeilingWatts = IntelPlatformPowerLimitMinimumWatts;

                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense\CpuLimits");
                if (key != null)
                {
                    CustomQuietPl1 = Math.Clamp((int)key.GetValue("QuietPl1", CustomQuietPl1), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomQuietPl2 = Math.Clamp((int)key.GetValue("QuietPl2", CustomQuietPl2), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomBalancedPl1 = Math.Clamp((int)key.GetValue("BalancedPl1", CustomBalancedPl1), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomBalancedPl2 = Math.Clamp((int)key.GetValue("BalancedPl2", CustomBalancedPl2), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomPerfPl1 = Math.Clamp((int)key.GetValue("PerfPl1", CustomPerfPl1), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomPerfPl2 = Math.Clamp((int)key.GetValue("PerfPl2", CustomPerfPl2), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomTurboPl1 = Math.Clamp((int)key.GetValue("TurboPl1", CustomTurboPl1), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    CustomTurboPl2 = Math.Clamp((int)key.GetValue("TurboPl2", CustomTurboPl2), 10, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    TauSeconds = Math.Clamp((int)key.GetValue("TauSeconds", TauSeconds), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
                    PcccCeilingWatts = Math.Clamp(
                        (int)key.GetValue("PcccCeilingWatts", PcccCeilingWatts),
                        IntelPlatformPowerLimitMinimumWatts,
                        CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    ClampCpuPl1ToPlatformLimit();
                    AppLogger.Log("[MainViewModel] Loaded CPU power limits from registry.");
                }

                using var gpuKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense\GpuLimits");
                if (gpuKey != null)
                {
                    CustomQuietGpuWatts = Math.Max(1, (int)gpuKey.GetValue("QuietGpuWatts", CustomQuietGpuWatts));
                    CustomBalancedGpuWatts = Math.Max(1, (int)gpuKey.GetValue("BalancedGpuWatts", CustomBalancedGpuWatts));
                    CustomPerfGpuWatts = Math.Max(1, (int)gpuKey.GetValue("PerfGpuWatts", CustomPerfGpuWatts));
                    CustomTurboGpuWatts = Math.Max(1, (int)gpuKey.GetValue("TurboGpuWatts", CustomTurboGpuWatts));
                    GpuPowerTargetWatts = CustomTurboGpuWatts;

                    CustomQuietGpuTripPoint = Math.Clamp((int)gpuKey.GetValue("QuietGpuTripPoint", CustomQuietGpuTripPoint), 70, 87);
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
                    key.SetValue("BalancedPl1", CustomBalancedPl1);
                    key.SetValue("BalancedPl2", CustomBalancedPl2);
                    key.SetValue("PerfPl1", CustomPerfPl1);
                    key.SetValue("PerfPl2", CustomPerfPl2);
                    key.SetValue("TurboPl1", CustomTurboPl1);
                    key.SetValue("TurboPl2", CustomTurboPl2);
                    key.SetValue("TauSeconds", TauSeconds);
                    key.SetValue("PcccCeilingWatts", Math.Clamp(PcccCeilingWatts, IntelPlatformPowerLimitMinimumWatts, CpuPowerLimitConfiguration.MaxPowerLimitWatts));
                }

                // Also persist GPU limits
                SaveGpuLimitsToRegistry();

                // Sync with ProfileManager per-mode profiles
                SyncProfile(0x00, CustomQuietPl1, CustomQuietPl2);
                SyncProfile(0x01, CustomBalancedPl1, CustomBalancedPl2);
                SyncProfile(0x04, CustomPerfPl1, CustomPerfPl2);
                SyncProfile(0x05, CustomTurboPl1, CustomTurboPl2);

                StatusMessage = "Power limits saved to registry.";
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[MainViewModel] SaveCpuLimitsToRegistry error: {ex.Message}");
            }
        }

        private void SyncProfile(byte mode, int pl1, int pl2)
        {
            try
            {
                var profile = PreySense.Mode.ProfileManager.LoadProfile(mode);
                profile.CpuPl1 = pl1;
                profile.CpuPl2 = pl2;
                profile.TauSeconds = TauSeconds;
                PreySense.Mode.ProfileManager.SaveProfile(profile);
            }
            catch { }
        }
    }
}
