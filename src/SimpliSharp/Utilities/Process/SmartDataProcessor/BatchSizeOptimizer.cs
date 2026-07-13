// using System;
// using System.Collections.Generic;
// using System.Linq;

// namespace SimpliSharp.Utilities.Process;

// /// <summary>
// /// Analyzes task execution metrics to recommend optimal batch sizes for data processing.
// /// </summary>
// public class BatchSizeOptimizer
// {
//     private const double MinTaskOverheadMs = 0.5; // Minimum overhead per task
//     private const double MinTaskDurationMs = 10; // Tasks faster than this need batching
//     private const double MaxTaskDurationMs = 200; // Tasks slower than this should be split
//     private const int SamplesForComparison = 20; // Samples needed to compare batch sizes
    
//     private readonly List<BatchSample> _samples = new();
//     private int _minSampleCount = 10;
    
//     /// <summary>
//     /// Current recommendation based on collected samples.
//     /// </summary>
//     public BatchRecommendation? CurrentRecommendation { get; private set; }
    
//     /// <summary>
//     /// Whether enough data has been collected to make a recommendation.
//     /// </summary>
//     public bool HasSufficientData => _samples.Count >= _minSampleCount;

//     /// <summary>
//     /// Records a completed batch execution for analysis.
//     /// </summary>
//     /// <param name="itemsProcessed">Number of items in the batch</param>
//     /// <param name="durationMs">Time taken to process the batch in milliseconds</param>
//     /// <param name="concurrencyLevel">Number of concurrent tasks at the time</param>
//     public void RecordBatch(int itemsProcessed, double durationMs, int concurrencyLevel)
//     {
//         if (itemsProcessed <= 0 || durationMs <= 0)
//             return;

//         _samples.Add(new BatchSample
//         {
//             ItemCount = itemsProcessed,
//             DurationMs = durationMs,
//             ConcurrencyLevel = concurrencyLevel,
//             TimePerItem = durationMs / itemsProcessed
//         });

//         // Keep only recent samples (last 100)
//         if (_samples.Count > 100)
//         {
//             _samples.RemoveAt(0);
//         }

//         UpdateRecommendation();
//     }

//     /// <summary>
//     /// Analyzes a SmartDataProcessor's metrics and provides a recommendation.
//     /// Call this after processing at least some items with varying batch sizes.
//     /// </summary>
//     public BatchRecommendation AnalyzeMetrics(ProcessingMetrics metrics)
//     {
//         if (metrics.AvgTaskTime <= 0)
//         {
//             return new BatchRecommendation
//             {
//                 RecommendedBatchSize = 1,
//                 Confidence = ConfidenceLevel.Low,
//                 Reasoning = "Insufficient data collected. Process at least 10 batches first.",
//                 EstimatedTaskDuration = 0
//             };
//         }

//         double avgTaskTime = metrics.AvgTaskTime;
//         int maxConcurrency = metrics.MaxConcurrency;

//         // Estimate optimal batch size based on task duration
//         return CalculateRecommendation(avgTaskTime, maxConcurrency, _samples);
//     }

//     /// <summary>
//     /// Quick recommendation without historical samples, based solely on current metrics.
//     /// </summary>
//     public static BatchRecommendation QuickRecommendation(
//         double averageTaskDurationMs, 
//         int currentBatchSize = 1,
//         int maxConcurrency = 0)
//     {
//         if (maxConcurrency <= 0)
//             maxConcurrency = Environment.ProcessorCount;

//         return CalculateRecommendation(averageTaskDurationMs, maxConcurrency, null, currentBatchSize);
//     }

//     private static BatchRecommendation CalculateRecommendation(
//         double avgTaskDurationMs,
//         int maxConcurrency,
//         List<BatchSample>? samples,
//         int currentBatchSize = 1)
//     {
//         // Determine workload characteristics
//         var workloadType = ClassifyWorkload(avgTaskDurationMs);
        
//         int recommendedSize;
//         string reasoning;
//         double estimatedDuration;
//         var confidence = samples?.Count >= 10 ? ConfidenceLevel.High : 
//                         samples?.Count >= 5 ? ConfidenceLevel.Medium : 
//                         ConfidenceLevel.Low;

//         if (samples != null && samples.Count >= 5)
//         {
//             // Use statistical analysis if we have samples
//             var analysis = AnalyzeSamples(samples);
            
