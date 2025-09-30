
namespace SimpliSharp.Utilities.Logging;

public enum Level
{
    Input,
    Output,
    InputAndOutput
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class TraceDataAttribute : Attribute
{
    public Level Level { get; }

    public TraceDataAttribute(Level level = Level.InputAndOutput)
    {
        Level = level;
    }
}

[AttributeUsage(AttributeTargets.Parameter)]
public class TraceParamAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Property)]
public class TracePropertyAttribute : Attribute
{
}
