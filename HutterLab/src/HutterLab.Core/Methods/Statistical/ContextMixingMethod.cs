using System.Diagnostics;
using HutterLab.Core.Coding;
using HutterLab.Core.Coding.Mixing;
using HutterLab.Core.Interfaces;
using HutterLab.Core.Models;

namespace HutterLab.Core.Methods.Statistical;

/// <summary>
/// Context mixing compression: blends multiple prediction models
/// (PPM at different orders + match model) using adaptive weighted mixing,
/// then encodes with arithmetic coding.
///
/// This is the same fundamental approach as PAQ/cmix — the key insight that
/// mixing diverse models produces better predictions than any single model.
/// </summary>
public sealed class ContextMixingMethod : CompressionMethodBase
{
    public override string Name => "cmix";
    public override string Description => "Context mixing with PPM + match model";
    public override string Category => "Statistical";

    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        // Header
        writer.Write((long)data.Length);

        // Build models and mixer
        var mixer = CreateMixer(data.Length);

        // Encode
        using var encodedStream = new MemoryStream();
        var encoder = new RangeEncoder(encodedStream);

        for (int i = 0; i < data.Length; i++)
        {
            mixer.Predict();
            mixer.GetEncodeInfo(data[i], out uint cumFreq, out uint freq, out uint total);
            encoder.Encode(cumFreq, freq, total);
            mixer.Update(data[i]);
        }

        encoder.Flush();

        var encoded = encodedStream.ToArray();
        writer.Write(encoded);

        sw.Stop();

        var compressedData = output.ToArray();
        Log(opts, $"ContextMix: {data.Length:N0} → {compressedData.Length:N0} ({(double)data.Length / compressedData.Length:F2}×)");

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

        var mixer = CreateMixer((int)originalSize);

        var decoder = new RangeDecoder(input);
        var output = new byte[originalSize];

        for (long i = 0; i < originalSize; i++)
        {
            mixer.Predict();
            uint cumValue = decoder.GetFreq((uint)ByteMixer.FREQ_TOTAL);
            byte symbol = mixer.DecodeByte(cumValue, out uint cumFreq, out uint freq);
            decoder.Update(cumFreq, freq);
            mixer.Update(symbol);
            output[i] = symbol;
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

    private static ByteMixer CreateMixer(int dataSize)
    {
        // Models producing smooth 256-way distributions work best with geometric mixing.
        // Single-byte prediction models (sparse, word) hurt because geometric mixing
        // destructively suppresses all non-predicted bytes.
        //
        // PPM-3:  converges fast (fewer contexts), good short-range predictions
        // PPM-6:  best overall, primary statistical model
        // PPM-8:  captures long contexts (full phrases, XML paths)
        // PPM-10: very long contexts (Wikipedia templates, repeated boilerplate)
        // Match:  exact repetition detector (invisible when inactive via geometric mix)
        var models = new IBytePredictor[]
        {
            new PPMPredictor(3),          // Fast convergence, short-range
            new PPMPredictor(6),          // Primary statistical model
            new PPMPredictor(8),          // Long-range structural patterns
            new PPMPredictor(10),         // Very long contexts (templates)
            new MatchModel(dataSize, 4),  // Exact repetition detector
        };
        return new ByteMixer(models, [0.08f, 0.40f, 0.25f, 0.17f, 0.10f]);
    }
}
