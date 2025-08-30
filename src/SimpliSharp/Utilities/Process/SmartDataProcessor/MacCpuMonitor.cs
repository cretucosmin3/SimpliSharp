using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SimpliSharp.Utilities.Process;

[SupportedOSPlatform("macos")]
public class MacCpuMonitor : ICpuMonitor
{
    [DllImport("libc")]
    private static extern int host_statistics(IntPtr host_port, int flavor, out CpuLoadInfo cpu_load_info, ref uint count);

    private const int HOST_CPU_LOAD_INFO = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct CpuLoadInfo
    {
        public uint cpu_ticks_user;
        public uint cpu_ticks_system;
        public uint cpu_ticks_idle;
        public uint cpu_ticks_nice;
    }

    private CpuLoadInfo _previousCpuLoadInfo;

    public MacCpuMonitor()
    {
        _previousCpuLoadInfo = GetCpuLoadInfo();
    }

    public double GetCpuUsage()
    {
        var currentCpuLoadInfo = GetCpuLoadInfo();

        var userDiff = currentCpuLoadInfo.cpu_ticks_user - _previousCpuLoadInfo.cpu_ticks_user;
        var systemDiff = currentCpuLoadInfo.cpu_ticks_system - _previousCpuLoadInfo.cpu_ticks_system;
        var niceDiff = currentCpuLoadInfo.cpu_ticks_nice - _previousCpuLoadInfo.cpu_ticks_nice;
        var idleDiff = currentCpuLoadInfo.cpu_ticks_idle - _previousCpuLoadInfo.cpu_ticks_idle;

        var totalTicks = userDiff + systemDiff + niceDiff + idleDiff;
        if (totalTicks == 0)
        {
            return 0.0f;
        }

        var usedTicks = userDiff + systemDiff + niceDiff;
        var cpuUsage = (double)usedTicks / totalTicks * 100.0f;

        _previousCpuLoadInfo = currentCpuLoadInfo;

        return cpuUsage;
    }

    private static CpuLoadInfo GetCpuLoadInfo()
    {
        uint count = (uint)Marshal.SizeOf<CpuLoadInfo>() / sizeof(uint);

        if (host_statistics(mach_host_self(), HOST_CPU_LOAD_INFO, out var cpuLoadInfo, ref count) != 0)
        {
            throw new Exception("Failed to retrieve CPU load info.");
        }

        return cpuLoadInfo;
    }

    [DllImport("libc")]
    private static extern IntPtr mach_host_self();
}
