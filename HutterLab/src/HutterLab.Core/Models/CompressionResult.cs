namespace HutterLab.Core.Models;

/// <summary>
/// Result of a compression operation, including metrics and metadata.
/// </summary>
public sealed class CompressionResult
{
    public required string Method { get; init; }
    public required long OriginalSize { get; init; }
    public required long CompressedSize { get; init; }
    public required byte[] CompressedData { get; init; }
    public required TimeSpan Duration { get; init; }
    
    /// <summary>
    /// Size of any auxiliary data (dictionaries, codebooks, etc.) that must be
    /// included for decompression.
    /// </summary>
    public long AuxiliarySize { get; init; }
    
    /// <summary>
    /// Total size = CompressedSize + AuxiliarySize
    /// </summary>
    public long TotalSize => CompressedSize + AuxiliarySize;
    
    /// <summary>
    /// Compression ratio (original / total)
    /// </summary>
    public double Ratio => OriginalSize / (double)TotalSize;
    
    /// <summary>
    /// Percentage of original size
    /// </summary>
    public double Percentage => 100.0 * TotalSize / OriginalSize;
    
    /// <summary>
    /// Bytes saved
    /// </summary>
    public long BytesSaved => OriginalSize - TotalSize;
    
    /// <summary>
    /// Processing speed in MB/s
    /// </summary>
    public double SpeedMBps => OriginalSize / (1024.0 * 1024.0) / Duration.TotalSeconds;
    
    /// <summary>
    /// Whether this method is lossless (byte-exact reconstruction).
    /// </summary>
    public bool IsLossless { get; init; } = true;
    
    /// <summary>
    /// Optional metadata about the compression (for debugging/analysis).
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
    
    public override string ToString() =>
        $"{Method}: {Ratio:F2}× ({Percentage:F2}%) | {BytesSaved:N0} bytes saved | {SpeedMBps:F1} MB/s";
}

/// <summary>
/// Result of a decompression operation.
/// </summary>
public sealed class DecompressionResult
{
    public required string Method { get; init; }
    public required long CompressedSize { get; init; }
    public required long DecompressedSize { get; init; }
    public required byte[] DecompressedData { get; init; }
    public required TimeSpan Duration { get; init; }
    
    /// <summary>
    /// Whether the decompressed data matches the original (for verification).
    /// </summary>
    public bool? Verified { get; init; }
    
    public double SpeedMBps => DecompressedSize / (1024.0 * 1024.0) / Duration.TotalSeconds;
}
