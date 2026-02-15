using HutterLab.Core.Models;

namespace HutterLab.Core.Interfaces;

/// <summary>
/// Interface for all compression methods in HutterLab.
/// </summary>
public interface ICompressionMethod
{
    /// <summary>
    /// Unique identifier for this method (e.g., "xml_patterns", "bpe", "zstd").
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Human-readable description.
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Category for grouping (e.g., "Preprocessing", "Statistical", "Backend").
    /// </summary>
    string Category { get; }
    
    /// <summary>
    /// Whether this method is lossless.
    /// </summary>
    bool IsLossless { get; }
    
    /// <summary>
    /// Whether this method can be chained with others (preprocessing methods).
    /// </summary>
    bool IsChainable { get; }
    
    /// <summary>
    /// Compress the input data.
    /// </summary>
    CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null);
    
    /// <summary>
    /// Decompress previously compressed data.
    /// </summary>
    DecompressionResult Decompress(ReadOnlySpan<byte> compressedData, CompressionOptions? options = null);
    
    /// <summary>
    /// Verify that Compress → Decompress produces the original data.
    /// </summary>
    bool Verify(ReadOnlySpan<byte> originalData, CompressionOptions? options = null)
    {
        var compressed = Compress(originalData, options);
        var decompressed = Decompress(compressed.CompressedData, options);
        return originalData.SequenceEqual(decompressed.DecompressedData);
    }
}

/// <summary>
/// Base class with common functionality for compression methods.
/// </summary>
public abstract class CompressionMethodBase : ICompressionMethod
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string Category { get; }
    public virtual bool IsLossless => true;
    public virtual bool IsChainable => false;
    
    public abstract CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null);
    public abstract DecompressionResult Decompress(ReadOnlySpan<byte> compressedData, CompressionOptions? options = null);
    
    protected CompressionOptions GetOptions(CompressionOptions? options) => options ?? CompressionOptions.Default;
    
    protected void Log(CompressionOptions options, string message)
    {
        if (options.Verbose)
            Console.WriteLine($"  [{Name}] {message}");
    }
}
