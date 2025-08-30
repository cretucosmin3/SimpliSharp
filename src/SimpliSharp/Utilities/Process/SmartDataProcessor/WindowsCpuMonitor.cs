
using System.Diagnostics;
using System.Runtime.Versioning;

namespace SimpliSharp.Utilities.Process;

/// <summary>
/// A CPU monitor for Windows systems.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsCpuMonitor : ICpuMonitor
{
    private readonly PerformanceCounter _cpuCounter;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsCpuMonitor"/> class.
    /// </summary>
    public WindowsCpuMonitor()
    {
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _cpuCounter.NextValue();
    }

    /// <summary>
    /// Gets the current CPU usage percentage.
    /// </summary>
    public double GetCpuUsage()
    {
        return _cpuCounter.NextValue();
    }
}
