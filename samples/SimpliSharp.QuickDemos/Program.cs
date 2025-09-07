using System.Threading.Tasks.Dataflow;
using SimpliSharp.Extensions.Batch;
using SimpliSharp.Utilities.Process;

Console.WriteLine("SimpliSharp Demo Application");
Console.WriteLine("---------------------------");

Console.WriteLine("Available Demos:");
Console.WriteLine("1. SmartDataProcessor Example");
Console.WriteLine("2. ActionBlock Example (from TPL Dataflow)");
Console.WriteLine("3. Enumerable.Batch");
Console.WriteLine("4. Enumerable.BatchSliding");

Console.WriteLine("[Enter] to exit");

var choice = Console.ReadKey();

switch (choice.Key)
{
    case ConsoleKey.D1:
    case ConsoleKey.NumPad1:
        SmartDataProcessor_Example().Wait();
        break;
    case ConsoleKey.D2:
    case ConsoleKey.NumPad2:
        ActionBlock_Example().Wait();
        break;
    case ConsoleKey.D3:
    case ConsoleKey.NumPad3:
        EnumerableBatch_Example();
        break;
    case ConsoleKey.D4:
    case ConsoleKey.NumPad4:
        EnumerableBatchSliding_Example();
        break;
    default:
        Console.WriteLine("\nExiting...");
        return;
}

Console.WriteLine();
Console.WriteLine("Press any key to exit...");
Console.ReadKey();

static async Task SmartDataProcessor_Example()
{
    Console.WriteLine("Starting data processing...");

    var settings = new SmartDataProcessorSettings
    {
        MaxCpuUsage = 95
    };

    using var processor = new SmartDataProcessor<int>(settings);

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var tasksCount = 1000;

    for (int i = 0; i < tasksCount; i++)
    {
        int line = i;

        await processor.EnqueueOrWaitAsync(line, data =>
        {
            double sum = 0;

            for (int j = 0; j < 10_000_000; j++)
            {
                double value = Math.Sqrt(j) * Math.Sin(j % 360) + Math.Log(j + 1);
                if (value > 1000)
                    sum -= value / 3.0;
                else
                    sum += value * 2.5;
            }
        });

        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write($"Processing item {i + 1} of {tasksCount} | queued: {processor.Metrics.QueueLength}");
    }

    processor.WaitForAllAsync().Wait();
    stopwatch.Stop();

    Console.WriteLine();
    Console.WriteLine($"Processing completed in {stopwatch.Elapsed.TotalSeconds} seconds");

    var finalMetrics = processor.Metrics;
    Console.WriteLine();
    Console.WriteLine("--- Final Metrics ---");
    Console.WriteLine($"Max Concurrency: {finalMetrics.MaxConcurrency}");
    Console.WriteLine($"Fastest Job: {finalMetrics.MinTaskTime:F2}ms");
    Console.WriteLine($"Slowest Job: {finalMetrics.MaxTaskTime:F2}ms");
    Console.WriteLine($"Average Job: {finalMetrics.AvgTaskTime:F2}ms");

    Console.WriteLine("All processing done");
}

static async Task ActionBlock_Example()
{
    Console.WriteLine("Starting data processing with ActionBlock...");

    var tasksCount = 1000;
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    Action<int> processAction = data =>
    {
        double sum = 0;
        for (int j = 0; j < 10_000_000; j++)
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
        MaxDegreeOfParallelism = Environment.ProcessorCount
    };

    var actionBlock = new ActionBlock<int>(processAction, executionOptions);

    for (int i = 0; i < tasksCount; i++)
    {
        await actionBlock.SendAsync(i);

        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write($"Posting item {i + 1} of {tasksCount}");
    }

    Console.WriteLine("\nAll items have been posted. Waiting for processing to complete...");

    actionBlock.Complete();

    await actionBlock.Completion;

    stopwatch.Stop();

    Console.WriteLine($"\nProcessing completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
    Console.WriteLine("All processing done.");
}

static void EnumerableBatch_Example()
{
    Console.WriteLine("Starting Enumerable.Batch demo...");

    string[] sample = ["Red", "Blue", "Purple", "Black", "Yellow", "Pink"];
    int batchSize = 3;

    var batches = sample.Batch(batchSize).ToList();

    Console.WriteLine($"Sample numbers: [{string.Join(", ", batches)}]");
    Console.WriteLine($"Batch size: {batchSize}");
    Console.WriteLine();

    Console.WriteLine($"{batches.Count} Batches created:");
    for (int i = 0; i < batches.Count; i++)
    {
        Console.WriteLine($"Batch {i + 1}: [{string.Join(", ", batches[i])}]");
    }
}

static void EnumerableBatchSliding_Example()
{
    Console.WriteLine("Starting Enumerable.BatchSliding demo...");

    var numbers = Enumerable.Range(1, 3);
    int batchSize = 2;

    var batches = numbers.BatchSliding(batchSize).ToList();

    Console.WriteLine();
    Console.WriteLine($"Sample numbers: [{string.Join(", ", numbers)}]");
    Console.WriteLine();

    Console.WriteLine($"{batches.Count} batches created:");
    for (int i = 0; i < batches.Count; i++)
    {
        Console.WriteLine($"Batch {i + 1}: [{string.Join(", ", batches[i])}]");
    }
}