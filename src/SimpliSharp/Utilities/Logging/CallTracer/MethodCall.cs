using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SimpliSharp.Utilities.Logging;

public class MethodCall
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public string ParentId { get; }
    public string MethodName { get; }
    public long StartTime { get; }
    public TimeSpan? EndTime { get; private set; }
    public string? Result { get; private set; }
    public Exception? Exception { get; private set; }
    public int ThreadId { get; }
    public List<MethodCall> Children { get; } = [];

    private string? _structuralHash;

    public MethodCall(string methodName, string? parentId = null)
    {
        MethodName = methodName;
        ParentId = parentId ?? "";
        StartTime = Stopwatch.GetTimestamp();
        ThreadId = Environment.CurrentManagedThreadId;
    }

    public void Complete(string? result = null, Exception? exception = null)
    {
        EndTime = Stopwatch.GetElapsedTime(StartTime);
        Result = result;
        Exception = exception;
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

        lock (Children)
        {
            foreach (var child in Children.OrderBy(c => c.StartTime))
            {
                sb.Append($":({child.GetStructuralHash()})");
            }
        }

        _structuralHash = sb.ToString();
        return _structuralHash;
    }
}