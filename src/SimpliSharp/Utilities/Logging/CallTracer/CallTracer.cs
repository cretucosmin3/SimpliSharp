using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace SimpliSharp.Utilities.Logging;

public static class CallTracer
{
    public static bool UseEmojis { get; set; } = true;

    private static long _nextRequestId;
    private static readonly AsyncLocal<AsyncContext> Context = new(valueChangedHandler: null);
    private static readonly ConcurrentDictionary<long, MethodCall> ActiveCalls = new();
    private static readonly ConcurrentDictionary<long, List<MethodCall>> RequestCalls = new();

    private static AsyncContext CurrentContext
    {
        get
        {
            Context.Value ??= new AsyncContext();
            return Context.Value;
        }
    }

    private static void SetRequestIdentifier(long requestId)
    {
        // Clean up existing request if present
        if (CurrentContext.RequestId != null && CurrentContext.RequestId != requestId)
        {
            EndTrace();
        }

        CurrentContext.RequestId = requestId;
        RequestCalls.TryAdd(requestId, new List<MethodCall>());
    }

    public static string GetTrace(Exception? breakingException = null)
    {
        var innerException = breakingException is AggregateException aggEx ? aggEx.InnerExceptions.FirstOrDefault() ?? breakingException : breakingException;
        var requestId = CurrentContext.RequestId;

        if (requestId == null || !RequestCalls.TryGetValue(requestId.Value, out var calls))
            return "No trace";

        var sb = new StringBuilder();

        lock (calls)
        {
            var rootCalls = calls.OrderBy(c => c.StartTime).ToList();
            if (rootCalls.Count > 0)
            {
                var rootCall = rootCalls[0];
                BuildTraceString(rootCall, sb, 0, innerException);
                CleanupMethodCallTree(rootCall);
            }
        }

        RequestCalls.TryRemove(requestId.Value, out _);
        Context.Value = null;

        return sb.ToString();
    }

    public static void EndTrace()
    {
        var requestId = CurrentContext.RequestId;

        if (requestId != null)
        {
            if (RequestCalls.TryGetValue(requestId.Value, out var calls))
            {
                lock (calls)
                {
                    foreach (var call in calls)
                    {
                        CleanupMethodCallTree(call);
                    }
                }
            }

            RequestCalls.TryRemove(requestId.Value, out _);
        }

        // Clear the context
        Context.Value = null;
    }

    private static void RestoreContext(long? parentId)
    {
        CurrentContext.CurrentMethodId = parentId;
    }

    private static void CleanupMethodCallTree(MethodCall call)
    {
        foreach (var child in call.Children)
        {
            CleanupMethodCallTree(child);
        }

        // Remove this call from ActiveCalls
        ActiveCalls.TryRemove(call.Id, out _);
    }

    private static void BuildTraceString(MethodCall call, StringBuilder sb, int depth, Exception? breakingException)
    {
        // --- Render the current call's line --- 
        sb.AppendLine();
        sb.Append(new string(' ', depth * 2));
        sb.Append("- ");
        sb.Append(call.MethodName);

        if (call.EndTime.HasValue)
        {
            var duration = call.EndTime.Value.TotalMilliseconds;
            sb.Append($" ({duration:F2}ms)");
        }

        if (call.Exception != null)
        {
            bool thrownByChild = call.Children.Any(c => c.Exception == call.Exception);
            if (thrownByChild)
            {
                if (call.Exception == breakingException)
                {
                    if (UseEmojis) sb.Append(" ⚠️");
                    sb.Append(" - Inner call failed");
                }
            }
            else
            {
                bool isBreakingException = call.Exception == breakingException;
                if (UseEmojis) sb.Append(isBreakingException ? " ❌" : " ⭕");
                sb.Append($" {GetErrorLineNumber(call.Exception)} - {call.Exception.GetType().Name}: {call.Exception.Message}");
            }
        }
        else if (call.Result != null)
        {
            sb.Append($" => {call.Result}");
        }
        else
        {
            sb.Append(" => void");
        }

        // --- Two-Pass Grouping Logic ---
        var children = call.Children.OrderBy(c => c.StartTime).ToList();
        if (children.Count == 0) return;

        // First pass: Group by method name and exception status
        var nameGroups = children
            .GroupBy(c => new { c.MethodName, HasException = c.Exception != null })
            .Select(g => g.ToList())
            .OrderBy(g => g.First().StartTime);

        foreach (var nameGroup in nameGroups)
        {
            // Check if all calls in the group are leaf nodes
            bool allAreLeafNodes = nameGroup.All(c => c.Children.IsEmpty);

            if (nameGroup.Count > 1 && allAreLeafNodes)
            {
                // Optimization: All are leaf nodes, render as a single group
                var firstInGroup = nameGroup.First();
                var totalDuration = nameGroup.Select(c => c.EndTime?.TotalMilliseconds ?? 0).Average();
                sb.AppendLine();
                sb.Append(new string(' ', (depth + 1) * 2));
                sb.Append($"- [x{nameGroup.Count}] {firstInGroup.MethodName} ({totalDuration:F2}ms avg)");

                // Since all are leaf nodes and have the same name, we can assume the result is similar.
                // We'll show the result from the first one as a representative example.
                if (firstInGroup.Exception == null)
                {
                    sb.Append($" => {firstInGroup.Result ?? "void"}");
                }
                else
                {
                    bool isBreaking = firstInGroup.Exception == breakingException;
                    if (UseEmojis) sb.Append(isBreaking ? " ❌" : " ⭕");
                    sb.Append($" {GetErrorLineNumber(firstInGroup.Exception)} - {firstInGroup.Exception.GetType().Name}: {firstInGroup.Exception.Message}");
                }
            }
            else
            {
                // Fallback to original structural hash grouping for this name group
                var structuralGroups = nameGroup
                    .GroupBy(c => c.GetStructuralHash())
                    .Select(g => g.ToList())
                    .OrderBy(g => g.First().StartTime)
                    .ToList();

                foreach (var group in structuralGroups)
                {
                    if (group.Count > 1)
                    {
                        var firstInGroup = group.First();
                        var totalDuration = group.Select(c => c.EndTime?.TotalMilliseconds ?? 0).Average();

                        sb.AppendLine();
                        sb.Append(new string(' ', (depth + 1) * 2));
                        sb.Append($"- [x{group.Count}] {firstInGroup.MethodName} ({totalDuration:F2}ms avg)");

                        if (firstInGroup.Exception == null)
                        {
                            sb.Append($" => {firstInGroup.Result ?? "void"}");
                        }
                        else
                        {
                            bool isBreaking = firstInGroup.Exception == breakingException;
                            if (UseEmojis) sb.Append(isBreaking ? " ❌" : " ⭕");
                            sb.Append($" {GetErrorLineNumber(firstInGroup.Exception)} - {firstInGroup.Exception.GetType().Name}: {firstInGroup.Exception.Message}");
                        }

                        // Render children of the first call as a representative example
                        foreach (var child in firstInGroup.Children.OrderBy(c => c.StartTime))
                        {
                            BuildTraceString(child, sb, depth + 2, breakingException);
                        }
                    }
                    else
                    {
                        // Not a group, render the single call and its children recursively
                        BuildTraceString(group.First(), sb, depth + 1, breakingException);
                    }
                }
            }
        }
    }

