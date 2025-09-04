namespace SimpliSharp.Utilities.Process;

/// <summary>
/// Settings for the SmartDataProcessor.
/// </summary>
public class SmartDataProcessorSettings
{
    /// <summary>
    /// The target maximum CPU usage percentage (0-100). 
    /// If set to 100 or more, CPU monitoring will be disabled, 
    /// and the processor will scale to the maximum number of threads.
    /// </summary>
    public double MaxCpuUsage { get; set; } = 100;

    /// <summary>
    /// An optional value to manually set the maximum number of concurrent threads. 
    /// If not provided, it defaults to Environment.ProcessorCount.
    /// </summary>
    public int? MaxDegreeOfParallelism { get; set; }

    /// <summary>
    /// A multiplier to determine the queue size limit for backpressure, 
    /// based on the current number of workers.
    /// </summary>
    public int QueueBufferMultiplier { get; set; } = 2;
}
