using SimpliSharp.Utilities.Process;

class Program
{
    static void Main()
    {
        Console.WriteLine("Starting data processing...");

        using var processor = new SmartDataProcessor<int>(maxCpuUsage: 90);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tasksCount = 2000;
        
        for (int i = 0; i < tasksCount; i++)
        {
            int line = i;

            processor.EnqueueOrWait(line, data =>
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
            Console.Write($"Processing item {i + 1} of {tasksCount}...");
        }

        processor.WaitForAllAsync().Wait();
        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine($"Processing completed in {stopwatch.Elapsed.TotalSeconds} seconds");
        
        var finalMetrics = processor.Metrics;
        Console.WriteLine("--- Final Metrics ---");
        Console.WriteLine($"Max Concurrency: {finalMetrics.MaxConcurrency}");
        Console.WriteLine($"Best Job Duration: {finalMetrics.BestJobDuration:F2}ms");
        Console.WriteLine($"Slowest Job Duration: {finalMetrics.SlowestJobDuration:F2}ms");
        Console.WriteLine($"Average Job Duration: {finalMetrics.AverageJobDuration:F2}ms");
        
        Console.WriteLine("All processing done");
    }
}