//             if (analysis.TimePerItem > 0)
//             {
//                 // Calculate batch size to hit viable task duration range (10-100ms)
//                 double targetDuration = avgTaskDurationMs < MinTaskDurationMs ? 50 : avgTaskDurationMs;
//                 recommendedSize = (int)Math.Ceiling(targetDuration / analysis.TimePerItem);
                
//                 // Constrain based on workload type
//                 recommendedSize = ConstrainBatchSize(recommendedSize, workloadType, maxConcurrency);
                
//                 estimatedDuration = recommendedSize * analysis.TimePerItem;
                
//                 reasoning = $"Based on {samples.Count} samples: " +
//                            $"avg {analysis.TimePerItem:F3}ms per item, " +
//                            $"targeting {targetDuration:F0}ms tasks. " +
//                            $"Workload is {workloadType}.";
//             }
//             else
//             {
//                 // Fallback if data is inconsistent
//                 recommendedSize = EstimateBatchSize(avgTaskDurationMs, workloadType, maxConcurrency);
//                 estimatedDuration = avgTaskDurationMs;
//                 reasoning = "Using heuristic estimation due to data inconsistency.";
//                 confidence = ConfidenceLevel.Low;
//             }
//         }
//         else
//         {
//             // Use heuristic approach
//             recommendedSize = EstimateBatchSize(avgTaskDurationMs, workloadType, maxConcurrency);
//             estimatedDuration = avgTaskDurationMs;
            
//             reasoning = workloadType switch
//             {
//                 WorkloadType.VeryLight => "Tasks are very fast (<10ms). Use large batches to reduce overhead.",
//                 WorkloadType.Light => "Tasks are light (10-50ms). Moderate batching recommended.",
//                 WorkloadType.Medium => "Tasks are well-sized (50-100ms). Current granularity is good.",
//                 WorkloadType.Heavy => "Tasks are heavy (100-200ms). Consider smaller batches.",
//                 WorkloadType.VeryHeavy => "Tasks are very heavy (>200ms). Use single-item batches or split work.",
//                 _ => "Unable to classify workload."
//             };
//         }

//         // Provide scaling guidance
//         var scalingGuidance = GenerateScalingGuidance(
//             recommendedSize, 
//             currentBatchSize, 
//             workloadType, 
//             maxConcurrency);

//         return new BatchRecommendation
//         {
//             RecommendedBatchSize = recommendedSize,
//             Confidence = confidence,
//             Reasoning = reasoning,
//             EstimatedTaskDuration = estimatedDuration,
//             WorkloadType = workloadType,
//             ScalingGuidance = scalingGuidance
//         };
//     }

//     private static WorkloadType ClassifyWorkload(double avgTaskDurationMs)
//     {
//         return avgTaskDurationMs switch
//         {
//             < MinTaskDurationMs => WorkloadType.VeryLight,
//             < 50 => WorkloadType.Light,
//             < 100 => WorkloadType.Medium,
//             < MaxTaskDurationMs => WorkloadType.Heavy,
//             _ => WorkloadType.VeryHeavy
//         };
//     }

//     private static int EstimateBatchSize(double avgTaskDurationMs, WorkloadType workload, int maxConcurrency)
//     {
//         if (avgTaskDurationMs < MinTaskOverheadMs)
//             return Math.Max(1000, maxConcurrency * 100); // Very fast tasks need large batches

//         if (avgTaskDurationMs > MaxTaskDurationMs)
//             return 1; // Already too slow, don't batch

//         // Calculate multiplier to reach viable task duration (aim for 50ms)
//         double targetDuration = 50.0;
//         double multiplier = targetDuration / avgTaskDurationMs;
//         int batchSize = (int)Math.Ceiling(multiplier);

//         return ConstrainBatchSize(batchSize, workload, maxConcurrency);
//     }

//     private static int ConstrainBatchSize(int proposed, WorkloadType workload, int maxConcurrency)
//     {
//         // Apply reasonable bounds
//         int min = workload switch
//         {
//             WorkloadType.VeryLight => 100,
//             WorkloadType.Light => 10,
//             WorkloadType.Medium => 1,
//             _ => 1
//         };

//         int max = workload switch
//         {
//             WorkloadType.VeryLight => 10000,
//             WorkloadType.Light => 1000,
//             WorkloadType.Medium => 100,
//             WorkloadType.Heavy => 10,
//             _ => 1
//         };

//         // Ensure we can saturate all cores
//         min = Math.Max(min, 1);
        
