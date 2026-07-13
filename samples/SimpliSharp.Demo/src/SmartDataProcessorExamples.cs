using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace SimpliSharp.Utilities.Process.Examples;

public static class ComparisonExamples
{
    // --- Configuration ---
    private const int TotalItems = 1_000_000; 
    private const int WorkIterations = 10_000; // Tuned for a balance of CPU vs Scheduling
    
    /// <summary>
    /// Compares standard TPL ActionBlock against the optimized SmartDataProcessor.
    /// </summary>
    public static async Task CompareSmartProcessorVsActionBlock()
    {
        Console.WriteLine("=== Performance Comparison: SmartDataProcessor vs ActionBlock ===\n");
        Console.WriteLine($"Dataset: {TotalItems:N0} items");
        Console.WriteLine($"Workload: {WorkIterations:N0} math ops per item");
        Console.WriteLine($"CPU Limit (SmartProcessor): 90%\n");

        var allData = Enumerable.Range(0, TotalItems).ToArray();

        // --- Test 1: ActionBlock ---
        Console.WriteLine("Test 1: ActionBlock (Standard TPL)");
        Console.WriteLine(new string('-', 60));
        var actionBlockResult = await RunWithActionBlock(allData);
        PrintResult(actionBlockResult);

        // Cool down
        await PerformGcAndCooldown();

        // --- Test 2: SmartDataProcessor ---
        Console.WriteLine("Test 2: SmartDataProcessor (Optimized)");
        Console.WriteLine(new string('-', 60));
        var smartProcessorResult = await RunWithSmartProcessor(allData);
        PrintResult(smartProcessorResult);

        // --- Summary ---
        PrintSummary(actionBlockResult, smartProcessorResult);
    }

    private static async Task<ProcessingResult> RunWithActionBlock(int[] data)
    {
        var stopwatch = Stopwatch.StartNew();
        int maxParallelism = Environment.ProcessorCount;

        // ActionBlock Setup
        var options = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = maxParallelism,
            SingleProducerConstrained = true
        };

        var actionBlock = new ActionBlock<int>(item => SimulateWork(item), options);

        // Feed Data
        foreach (var item in data)
        {
            await actionBlock.SendAsync(item);
        }

        actionBlock.Complete();
        await actionBlock.Completion;
        stopwatch.Stop();

        return new ProcessingResult
        {
            Name = "ActionBlock",
            Duration = stopwatch.Elapsed.TotalSeconds,
            Throughput = data.Length / stopwatch.Elapsed.TotalSeconds,
            MaxConcurrency = maxParallelism,
            AvgCpu = -1 // Not monitored
        };
    }

    private static async Task<ProcessingResult> RunWithSmartProcessor(int[] data)
    {
        var settings = new SmartDataProcessorSettings
        {
            MaxCpuUsage = 95,
            ScalingBehavior = ScalingBehavior.Aggressive
        };

        using var processor = new SmartDataProcessor<int>(settings);
        
        // Track CPU locally for the report
        double totalCpu = 0;
        int cpuSamples = 0;
        processor.OnCpuUsageChange += cpu => { totalCpu += cpu; cpuSamples++; };

        var stopwatch = Stopwatch.StartNew();
        int reportedCount = 0;

        // Feed Data
        foreach (var item in data)
        {
            // We feed individual items to test the overhead of the Enqueue system
            await processor.EnqueueOrWaitAsync(item, SimulateWork);

            // Simple progress bar
            if (++reportedCount % 50_000 == 0)
            {
                Console.Write(".");
            }
        }
        Console.WriteLine();

        await processor.WaitForAllAsync();
        stopwatch.Stop();

        return new ProcessingResult
        {
            Name = "SmartDataProcessor",
            Duration = stopwatch.Elapsed.TotalSeconds,
            Throughput = data.Length / stopwatch.Elapsed.TotalSeconds,
            MaxConcurrency = processor.Metrics.MaxConcurrency,
            AvgCpu = cpuSamples > 0 ? totalCpu / cpuSamples : 0
        };
    }

    // --- Helper Logic ---

    // The actual work payload - identical for both
    private static void SimulateWork(int input)
    {
        double sum = 0;
        for (int i = 0; i < WorkIterations; i++)
        {
            sum += Math.Sqrt(input + i) * Math.Sin(i);
        }
    }

    private static async Task PerformGcAndCooldown()
    {
        Console.WriteLine("\n... Cooldown & GC ...\n");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(2000);
    }

    private static void PrintResult(ProcessingResult result)
    {
        Console.WriteLine($"\n✓ {result.Name} completed in {result.Duration:F2}s");
        Console.WriteLine($"  Throughput:      {result.Throughput:N0} items/sec");
        Console.WriteLine($"  Max Concurrency: {result.MaxConcurrency}");
        if (result.AvgCpu > 0)
            Console.WriteLine($"  Avg CPU (Gov):   {result.AvgCpu:F1}%");
        Console.WriteLine(new string('=', 60) + "\n");
    }

    private static void PrintSummary(ProcessingResult ab, ProcessingResult sp)
    {
        Console.WriteLine("COMPARISON SUMMARY");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"{"Metric",-20} {"ActionBlock",-15} {"SmartProcessor",-15} {"Difference"}");
        Console.WriteLine(new string('-', 65));

        // Duration
        double durDiff = ((sp.Duration - ab.Duration) / ab.Duration) * 100;
        string durSign = durDiff < 0 ? "-" : "+";
        Console.WriteLine($"{"Duration",-20} {ab.Duration,-15:F2} {sp.Duration,-15:F2} {durSign}{Math.Abs(durDiff):F1}%");

        // Throughput
        double tpDiff = ((sp.Throughput - ab.Throughput) / ab.Throughput) * 100;
        string tpMarker = tpDiff > 0 ? "🚀 Faster" : "Slower";
        Console.WriteLine($"{"Throughput",-20} {ab.Throughput,-15:N0} {sp.Throughput,-15:N0} {tpMarker} ({tpDiff:F1}%)");

        Console.WriteLine(new string('-', 65));
        Console.WriteLine("Observations:");
        Console.WriteLine("1. SmartProcessor maintains CPU limits (if set), ActionBlock creates tasks blindly.");
        Console.WriteLine("2. SmartProcessor reduces GC pressure by reusing worker threads.");
        Console.WriteLine("3. SmartProcessor handles backpressure automatically via the QueueBuffer.");
    }

    private record ProcessingResult
    {
        public string Name { get; init; } = "";
        public double Duration { get; init; }
        public double Throughput { get; init; }
        public int MaxConcurrency { get; init; }
        public double AvgCpu { get; init; }
    }
}