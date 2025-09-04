namespace SimpliSharp.Tests.Utilities.Process.SmartDataProcessor;

using SimpliSharp.Utilities.Process;

public class MockCpuMonitor : ICpuMonitor
{
    private double _cpuUsage;

    public void SetCpuUsage(double cpuUsage)
    {
        _cpuUsage = cpuUsage;
    }

    public double GetCpuUsage()
    {
        return _cpuUsage;
    }
}
