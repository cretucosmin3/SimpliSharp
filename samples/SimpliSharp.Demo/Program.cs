using SimpliSharp.Extensions.Batch;
using SimpliSharp.Utilities.Process;

Console.WriteLine("SimpliSharp Demo Application");
Console.WriteLine("---------------------------");

Console.WriteLine("Available Demos:");
Console.WriteLine("1. SmartDataProcessor Example");
Console.WriteLine("2. Enumerable.Batch");
Console.WriteLine("3. Enumerable.BatchSliding");

Console.WriteLine("[Enter] to exit");

var choice = Console.ReadKey();

switch (choice.Key)
{
    case ConsoleKey.D1:
    case ConsoleKey.NumPad1:
        SmartDataProcessor_Example();
        break;
    case ConsoleKey.D2:
    case ConsoleKey.NumPad2:
        EnumerableBatch_Example();
        break;
    case ConsoleKey.D3:
    case ConsoleKey.NumPad3:
        EnumerableBatchSliding_Example();
        break;
    default:
        Console.WriteLine("\nExiting...");
        return;
}

static void SmartDataProcessor_Example()
{
    Console.Clear();
    Console.WriteLine("Starting data processing...");

    using var processor = new SmartDataProcessor<int>(maxCpuUsage: 90);
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var tasksCount = 2000;

    for (int i = 0; i < tasksCount; i++)
    {
        int line = i;

        processor.EnqueueOrWait(line, data =>
        {
            int simMax = Random.Shared.Next(5_000_000, 20_000_000);
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

static void EnumerableBatch_Example()
{
    Console.Clear();
    Console.WriteLine("Starting Enumerable.Batch demo...");

    // yields: [ ["Red", "Blue"], ["Purple", "Black"], ["Yellow", "Pink"] ]


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
    Console.Clear();
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