//         return Math.Clamp(proposed, min, max);
//     }

//     private static SampleAnalysis AnalyzeSamples(List<BatchSample> samples)
//     {
//         var recentSamples = samples.TakeLast(20).ToList();
        
//         double avgTimePerItem = recentSamples.Average(s => s.TimePerItem);
//         double stdDevTimePerItem = CalculateStdDev(recentSamples.Select(s => s.TimePerItem));
        
//         return new SampleAnalysis
//         {
//             TimePerItem = avgTimePerItem,
//             StdDevTimePerItem = stdDevTimePerItem,
//             Stability = stdDevTimePerItem / avgTimePerItem // Coefficient of variation
//         };
//     }

//     private static double CalculateStdDev(IEnumerable<double> values)
//     {
//         var valuesList = values.ToList();
//         if (valuesList.Count < 2)
//             return 0;

//         double avg = valuesList.Average();
//         double sumSquaredDiffs = valuesList.Sum(v => Math.Pow(v - avg, 2));
//         return Math.Sqrt(sumSquaredDiffs / (valuesList.Count - 1));
//     }

//     private static string GenerateScalingGuidance(
//         int recommended, 
//         int current, 
//         WorkloadType workload,
//         int maxConcurrency)
//     {
//         if (current == 0)
//             return $"Start with batches of {recommended} items.";

//         double ratio = (double)recommended / current;

//         if (ratio > 2)
//             return $"Increase batch size from {current} to {recommended} ({ratio:F1}x larger) to reduce task overhead.";
        
//         if (ratio < 0.5)
//             return $"Decrease batch size from {current} to {recommended} ({1/ratio:F1}x smaller) to improve parallelism.";
        
//         return $"Current batch size ({current}) is near optimal. Fine-tune between {(int)(recommended * 0.8)} and {(int)(recommended * 1.2)}.";
//     }

//     private void UpdateRecommendation()
//     {
//         if (_samples.Count < _minSampleCount)
//         {
//             CurrentRecommendation = null;
//             return;
//         }

//         var avgDuration = _samples.Average(s => s.DurationMs);
//         var maxConcurrency = _samples.Max(s => s.ConcurrencyLevel);
        
//         CurrentRecommendation = CalculateRecommendation(avgDuration, maxConcurrency, _samples);
//     }

//     private class BatchSample
//     {
//         public int ItemCount { get; init; }
//         public double DurationMs { get; init; }
//         public int ConcurrencyLevel { get; init; }
//         public double TimePerItem { get; init; }
//     }

//     private class SampleAnalysis
//     {
//         public double TimePerItem { get; init; }
//         public double StdDevTimePerItem { get; init; }
//         public double Stability { get; init; }
//     }
// }

// public class BatchRecommendation
// {
//     /// <summary>
//     /// Recommended number of items to process per task.
//     /// </summary>
//     public int RecommendedBatchSize { get; init; }
    
//     /// <summary>
//     /// Confidence in the recommendation based on data quality.
//     /// </summary>
//     public ConfidenceLevel Confidence { get; init; }
    
//     /// <summary>
//     /// Human-readable explanation of the recommendation.
//     /// </summary>
//     public string Reasoning { get; init; } = string.Empty;
    
//     /// <summary>
//     /// Expected duration of a task with the recommended batch size.
//     /// </summary>
//     public double EstimatedTaskDuration { get; init; }
    
//     /// <summary>
//     /// Classification of the workload type.
//     /// </summary>
//     public WorkloadType WorkloadType { get; init; }
    
//     /// <summary>
//     /// Specific guidance on how to adjust current batching strategy.
//     /// </summary>
//     public string ScalingGuidance { get; init; } = string.Empty;

//     public override string ToString()
//     {
//         return $"Recommended Batch Size: {RecommendedBatchSize} items\n" +
//                $"Confidence: {Confidence}\n" +
//                $"Estimated Task Duration: {EstimatedTaskDuration:F2}ms\n" +
//                $"Workload Type: {WorkloadType}\n" +
//                $"Reasoning: {Reasoning}\n" +
//                $"Guidance: {ScalingGuidance}";
//     }
// }

// public enum ConfidenceLevel
// {
//     Low,
//     Medium,
//     High
// }

// public enum WorkloadType
// {
//     VeryLight,  // < 10ms per task
//     Light,      // 10-50ms
//     Medium,     // 50-100ms
//     Heavy,      // 100-200ms
//     VeryHeavy   // > 200ms
// }