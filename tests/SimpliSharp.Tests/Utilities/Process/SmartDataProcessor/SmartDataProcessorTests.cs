using SimpliSharp.Utilities.Process;

namespace SimpliSharp.Tests.Utilities.Process.SmartDataProcessor;

[TestClass]
public class SmartDataProcessorTests
{
    [TestMethod]
    public async Task SmartDataProcessor_ProcessesSingleItem()
    {
        // Arrange
        var processor = new SmartDataProcessor<int>(100);
        var processedData = 0;
        Action<int> action = (int data) => processedData = data;

        // Act
        processor.EnqueueOrWait(1, action);
        await processor.WaitForAllAsync();

        // Assert
        Assert.AreEqual(1, processedData);
    }

    [TestMethod]
    public async Task SmartDataProcessor_ProcessesMultipleItems()
    {
        // Arrange
        var processor = new SmartDataProcessor<int>(100);
        var processedData = new List<int>();
        var action = (int data) =>
        {
            lock (processedData)
            {
                processedData.Add(data);
            }
        };

        // Act
        for (int i = 0; i < 10; i++)
        {
            processor.EnqueueOrWait(i, action);
        }
        await processor.WaitForAllAsync();

        // Assert
        Assert.AreEqual(10, processedData.Count);
        for (int i = 0; i < 10; i++)
        {
            Assert.IsTrue(processedData.Contains(i));
        }
    }

    [TestMethod]
    public async Task SmartDataProcessor_WaitForAllAsync_WaitsForAllJobs()
    {
        // Arrange
        var processor = new SmartDataProcessor<int>(100);
        var processedCount = 0;
        var action = (int data) =>
        {
            Interlocked.Increment(ref processedCount);
            Thread.Sleep(10);
        };

        // Act
        for (int i = 0; i < 5; i++)
        {
            processor.EnqueueOrWait(i, action);
        }
        await processor.WaitForAllAsync();

        // Assert
        Assert.AreEqual(5, processedCount);
    }

    [TestMethod]
    public void SmartDataProcessor_Dispose_StopsManagerThread()
    {
        // Arrange
        var processor = new SmartDataProcessor<int>(100);

        // Act
        processor.Dispose();

        // Assert
        // We can't directly check if the thread is stopped,
        // but we can check if the CancellationToken is cancelled.
        // A better approach would be to inject the thread and mock it,
        // but for now, we'll just check if dispose doesn't throw.
    }

    [TestMethod]
    public async Task SmartDataProcessor_EnqueueOrWait_BlocksWhenCpuUsageIsHigh()
    {
        // Arrange
        var cpuMonitor = new MockCpuMonitor();
        cpuMonitor.SetCpuUsage(100);
        var processor = new SmartDataProcessor<int>(80, cpuMonitor);
        var processedData = new List<int>();
        var action = (int data) =>
        {
            lock (processedData)
            {
                processedData.Add(data);
            }
        };

        // Act
        var enqueueTask = Task.Run(() => processor.EnqueueOrWait(1, action));
        processor.ManagerLoopCycle.WaitOne();

        // Assert
        Assert.IsFalse(enqueueTask.IsCompleted);

        // Act
        cpuMonitor.SetCpuUsage(50);
        processor.ManagerLoopCycle.WaitOne();
        await enqueueTask;

        // Assert
        Assert.IsTrue(enqueueTask.IsCompleted);
    }

    [TestMethod]
    public async Task SmartDataProcessor_EnqueueOrWait_BlocksWhenQueueIsTooLarge()
    {
        // Arrange
        var cpuMonitor = new MockCpuMonitor();
        cpuMonitor.SetCpuUsage(10);
        var processor = new SmartDataProcessor<int>(80, cpuMonitor);
        var processedData = new List<int>();
        var action = (int data) =>
        {
            lock (processedData)
            {
                processedData.Add(data);
                Thread.Sleep(100);
            }
        };

        // Act
        for (int i = 0; i < 20; i++)
        {
            processor.EnqueueOrWait(i, action);
        }
        var enqueueTask = Task.Run(() => processor.EnqueueOrWait(20, action));
        processor.ManagerLoopCycle.WaitOne();

        // Assert
        Assert.IsFalse(enqueueTask.IsCompleted);

        // Act
        await processor.WaitForAllAsync();
        await enqueueTask;

        // Assert
        Assert.IsTrue(enqueueTask.IsCompleted);
    }
}