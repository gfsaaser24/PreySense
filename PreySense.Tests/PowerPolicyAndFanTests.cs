using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Services;
using PreySense.Helpers;
using PreySense.Mode;

namespace PreySense.Tests
{
    [TestClass]
    public class PowerPolicyAndFanTests
    {
        [TestMethod]
        public void OverlayScheme_MapsEcoAndQuietToEfficiency()
        {
            Assert.AreEqual(WindowsPowerPolicy.OverlayEfficiency, WindowsPowerPolicy.OverlaySchemeForMode(0x00));
            Assert.AreEqual(WindowsPowerPolicy.OverlayEfficiency, WindowsPowerPolicy.OverlaySchemeForMode(0x06));
        }

        [TestMethod]
        public void OverlayScheme_MapsBalancedToBalanced()
        {
            Assert.AreEqual(WindowsPowerPolicy.OverlayBalanced, WindowsPowerPolicy.OverlaySchemeForMode(0x01));
        }

        [TestMethod]
        public void OverlayScheme_MapsPerformanceAndTurboToPerformance()
        {
            Assert.AreEqual(WindowsPowerPolicy.OverlayPerformance, WindowsPowerPolicy.OverlaySchemeForMode(0x04));
            Assert.AreEqual(WindowsPowerPolicy.OverlayPerformance, WindowsPowerPolicy.OverlaySchemeForMode(0x05));
        }

        [TestMethod]
        public void ProfileManager_UsesQuietNameAndDropsOverlayValues()
        {
            Assert.AreEqual("Quiet", ProfileManager.ModeToProfileName(0x00));
            string source = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Core", "Mode", "PerformanceProfile.cs"));
            Assert.IsFalse(source.Contains("WindowsPowerMode"));
        }

        [TestMethod]
        public void WindowFooters_UseSharedBottomMargin()
        {
            string root = GetSolutionRoot();
            string spacing = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Styles", "Spacing.xaml"));
            string controls = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Styles", "Controls.xaml"));
            string mainWindow = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml"));
            Assert.Contains("x:Key=\"WindowFooterMargin\"", spacing);
            Assert.Contains("14,10,14,16", spacing);
            Assert.Contains("{StaticResource WindowFooterMargin}", controls);
            Assert.DoesNotContain("Margin=\"0,10,0,0\"", mainWindow);
        }

        [TestMethod]
        public void CustomFans_UseSlidersInsteadOfCurves()
        {
            string root = GetSolutionRoot();
            string mainWindow = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml"));
            Assert.Contains("CustomCpuFanPercent", mainWindow);
            Assert.Contains("CustomGpuFanPercent", mainWindow);
            Assert.Contains("SmallChange=\"5\"", mainWindow);
            Assert.Contains("TickFrequency=\"5\"", mainWindow);
            Assert.DoesNotContain("FanCurveWindow", mainWindow);
            Assert.IsFalse(File.Exists(Path.Combine(root, "PreySense.Frontend", "Views", "FanCurveWindow.xaml")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "PreySense.Frontend", "Controls", "WpfFanCurveControl.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "PreySense.Core", "Mode", "FanCurveStorage.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "PreySense.Core", "Mode", "CustomFanFollower.cs")));
        }

        [TestMethod]
        public void ModeSettingsWindow_DoesNotAddOverlayOrBoostControls()
        {
            string root = GetSolutionRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml"));
            Assert.IsFalse(xaml.Contains("ComboCpuBoost"));
            Assert.IsFalse(xaml.Contains("CPU Turbo Boost"));
            Assert.IsFalse(xaml.Contains("Windows Power"));
            Assert.IsFalse(xaml.Contains("ApplyOverlayScheme"));
            Assert.IsFalse(xaml.Contains("GpuCoreOffset"));
            Assert.IsFalse(xaml.Contains("GpuMemoryOffset"));
        }

