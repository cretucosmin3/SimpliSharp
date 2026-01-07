using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace SimpliSharp.Utilities.Process.Examples;

public static class ComparisonExamples
{
    /// <summary>
    /// Compares SmartDataProcessor with auto-learning batch optimization 
    /// against SmartDataProcessor without adaptation and ActionBlock
    /// </summary>
    public static async Task CompareSmartProcessorVsActionBlock()
    {
        Console.WriteLine("=== Performance Comparison: SmartDataProcessor vs ActionBlock ===\n");

        // Increased dataset size for longer running test (15+ seconds)
        int totalItems = 500_000;
        var allData = Enumerable.Range(0, totalItems).ToArray();
        
        Console.WriteLine($"Dataset: {totalItems:N0} items");
        Console.WriteLine($"Expected duration: ~15-20 seconds per test\n");

        // Test 1: ActionBlock (Standard TPL Dataflow)
        Console.WriteLine("Test 1: ActionBlock (Standard TPL Dataflow)");
        Console.WriteLine(new string('-', 60));
        var actionBlockResult = await RunWithActionBlock(allData);
        
        Console.WriteLine($"\n✓ ActionBlock completed in {actionBlockResult.Duration:F2}s");
        Console.WriteLine($"  Throughput: {actionBlockResult.Throughput:F0} items/sec");
        Console.WriteLine($"  Max Parallelism: {actionBlockResult.MaxParallelism}");
        Console.WriteLine($"  Batch Size: {actionBlockResult.OptimalBatchSize} (fixed)");
        Console.WriteLine($"  CPU Usage: N/A (not monitored)");

        Console.WriteLine("\n" + new string('=', 60) + "\n");

        // Small delay between tests
        await Task.Delay(2000);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(1000);

        // Test 2: SmartDataProcessor without adaptation (fixed batch size)
        Console.WriteLine("Test 2: SmartDataProcessor (Fixed Batch Size - No Adaptation)");
        Console.WriteLine(new string('-', 60));
        var smartProcessorFixedResult = await RunWithSmartProcessorFixed(allData);
        
        Console.WriteLine($"\n✓ SmartDataProcessor (Fixed) completed in {smartProcessorFixedResult.Duration:F2}s");
        Console.WriteLine($"  Throughput: {smartProcessorFixedResult.Throughput:F0} items/sec");
        Console.WriteLine($"  Max Concurrency: {smartProcessorFixedResult.MaxParallelism}");
        Console.WriteLine($"  Batch Size: {smartProcessorFixedResult.OptimalBatchSize} (fixed)");
        Console.WriteLine($"  Average CPU: {smartProcessorFixedResult.AvgCpu:F1}%");

        Console.WriteLine("\n" + new string('=', 60) + "\n");

        // Small delay between tests
        await Task.Delay(2000);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(1000);

        // Test 3: SmartDataProcessor with auto-learning and adaptation
        Console.WriteLine("Test 3: SmartDataProcessor (Adaptive with Auto-Learning)");
        Console.WriteLine(new string('-', 60));
        var smartProcessorResult = await RunWithSmartProcessor(allData);
        
        Console.WriteLine($"\n✓ SmartDataProcessor (Adaptive) completed in {smartProcessorResult.Duration:F2}s");
        Console.WriteLine($"  Throughput: {smartProcessorResult.Throughput:F0} items/sec");
        Console.WriteLine($"  Max Concurrency: {smartProcessorResult.MaxParallelism}");
        Console.WriteLine($"  Learned Optimal Batch: {smartProcessorResult.OptimalBatchSize}");
        Console.WriteLine($"  Average CPU: {smartProcessorResult.AvgCpu:F1}%");
        Console.WriteLine($"  Recommendation: {smartProcessorResult.Recommendation}");

        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("COMPARISON SUMMARY");
        Console.WriteLine(new string('=', 60));

        // Calculate differences
        var results = new[]
        {
            ("ActionBlock", actionBlockResult),
            ("SmartProcessor (Fixed)", smartProcessorFixedResult),
            ("SmartProcessor (Adaptive)", smartProcessorResult)
        };

        var fastest = results.OrderByDescending(r => r.Item2.Throughput).First();

        Console.WriteLine($"\n{"Approach",-30} {"Duration",-12} {"Throughput",-15} {"Relative"}");
        Console.WriteLine(new string('-', 70));

        foreach (var (name, result) in results)
        {
            double relativeDiff = ((result.Throughput - fastest.Item2.Throughput) / fastest.Item2.Throughput) * 100;
            string marker = name == fastest.Item1 ? "🏆 Fastest" : $"{relativeDiff:F1}%";
            
            Console.WriteLine($"{name,-30} {result.Duration,-12:F2}s {result.Throughput,-15:F0}/sec {marker}");
        }

        Console.WriteLine("\nKey Differences:");
        Console.WriteLine($"• ActionBlock: Fixed parallelism ({actionBlockResult.MaxParallelism}), No CPU monitoring");
        Console.WriteLine($"• SmartProcessor (Fixed): Adaptive concurrency ({smartProcessorFixedResult.MaxParallelism}), CPU monitoring ({smartProcessorFixedResult.AvgCpu:F1}%)");
        Console.WriteLine($"• SmartProcessor (Adaptive): Learned batch size ({smartProcessorResult.OptimalBatchSize}), Adaptive concurrency ({smartProcessorResult.MaxParallelism}), CPU monitoring ({smartProcessorResult.AvgCpu:F1}%)");
    }

