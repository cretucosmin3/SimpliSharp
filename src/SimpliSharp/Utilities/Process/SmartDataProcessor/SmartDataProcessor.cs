using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;

namespace SimpliSharp.Utilities.Process;

public class SmartDataProcessor<T> : IDisposable
{
    // --- Configuration ---

    /// <summary>
    /// Interval in milliseconds for the manager loop to check CPU usage and manage concurrency.
    /// </summary>
    private const int ManagerLoopIntervalMs = 50;

    /// <summary>
    /// A buffer to keep CPU usage below the absolute maximum, allowing for scaling.
    /// </summary>
    private const double CpuHeadroomBuffer = 5;

    /// <summary>
    /// Threshold in milliseconds to consider a job "short" for faster concurrency scaling.
    /// </summary>
    private const double ShortJobThresholdMs = 100;

    /// <summary>
    /// The weight for the Exponential Moving Average (EMA) for smoothing CPU readings.
    /// </summary>
    private const double SmoothingFactor = 0.3;

    /// <summary>
    /// A multiplier to determine the queue size limit based on the current number of workers.
    /// This creates back-pressure to prevent the queue from growing too quickly.
    /// </summary>
    private const int QueueBufferMultiplier = 10;

    // --- State ---
    private readonly double _maxCpuUsage;
    private readonly ConcurrentQueue<(T Data, Action<T> Action)> _jobs = new();
    private readonly ConcurrentDictionary<Task, bool> _runningTasks = new();
    private readonly Thread _managerThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly ICpuMonitor _cpuMonitor;
    internal readonly ManualResetEvent ManagerLoopCycle = new(false);

    private double _smoothedCpu = 0;
    private int _targetConcurrency = 1;
    private double _lastAverageDuration;

    public ProcessingMetrics Metrics { get; } = new();

    public SmartDataProcessor(double maxCpuUsage = 100)
    {
        _maxCpuUsage = Math.Max(maxCpuUsage - CpuHeadroomBuffer, CpuHeadroomBuffer);

        if (OperatingSystem.IsWindows())
        {
            _cpuMonitor = new WindowsCpuMonitor();
        }
        else if (OperatingSystem.IsLinux())
        {
            _cpuMonitor = new LinuxCpuMonitor();
        }
        else if (OperatingSystem.IsMacOS())
        {
            _cpuMonitor = new MacCpuMonitor();
        }
        else
        {
            _cpuMonitor = new NullCpuMonitor();
        }

        _managerThread = new Thread(ManagerLoop) { IsBackground = true };
        _managerThread.Start();
    }

    internal SmartDataProcessor(double maxCpuUsage, ICpuMonitor cpuMonitor)
    {
        _maxCpuUsage = maxCpuUsage;
        _cpuMonitor = cpuMonitor;

        _managerThread = new Thread(ManagerLoop) { IsBackground = true };
        _managerThread.Start();
    }

    public void EnqueueOrWait(T data, Action<T> action)
    {
        // Wait if CPU is saturated OR if the queue is growing too large relative to our processing power.
        while ((_smoothedCpu > _maxCpuUsage && _cpuMonitor is not NullCpuMonitor) ||
               (_jobs.Count > _targetConcurrency * QueueBufferMultiplier))
        {
            Thread.Sleep(10);
        }

        _jobs.Enqueue((data, action));
    }

    public async Task WaitForAllAsync()
    {
        while (!_jobs.IsEmpty)
        {
            await Task.Delay(50);
        }

        await Task.WhenAll(_runningTasks.Keys.ToArray());
    }

    public void Dispose()
    {
        _cts.Cancel();
        _managerThread.Join();
    }

    /// <summary>
    /// The main management loop that coordinates concurrency adjustments and task launching.
    /// </summary>
    private void ManagerLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                UpdateConcurrency();
                LaunchWorkerTasks();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ManagerLoop: {ex.Message}");
            }

            ManagerLoopCycle.Set();
            Thread.Sleep(ManagerLoopIntervalMs);
        }
    }

    /// <summary>
    /// Adjusts the target concurrency level based on CPU usage and job duration.
    /// </summary>
    private void UpdateConcurrency()
    {
        double cpuUsage = _cpuMonitor.GetCpuUsage();
        _smoothedCpu = (SmoothingFactor * cpuUsage) + (1 - SmoothingFactor) * _smoothedCpu;
        Metrics.UpdateSmoothedCpu(_smoothedCpu);

        if (_smoothedCpu > _maxCpuUsage && _targetConcurrency > 1)
        {
            _targetConcurrency--;
        }
        else if ((_smoothedCpu < _maxCpuUsage - CpuHeadroomBuffer || _cpuMonitor is NullCpuMonitor)
                 && _targetConcurrency < Environment.ProcessorCount)
        {
            // If the average duration is increasing, it means we might be adding too much concurrency.
            if (Metrics.AverageJobDuration > _lastAverageDuration && _targetConcurrency > 1 && _lastAverageDuration > 0)
            {
                // Don't increase concurrency for now, let's see if the duration stabilizes.
            }
            else
            {
                int increase = (Metrics.AverageJobDuration > 0 && Metrics.AverageJobDuration < ShortJobThresholdMs) ? 2 : 1;
                _targetConcurrency = Math.Min(_targetConcurrency + increase, Environment.ProcessorCount);
            }
        }

        _lastAverageDuration = Metrics.AverageJobDuration;
    }

    /// <summary>
    /// Launches new tasks from the queue to meet the target concurrency level.
    /// </summary>
    private void LaunchWorkerTasks()
    {
        // Clean up completed tasks from our tracking dictionary
        foreach (var task in _runningTasks.Keys.Where(t => t.IsCompleted).ToList())
        {
            _runningTasks.TryRemove(task, out _);
        }

        Metrics.UpdateConcurrency(_runningTasks.Count);
        Metrics.UpdateQueueLength(_jobs.Count);

        int slotsToFill = _targetConcurrency - _runningTasks.Count;
        for (int i = 0; i < slotsToFill; i++)
        {
            if (_jobs.TryDequeue(out var job))
            {
                var task = Task.Run(() =>
                {
                    var sw = Stopwatch.StartNew();

                    try
                    {
                        job.Action(job.Data);
                    }
                    finally
                    {
                        sw.Stop();
                        Metrics.AddJobDuration(sw.Elapsed.TotalMilliseconds);
                    }
                });
                _runningTasks.TryAdd(task, true);
            }
            else
            {
                break;
            }
        }
    }
}
