using System.Diagnostics;
using System.IO.Compression;
using HutterLab.Core.Interfaces;
using HutterLab.Core.Models;

namespace HutterLab.Core.Methods.Backend;

/// <summary>
/// Brotli compression backend (built into .NET).
/// Good general-purpose compressor with excellent ratios.
/// </summary>
public sealed class BrotliBackend : CompressionMethodBase
{
    public override string Name => "brotli";
    public override string Description => "Brotli compression (quality 11)";
    public override string Category => "Backend";
    
    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();
        
        var level = opts.GetParameter("brotli_level", 11); // Max quality
        var compressionLevel = level switch
        {
            <= 1 => CompressionLevel.Fastest,
            >= 11 => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };
        
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, compressionLevel, leaveOpen: true))
        {
            brotli.Write(data);
        }
        
        sw.Stop();
        
        var compressedData = output.ToArray();
        Log(opts, $"Brotli: {data.Length:N0} → {compressedData.Length:N0} ({(double)data.Length / compressedData.Length:F2}×)");
        
        return new CompressionResult
        {
            Method = Name,
            OriginalSize = data.Length,
            CompressedSize = compressedData.Length,
            CompressedData = compressedData,
            Duration = sw.Elapsed,
            IsLossless = true
        };
    }
    
    public override DecompressionResult Decompress(ReadOnlySpan<byte> compressedData, CompressionOptions? options = null)
    {
        var sw = Stopwatch.StartNew();
        
        using var input = new MemoryStream(compressedData.ToArray());
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        
        sw.Stop();
        
        var decompressedData = output.ToArray();
        
        return new DecompressionResult
        {
            Method = Name,
            CompressedSize = compressedData.Length,
            DecompressedSize = decompressedData.Length,
            DecompressedData = decompressedData,
            Duration = sw.Elapsed
        };
    }
}

/// <summary>
/// GZip compression backend (built into .NET).
/// Fast and widely compatible.
/// </summary>
public sealed class GZipBackend : CompressionMethodBase
{
    public override string Name => "gzip";
    public override string Description => "GZip compression";
    public override string Category => "Backend";
    
    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();
        
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(data);
        }
        
        sw.Stop();
        
        var compressedData = output.ToArray();
        Log(opts, $"GZip: {data.Length:N0} → {compressedData.Length:N0} ({(double)data.Length / compressedData.Length:F2}×)");
        
        return new CompressionResult
        {
            Method = Name,
            OriginalSize = data.Length,
            CompressedSize = compressedData.Length,
            CompressedData = compressedData,
            Duration = sw.Elapsed,
            IsLossless = true
        };
    }
    
    public override DecompressionResult Decompress(ReadOnlySpan<byte> compressedData, CompressionOptions? options = null)
    {
        var sw = Stopwatch.StartNew();
        
        using var input = new MemoryStream(compressedData.ToArray());
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        
        sw.Stop();
        
        var decompressedData = output.ToArray();
        
        return new DecompressionResult
        {
            Method = Name,
            CompressedSize = compressedData.Length,
            DecompressedSize = decompressedData.Length,
            DecompressedData = decompressedData,
            Duration = sw.Elapsed
        };
    }
}

/// <summary>
/// Deflate compression backend (built into .NET).
/// Raw DEFLATE without headers.
/// </summary>
public sealed class DeflateBackend : CompressionMethodBase
{
    public override string Name => "deflate";
    public override string Description => "Deflate compression";
    public override string Category => "Backend";
    
    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();
        
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(data);
        }
        
        sw.Stop();
        
        var compressedData = output.ToArray();
        
        return new CompressionResult
        {
            Method = Name,
            OriginalSize = data.Length,
            CompressedSize = compressedData.Length,
            CompressedData = compressedData,
            Duration = sw.Elapsed,
            IsLossless = true
        };
    }
    
    public override DecompressionResult Decompress(ReadOnlySpan<byte> compressedData, CompressionOptions? options = null)
    {
        var sw = Stopwatch.StartNew();
        
        using var input = new MemoryStream(compressedData.ToArray());
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        
        sw.Stop();
        
        var decompressedData = output.ToArray();
        
        return new DecompressionResult
        {
            Method = Name,
            CompressedSize = compressedData.Length,
            DecompressedSize = decompressedData.Length,
            DecompressedData = decompressedData,
            Duration = sw.Elapsed
        };
    }
}

/// <summary>
/// ZLib compression backend (built into .NET 6+).
/// </summary>
public sealed class ZLibBackend : CompressionMethodBase
{
    public override string Name => "zlib";
    public override string Description => "ZLib compression";
    public override string Category => "Backend";
    
    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();
        
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }
        
        sw.Stop();
        
        var compressedData = output.ToArray();
        
        return new CompressionResult
        {
            Method = Name,
            OriginalSize = data.Length,
            CompressedSize = compressedData.Length,
            CompressedData = compressedData,
            Duration = sw.Elapsed,
            IsLossless = true
        };
    }
    
    public override DecompressionResult Decompress(ReadOnlySpan<byte> compressedData, CompressionOptions? options = null)
    {
        var sw = Stopwatch.StartNew();
        
        using var input = new MemoryStream(compressedData.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        
        sw.Stop();
        
        var decompressedData = output.ToArray();
        
        return new DecompressionResult
        {
            Method = Name,
            CompressedSize = compressedData.Length,
            DecompressedSize = decompressedData.Length,
            DecompressedData = decompressedData,
            Duration = sw.Elapsed
        };
    }
}
