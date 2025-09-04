<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://github.com/cretucosmin3/SimpliSharp/blob/main/assets/simpli-sharp-light.png?raw=true">
    <source media="(prefers-color-scheme: light)" srcset="https://github.com/cretucosmin3/SimpliSharp/blob/main/assets/simpli-sharp-dark.png?raw=true">
    <img alt="SimpliSharp" src="assets/simpli-sharp-dark.png" height="110">
  </picture>
  
<table width="600" align="center">
  <tr>
    <td align="center">
      <strong>SimpliSharp is a C# utility library designed to streamline development with useful extensions, helpers, data processing tools, and logging helpers.</strong>
    </td>
  </tr>
</table>

[![.NET](https://github.com/cretucosmin3/SimpliSharp/actions/workflows/dotnet.yml/badge.svg)](https://github.com/cretucosmin3/SimpliSharp/actions/workflows/dotnet.yml)[![GitHub last commit](https://img.shields.io/github/last-commit/cretucosmin3/SimpliSharp.svg)](https://github.com/cretucosmin3/SimpliSharp/commits/main)
[![GitHub stars](https://img.shields.io/github/stars/cretucosmin3/SimpliSharp.svg)](https://github.com/cretucosmin3/SimpliSharp/stargazers)

</div>

## SmartDataProcessor

The `SmartDataProcessor<T>` is designed to process a queue of items in parallel, while automatically adjusting the level of concurrency to stay within a specified CPU usage limit.

### Features
- **Dynamic Concurrency**: Automatically adjusts the number of worker threads based on real-time CPU load.
- **CPU Throttling**: Ensures that CPU usage does not exceed a configurable maximum limit.
- **Backpressure**: The `EnqueueOrWait` method blocks when the queue is full or the CPU is saturated, preventing memory overload.
- **Lazy Initialization**: The processing thread pool is only created when the first item is enqueued.
- **Configurable**: Fine-tune performance with the `SmartDataProcessorSettings` class.
- **Event-driven**: Subscribe to events for CPU usage changes and exceptions.
- **Runtime Control**: Pause and resume the processor on the fly.

### Usage Example

You can now configure the processor using the `SmartDataProcessorSettings` class:

```csharp
var settings = new SmartDataProcessorSettings
{
    MaxCpuUsage = 80, // Target 80% CPU usage
    MaxDegreeOfParallelism = 4, // Use a maximum of 4 threads
    QueueBufferMultiplier = 8 // Set a larger queue buffer
};

using var processor = new SmartDataProcessor<int>(settings);

// Subscribe to events
processor.OnCpuUsageChange += (cpuLoad) => Console.WriteLine($"CPU Load: {cpuLoad:F1}%");
processor.OnException += (ex) => Console.WriteLine($"An error occurred: {ex.Message}");

// Enqueue items
for (int i = 0; i < 100; i++)
{
    processor.EnqueueOrWait(i, data =>
    {
        // Your processing logic here...
    });
}

// Pause and resume processing
processor.Pause();
Thread.Sleep(5000);
processor.Resume();

processor.WaitForAllAsync().Wait();
```

![Alt text for your image](https://raw.githubusercontent.com/cretucosmin3/SimpliSharp/refs/heads/main/assets/75-cpu-usage.png)

## Enumerable Extensions

### Batching

Batching enumerables using `Enumerables.Batch(batchSize)` or `Enumerables.BatchSliding(windowSize)` will simply yield the requested batches,

```csharp
string[] sample = ["Red", "Blue", "Purple", "Black", "Yellow", "Pink"];
string[][] batches = sample.Batch(3).ToArray();

// Batch 1: [Red, Blue, Purple]
// Batch 2: [Black, Yellow, Pink]
```

```csharp
int[] sample = [1, 2, 3];
int[][] batches = sample.BatchSliding(2).ToArray();

// Batch 1: [1, 2]
// Batch 2: [2, 3]
```