    private static string GetErrorLineNumber(Exception ex)
    {
        try
        {
            var stackTrace = new StackTrace(ex, true);
            var frame = stackTrace.GetFrames()?.FirstOrDefault();

            if (frame != null)
            {
                var fileName = Path.GetFileName(frame.GetFileName() ?? "Unknown");
                return $"{fileName}:{frame.GetFileLineNumber()}";
            }
        }
        finally
        {
        }

        return "line unknown";
    }

    internal static MethodTracer TraceMethod(string methodName)
    {
        var stopwatch = Stopwatch.StartNew();

        bool isFirstCall = CurrentContext.RequestId == null;
        if (isFirstCall)
        {
            // If this is the first call in the context, we need to set a request ID
            SetRequestIdentifier(Interlocked.Increment(ref _nextRequestId));
        }

        long? parentId = CurrentContext.CurrentMethodId;
        MethodCall methodCall = new(methodName, parentId);

        // Store the call
        ActiveCalls.TryAdd(methodCall.Id, methodCall);

        // If we have a parent, add this as a child
        if (parentId.HasValue && ActiveCalls.TryGetValue(parentId.Value, out var parentCall))
        {
            parentCall.Children.Add(methodCall);
        }
        else if (CurrentContext.RequestId != null)
        {
            // This is a root call
            if (RequestCalls.TryGetValue(CurrentContext.RequestId.Value, out var requestCalls))
            {
                lock (requestCalls)
                {
                    requestCalls.Add(methodCall);
                }
            }
        }

        // Set this as the current method
        CurrentContext.CurrentMethodId = methodCall.Id;

        var tracer = new MethodTracer(
            methodCall.Id,
            parentId,
            CurrentContext.RequestId,
            () => RestoreContext(parentId));

        stopwatch.Stop();
        TracerProfiler.Add(stopwatch.Elapsed);

        return tracer;
    }

    internal class MethodTracer : IDisposable
    {
        private readonly long _methodId;
        private readonly long? _parentId;
        private readonly long? _requestId;
        private readonly Action _onDispose;
        private bool _disposed;
        private readonly object _lock = new object();

        public MethodTracer(long methodId, long? parentId, long? requestId, Action onDispose)
        {
            _methodId = methodId;
            _parentId = parentId;
            _requestId = requestId;
            _onDispose = onDispose;
        }

        private void RestoreMethodContext(Action action)
        {
            lock (_lock)
            {
                try
                {
                    Context.Value = new AsyncContext
                    {
                        CurrentMethodId = _methodId,
                        RequestId = _requestId
                    };

                    action();
                }
                finally
                {
                    RestoreContext(_parentId);
                }
            }
        }


        public void SetResult(string? result)
        {
            var stopwatch = Stopwatch.StartNew();
            RestoreMethodContext(() =>
            {
                if (ActiveCalls.TryGetValue(_methodId, out var call))
                {
                    call.SetResult(result);
                }
            });
            stopwatch.Stop();
            TracerProfiler.Add(stopwatch.Elapsed);
        }

        public void SetException(Exception ex)
        {
            var stopwatch = Stopwatch.StartNew();
            RestoreMethodContext(() =>
            {
                if (ActiveCalls.TryGetValue(_methodId, out var call))
                {
                    call.SetResult(exception: ex);
                }
            });
            stopwatch.Stop();
            TracerProfiler.Add(stopwatch.Elapsed);
        }

        public void Complete()
        {
            var stopwatch = Stopwatch.StartNew();
            RestoreMethodContext(() =>
            {
                if (ActiveCalls.TryGetValue(_methodId, out var call))
                {
                    call.Complete();
                }
            });
            stopwatch.Stop();
            TracerProfiler.Add(stopwatch.Elapsed);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;

                var stopwatch = Stopwatch.StartNew();

                _onDispose();
                _disposed = true;

                stopwatch.Stop();
                TracerProfiler.Add(stopwatch.Elapsed);
            }
        }
    }
}
