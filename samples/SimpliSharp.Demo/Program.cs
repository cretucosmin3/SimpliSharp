using SimpliSharp.Utilities.Process;

class Program
{
    static void Main()
    {
        Console.WriteLine("Starting data processing...");

        using var processor = new SmartDataProcessor<int>(maxCpuUsage: 75);
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

            var cursorPos = Console.CursorTop;
            Console.SetCursorPosition(0, cursorPos);
            Console.Write($"Processing item {i + 1} of {tasksCount}...");
        }

        processor.WaitForAllAsync().Wait();
        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine($"Processing completed in {stopwatch.ElapsedMilliseconds} ms | {stopwatch.Elapsed.TotalSeconds} seconds");
        Console.WriteLine("All processing done");
    }
}

