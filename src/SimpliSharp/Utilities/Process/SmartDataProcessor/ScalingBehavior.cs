namespace SimpliSharp.Utilities.Process;

/// <summary>
/// Defines how aggressively the processor should scale up concurrency.
/// </summary>
public enum ScalingBehavior
{
    /// <summary>
    /// Gradual scaling: starts at 1 thread and slowly increases by 1-2 threads at a time.
    /// Most conservative, takes longest to reach full capacity.
    /// </summary>
    Gradual,
    
    /// <summary>
    /// Normal scaling: quick initial ramp-up to max, then backs off if needed.
    /// Good balance between responsiveness and stability.
    /// </summary>
    Normal,
    
    /// <summary>
    /// Aggressive scaling: immediately jumps to max concurrency and stays there,
    /// only backing off when CPU limit is exceeded. Fastest startup.
    /// </summary>
    Aggressive
}