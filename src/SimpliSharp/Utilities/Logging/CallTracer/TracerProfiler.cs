
using System.Diagnostics;
using System.Threading;

namespace SimpliSharp.Utilities.Logging;

/// <summary>
/// Provides a way to profile the performance overhead of the CallTracer itself.
/// This class is thread-safe.
/// </summary>
public static class TracerProfiler
{
    private static long _totalOverheadTicks = 0;

    /// <summary>
    /// Gets the total accumulated overhead time spent in tracing operations.
    /// </summary>
    public static TimeSpan TotalOverhead => TimeSpan.FromTicks(_totalOverheadTicks);

    /// <summary>
    /// Adds a duration to the total overhead.
    /// </summary>
    internal static void Add(TimeSpan duration)
    {
        Interlocked.Add(ref _totalOverheadTicks, duration.Ticks);
    }

    /// <summary>
    /// Resets the total overhead counter to zero.
    /// </summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref _totalOverheadTicks, 0);
    }
}
