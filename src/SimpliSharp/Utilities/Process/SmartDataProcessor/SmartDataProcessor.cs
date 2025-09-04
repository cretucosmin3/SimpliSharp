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
    private const int QueueBufferMultiplier = 2;

    // --- State ---
    private readonly double _maxCpuUsage;
    private readonly ConcurrentQueue<(T Data, Action<T> Action)> _jobs = new();
    private readonly ConcurrentDictionary<Task, bool> _runningTasks = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ICpuMonitor _cpuMonitor;
    private readonly Task _managerTask;

    private double _smoothedCpu = 0;
    private int _targetConcurrency = 1;
    private double _lastAverageDuration;

    public ProcessingMetrics Metrics { get; } = new();

    /// <summary>
    /// Creates a new SmartDataProcessor with the specified maximum CPU usage.
    /// </summary>
    /// <param name="maxCpuUsage"> The maximum CPU usage percentage (0-100) to target.</param>
    public SmartDataProcessor(double maxCpuUsage = 100)
    {
        _maxCpuUsage = Math.Max(maxCpuUsage - CpuHeadroomBuffer, CpuHeadroomBuffer);

        // OS-specific monitor logic is unchanged...
        if (OperatingSystem.IsWindows()) _cpuMonitor = new WindowsCpuMonitor();
        else if (OperatingSystem.IsLinux()) _cpuMonitor = new LinuxCpuMonitor();
        else if (OperatingSystem.IsMacOS()) _cpuMonitor = new MacCpuMonitor();
        else _cpuMonitor = new NullCpuMonitor();

        _managerTask = Task.Run(ManagerLoopAsync);
    }

    internal SmartDataProcessor(double maxCpuUsage, ICpuMonitor cpuMonitor)
    {
        _maxCpuUsage = maxCpuUsage;
        _cpuMonitor = cpuMonitor;

        _managerTask = Task.Run(ManagerLoopAsync);
    }

    /// <summary>
    /// Enqueues a data item for processing. If the CPU is saturated or the queue is overloaded,
    /// this method will block until it is safe to enqueue the item.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="action"></param>
    public void EnqueueOrWait(T data, Action<T> action)
    {
        while (true)
        {
            bool isCpuSaturated = _smoothedCpu > _maxCpuUsage && _cpuMonitor is not NullCpuMonitor;
            bool isQueueOverloaded = _jobs.Count > _targetConcurrency * QueueBufferMultiplier;

            if (!isCpuSaturated && !isQueueOverloaded)
            {
                break;
            }

            Thread.Sleep(10);
        }

        _jobs.Enqueue((data, action));
    }

    /// <summary>
    /// Waits for all currently queued and running jobs to complete.
    /// </summary>
    /// <returns></returns>
    public async Task WaitForAllAsync()
    {
        while (!_jobs.IsEmpty)
        {
            await Task.Delay(50);
        }

        await Task.WhenAll(_runningTasks.Keys.ToArray());
    }

    /// <summary>
    /// Disposes the processor, stopping all management and worker tasks.
    /// Note: This does not cancel running jobs; it only stops accepting new ones and waits
    /// for current jobs to finish.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();

        try
        {
            _managerTask.Wait();
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is TaskCanceledException))
        {
            // This is expected when the task is cancelled. We can ignore it.
        }
        finally
        {
            _cts.Dispose();
        }
    }

    /// <summary>
    /// The main management loop that coordinates concurrency adjustments and task launching.
    /// </summary>
    private async Task ManagerLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                UpdateConcurrency();
                LaunchWorkerTasks();

                await Task.Delay(ManagerLoopIntervalMs, _cts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ManagerLoopAsync: {ex.Message}");
            }
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

        // --- Concurrency Decrease Logic ---
        bool isCpuAboveMax = _smoothedCpu > _maxCpuUsage;
        bool canReduceConcurrency = _targetConcurrency > 1;

        if (isCpuAboveMax && canReduceConcurrency)
        {
            _targetConcurrency--;
            _lastAverageDuration = Metrics.AvgTaskTime;
            return;
        }

        // --- Concurrency Increase Logic ---
        bool hasCpuHeadroom = _smoothedCpu < _maxCpuUsage - CpuHeadroomBuffer;
        bool isMonitorDisabled = _cpuMonitor is NullCpuMonitor;
        bool canIncreaseConcurrency = _targetConcurrency < Environment.ProcessorCount;

        if ((hasCpuHeadroom || isMonitorDisabled) && canIncreaseConcurrency)
        {
            // Check if adding threads is causing performance to degrade (contention).
            bool isJobDurationIncreasing = Metrics.AvgTaskTime > _lastAverageDuration;
            bool hasHistoricData = _lastAverageDuration > 0;

            if (isJobDurationIncreasing && canReduceConcurrency && hasHistoricData)
            {
                // Duration is increasing, which might indicate thread contention.
                // Hold the current concurrency level to allow the system to stabilize.
            }
            else
            {
                // If jobs are very short, we can be more aggressive in scaling up.
                bool areJobsShort = Metrics.AvgTaskTime > 0 && Metrics.AvgTaskTime < ShortJobThresholdMs;
                int increaseAmount = areJobsShort ? 2 : 1;
                _targetConcurrency = Math.Min(_targetConcurrency + increaseAmount, Environment.ProcessorCount);
            }
        }

        _lastAverageDuration = Metrics.AvgTaskTime;
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