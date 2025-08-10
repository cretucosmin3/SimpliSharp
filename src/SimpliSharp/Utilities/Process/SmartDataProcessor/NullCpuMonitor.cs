namespace SimpliSharp.Utilities.Process;

/// <summary>
/// A fallback monitor for non-Linux systems that does not report CPU usage.
/// </summary>
public class NullCpuMonitor : ICpuMonitor
{
    public double GetCpuUsage() => 0.0;
}