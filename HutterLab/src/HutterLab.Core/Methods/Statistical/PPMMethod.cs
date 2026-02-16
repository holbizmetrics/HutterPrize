using System.Diagnostics;
using HutterLab.Core.Coding;
using HutterLab.Core.Coding.PPM;
using HutterLab.Core.Interfaces;
using HutterLab.Core.Models;

namespace HutterLab.Core.Methods.Statistical;

/// <summary>
/// PPM (Prediction by Partial Matching) compression with arithmetic coding.
/// Uses PPMC escape estimation and full exclusion.
/// Parameter "order" controls context depth (default: 5).
/// </summary>
public sealed class PPMMethod : CompressionMethodBase
{
    public override string Name => "ppm";
    public override string Description => "PPM context modeling with arithmetic coding";
    public override string Category => "Statistical";

    private static int GetOrder(CompressionOptions? options)
    {
        if (options?.Parameters?.TryGetValue("order", out var val) == true)
        {
            if (val is int i) return Math.Clamp(i, 1, 12);
            if (val is string s && int.TryParse(s, out var order))
                return Math.Clamp(order, 1, 12);
        }
        return 5;
    }

    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();
        int order = GetOrder(options);

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        // Header: original size + order
        writer.Write((long)data.Length);
        writer.Write((byte)order);

        // Encode
        using var encodedStream = new MemoryStream();
        var encoder = new RangeEncoder(encodedStream);
        var model = new PPMModel(order);

        for (int i = 0; i < data.Length; i++)
            model.Encode(data[i], encoder);

        encoder.Flush();

        var encoded = encodedStream.ToArray();
        writer.Write(encoded);

        sw.Stop();

        var compressedData = output.ToArray();
        Log(opts, $"PPM(order={order}): {data.Length:N0} → {compressedData.Length:N0} ({(double)data.Length / compressedData.Length:F2}×)");

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

        var originalSize = reader.ReadInt64();
        var order = reader.ReadByte();

        var decoder = new RangeDecoder(input);
        var model = new PPMModel(order);
        var output = new byte[originalSize];

        for (long i = 0; i < originalSize; i++)
            output[i] = model.Decode(decoder);

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