    /// <summary>
    /// Detailed comparison showing both approaches side-by-side with identical workload
    /// </summary>
    public static async Task DetailedComparison()
    {
        Console.WriteLine("=== Detailed Side-by-Side Comparison ===\n");

        // Increased for longer running test
        int totalItems = 50000;
        var allData = Enumerable.Range(0, totalItems).ToArray();

        Console.WriteLine($"Workload: {totalItems:N0} items");
        Console.WriteLine($"Work per item: 500,000 iterations");
        Console.WriteLine($"Expected duration per run: ~12-15 seconds\n");

        // Run both tests multiple times for consistency
        int runs = 3;
        var actionBlockTimes = new double[runs];
        var smartProcessorTimes = new double[runs];

        for (int run = 0; run < runs; run++)
        {
            Console.WriteLine($"\n--- Run {run + 1}/{runs} ---");

            // ActionBlock
            Console.WriteLine("Running ActionBlock...");
            var abResult = await RunWithActionBlock(allData, useHeavierWorkload: true);
            actionBlockTimes[run] = abResult.Duration;
            Console.WriteLine($"Completed in {abResult.Duration:F2}s");

            // Cleanup and delay between tests
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(2000);

            // SmartDataProcessor
            Console.WriteLine("Running SmartDataProcessor...");
            var spResult = await RunWithSmartProcessor(allData, useHeavierWorkload: true);
            smartProcessorTimes[run] = spResult.Duration;
            Console.WriteLine($"Completed in {spResult.Duration:F2}s");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(2000);
        }

        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("AGGREGATE RESULTS");
        Console.WriteLine(new string('=', 60));

        double abAvg = actionBlockTimes.Average();
        double spAvg = smartProcessorTimes.Average();
        double abMin = actionBlockTimes.Min();
        double spMin = smartProcessorTimes.Min();

        Console.WriteLine("\nActionBlock:");
        Console.WriteLine($"  Average: {abAvg:F2}s");
        Console.WriteLine($"  Best:    {abMin:F2}s");
        Console.WriteLine($"  Worst:   {actionBlockTimes.Max():F2}s");
        Console.WriteLine($"  Std Dev: {CalculateStdDev(actionBlockTimes):F2}s");

        Console.WriteLine("\nSmartDataProcessor:");
        Console.WriteLine($"  Average: {spAvg:F2}s");
        Console.WriteLine($"  Best:    {spMin:F2}s");
        Console.WriteLine($"  Worst:   {smartProcessorTimes.Max():F2}s");
        Console.WriteLine($"  Std Dev: {CalculateStdDev(smartProcessorTimes):F2}s");

        double avgImprovement = ((abAvg - spAvg) / abAvg) * 100;
        
        if (avgImprovement > 0)
        {
            Console.WriteLine($"\n🚀 SmartDataProcessor was {avgImprovement:F1}% faster on average");
        }
        else
        {
            Console.WriteLine($"\n⚠️  ActionBlock was {-avgImprovement:F1}% faster on average");
        }
    }

    // ==================== HELPER METHODS ====================

    private static async Task<ProcessingResult> RunWithActionBlock(int[] allData, bool useHeavierWorkload = false)
    {
        var stopwatch = Stopwatch.StartNew();
        int maxParallelism = Environment.ProcessorCount;

        // Choose workload intensity
        int iterationsPerItem = useHeavierWorkload ? 500_000 : 50_000;

        Action<int[]> processAction = data =>
        {
            double sum = 0;
            foreach (var item in data)
            {
                for (int j = 0; j < iterationsPerItem; j++)
                {
                    sum += Math.Sqrt(item + j) * 0.001;
                }
            }
        };

        var executionOptions = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = maxParallelism,
            BoundedCapacity = maxParallelism * 10
        };

