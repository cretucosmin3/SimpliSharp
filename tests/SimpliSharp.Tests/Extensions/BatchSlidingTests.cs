using SimpliSharp.Extensions.Batch;

namespace SimpliSharp.Tests.MSTest;

[TestClass]
public class BatchSlidingTests
{
    [TestMethod]
    public void BatchSliding_WithStandardInput_ShouldReturnOverlappingWindows()
    {
        // Arrange
        int[] source = { 1, 2, 3, 4, 5, 6 };
        int windowSize = 3;
        var expected = new[]
        {
            new[] { 1, 2, 3 },
            new[] { 2, 3, 4 },
            new[] { 3, 4, 5 },
            new[] { 4, 5, 6 }
        };

        // Act
        var result = source.BatchSliding(windowSize).ToList();

        // Assert
        Assert.AreEqual(expected.Length, result.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            CollectionAssert.AreEqual(expected[i], result[i]);
        }
    }

    [TestMethod]
    public void BatchSliding_WithWindowSizeEqualToSourceLength_ShouldReturnOneWindow()
    {
        // Arrange
        int[] source = { 1, 2, 3, 4 };
        int windowSize = 4;

        // Act
        var result = source.BatchSliding(windowSize).ToList();

        // Assert
        Assert.AreEqual(1, result.Count);
        CollectionAssert.AreEqual(source, result[0]);
    }

    [TestMethod]
    public void BatchSliding_WithWindowSizeLargerThanSource_ShouldReturnEmptyResult()
    {
        // Arrange
        int[] source = { 1, 2, 3 };
        int windowSize = 4;

        // Act
        var result = source.BatchSliding(windowSize).ToList();

        // Assert
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void BatchSliding_WithNullSource_ShouldThrowArgumentNullException()
    {
        // Arrange
        IEnumerable<int>? source = null;

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => source!.BatchSliding(3).ToList());
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void BatchSliding_WithNonPositiveWindowSize_ShouldThrowArgumentOutOfRangeException(int windowSize)
    {
        // Arrange
        int[] source = { 1, 2, 3 };

        // Act & Assert
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => source.BatchSliding(windowSize).ToList());
    }
}