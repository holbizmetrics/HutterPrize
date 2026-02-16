using System.Diagnostics;
using HutterLab.Core.Coding;
using HutterLab.Core.Interfaces;
using HutterLab.Core.Models;

namespace HutterLab.Core.Methods.Statistical;

/// <summary>
/// Order-0 adaptive arithmetic (range) coding.
///
/// This is the simplest useful arithmetic coder: each byte is encoded
/// using a single adaptive frequency model with no context.
///
/// Expected ratio: ~5x on English text (near order-0 entropy).
/// This forms the encoding foundation — PPM adds context modeling on top.
/// </summary>
public sealed class Order0ArithmeticMethod : CompressionMethodBase
{
    public override string Name => "arithmetic";
    public override string Description => "Order-0 adaptive arithmetic coding";
    public override string Category => "Statistical";

    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        // Header: original size (for decoder to know when to stop)
        writer.Write((long)data.Length);

        // Model: 256 byte values, adaptive
        var model = new AdaptiveModel(256);

        // Encode
        using var encodedStream = new MemoryStream();
        var encoder = new RangeEncoder(encodedStream);

        for (int i = 0; i < data.Length; i++)
        {
            model.GetEncodeInfo(data[i], out uint cumFreq, out uint freq, out uint total);
            encoder.Encode(cumFreq, freq, total);
            model.Update(data[i]);
        }

        encoder.Flush();

        // Write encoded data
        var encoded = encodedStream.ToArray();
        writer.Write(encoded);

        sw.Stop();

        var compressedData = output.ToArray();
        Log(opts, $"Arithmetic: {data.Length:N0} → {compressedData.Length:N0} ({(double)data.Length / compressedData.Length:F2}×)");

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
        using var reader = new BinaryReader(input);

        // Read header
        var originalSize = reader.ReadInt64();

        // Model: must match encoder exactly
        var model = new AdaptiveModel(256);

        // Decode
        var decoder = new RangeDecoder(input);
        var output = new byte[originalSize];

        for (long i = 0; i < originalSize; i++)
        {
            uint cumValue = decoder.GetFreq((uint)model.Total);
            int symbol = model.GetSymbol(cumValue);

            model.GetEncodeInfo(symbol, out uint cumFreq, out uint freq, out uint total);
            decoder.Update(cumFreq, freq);

            model.Update(symbol);
            output[i] = (byte)symbol;
        }

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
