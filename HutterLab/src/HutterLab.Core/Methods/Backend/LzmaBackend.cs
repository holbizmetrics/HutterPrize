using System.Diagnostics;
using HutterLab.Core.Interfaces;
using HutterLab.Core.Models;
using SharpCompress.Compressors.LZMA;

namespace HutterLab.Core.Methods.Backend;

/// <summary>
/// LZMA compression backend via SharpCompress LZipStream.
/// LZMA typically achieves excellent compression ratios on text data.
/// Used by 7-Zip. Slower compression, good decompression speed.
/// </summary>
public sealed class LzmaBackend : CompressionMethodBase
{
    public override string Name => "lzma";
    public override string Description => "LZMA/LZip compression";
    public override string Category => "Backend";

    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();

        using var output = new MemoryStream();

        // Use LZipStream which wraps LZMA with proper headers
        using (var lzip = new LZipStream(output, SharpCompress.Compressors.CompressionMode.Compress))
        {
            lzip.Write(data);
            lzip.Finish();
        }

        sw.Stop();

        var compressedData = output.ToArray();
        Log(opts, $"LZMA: {data.Length:N0} → {compressedData.Length:N0} ({(double)data.Length / compressedData.Length:F2}×)");

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
        using var lzip = new LZipStream(input, SharpCompress.Compressors.CompressionMode.Decompress);
        using var output = new MemoryStream();

        lzip.CopyTo(output);

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
