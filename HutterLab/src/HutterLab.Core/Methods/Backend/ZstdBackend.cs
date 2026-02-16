using System.Diagnostics;
using HutterLab.Core.Interfaces;
using HutterLab.Core.Models;
using ZstdSharp;

namespace HutterLab.Core.Methods.Backend;

/// <summary>
/// Zstandard compression backend via ZstdSharp.
/// Facebook's Zstandard — excellent ratio at high levels, very fast decompress.
/// Level 22 is max compression (slow but best ratio).
/// </summary>
public sealed class ZstdBackend : CompressionMethodBase
{
    public override string Name => "zstd";
    public override string Description => "Zstandard compression (level 22)";
    public override string Category => "Backend";

    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();

        var level = opts.GetParameter("zstd_level", 22); // Max compression

        using var compressor = new Compressor(level);
        var compressedData = compressor.Wrap(data).ToArray();

        sw.Stop();

        Log(opts, $"Zstd (level {level}): {data.Length:N0} → {compressedData.Length:N0} ({(double)data.Length / compressedData.Length:F2}×)");

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

        using var decompressor = new Decompressor();
        var decompressedData = decompressor.Unwrap(compressedData).ToArray();

        sw.Stop();

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
