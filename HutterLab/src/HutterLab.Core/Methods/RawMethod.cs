using System.Diagnostics;
using HutterLab.Core.Interfaces;
using HutterLab.Core.Models;

namespace HutterLab.Core.Methods;

/// <summary>
/// Baseline method - no compression, just passes data through.
/// Useful for measuring overhead and as a reference.
/// </summary>
public sealed class RawMethod : CompressionMethodBase
{
    public override string Name => "raw";
    public override string Description => "No compression (baseline)";
    public override string Category => "Baseline";
    public override bool IsChainable => true;
    
    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var sw = Stopwatch.StartNew();
        var output = data.ToArray();
        sw.Stop();
        
        return new CompressionResult
        {
            Method = Name,
            OriginalSize = data.Length,
            CompressedSize = output.Length,
            CompressedData = output,
            Duration = sw.Elapsed,
            IsLossless = true
        };
    }
    
    public override DecompressionResult Decompress(ReadOnlySpan<byte> compressedData, CompressionOptions? options = null)
    {
        var sw = Stopwatch.StartNew();
        var output = compressedData.ToArray();
        sw.Stop();
        
        return new DecompressionResult
        {
            Method = Name,
            CompressedSize = compressedData.Length,
            DecompressedSize = output.Length,
            DecompressedData = output,
            Duration = sw.Elapsed
        };
    }
}
