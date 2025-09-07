
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    private const double CpuHeadroomBuffer = 2;

    /// <summary>
    /// Threshold in milliseconds to consider a job "short" for faster concurrency scaling.
    /// </summary>
    private const double ShortJobThresholdMs = 100;

    /// <summary>
    /// The weight for the Exponential Moving Average (EMA) for smoothing CPU readings.
    /// </summary>
    private const double SmoothingFactor = 0.3;

    // --- State ---
    private readonly SmartDataProcessorSettings _settings;
    private readonly double _maxCpuUsage;
    private readonly ConcurrentQueue<(T Data, Action<T> Action)> _jobs = new();
    private readonly ConcurrentDictionary<Task, bool> _runningTasks = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ICpuMonitor _cpuMonitor;
    private object _managerLock = new();

    private Task? _managerTask;
    private double _smoothedCpu = 0;
    private int _targetConcurrency = 1;
    private double _lastAverageDuration;

    public ProcessingMetrics Metrics { get; } = new();
    public bool IsPaused { get; private set; }

    // --- Events ---
    public event Action<Exception> OnException;
    public event Action<double> OnCpuUsageChange;

    /// <summary>
    /// Creates a new SmartDataProcessor with default settings.
    /// </summary>
    public SmartDataProcessor() : this(new SmartDataProcessorSettings())
    {
    }

    /// <summary>
    /// Creates a new SmartDataProcessor with the specified maximum CPU usage.
    /// </summary>
    /// <param name="maxCpuUsage"> The maximum CPU usage percentage (0-100) to target.</param>
    public SmartDataProcessor(double maxCpuUsage) : this(new SmartDataProcessorSettings { MaxCpuUsage = maxCpuUsage })
    {
    }
    
    /// <summary>
    /// Creates a new SmartDataProcessor with the specified settings.
    /// </summary>
    /// <param name="settings">The settings to use for this processor.</param>
    public SmartDataProcessor(SmartDataProcessorSettings settings)
    {
        _settings = settings;
        _maxCpuUsage = Math.Max(_settings.MaxCpuUsage - CpuHeadroomBuffer, CpuHeadroomBuffer);

        bool useCpuMonitoring = _settings.MaxCpuUsage < 100;
        if (useCpuMonitoring)
        {
            if (OperatingSystem.IsWindows()) _cpuMonitor = new WindowsCpuMonitor();
            else if (OperatingSystem.IsLinux()) _cpuMonitor = new LinuxCpuMonitor();
            else if (OperatingSystem.IsMacOS()) _cpuMonitor = new MacCpuMonitor();
            else _cpuMonitor = new NullCpuMonitor();
        }
        else
        {
            _cpuMonitor = new NullCpuMonitor();
        }
    }

    internal SmartDataProcessor(SmartDataProcessorSettings settings, ICpuMonitor cpuMonitor)
    {
        _settings = settings;
        _maxCpuUsage = Math.Max(_settings.MaxCpuUsage - CpuHeadroomBuffer, CpuHeadroomBuffer);
        _cpuMonitor = cpuMonitor;
    }

    /// <summary>
    /// Pauses the processing of new items.
    /// </summary>
    public void Pause() => IsPaused = true;

    /// <summary>
    /// Resumes the processing of new items.
    /// </summary>
    public void Resume() => IsPaused = false;

    /// <summary>
    /// Enqueues a data item for processing. If the CPU is saturated or the queue is overloaded,
    /// this method will wait until it is safe to enqueue the item.
    /// </summary>
    /// <param name="data">Data to be processed</param>
    /// <param name="action">Action to process the data with</param>
    public async Task EnqueueOrWaitAsync(T data, Action<T> action)
    {
        LazyInitializer.EnsureInitialized(ref _managerTask, ref _managerLock, () => Task.Run(ManagerLoopAsync));

        while (true)
        {
            bool isCpuSaturated = _smoothedCpu > _maxCpuUsage && _cpuMonitor is not NullCpuMonitor;
            bool isQueueOverloaded = _jobs.Count > _targetConcurrency * _settings.QueueBufferMultiplier;

            if (!isCpuSaturated && !isQueueOverloaded)
            {
                break;
            }

            await Task.Delay(5);
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

        if (_managerTask != null)
        {
            await Task.WhenAll(_runningTasks.Keys.ToArray());
        }
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
            _managerTask?.Wait();
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
                if (!IsPaused)
                {
                    UpdateConcurrency();
                    LaunchWorkerTasks();
                }

                await Task.Delay(ManagerLoopIntervalMs, _cts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
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
        OnCpuUsageChange?.Invoke(_smoothedCpu);

        int maxConcurrency = _settings.MaxDegreeOfParallelism ?? Environment.ProcessorCount;

        // If CPU monitoring is disabled, just max out the concurrency.
        if (_cpuMonitor is NullCpuMonitor)
        {
            _targetConcurrency = maxConcurrency;
            return;
        }

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
        bool canIncreaseConcurrency = _targetConcurrency < maxConcurrency;

        if (hasCpuHeadroom && canIncreaseConcurrency)
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
                _targetConcurrency = Math.Min(_targetConcurrency + increaseAmount, maxConcurrency);
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
                    catch (Exception ex)
                    {
                        OnException?.Invoke(ex);
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