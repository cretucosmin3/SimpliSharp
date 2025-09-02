using SimpliSharp.Extensions.Batch;

namespace SimpliSharp.Tests.MSTest;

[TestClass]
public class BatchTests
{
    [TestMethod]
    public void Batch_WithArraySourceAndEvenlyDivisible_ShouldReturnFullBatches()
    {
        // Arrange
        int[] source = { 1, 2, 3, 4, 5, 6 };
        int batchSize = 3;
        var expected = new[]
        {
            new[] { 1, 2, 3 },
            new[] { 4, 5, 6 }
        };

        // Act
        var result = source.Batch(batchSize).ToList();

        // Assert
        Assert.AreEqual(expected.Length, result.Count);
        CollectionAssert.AreEqual(expected[0], result[0]);
        CollectionAssert.AreEqual(expected[1], result[1]);
    }

    [TestMethod]
    public void Batch_WithListSourceAndUnevenlyDivisible_ShouldReturnLastPartialBatch()
    {
        // Arrange
        var source = new List<int> { 1, 2, 3, 4, 5, 6, 7 };
        int batchSize = 3;
        var expected = new[]
        {
            new[] { 1, 2, 3 },
            new[] { 4, 5, 6 },
            new[] { 7 }
        };

        // Act
        var result = source.Batch(batchSize).ToList();

        // Assert
        Assert.AreEqual(expected.Length, result.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            CollectionAssert.AreEqual(expected[i], result[i]);
        }
    }

    [TestMethod]
    public void Batch_WithBatchSizeLargerThanSource_ShouldReturnSingleBatch()
    {
        // Arrange
        int[] source = { 1, 2, 3 };
        int batchSize = 5;

        // Act
        var result = source.Batch(batchSize).ToList();

        // Assert
        Assert.AreEqual(1, result.Count);
        CollectionAssert.AreEqual(source, result[0]);
    }

    [TestMethod]
    public void Batch_WithEmptySource_ShouldReturnEmptyResult()
    {
        // Arrange
        var source = Enumerable.Empty<int>();
        int batchSize = 5;

        // Act
        var result = source.Batch(batchSize).ToList();

        // Assert
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Batch_WithNullSource_ShouldThrowArgumentNullException()
    {
        // Arrange
        IEnumerable<int>? source = null;

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => source!.Batch(3).ToList());
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Batch_WithNonPositiveBatchSize_ShouldThrowArgumentOutOfRangeException(int batchSize)
    {
        // Arrange
        int[] source = { 1, 2, 3 };

        // Act & Assert
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => source.Batch(batchSize).ToList());
    }
}