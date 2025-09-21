using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace SimpliSharp.Utilities.Logging;

public class MethodCall
{
    private static long _nextId;
    public long Id { get; }
    public long? ParentId { get; }
    public string MethodName { get; }
    public long StartTime { get; }
    public bool Completed { get; private set; }
    public TimeSpan? EndTime { get; private set; }
    public string? Result { get; private set; }
    public Exception? Exception { get; private set; }
    public int ThreadId { get; }
    public ConcurrentBag<MethodCall> Children { get; } = new();

    private string? _structuralHash;

    public MethodCall(string methodName, long? parentId = null)
    {
        Id = Interlocked.Increment(ref _nextId);
        MethodName = methodName;
        ParentId = parentId;
        StartTime = Stopwatch.GetTimestamp();
        ThreadId = Environment.CurrentManagedThreadId;
    }

    public void SetResult(string? result = null, Exception? exception = null)
    {
        Result = result;
        Exception = exception;
    }

    public void Complete()
    {
        EndTime = Stopwatch.GetElapsedTime(StartTime);
        Completed = true;
    }

    public string GetStructuralHash()
    {
        if (_structuralHash != null) return _structuralHash;

        var sb = new StringBuilder();
        sb.Append(MethodName);
        if (Exception != null)
        {
            sb.Append(Exception.GetType().Name);
        }

        foreach (var child in Children.OrderBy(c => c.StartTime))
        {
            sb.Append($":({child.GetStructuralHash()})");
        }

        _structuralHash = sb.ToString();
        return _structuralHash;
    }
}