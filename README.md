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
