using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SimpliSharp.Utilities.Process;

[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct PaddedInt64
{
    [FieldOffset(64)]
    public long Value;
}

[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct PaddedDouble
{
    [FieldOffset(64)]
    public double Value;
}

public sealed class SmartDataProcessor<T> : IAsyncDisposable, IDisposable
{
    // --- Constants ---
    private const int MinCheckIntervalMs = 20;
    private const double SmoothingFactor = 0.2;
    private const int LocalBatchFetchSize = 16; 

    // --- State ---
    private readonly SmartDataProcessorSettings _settings;
    private readonly Channel<Job> _jobChannel;
    private readonly ICpuMonitor _cpuMonitor;
    private readonly CancellationTokenSource _cts = new();
    
    // --- Hot Counters (Padded) ---
    // Total workers currently alive (working OR waiting)
    private PaddedInt64 _activeWorkerCount; 
    // Workers currently executing a job (not waiting on channel)
    private PaddedInt64 _busyWorkerCount;   
    private PaddedInt64 _totalProcessedCount;
    private PaddedDouble _smoothedCpu;
    
    // Cold State
    private readonly Task _managerTask;
    private volatile int _targetConcurrency;
    private bool _disposed;
    
    private readonly ProcessingMetrics _metrics = new();
    
    public ProcessingMetrics Metrics => _metrics;
    public bool IsPaused { get; private set; }

    public event Action<Exception>? OnException;
    public event Action<double>? OnCpuUsageChange;
    
    private readonly struct Job
    {
        public readonly T Data;
        public readonly Action<T> Action;

        public Job(T data, Action<T> action)
        {
            Data = data;
            Action = action;
        }
    }

    public SmartDataProcessor(SmartDataProcessorSettings? settings = null)
    {
        _settings = settings ?? new SmartDataProcessorSettings();
        
        var options = new BoundedChannelOptions((_settings.MaxDegreeOfParallelism ?? Environment.ProcessorCount) * _settings.QueueBufferMultiplier)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false, 
            SingleWriter = false,
            AllowSynchronousContinuations = false 
        };
        
        _jobChannel = Channel.CreateBounded<Job>(options);

        // CPU Monitor Setup
        if (_settings.MaxCpuUsage < 100)
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
        
        _targetConcurrency = Math.Max(1, (_settings.MaxDegreeOfParallelism ?? Environment.ProcessorCount) / 2);
        
        _managerTask = Task.Factory.StartNew(ManagerLoopAsync, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask EnqueueOrWaitAsync(T data, Action<T> action, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SmartDataProcessor<T>));

        // Fast path
        if (_jobChannel.Writer.TryWrite(new Job(data, action)))
        {
            return ValueTask.CompletedTask;
        }

        return _jobChannel.Writer.WriteAsync(new Job(data, action), cancellationToken);
    }

    /// <summary>
    /// Waits until the queue is empty AND all workers are idle (not processing items).
    /// Does NOT dispose the processor.
    /// </summary>
    public async Task WaitForAllAsync()
    {
        if (_disposed) return;

        while (true)
        {
            bool isQueueEmpty = _jobChannel.Reader.Count == 0;
            // We check if BusyWorkers are 0, not ActiveWorkers
            long busyWorkers = Interlocked.Read(ref _busyWorkerCount.Value);

            if (isQueueEmpty && busyWorkers == 0)
            {
                // Double check to prevent race where item was added just as we checked
                if (_jobChannel.Reader.Count == 0)
                    return;
            }

            await Task.Delay(15).ConfigureAwait(false);
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _jobChannel.Writer.Complete();
        await _cts.CancelAsync();

        try { await _managerTask.ConfigureAwait(false); } catch { }

        _cts.Dispose();
        (_cpuMonitor as IDisposable)?.Dispose();
    }

    [SkipLocalsInit]
    private async Task ManagerLoopAsync()
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(MinCheckIntervalMs));
        int maxConcurrency = _settings.MaxDegreeOfParallelism ?? Environment.ProcessorCount;
        
        while (await timer.WaitForNextTickAsync(_cts.Token))
        {
            if (IsPaused) continue;

            AdjustConcurrency(maxConcurrency);
            
            long active = Interlocked.Read(ref _activeWorkerCount.Value);
            int target = _targetConcurrency;
            int queueCount = _jobChannel.Reader.Count;

            // Spawn logic
            if (active < target && queueCount > 0)
            {
                int needed = target - (int)active;
                int spawnBatch = Math.Min(needed, 4); 

                for (int i = 0; i < spawnBatch; i++)
                {
                    _ = Task.Run(WorkerLoop); 
                }
            }
        }
    }

    [SkipLocalsInit]
    private async Task WorkerLoop()
    {
        Interlocked.Increment(ref _activeWorkerCount.Value);
        
        // We use a local flag to ensure we decrement Active count correctly in finally block
        // regardless of how we exit (exception, cancellation, or suicide)
        bool suicide = false;

        try
        {
            var reader = _jobChannel.Reader;
            var sw = new Stopwatch();
            Job[] localBuffer = new Job[LocalBatchFetchSize];

            while (!_cts.IsCancellationRequested)
            {
                // --- ATOMIC SCALE DOWN CHECK ---
                long currentActive = Interlocked.Read(ref _activeWorkerCount.Value);
                int currentTarget = _targetConcurrency;

                if (currentActive > currentTarget)
                {
                    // Attempt to suicide using CAS
                    // We only break if WE are the one who successfully decremented the counter
                    if (Interlocked.CompareExchange(ref _activeWorkerCount.Value, currentActive - 1, currentActive) == currentActive)
                    {
                        suicide = true; 
                        return; // Exit immediately
                    }
                }

                // --- FETCH WORK ---
                int fetched = 0;
                
                // 1. Try greedy synchronous read
                if (reader.TryRead(out localBuffer[0]))
                {
                    fetched = 1;
                    while (fetched < LocalBatchFetchSize && reader.TryRead(out localBuffer[fetched]))
                    {
                        fetched++;
                    }
                }
                else
                {
                    // 2. Async wait if empty
                    try 
                    {
                        await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false);
                        continue; 
                    }
                    catch (OperationCanceledException) { break; }
                }

                // --- PROCESS BATCH ---
                // Mark as busy before processing
                Interlocked.Add(ref _busyWorkerCount.Value, 1);
                
                try
                {
                    for (int i = 0; i < fetched; i++)
                    {
                        ProcessSingleJob(localBuffer[i], sw);
                        localBuffer[i] = default; 
                    }
                }
                finally
                {
                    // Always mark as not busy after batch
                    Interlocked.Add(ref _busyWorkerCount.Value, -1);
                }
            }
        }
        catch (Exception ex)
        {
            OnException?.Invoke(ex);
        }
        finally
        {
            // Only decrement if we didn't already decrement via the suicide path
            if (!suicide)
            {
                Interlocked.Decrement(ref _activeWorkerCount.Value);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessSingleJob(Job job, Stopwatch sw)
    {
        sw.Restart();
        try
        {
            job.Action(job.Data);
        }
        catch (Exception ex)
        {
            OnException?.Invoke(ex);
        }
        sw.Stop();
        
        _metrics.AddJobDuration(sw.Elapsed.TotalMilliseconds);
        Interlocked.Increment(ref _totalProcessedCount.Value);
    }

    private void AdjustConcurrency(int maxConcurrency)
    {
        if (_cpuMonitor is NullCpuMonitor)
        {
            _targetConcurrency = maxConcurrency;
            return;
        }

        double cpu = _cpuMonitor.GetCpuUsage();
        
        double currentSmoothed = _smoothedCpu.Value;
        double newSmoothed = (SmoothingFactor * cpu) + ((1.0 - SmoothingFactor) * currentSmoothed);
        _smoothedCpu.Value = newSmoothed;
        
        OnCpuUsageChange?.Invoke(newSmoothed);

        int target = _targetConcurrency;
        double maxCpu = _settings.MaxCpuUsage;

        // More aggressive scaling down to prevent lock-up at 100%
        if (newSmoothed > maxCpu)
        {
            if (target > 1) 
                _targetConcurrency = target - 1;
        }
        else if (newSmoothed < (maxCpu - 5.0))
        {
            if (target < maxConcurrency) 
                _targetConcurrency = target + 1;
        }
    }
}