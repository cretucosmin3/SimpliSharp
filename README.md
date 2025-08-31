# SimpliSharp

[![.NET](https://github.com/cretucosmin3/SimpliSharp/actions/workflows/dotnet.yml/badge.svg)](https://github.com/cretucosmin3/SimpliSharp/actions/workflows/dotnet.yml) [![GitHub last commit](https://img.shields.io/github/last-commit/cretucosmin3/SimpliSharp.svg)](https://github.com/cretucosmin3/SimpliSharp/commits/main)
[![GitHub stars](https://img.shields.io/github/stars/cretucosmin3/SimpliSharp.svg)](https://github.com/cretucosmin3/SimpliSharp/stargazers)


SimpliSharp is a .NET library designed to simplify common programming tasks with a focus on performance and ease of use. As the library grows, it will be populated with a variety of tools and utilities to help developers write cleaner, more efficient code.

## Features

### SmartDataProcessor

The `SmartDataProcessor<T>` is designed to process a queue of items in parallel, while automatically adjusting the level of concurrency to stay within a specified CPU usage limit.

Usage example:

```csharp
using var processor = new SmartDataProcessor<int>(maxCpuUsage: 75);

for (...)
{
    processor.EnqueueOrWait(dtIn, data =>
    {
        ...
    });
}
```

![Alt text for your image](https://raw.githubusercontent.com/cretucosmin3/SimpliSharp/refs/heads/main/assets/75-cpu-usage.png)
