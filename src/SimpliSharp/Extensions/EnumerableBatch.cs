namespace SimpliSharp.Extensions.Batch;

public static class EnumerableExtensions
{
    /// <summary>
    /// Splits an enumerable sequence into batches of a specified size, creating batches with original object references.
    /// </summary>
    /// <remarks>
    /// This method is optimized for arrays (`T[]`) for potential performance benefits.
    /// For other `IEnumerable<T>` types, it uses a standard iteration approach.
    /// The method uses deferred execution (yield return).
    /// The last batch may contain fewer items than <paramref name="batchSize"/> if the total number of items
    /// is not evenly divisible by <paramref name="batchSize"/>.
    /// </remarks>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="source">The source enumerable sequence to batch.</param>
    /// <param name="batchSize">The desired maximum size for each batch.</param>
    /// <returns>An `IEnumerable<T[]>` where each element is an array representing a batch.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="batchSize"/> is less than or equal to 0.</exception>
    public static IEnumerable<T[]> Batch<T>(this IEnumerable<T> source, int batchSize)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be positive.");
        }

        // Handle array case for efficiency. No new copies of objects are made.
        if (source is T[] sourceArray)
        {
            for (int i = 0; i < sourceArray.Length;)
            {
                int currentBatchSize = Math.Min(batchSize, sourceArray.Length - i);
                T[] batch = new T[currentBatchSize];
                Array.Copy(sourceArray, i, batch, 0, currentBatchSize);
                yield return batch;
                i += currentBatchSize;
            }
        }
        else
        {
            // Handle non-array enumerables
            List<T> currentBatch = new List<T>(batchSize);
            using IEnumerator<T> enumerator = source.GetEnumerator();
            while (enumerator.MoveNext())
            {
                currentBatch.Add(enumerator.Current);
                if (currentBatch.Count == batchSize)
                {
                    yield return currentBatch.ToArray();
                    currentBatch.Clear(); // Reuse the list
                }
            }

            // Yield the last partial batch if it has items
            if (currentBatch.Count > 0)
            {
                yield return currentBatch.ToArray();
            }
        }
    }
}