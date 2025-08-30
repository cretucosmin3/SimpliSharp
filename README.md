# SimpliSharp

[![.NET](https://github.com/kozmo/SimpliSharp/actions/workflows/dotnet.yml/badge.svg)](https://github.com/kozmo/SimpliSharp/actions/workflows/dotnet.yml)

SimpliSharp is a .NET library designed to simplify common programming tasks with a focus on performance and ease of use. As the library grows, it will be populated with a variety of tools and utilities to help developers write cleaner, more efficient code.

## Features

*   **SmartDataProcessor**: A generic data processing class that intelligently manages concurrency based on real-time CPU usage. This is ideal for CPU-intensive tasks where you want to maximize throughput without overloading the system.

## Usage

### SmartDataProcessor

The `SmartDataProcessor<T>` is designed to process a queue of items in parallel, while automatically adjusting the level of concurrency to stay within a specified CPU usage limit.

Usage example:

```csharp
using SimpliSharp.Utilities.Process;

class Program
{
    static void Main()
    {
        using var processor = new SmartDataProcessor<int>(maxCpuUsage: 60);

        for (int i = 0; i < 200; i++)
        {
            processor.EnqueueOrWait(i, data =>
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
        }

        processor.WaitForAllAsync().Wait();
    }
}
```