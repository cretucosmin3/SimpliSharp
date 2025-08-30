using System;
using SimpliSharp.Utilities.Process;

namespace SimpliSharp.Tests.Utilities.Process.SmartDataProcessor;

[TestClass]
public class CpuMonitorTests
{
    [TestMethod]
    public void LinuxCpuMonitor_GetCpuUsage_ReturnsValueBetween0And100()
    {
        // Arrange
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("This test is for Linux only.");
            return;
        }

        var monitor = new LinuxCpuMonitor();

        // Act
        var usage = monitor.GetCpuUsage();

        // Assert
        Assert.IsTrue(usage >= 0 && usage <= 100);
    }

    [TestMethod]
    public void WindowsCpuMonitor_GetCpuUsage_ReturnsValueBetween0And100()
    {
        // Arrange
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This test is for Windows only.");
            return;
        }

        var monitor = new WindowsCpuMonitor();

        // Act
        var usage = monitor.GetCpuUsage();

        // Assert
        Assert.IsTrue(usage >= 0 && usage <= 100);
    }

    [TestMethod]
    public void MacCpuMonitor_GetCpuUsage_ReturnsValueBetween0And100()
    {
        // Arrange
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("This test is for macOS only.");
            return;
        }

        var monitor = new MacCpuMonitor();

        // Act
        var usage = monitor.GetCpuUsage();

        // Assert
        Assert.IsTrue(usage >= 0 && usage <= 100);
    }
}
