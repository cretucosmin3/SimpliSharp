using SimpliSharp.Utilities.Process;

namespace SimpliSharp.Tests.Utilities.Process.SmartDataProcessor;

[TestClass]
public class SmartDataProcessorTests
{
    [TestMethod]
    public async Task SmartDataProcessor_ProcessesItems_InOrder()
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
            processor.EnqueueOrWaitAsync(i, action).Wait();
        }
        await processor.WaitForAllAsync();

        // Assert
        Assert.AreEqual(10, processedData.Count, "The number of processed items should be 10.");
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 10).ToList(), processedData, "The processed items should be equivalent to the original items.");
    }

    [TestMethod]
    public async Task SmartDataProcessor_Honors_MaxDegreeOfParallelism()
    {
        // Arrange
        var settings = new SmartDataProcessorSettings
        {
            MaxDegreeOfParallelism = 2
        };
        var processor = new SmartDataProcessor<int>(settings);
        var mre = new ManualResetEventSlim(false);
        var runningTasks = 0;
        var maxRunningTasks = 0;

        var action = (int data) =>
        {
            Interlocked.Increment(ref runningTasks);
            maxRunningTasks = Math.Max(maxRunningTasks, runningTasks);
            mre.Wait();
            Interlocked.Decrement(ref runningTasks);
        };

        // Act
        for (int i = 0; i < 5; i++)
        {
            processor.EnqueueOrWaitAsync(i, action).Wait();
        }

        await Task.Delay(100); // Give time for tasks to start

        // Assert
        Assert.AreEqual(2, maxRunningTasks, "The maximum number of running tasks should not exceed the specified MaxDegreeOfParallelism.");

        // Cleanup
        mre.Set();
        await processor.WaitForAllAsync();
    }

    [TestMethod]
    public async Task SmartDataProcessor_Blocks_When_QueueIsFull()
    {
        // Arrange
        var settings = new SmartDataProcessorSettings
        {
            MaxDegreeOfParallelism = 1,
            QueueBufferMultiplier = 1
        };
        var processor = new SmartDataProcessor<int>(settings);
        var mre = new ManualResetEventSlim(false);
        var tasksStarted = 0;

        var action = (int data) =>
        {
            Interlocked.Increment(ref tasksStarted);
            mre.Wait();
        };

        // Act
        processor.EnqueueOrWaitAsync(1, action).Wait();
        await Task.Delay(100); // Give the manager loop time to start
        processor.EnqueueOrWaitAsync(2, action).Wait();
        processor.EnqueueOrWaitAsync(3, action).Wait();

        var blockedTask = Task.Run(async () => await processor.EnqueueOrWaitAsync(4, action));

        await Task.Delay(100);

        // Assert
        Assert.AreEqual(1, tasksStarted, "Only one task should have started because the processor is blocked.");
        Assert.IsFalse(blockedTask.IsCompleted, "The enqueue task should be blocked because the queue is full.");

        // Cleanup
        mre.Set();
        await blockedTask;
        await processor.WaitForAllAsync();
    }

    [TestMethod]
    public async Task SmartDataProcessor_Blocks_When_CpuIsHigh()
    {
        // Arrange
        var settings = new SmartDataProcessorSettings
        {
            MaxCpuUsage = 50
        };
        var cpuMonitor = new MockCpuMonitor();
        var processor = new SmartDataProcessor<int>(settings, cpuMonitor);
        var mre = new ManualResetEventSlim(false);

        var action = (int data) =>
        {
            mre.Wait();
        };

        // Act
        cpuMonitor.SetCpuUsage(100);
        processor.EnqueueOrWaitAsync(1, action).Wait(); // This will start the manager loop
        await Task.Delay(100); // Give time for the manager to update the smoothed CPU

        var blockedTask = Task.Run(async () => await processor.EnqueueOrWaitAsync(2, action));

        await Task.Delay(100);

        // Assert
        Assert.IsFalse(blockedTask.IsCompleted, "The enqueue task should be blocked because the CPU usage is high.");

        // Act
        cpuMonitor.SetCpuUsage(20);
        await Task.Delay(200); // Give time for the manager to update the smoothed CPU

        // Assert
        Assert.IsTrue(blockedTask.IsCompleted, "The enqueue task should be completed after the CPU usage is lowered.");

        // Cleanup
        mre.Set();
        await processor.WaitForAllAsync();
    }

    [TestMethod]
    public async Task SmartDataProcessor_PauseAndResume_Works()
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
        processor.Pause();
        await processor.EnqueueOrWaitAsync(1, action);
        await Task.Delay(100);

        // Assert
        Assert.AreEqual(0, processedData.Count, "No items should be processed while the processor is paused.");

        // Act
        processor.Resume();
        await processor.WaitForAllAsync();

        // Assert
        Assert.AreEqual(1, processedData.Count, "Items should be processed after the processor is resumed.");
    }

    [TestMethod]
    public async Task SmartDataProcessor_OnException_EventIsFired()
    {
        // Arrange
        var processor = new SmartDataProcessor<int>(100);
        Exception? caughtException = null;
        processor.OnException += (ex) => caughtException = ex;

        var action = (int data) =>
        {
            throw new InvalidOperationException("Test Exception");
        };

        // Act
        processor.EnqueueOrWaitAsync(1, action).Wait();
        await processor.WaitForAllAsync();

        // Assert
        Assert.IsNotNull(caughtException, "The OnException event should be fired.");
        Assert.IsInstanceOfType(caughtException, typeof(InvalidOperationException), "The exception should be of the correct type.");
    }
}