namespace SimpliSharp.Utilities.Process;

/// <summary>
/// A CPU monitor for Linux that reads /proc/stat.
/// </summary>
public class LinuxCpuMonitor : ICpuMonitor
{
    private long _prevIdle, _prevTotal;

    public LinuxCpuMonitor()
    {
        ReadCpuStats(out _prevIdle, out _prevTotal);
    }

    public double GetCpuUsage()
    {
        ReadCpuStats(out var idle, out var total);

        var idleDiff = idle - _prevIdle;
        var totalDiff = total - _prevTotal;

        _prevIdle = idle;
        _prevTotal = total;

        if (totalDiff == 0) return 0;
        return (1.0 - (idleDiff / (double)totalDiff)) * 100.0;
    }

    private void ReadCpuStats(out long idle, out long total)
    {
        idle = 0;
        total = 0;
        
        try
        {
            var cpuLine = File.ReadAllLines("/proc/stat")[0];
            var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            idle = long.Parse(parts[4]);
            total = parts.Skip(1).Select(long.Parse).Sum();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not read CPU stats: {ex.Message}");
        }
    }
}