        [TestMethod]
        public void AdvancedSettingsWindow_RemovesIgpuOnBatteryAndCpuBoost()
        {
            string root = GetSolutionRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "AdvancedSettingsWindow.xaml"));
            Assert.DoesNotContain("iGPU on Battery", xaml);
            Assert.DoesNotContain("CPU Boost", xaml);
            Assert.DoesNotContain("IsGpuBatteryAutoEnabled", xaml);
            Assert.DoesNotContain("CpuBoostModes", xaml);
            Assert.DoesNotContain("ResetAdvancedSettingsCommand", xaml);
            Assert.DoesNotContain("ApplyAdvancedSettingsCommand", xaml);
            Assert.DoesNotContain("SaveAdvancedSettingsCommand", xaml);
        }

        [TestMethod]
        public void DisplayManager_NoLongerExposesColorProfiles()
        {
            string root = GetSolutionRoot();
            string source = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Display", "DisplayManager.cs"));
            Assert.IsFalse(source.Contains("class DisplayColorProfile"));
            Assert.IsFalse(source.Contains("LoadProfile(int hz)"));
        }

        [TestMethod]
        public void EcFanPolicy_ClampsDutyToZeroThrough100()
        {
            Assert.AreEqual(0, EcFanPolicy.ClampDuty(-10));
            Assert.AreEqual(0, EcFanPolicy.ClampDuty(0));
            Assert.AreEqual(40, EcFanPolicy.ClampDuty(40));
            Assert.AreEqual(100, EcFanPolicy.ClampDuty(120));
        }

        [TestMethod]
        public void EcFanPolicy_SnapsDutyToFivePercentSteps()
        {
            Assert.AreEqual(0, EcFanPolicy.SnapDuty(1));
            Assert.AreEqual(45, EcFanPolicy.SnapDuty(47));
            Assert.AreEqual(50, EcFanPolicy.SnapDuty(48));
            Assert.AreEqual(100, EcFanPolicy.SnapDuty(99));
        }

        [TestMethod]
        public void SourceProfiles_MapUsbCAndBatteryModes()
        {
            Assert.AreEqual(0x01, ProfileManager.SourceProfileMode(1));
            Assert.AreEqual(0x06, ProfileManager.SourceProfileMode(4));
            Assert.AreEqual(1, ProfileManager.SourceProfileIndex(0x01));
            Assert.AreEqual(4, ProfileManager.SourceProfileIndex(0x06));
            Assert.AreEqual("Eco", ProfileManager.SourceProfileNames[4]);

            string xaml = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Frontend", "Views", "AdvancedWindow.xaml"));
            Assert.Contains("Power Profile", xaml);
            Assert.Contains("Title=\"AC\"", xaml);
            Assert.Contains("Title=\"USB-C\"", xaml);
            Assert.Contains("Title=\"Battery\"", xaml);
            Assert.Contains("Windows Energy Saver", xaml);
            Assert.Contains("AcPowerModeIndex", xaml);
            Assert.Contains("UsbCPowerModeIndex", xaml);
            Assert.Contains("BatteryPowerModeIndex", xaml);
            Assert.DoesNotContain("IsPowerProfileSwitchEnabled", xaml);

            Assert.IsTrue(WmiController.TryParseAcerPowerEvent(new byte[] { 8, 0 }, out PowerSourceKind battery));
            Assert.AreEqual(PowerSourceKind.Battery, battery);
            Assert.IsTrue(WmiController.TryParseAcerPowerEvent(new byte[] { 8, 1 }, out PowerSourceKind ac));
            Assert.AreEqual(PowerSourceKind.Ac, ac);
            Assert.IsTrue(WmiController.TryParseAcerPowerEvent(new byte[] { 8, 4 }, out PowerSourceKind usbC));
            Assert.AreEqual(PowerSourceKind.UsbC, usbC);
            Assert.IsFalse(WmiController.TryParseAcerPowerEvent(new byte[] { 1, 1 }, out _));
            Assert.AreEqual(100, PowerLimitController.EnergySaverBatteryThreshold(true));
            Assert.AreEqual(0, PowerLimitController.EnergySaverBatteryThreshold(false));
        }

        private static string GetSolutionRoot()
        {
            DirectoryInfo? dir = new(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PreySense.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
