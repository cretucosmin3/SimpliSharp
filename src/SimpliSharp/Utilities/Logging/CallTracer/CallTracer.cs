using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace SimpliSharp.Utilities.Logging;

public static class CallTracer
{
    public static bool UseEmojis { get; set; } = true;

    private static readonly AsyncLocal<AsyncContext> Context = new(valueChangedHandler: null);
    private static readonly ConcurrentDictionary<string, MethodCall> ActiveCalls = new();
    private static readonly ConcurrentDictionary<string, List<MethodCall>> RequestCalls = new();

    private static AsyncContext CurrentContext
    {
        get
        {
            Context.Value ??= new AsyncContext();
            return Context.Value;
        }
    }

    public static void SetRequestIdentifier(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Request id cannot be null or empty", nameof(requestId));

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

        if (requestId == null || !RequestCalls.TryGetValue(requestId, out var calls))
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

        RequestCalls.TryRemove(requestId, out _);
        Context.Value = null;

        return sb.ToString();
    }

    public static void EndTrace()
    {
        var requestId = CurrentContext.RequestId;

        if (requestId != null)
        {
            if (RequestCalls.TryGetValue(requestId, out var calls))
            {
                lock (calls)
                {
                    foreach (var call in calls)
                    {
                        CleanupMethodCallTree(call);
                    }
                }
            }

            RequestCalls.TryRemove(requestId, out _);
        }

        // Clear the context
        Context.Value = null;
    }

    private static void RestoreContext(string? parentId)
    {
        CurrentContext.CurrentMethodId = parentId;
    }

    private static void CleanupMethodCallTree(MethodCall call)
    {
        List<MethodCall> childrenCopy;

        lock (call.Children)
        {
            childrenCopy = new List<MethodCall>(call.Children);
        }

        foreach (var child in childrenCopy)
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

        // --- Process and render children ---
        List<MethodCall> children;
        lock (call.Children)
        {
            children = call.Children.ToList();
        }

        if (children.Count == 0) return;

        var groups = children
            .GroupBy(c => c.GetStructuralHash())
            .Select(g => g.ToList())
            .OrderBy(g => g.First().StartTime)
            .ToList();

        foreach (var group in groups)
        {
            if (group.Count > 1)
            {
                var firstInGroup = group.First();
                var totalDuration = group.Select(c => c.EndTime?.TotalMilliseconds ?? 0).Sum();
                sb.AppendLine();
                sb.Append(new string(' ', (depth + 1) * 2));
                sb.Append($"- [x{group.Count}] {firstInGroup.MethodName} ({totalDuration:F2}ms)");

                // Since all are structurally identical, the result is the same
                if (firstInGroup.Exception == null)
                {
                    sb.Append($" => {firstInGroup.Result ?? "void"}");
                }
                else
                {
                    // If the first one has an exception, the whole group does. Show the first one's exception.
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
        bool isFirstCall = CurrentContext.RequestId == null;
        if (isFirstCall)
        {
            // If this is the first call in the context, we need to set a request ID
            SetRequestIdentifier(Guid.NewGuid().ToString());
        }

        string? parentId = CurrentContext.CurrentMethodId;
        MethodCall methodCall = new(methodName, parentId);

        // Store the call
        ActiveCalls.TryAdd(methodCall.Id, methodCall);

        // If we have a parent, add this as a child
        if (!string.IsNullOrEmpty(parentId) && ActiveCalls.TryGetValue(parentId, out var parentCall))
        {
            lock (parentCall.Children)
            {
                parentCall.Children.Add(methodCall);
            }
        }
        else if (CurrentContext.RequestId != null)
        {
            // This is a root call
            if (RequestCalls.TryGetValue(CurrentContext.RequestId, out var requestCalls))
            {
                lock (requestCalls)
                {
                    requestCalls.Add(methodCall);
                }
            }
        }

        // Set this as the current method
        CurrentContext.CurrentMethodId = methodCall.Id;

        return new MethodTracer(
            methodCall.Id,
            parentId,
            CurrentContext.RequestId,
            () => RestoreContext(parentId));
    }

    internal class MethodTracer : IDisposable
    {
        private readonly string _methodId;
        private readonly string? _parentId;
        private readonly string? _requestId;
        private readonly Action _onDispose;
        private bool _disposed;
        private readonly object _lock = new object();

        public MethodTracer(string methodId, string? parentId, string? requestId, Action onDispose)
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
            RestoreMethodContext(() =>
            {
                if (ActiveCalls.TryGetValue(_methodId, out var call))
                {
                    call.Complete(result);
                }
            });
        }

        public void SetException(Exception ex)
        {
            RestoreMethodContext(() =>
            {
                if (ActiveCalls.TryGetValue(_methodId, out var call))
                {
                    call.Complete(exception: ex);
                }
            });
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;

                Context.Value = new AsyncContext
                {
                    CurrentMethodId = _methodId,
                    RequestId = _requestId
                };

                if (ActiveCalls.TryGetValue(_methodId, out var call))
                {
                    if (!call.EndTime.HasValue)
                    {
                        call.Complete();
                    }
                }

                _onDispose();
                _disposed = true;
            }
        }
    }
}
