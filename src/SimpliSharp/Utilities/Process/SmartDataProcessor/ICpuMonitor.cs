namespace SimpliSharp.Utilities.Process;

/// <summary>
/// Interface for a platform-specific CPU usage monitor.
/// </summary>
public interface ICpuMonitor
{
    /// <summary>
    /// Gets the current CPU usage percentage.
    /// </summary>
    double GetCpuUsage();
}