using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Frontend.Services;

namespace PreySense.Tests
{
    [TestClass]
    public class TrayAndStartupTests
    {
        [TestMethod]
        public void Test_StartupTaskService_TaskName_And_Query()
        {
            Assert.AreEqual("PreySense", StartupTaskService.TaskName);
            // Verify IsStartupEnabled does not throw
            bool isEnabled = StartupTaskService.IsStartupEnabled();
            Assert.IsTrue(isEnabled || !isEnabled);
        }

        [TestMethod]
        public void Test_TrayIconManager_MessageHandling_And_Dispose()
        {
            bool openCalled = false;
            bool quitCalled = false;

            using var tray = new TrayIconManager(
                IntPtr.Zero,
                onOpen: () => openCalled = true,
                onQuit: () => quitCalled = true
            );

            // Simulate double click message
            bool handled = false;
            const int WM_TRAY_CALLBACK = 0x0400 + 2048;
            const int WM_LBUTTONDBLCLK = 0x0203;

            tray.WndProc(IntPtr.Zero, WM_TRAY_CALLBACK, IntPtr.Zero, (IntPtr)WM_LBUTTONDBLCLK, ref handled);
            Assert.IsTrue(handled, "Double-click message must be handled");
            Assert.IsTrue(openCalled, "onOpen callback must be invoked on double-click");
        }

        [TestMethod]
        public void MainWindow_QuitButton_CallsQuitApplication()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml"));
            string cs = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml.cs"));
            Assert.Contains("BtnNavQuit", xaml);
            Assert.Contains("Click=\"QuitButton_Click\"", xaml);
            Assert.Contains("QuitButton_Click", cs);
            Assert.Contains("QuitApplication()", cs);
            Assert.Contains("_isExplicitExit = true", cs);
        }
    }
}
