using System.Collections.Concurrent;
using System.Linq;

namespace SimpliSharp.Utilities.Process;

public class ProcessingMetrics
{
    private const int MaxDurationSamples = 25;
    private readonly ConcurrentQueue<double> _jobDurations = new();
    
    public int MaxConcurrency { get; private set; }
    public double BestJobDuration { get; private set; } = double.MaxValue;
    public double AverageJobDuration { get; private set; }
    public double SlowestJobDuration { get; private set; }
    public int CurrentConcurrency { get; private set; }
    public int QueueLength { get; private set; }
    public double SmoothedCpu { get; private set; }

    internal void AddJobDuration(double duration)
    {
        _jobDurations.Enqueue(duration);
        
        BestJobDuration = Math.Min(BestJobDuration, duration);
        SlowestJobDuration = Math.Max(SlowestJobDuration, duration);
        AverageJobDuration = _jobDurations.Average();

        while (_jobDurations.Count > MaxDurationSamples)
            _jobDurations.TryDequeue(out _);
    }

    internal void UpdateConcurrency(int concurrency)
    {
        CurrentConcurrency = concurrency;
        MaxConcurrency = Math.Max(MaxConcurrency, concurrency);
    }

    internal void UpdateQueueLength(int queueLength)
    {
        QueueLength = queueLength;
    }
    
    internal void UpdateSmoothedCpu(double smoothedCpu)
    {
        SmoothedCpu = smoothedCpu;
    }
}