        var actionBlock = new ActionBlock<int[]>(processAction, executionOptions);

        // Use fixed batch size for ActionBlock
        int batchSize = 10;
        int batches = 0;
        
        for (int i = 0; i < allData.Length; i += batchSize)
        {
            var batch = allData.Skip(i).Take(batchSize).ToArray();
            await actionBlock.SendAsync(batch);
            batches++;

            if (batches % 50 == 0)
            {
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"Progress: {i}/{allData.Length} ({(i * 100.0 / allData.Length):F1}%) | Batch: {batchSize} | Elapsed: {stopwatch.Elapsed.TotalSeconds:F1}s     ");
            }
        }

        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write($"Progress: {allData.Length}/{allData.Length} (100.0%) | Waiting for completion...                    ");

        actionBlock.Complete();
        await actionBlock.Completion;
        stopwatch.Stop();

        return new ProcessingResult
        {
            Duration = stopwatch.Elapsed.TotalSeconds,
            Throughput = allData.Length / stopwatch.Elapsed.TotalSeconds,
            MaxParallelism = maxParallelism,
            OptimalBatchSize = batchSize, // Fixed
            AvgCpu = 0, // Not monitored
            Recommendation = "N/A"
        };
    }

    private static async Task<ProcessingResult> RunWithSmartProcessorFixed(int[] allData, bool useHeavierWorkload = false)
    {
        var settings = new SmartDataProcessorSettings
        {
            MaxCpuUsage = 95,
            ScalingBehavior = ScalingBehavior.Aggressive
        };

        using var processor = new SmartDataProcessor<int[]>(settings);
        
        double totalCpu = 0;
        int cpuSamples = 0;

        processor.OnCpuUsageChange += cpu =>
        {
            totalCpu += cpu;
            cpuSamples++;
        };

        var stopwatch = Stopwatch.StartNew();
        
        // Choose workload intensity
        int iterationsPerItem = useHeavierWorkload ? 500_000 : 50_000;
        
        // Use FIXED batch size - never adapt
        // Use same as ActionBlock for fair comparison
        int fixedBatchSize = 10;
        int itemsProcessed = 0;
        int batchCount = 0;

        while (itemsProcessed < allData.Length)
        {
            int batchSize = Math.Min(fixedBatchSize, allData.Length - itemsProcessed);
            var batch = allData.Skip(itemsProcessed).Take(batchSize).ToArray();

            await processor.EnqueueOrWaitAsync(batch, data =>
            {
                double sum = 0;
                foreach (var item in data)
                {
                    for (int j = 0; j < iterationsPerItem; j++)
                    {
                        sum += Math.Sqrt(item + j) * 0.001;
                    }
                }
            });

            itemsProcessed += batchSize;
            batchCount++;

            // DO NOT adapt - keep fixed batch size
            // Note: processor still learns optimal size internally, we just don't use it

            if (batchCount % 50 == 0)
            {
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"Progress: {itemsProcessed}/{allData.Length} ({(itemsProcessed * 100.0 / allData.Length):F1}%) | " +
                             $"Batch: {fixedBatchSize} (fixed) | " +
                             $"Learned: {processor.OptimalBatchSize} | " +
                             $"Queue: {processor.Metrics.QueueLength} | " +
                             $"CPU: {(cpuSamples > 0 ? totalCpu / cpuSamples : 0):F1}% | " +
                             $"Elapsed: {stopwatch.Elapsed.TotalSeconds:F1}s" + new string(' ', 20));
            }
        }

        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write($"Progress: {allData.Length}/{allData.Length} (100.0%) | Waiting for completion..." + new string(' ', 80));

        await processor.WaitForAllAsync();
        stopwatch.Stop();

        double avgCpu = cpuSamples > 0 ? totalCpu / cpuSamples : 0;

        return new ProcessingResult
        {
            Duration = stopwatch.Elapsed.TotalSeconds,
            Throughput = allData.Length / stopwatch.Elapsed.TotalSeconds,
            MaxParallelism = processor.Metrics.MaxConcurrency,
            OptimalBatchSize = fixedBatchSize, // Report the fixed size used
            AvgCpu = avgCpu,
            Recommendation = $"Used fixed batch size {fixedBatchSize}. Processor learned optimal: {processor.OptimalBatchSize}"
        };
    }

    private static async Task<ProcessingResult> RunWithSmartProcessor(int[] allData, bool useHeavierWorkload = false)
    {
        var settings = new SmartDataProcessorSettings
        {
            MaxCpuUsage = 95,
            ScalingBehavior = ScalingBehavior.Aggressive
        };

        using var processor = new SmartDataProcessor<int[]>(settings);
        
        double totalCpu = 0;
        int cpuSamples = 0;
        int batchSizeChanges = 0;
        int lastBatchSize = 0;

        processor.OnCpuUsageChange += cpu =>
        {
            totalCpu += cpu;
            cpuSamples++;
        };

        int batchSizeUpdateLine = Console.CursorTop + 1; // Reserve next line for updates

        processor.OnOptimalBatchSizeChanged += newSize =>
        {
            batchSizeChanges++;
            int currentLine = Console.CursorTop;
            
            // Move to dedicated update line
            Console.SetCursorPosition(0, batchSizeUpdateLine);
            Console.Write(new string(' ', Console.WindowWidth - 1)); // Clear line
            Console.SetCursorPosition(0, batchSizeUpdateLine);
            Console.Write($"💡 Batch size: {lastBatchSize} → {newSize} (change #{batchSizeChanges})");
            
            // Return to progress line
            Console.SetCursorPosition(0, currentLine);
            
            lastBatchSize = newSize;
        };

        var stopwatch = Stopwatch.StartNew();
        
        // Choose workload intensity
        int iterationsPerItem = useHeavierWorkload ? 500_000 : 50_000;
        
        // Start with small batches
        int currentBatchSize = 10;
        lastBatchSize = currentBatchSize;
        int itemsProcessed = 0;
        int batchCount = 0;

        while (itemsProcessed < allData.Length)
        {
            int batchSize = Math.Min(currentBatchSize, allData.Length - itemsProcessed);
            var batch = allData.Skip(itemsProcessed).Take(batchSize).ToArray();

            await processor.EnqueueOrWaitAsync(batch, data =>
            {
                double sum = 0;
                foreach (var item in data)
                {
                    for (int j = 0; j < iterationsPerItem; j++)
                    {
                        sum += Math.Sqrt(item + j) * 0.001;
                    }
                }
            });

            itemsProcessed += batchSize;
            batchCount++;

            // Adapt to processor's recommendation
            if (processor.HasOptimalBatchSizeData)
            {
                currentBatchSize = (int)(processor.OptimalBatchSize * 1.5);
            }

            if (batchCount % 50 == 0)
            {
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"Progress: {itemsProcessed}/{allData.Length} ({(itemsProcessed * 100.0 / allData.Length):F1}%) | " +
                             $"Batch: {currentBatchSize} | " +
                             $"Optimal: {processor.OptimalBatchSize} | " +
                             $"Queue: {processor.Metrics.QueueLength} | " +
                             $"CPU: {(cpuSamples > 0 ? totalCpu / cpuSamples : 0):F1}% | " +
                             $"Elapsed: {stopwatch.Elapsed.TotalSeconds:F1}s" + new string(' ', 20)); // Padding to clear old text
            }
        }

        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write($"Progress: {allData.Length}/{allData.Length} (100.0%) | Waiting for completion..." + new string(' ', 80));

        await processor.WaitForAllAsync();
        stopwatch.Stop();

        // Clear the batch size update line
        Console.SetCursorPosition(0, batchSizeUpdateLine);
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.SetCursorPosition(0, batchSizeUpdateLine);

        double avgCpu = cpuSamples > 0 ? totalCpu / cpuSamples : 0;

        return new ProcessingResult
        {
            Duration = stopwatch.Elapsed.TotalSeconds,
            Throughput = allData.Length / stopwatch.Elapsed.TotalSeconds,
            MaxParallelism = processor.Metrics.MaxConcurrency,
            OptimalBatchSize = processor.OptimalBatchSize,
            AvgCpu = avgCpu,
            Recommendation = processor.GetBatchSizeRecommendation()
        };
    }

    private static double CalculateStdDev(double[] values)
    {
        if (values.Length < 2) return 0;
        
        double avg = values.Average();
        double sumSquaredDiffs = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumSquaredDiffs / (values.Length - 1));
    }

    private record ProcessingResult
    {
        public double Duration { get; init; }
        public double Throughput { get; init; }
        public int MaxParallelism { get; init; }
        public int OptimalBatchSize { get; init; }
        public double AvgCpu { get; init; }
        public string Recommendation { get; init; } = string.Empty;
    }
}

