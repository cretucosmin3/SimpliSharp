using System.Threading.Tasks.Dataflow;
using SimpliSharp.Extensions.Batch;
using SimpliSharp.Utilities.Process;
using SimpliSharp.Utilities.Logging;
using System.Text.RegularExpressions;

static class Program
{
    [CallTrace]
    static void Main()
    {
        try
        {
            TestMethodA();
            TestMethodC().Wait();
        }
        catch (Exception breakingException)
        {
            string traceOutput = CallTracer.GetTrace(breakingException);
            File.WriteAllText("trace.log", traceOutput);
        }
    }

    [CallTrace]
    private static void TestMethodA()
    {
        try
        {
            TestMethodB();
        }
        catch (Exception)
        {
            // We don't care about this one
        }
    }

    [CallTrace]
    private static void TestMethodB()
    {
        throw new Exception("Michael had a yellow rabbit");
    }

    [CallTrace]
    private static async Task TestMethodC()
    {
        Thread.Sleep(100);
        ClassB.MethodB();
    }
}

public static class ClassB
{
    [CallTrace]
    public static void MethodB()
    {
        MethodA();
    }

    [CallTrace]
    private static void MethodA()
    {
        // Throw few exceptions in an aggregate to test that scenario
        var exceptions = new List<Exception>
        {
            new Exception("Bob had a blue dog"),
            new Exception("Alice had a red cat"),
            new Exception("Eve had a green mouse")
        };

        throw new AggregateException(exceptions);
    }
}