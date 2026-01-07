using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SimpliSharp.Utilities.Process;

public class SmartDataProcessor<T> : IAsyncDisposable, IDisposable
{
    // --- Configuration ---
    private const int MinCheckIntervalMs = 15;
    private const double CpuHeadroomBuffer = 2;
    private const double ShortJobThresholdMs = 100;
    private const double SmoothingFactor = 0.3;
    
    // --- Batch Optimization Constants ---
    private const double TargetTaskDurationMs = 50.0;
    private const int MinSamplesForOptimization = 10;

    // --- State ---
    private readonly SmartDataProcessorSettings _settings;
    private readonly double _maxCpuUsage;
    private readonly Channel<(T Data, Action<T> Action)> _jobChannel;
    private readonly ICpuMonitor _cpuMonitor;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _concurrencyCheckLock = new(1, 1);
    
    private Task? _managerTask;
    private int _activeTaskCount = 0;
    private double _smoothedCpu = 0;
    private int _targetConcurrency = 1;
    private double _lastAverageDuration;
    private bool _isInitialRampUp = true;
    private long _lastCheckTimestamp = 0;
    private bool _disposed = false;
    
    // --- Batch Optimization State ---
    private readonly object _optimizationLock = new();
    private double _smoothedTimePerItem = 0;
    private int _samplesCollected = 0;
    private int _optimalBatchSize = 1;

    public ProcessingMetrics Metrics { get; } = new();
    public bool IsPaused { get; private set; }
    
    /// <summary>
    /// The recommended number of items to batch together for optimal performance.
    /// Updates automatically as the processor learns from execution patterns.
    /// Returns 1 if batch size optimization is not applicable or insufficient data is available.
    /// </summary>
    public int OptimalBatchSize => _optimalBatchSize;
    
    /// <summary>
    /// Indicates whether enough data has been collected to provide a reliable batch size recommendation.
    /// </summary>
    public bool HasOptimalBatchSizeData => _samplesCollected >= MinSamplesForOptimization;

    // --- Events ---
    public event Action<Exception>? OnException;
    public event Action<double>? OnCpuUsageChange;
    public event Action<int>? OnOptimalBatchSizeChanged;

    public SmartDataProcessor() : this(new SmartDataProcessorSettings())
    {
    }

    public SmartDataProcessor(double maxCpuUsage) 
        : this(new SmartDataProcessorSettings { MaxCpuUsage = maxCpuUsage })
    {
    }
    
    public SmartDataProcessor(SmartDataProcessorSettings settings)
    {
        _settings = settings;
        _maxCpuUsage = Math.Max(_settings.MaxCpuUsage - CpuHeadroomBuffer, CpuHeadroomBuffer);

        // Create bounded channel for backpressure
        int capacity = (_settings.MaxDegreeOfParallelism ?? Environment.ProcessorCount) 
                       * _settings.QueueBufferMultiplier;
        _jobChannel = Channel.CreateBounded<(T, Action<T>)>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

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

        // Start the manager task immediately
        _managerTask = Task.Run(ManagerLoopAsync);
    }

    internal SmartDataProcessor(SmartDataProcessorSettings settings, ICpuMonitor cpuMonitor)
        : this(settings)
    {
        _cpuMonitor = cpuMonitor;
    }

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;

