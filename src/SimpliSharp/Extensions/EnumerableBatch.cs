namespace SimpliSharp.Extensions.Batch;

public static class EnumerableExtensions
{
    /// <summary>
    /// Splits an enumerable sequence into non-overlapping batches of a specified size.
    /// </summary>
    /// <remarks>
    /// This method partitions the source sequence into chunks.
    /// For example, `[1,2,3,4,5,6]` with a batch size of 3 results in `[1,2,3]` and `[4,5,6]`.
    /// The last batch may contain fewer items.
    /// This method uses deferred execution.
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

    /// <summary>
    /// Creates overlapping batches from an enumerable sequence using a sliding window approach.
    /// </summary>
    /// <remarks>
    /// This method generates batches by taking sections of the source sequence.
    /// For example, `[1,2,3,4,5,6]` with a window size of 3 results in `[1,2,3]`, `[2,3,4]`, `[3,4,5]`, and `[4,5,6]`.
    /// Unlike the `Batch` method, all returned batches will have the exact size of <paramref name="windowSize"/>.
    /// If the source sequence contains fewer items than the <paramref name="windowSize"/>, no batches will be returned.
    /// This method uses deferred execution.
    /// </remarks>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="source">The source enumerable sequence.</param>
    /// <param name="windowSize">The exact size for each sliding window batch.</param>
    /// <returns>An `IEnumerable<T[]>` where each element is an array representing a sliding window batch.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="windowSize"/> is less than or equal to 0.</exception>
    public static IEnumerable<T[]> BatchSliding<T>(this IEnumerable<T> source, int windowSize)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (windowSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be positive.");
        }

        // Eagerly convert to an array to allow for indexed access, which is required for a sliding window.
        var sourceArray = source.ToArray();

        // Determine the number of possible windows we can create.
        int possibleWindows = sourceArray.Length - windowSize + 1;

        // Iterate from the first possible window to the last.
        for (int i = 0; i < possibleWindows; i++)
        {
            // Create a new array for the current window.
            T[] window = new T[windowSize];
            Array.Copy(sourceArray, i, window, 0, windowSize);
            yield return window;
        }
    }
}