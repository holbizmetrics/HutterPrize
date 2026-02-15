namespace HutterLab.Core.Models;

/// <summary>
/// Options for compression operations.
/// </summary>
public sealed class CompressionOptions
{
    /// <summary>
    /// Enable verbose output during compression.
    /// </summary>
    public bool Verbose { get; init; }
    
    /// <summary>
    /// Maximum memory to use (in bytes). 0 = unlimited.
    /// </summary>
    public long MaxMemory { get; init; }
    
    /// <summary>
    /// Number of threads to use. 0 = auto-detect.
    /// </summary>
    public int Threads { get; init; }
    
    /// <summary>
    /// Method-specific parameters.
    /// </summary>
    public Dictionary<string, object> Parameters { get; init; } = [];
    
    /// <summary>
    /// Get a typed parameter with default fallback.
    /// </summary>
    public T GetParameter<T>(string key, T defaultValue)
    {
        if (Parameters.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return defaultValue;
    }
    
    /// <summary>
    /// Default options for quick usage.
    /// </summary>
    public static CompressionOptions Default => new();
    
    /// <summary>
    /// Verbose options for debugging.
    /// </summary>
    public static CompressionOptions VerboseDefault => new() { Verbose = true };
}