    /// <summary>
    /// Enqueues a data item for processing with natural backpressure.
    /// </summary>
    public async Task EnqueueOrWaitAsync(T data, Action<T> action, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check CPU saturation for additional backpressure
        while (_smoothedCpu > _maxCpuUsage && _cpuMonitor is not NullCpuMonitor)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        // Channel handles queue-based backpressure automatically
        await _jobChannel.Writer.WriteAsync((data, action), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for all currently queued and running jobs to complete.
    /// </summary>
    public async Task WaitForAllAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Wait for queue to drain
        while (_jobChannel.Reader.Count > 0)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }

        // Wait for active tasks to complete
        while (Interlocked.CompareExchange(ref _activeTaskCount, 0, 0) > 0)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Signal shutdown
        _jobChannel.Writer.Complete();
        await _cts.CancelAsync();

        // Wait for manager to finish
        if (_managerTask != null)
        {
            try
            {
                await _managerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during cancellation
            }
        }

        // Dispose resources
        _cts.Dispose();
        _concurrencyCheckLock.Dispose();
        
        if (_cpuMonitor is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// Main manager loop that coordinates task launching and concurrency adjustments.
    /// </summary>
    private async Task ManagerLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // Wait for work to be available or cancellation
                try
                {
                    await _jobChannel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (IsPaused)
                {
                    await Task.Delay(100, _cts.Token).ConfigureAwait(false);
                    continue;
                }

                // Throttle checks
                long currentTime = Stopwatch.GetTimestamp();
                long lastCheck = Interlocked.Read(ref _lastCheckTimestamp);
                long elapsedMs = (currentTime - lastCheck) * 1000 / Stopwatch.Frequency;

                if (elapsedMs >= MinCheckIntervalMs || lastCheck == 0)
                {
                    Interlocked.Exchange(ref _lastCheckTimestamp, currentTime);
                    
                    await CheckAndLaunchTasksAsync().ConfigureAwait(false);
                }
                else
                {
                    // Small delay to avoid tight loop
                    await Task.Delay(1, _cts.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            OnException?.Invoke(ex);
        }
    }

    /// <summary>
    /// Updates concurrency and launches worker tasks as needed.
    /// </summary>
    private async Task CheckAndLaunchTasksAsync()
    {
        // Use semaphore to prevent concurrent execution
        if (!await _concurrencyCheckLock.WaitAsync(0).ConfigureAwait(false))
        {
            return; // Another check is in progress
        }

        try
        {
            UpdateConcurrency();
            await LaunchWorkerTasksAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnException?.Invoke(ex);
        }
        finally
        {
            _concurrencyCheckLock.Release();
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

        if (_cpuMonitor is NullCpuMonitor)
        {
            _targetConcurrency = maxConcurrency;
            _isInitialRampUp = false;
            return;
        }

        switch (_settings.ScalingBehavior)
        {
            case ScalingBehavior.Aggressive:
                HandleAggressiveScaling(maxConcurrency);
                break;
            case ScalingBehavior.Normal:
                HandleNormalScaling(maxConcurrency);
                break;
            case ScalingBehavior.Gradual:
            default:
                HandleGradualScaling(maxConcurrency);
                break;
        }

        _lastAverageDuration = Metrics.AvgTaskTime;
    }

    private void HandleAggressiveScaling(int maxConcurrency)
    {
        if (_isInitialRampUp)
        {
            _targetConcurrency = maxConcurrency;
            _isInitialRampUp = false;
        }

        if (_smoothedCpu > _maxCpuUsage && _targetConcurrency > 1)
        {
            _targetConcurrency--;
        }
        else if (_smoothedCpu < _maxCpuUsage && _targetConcurrency < maxConcurrency)
        {
            int increaseAmount = Math.Max(1, (maxConcurrency - _targetConcurrency) / 2);
            _targetConcurrency = Math.Min(_targetConcurrency + increaseAmount, maxConcurrency);
        }
    }

    private void HandleNormalScaling(int maxConcurrency)
    {
        if (_isInitialRampUp)
        {
            if (_smoothedCpu < _maxCpuUsage - CpuHeadroomBuffer)
            {
                _targetConcurrency = maxConcurrency;
            }
            else if (_smoothedCpu > _maxCpuUsage)
            {
                _targetConcurrency = Math.Max(1, _targetConcurrency - 1);
                _isInitialRampUp = false;
            }
            else
            {
                _isInitialRampUp = false;
            }
            return;
        }

        ApplyStandardScaling(maxConcurrency);
    }

    private void HandleGradualScaling(int maxConcurrency)
    {
        _isInitialRampUp = false;
        ApplyStandardScaling(maxConcurrency);
    }

    private void ApplyStandardScaling(int maxConcurrency)
    {
        bool isCpuAboveMax = _smoothedCpu > _maxCpuUsage;
        bool canReduceConcurrency = _targetConcurrency > 1;

        if (isCpuAboveMax && canReduceConcurrency)
        {
            _targetConcurrency--;
            return;
        }

        bool hasCpuHeadroom = _smoothedCpu < _maxCpuUsage - CpuHeadroomBuffer;
        bool canIncreaseConcurrency = _targetConcurrency < maxConcurrency;

        if (hasCpuHeadroom && canIncreaseConcurrency)
        {
            bool isJobDurationIncreasing = Metrics.AvgTaskTime > _lastAverageDuration;
            bool hasHistoricData = _lastAverageDuration > 0;

            if (isJobDurationIncreasing && canReduceConcurrency && hasHistoricData)
            {
                // Hold steady to avoid contention
            }
            else
            {
                bool areJobsShort = Metrics.AvgTaskTime > 0 && Metrics.AvgTaskTime < ShortJobThresholdMs;
                int increaseAmount = areJobsShort ? 2 : 1;
                _targetConcurrency = Math.Min(_targetConcurrency + increaseAmount, maxConcurrency);
            }
        }
    }

    /// <summary>
    /// Launches new tasks from the channel to meet the target concurrency level.
    /// </summary>
    private async Task LaunchWorkerTasksAsync()
    {
        int activeCount = Interlocked.CompareExchange(ref _activeTaskCount, 0, 0);
        Metrics.UpdateConcurrency(activeCount);
        Metrics.UpdateQueueLength(_jobChannel.Reader.Count);

        int slotsToFill = _targetConcurrency - activeCount;
        
        for (int i = 0; i < slotsToFill; i++)
        {
            if (_jobChannel.Reader.TryRead(out var job))
            {
                Interlocked.Increment(ref _activeTaskCount);
                
                // Launch worker without tracking the Task object
                _ = ProcessJobAsync(job);
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>
    /// Processes a single job and updates metrics.
    /// </summary>
    private async Task ProcessJobAsync((T Data, Action<T> Action) job)
    {
        var sw = Stopwatch.StartNew();
        int itemCount = EstimateItemCount(job.Data);

        try
        {
            // Run synchronous action on thread pool
            await Task.Run(() => job.Action(job.Data)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnException?.Invoke(ex);
        }
        finally
        {
            sw.Stop();
            double durationMs = sw.Elapsed.TotalMilliseconds;
            
            Metrics.AddJobDuration(durationMs);
            Interlocked.Decrement(ref _activeTaskCount);
            
            // Update batch size optimization
            if (itemCount > 0)
            {
                UpdateBatchSizeOptimization(itemCount, durationMs);
            }
        }
    }

    /// <summary>
    /// Estimates the number of items in the data being processed.
    /// Returns 0 if item count cannot be determined.
    /// </summary>
    private static int EstimateItemCount(T data)
    {
        return data switch
        {
            Array array => array.Length,
            System.Collections.ICollection collection => collection.Count,
            System.Collections.Generic.ICollection<object> genericCollection => genericCollection.Count,
            System.Collections.Generic.IReadOnlyCollection<object> readOnlyCollection => readOnlyCollection.Count,
            _ => 0
        };
    }

    /// <summary>
    /// Updates the optimal batch size recommendation based on observed execution patterns.
    /// </summary>
    private void UpdateBatchSizeOptimization(int itemCount, double durationMs)
    {
        lock (_optimizationLock)
        {
            double timePerItem = durationMs / itemCount;
            
            // Use exponential moving average for smoothing
            const double alpha = 0.2;
            if (_samplesCollected == 0)
            {
                _smoothedTimePerItem = timePerItem;
            }
            else
            {
                _smoothedTimePerItem = (alpha * timePerItem) + ((1 - alpha) * _smoothedTimePerItem);
            }
            
            _samplesCollected++;

            // Only update recommendation after collecting sufficient samples
            if (_samplesCollected >= MinSamplesForOptimization)
            {
                int newOptimalSize = CalculateOptimalBatchSize(_smoothedTimePerItem, Metrics.AvgTaskTime);
                
                if (newOptimalSize != _optimalBatchSize)
                {
                    _optimalBatchSize = newOptimalSize;
                    OnOptimalBatchSizeChanged?.Invoke(_optimalBatchSize);
                }
            }
        }
    }

    /// <summary>
    /// Calculates the optimal batch size based on time per item and average task duration.
    /// </summary>
    private static int CalculateOptimalBatchSize(double timePerItem, double avgTaskDuration)
    {
        if (timePerItem <= 0)
            return 1;

        // If tasks are already in the sweet spot, don't change batch size
        if (avgTaskDuration >= TargetTaskDurationMs * 0.8 && 
            avgTaskDuration <= TargetTaskDurationMs * 1.2)
        {
            return 1;
        }

        // Calculate batch size to reach target duration
        int calculatedSize = (int)Math.Ceiling(TargetTaskDurationMs / timePerItem);

        // Apply reasonable bounds based on current task duration
        if (avgTaskDuration < 5) // Very light tasks
        {
            return Math.Clamp(calculatedSize, 100, 10000);
        }
        else if (avgTaskDuration < 50) // Light tasks
        {
            return Math.Clamp(calculatedSize, 10, 1000);
        }
        else if (avgTaskDuration < 200) // Medium tasks
        {
            return Math.Clamp(calculatedSize, 1, 100);
        }
        else if (avgTaskDuration < 500) // Heavy tasks
        {
            return Math.Clamp(calculatedSize, 1, 10);
        }
        else // Very heavy tasks
        {
            return 1;
        }
    }

    /// <summary>
    /// Gets a human-readable recommendation for batch sizing based on current data.
    /// </summary>
    public string GetBatchSizeRecommendation()
    {
        if (!HasOptimalBatchSizeData)
        {
            return "Insufficient data collected. Process at least 10 batches to get a recommendation.";
        }

        double avgTaskTime = Metrics.AvgTaskTime;
        
        string workloadType = avgTaskTime switch
        {
            < 5 => "very light",
            < 50 => "light",
            < 200 => "medium",
            < 500 => "heavy",
            _ => "very heavy"
        };

        if (_optimalBatchSize == 1)
        {
            return $"Tasks are {workloadType} ({avgTaskTime:F2}ms avg). " +
                   $"Current granularity is optimal - continue processing individual items.";
        }
        else
        {
            return $"Tasks are {workloadType} ({avgTaskTime:F2}ms avg). " +
                   $"Recommend batching {_optimalBatchSize} items together to reduce overhead " +
                   $"and target ~{TargetTaskDurationMs}ms per task.";
        }
    }
}