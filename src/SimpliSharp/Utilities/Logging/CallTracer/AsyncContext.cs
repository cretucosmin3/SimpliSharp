namespace SimpliSharp.Utilities.Logging;

internal class AsyncContext
{
    public long? CurrentMethodId { get; set; }
    public long? RequestId { get; set; }
}