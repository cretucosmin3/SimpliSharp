using MethodBoundaryAspect.Fody.Attributes;
using static SimpliSharp.Utilities.Logging.CallTracer;

namespace SimpliSharp.Utilities.Logging;

public class CallTraceAttribute : OnMethodBoundaryAspect
{
    private MethodTracer? localTrace;

    public override void OnEntry(MethodExecutionArgs args)
    {
        var invocationName = $"{args.Method.DeclaringType?.Name}.{args.Method.Name}";
        localTrace = TraceMethod(invocationName);
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        if (args.Exception != null) return;

        var result = args.ReturnValue != null ? "{...}" : "null";
        localTrace?.SetResult(result);

        if (args.ReturnValue is Task task)
        {
            task.ContinueWith(t =>
            {
                localTrace?.Complete();
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
        else
        {
            localTrace?.Complete();
        }
    }

    public override void OnException(MethodExecutionArgs args)
    {
        localTrace?.SetException(args.Exception);
        localTrace?.Complete();
    }
}