// ==================== SIMPLE USAGE EXAMPLES ====================

public static class SimpleExamples
{
    /// <summary>
    /// Simple ActionBlock example for reference
    /// </summary>
    public static async Task ActionBlock_Simple()
    {
        Console.WriteLine("Simple ActionBlock Example\n");

        var tasksCount = 10000; // Increased
        var stopwatch = Stopwatch.StartNew();

        Action<int> processAction = data =>
        {
            double sum = 0;
            for (int j = 0; j < 1_000_000; j++) // Increased workload
            {
                double value = Math.Sqrt(j) * Math.Sin(j % 360) + Math.Log(j + 1);
                if (value > 1000)
                    sum -= value / 3.0;
                else
                    sum += value * 2.5;
            }
        };

        var executionOptions = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            BoundedCapacity = Environment.ProcessorCount * 10
        };

        var actionBlock = new ActionBlock<int>(processAction, executionOptions);

        for (int i = 0; i < tasksCount; i++)
        {
            await actionBlock.SendAsync(i);

            if (i % 100 == 0)
            {
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"Posting item {i + 1} of {tasksCount} | Elapsed: {stopwatch.Elapsed.TotalSeconds:F1}s     ");
            }
        }

        Console.WriteLine("\nAll items posted. Waiting for completion...");

        actionBlock.Complete();
        await actionBlock.Completion;
        stopwatch.Stop();

        Console.WriteLine($"\n✓ Completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        Console.WriteLine($"Throughput: {tasksCount / stopwatch.Elapsed.TotalSeconds:F0} items/sec");
    }

    /// <summary>
    /// Simple SmartDataProcessor example with auto-learning
    /// </summary>
    public static async Task SmartDataProcessor_Simple()
    {
        Console.WriteLine("Simple SmartDataProcessor Example (Auto-Learning)\n");

        var settings = new SmartDataProcessorSettings
        {
            MaxCpuUsage = 95,
            ScalingBehavior = ScalingBehavior.Normal
        };

        using var processor = new SmartDataProcessor<int[]>(settings);
        
        int batchSizeUpdateLine = Console.CursorTop + 1; // Reserve line for updates
        int lastBatchSize = 10;
        int batchSizeChanges = 0;

        processor.OnOptimalBatchSizeChanged += newSize =>
        {
            batchSizeChanges++;
            int currentLine = Console.CursorTop;
            
            Console.SetCursorPosition(0, batchSizeUpdateLine);
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.SetCursorPosition(0, batchSizeUpdateLine);
            Console.Write($"💡 Batch size: {lastBatchSize} → {newSize} (change #{batchSizeChanges})");
            
            Console.SetCursorPosition(0, currentLine);
            lastBatchSize = newSize;
        };

        var stopwatch = Stopwatch.StartNew();
        int totalItems = 100000; // Increased
        var allData = Enumerable.Range(0, totalItems).ToArray();
        
        int currentBatchSize = 10;
        int itemsProcessed = 0;
        int updates = 0;

        while (itemsProcessed < totalItems)
        {
            int batchSize = Math.Min(currentBatchSize, totalItems - itemsProcessed);
            var batch = allData.Skip(itemsProcessed).Take(batchSize).ToArray();

            await processor.EnqueueOrWaitAsync(batch, data =>
            {
                double sum = 0;
                foreach (var item in data)
                {
                    for (int j = 0; j < 50_000; j++)
                    {
                        sum += Math.Sqrt(item + j) * 0.001;
                    }
                }
            });

            itemsProcessed += batchSize;

            if (processor.HasOptimalBatchSizeData)
            {
                currentBatchSize = processor.OptimalBatchSize;
            }

            if (++updates % 100 == 0)
            {
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"Progress: {itemsProcessed}/{totalItems} ({(itemsProcessed * 100.0 / totalItems):F1}%) | " +
                             $"Batch: {currentBatchSize} | " +
                             $"Queue: {processor.Metrics.QueueLength} | " +
                             $"Elapsed: {stopwatch.Elapsed.TotalSeconds:F1}s" + new string(' ', 20));
            }
        }

        await processor.WaitForAllAsync();
        stopwatch.Stop();

        // Clear update line and move past it
        Console.SetCursorPosition(0, batchSizeUpdateLine);
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.WriteLine();

        Console.WriteLine($"\n✓ Completed in {stopwatch.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"Throughput: {totalItems / stopwatch.Elapsed.TotalSeconds:F0} items/sec");
        Console.WriteLine($"\nRecommendation: {processor.GetBatchSizeRecommendation()}");
    }
}