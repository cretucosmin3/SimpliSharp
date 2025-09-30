using System.Reflection;
using MethodBoundaryAspect.Fody.Attributes;
using static SimpliSharp.Utilities.Logging.CallTracer;

namespace SimpliSharp.Utilities.Logging;

public class CallTraceAttribute : OnMethodBoundaryAspect
{
    private MethodTracer? localTrace;

    public override void OnEntry(MethodExecutionArgs args)
    {
        var invocationName = $"{args.Method.DeclaringType?.Name}.{args.Method.Name}";
        var traceDataAttr = args.Method.GetCustomAttribute<TraceDataAttribute>();
        var parameters = new Dictionary<string, object>();

        if (traceDataAttr != null && (traceDataAttr.Level == Level.Input || traceDataAttr.Level == Level.InputAndOutput))
        {
            var methodParameters = args.Method.GetParameters();
            for (int i = 0; i < methodParameters.Length; i++)
            {
                if (methodParameters[i].GetCustomAttribute<TraceParamAttribute>() != null)
                {
                    var parameterName = methodParameters[i].Name;
                    if (parameterName != null)
                    {
                        parameters.Add(parameterName, args.Arguments[i]);
                    }
                }
            }
        }

        localTrace = TraceMethod(invocationName, parameters);
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        if (args.Exception != null) return;

        var traceDataAttr = args.Method.GetCustomAttribute<TraceDataAttribute>();
        object? result = null;

        if (traceDataAttr != null && (traceDataAttr.Level == Level.Output || traceDataAttr.Level == Level.InputAndOutput))
        {
            result = args.ReturnValue;
        }
        else
        {
            result = args.ReturnValue != null ? "{...}" : "null";
        }
        
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