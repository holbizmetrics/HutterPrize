using System.Diagnostics;
using HutterLab.Core.Coding.BitLevel;
using HutterLab.Core.Coding.PPM;
using HutterLab.Core.Interfaces;
using HutterLab.Core.Models;

namespace HutterLab.Core.Methods.Statistical;

/// <summary>
/// Bit-level context mixing compression (PAQ-style).
///
/// Encodes each byte as 8 bits MSB-first. Multiple context models predict
/// each bit, predictions are mixed in logit space (logistic mixing), then
/// refined by an APM (Secondary Symbol Estimation).
///
/// Key models:
///   PPM-3/6/8: byte-level distributions marginalized to bit predictions
///   Order-0/1 counters: fast bit-level adaptation
///   Word model: word-boundary context
///   Match model: exact repetition at bit level
/// </summary>
public sealed class BitLevelMixMethod : CompressionMethodBase
{
    public override string Name => "bitmix";
    public override string Description => "Bit-level context mixing (PAQ-style)";
    public override string Category => "Statistical";

    private const int NUM_MODELS = 8;

    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((long)data.Length);

        using var encodedStream = new MemoryStream();
        var encoder = new BinaryEncoder(encodedStream);
        var state = new ModelState(data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            state.PrepareByte();
            int ctx = 1;

            for (int j = 7; j >= 0; j--)
            {
                int bit = (b >> j) & 1;
                int p = state.PredictAndMix(ctx);
                encoder.Encode(bit, p);
                state.Update(bit, ctx);
                ctx = (ctx << 1) | bit;
            }

            state.UpdateByte(b);
        }

        encoder.Flush();
        writer.Write(encodedStream.ToArray());
        sw.Stop();

        var compressed = output.ToArray();
        Log(opts, $"BitMix: {data.Length:N0} → {compressed.Length:N0} ({(double)data.Length / compressed.Length:F2}×)");

        return new CompressionResult
        {
            Method = Name,
            OriginalSize = data.Length,
            CompressedSize = compressed.Length,
            CompressedData = compressed,
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
        var decoder = new BinaryDecoder(input);
        var state = new ModelState((int)originalSize);
        var result = new byte[originalSize];

        for (long i = 0; i < originalSize; i++)
        {
            state.PrepareByte();
            int ctx = 1;

            for (int j = 7; j >= 0; j--)
            {
                int p = state.PredictAndMix(ctx);
                int bit = decoder.Decode(p);
                state.Update(bit, ctx);
                ctx = (ctx << 1) | bit;
            }

            byte b = (byte)(ctx & 0xFF);
            result[i] = b;
            state.UpdateByte(b);
        }

        sw.Stop();

        return new DecompressionResult
        {
            Method = Name,
            CompressedSize = compressedData.Length,
            DecompressedSize = result.Length,
            DecompressedData = result,
            Duration = sw.Elapsed
        };
    }

    private sealed class ModelState
    {
        // PPM models (byte-level, marginalized to bit predictions)
        private readonly PPMModel _ppm3;
        private readonly PPMModel _ppm6;
        private readonly PPMModel _ppm8;
        private readonly float[] _dist3 = new float[256];
        private readonly float[] _dist6 = new float[256];
        private readonly float[] _dist8 = new float[256];

        // Simple bit-level adaptive counters
        private readonly Predictor _o0;   // partial byte only
        private readonly Predictor _o1;   // prev byte + partial
        private readonly Predictor _word; // word hash + partial

        private readonly Mixer _mixer;
        private readonly APM _apm;

        // Byte history
        private readonly byte[] _buf;
        private int _pos;

        // Match model
        private readonly Dictionary<uint, int> _hashTable;
        private int _matchStart;
        private int _matchLen;
        private bool _matching;

        // Word model
        private uint _wordHash;
        private uint _prevWordHash;

        // Precomputed per-byte hashes
        private uint _h1, _wordCtx;

        private readonly int[] _preds = new int[NUM_MODELS];

        public ModelState(int maxSize)
        {
            _ppm3 = new PPMModel(3);
            _ppm6 = new PPMModel(6);
            _ppm8 = new PPMModel(8);

            _o0 = new Predictor(9);       // 512
            _o1 = new Predictor(17);      // 128K
            _word = new Predictor(22);    // 4M

            _mixer = new Mixer(NUM_MODELS);
            _apm = new APM(9, 33);

            _buf = new byte[maxSize + 16];
            _hashTable = new Dictionary<uint, int>(Math.Min(maxSize, 1 << 20));
        }

        /// <summary>
        /// Compute PPM distributions once per byte (before encoding its 8 bits).
        /// </summary>
        public void PrepareByte()
        {
            _ppm3.PredictDistribution(_dist3);
            _ppm6.PredictDistribution(_dist6);
            _ppm8.PredictDistribution(_dist8);
        }

        public int PredictAndMix(int partial)
        {
            uint p = (uint)partial;

            // PPM marginal predictions (strong, from byte-level distributions)
            _preds[0] = MarginalP1(_dist3, partial);
            _preds[1] = MarginalP1(_dist6, partial);
            _preds[2] = MarginalP1(_dist8, partial);

            // Simple adaptive counters (fast convergence)
            _preds[3] = _o0.Predict(p);
            _preds[4] = _o1.Predict((_h1 << 9 | p) & 0x1FFFF);
            _preds[5] = _word.Predict((_wordCtx * 512 + p) & 0x3FFFFF);

            // Match model
            _preds[6] = PredictMatch(partial);

            // Duplicate PPM-6 (highest weight model, helps mixer stability)
            _preds[7] = _preds[1];

            int mixed = _mixer.Mix(_preds);
            return _apm.Map(p & 0x1FF, mixed);
        }

        public void Update(int bit, int partial)
        {
            uint p = (uint)partial;

            _o0.Update(p, bit);
            _o1.Update((_h1 << 9 | p) & 0x1FFFF, bit);
            _word.Update((_wordCtx * 512 + p) & 0x3FFFFF, bit);

            _mixer.Update(bit);
            _apm.Update(bit);
        }

        public void UpdateByte(byte b)
        {
            _buf[_pos] = b;

            _ppm3.UpdateModel(b);
            _ppm6.UpdateModel(b);
            _ppm8.UpdateModel(b);

            UpdateMatch(b);
            UpdateWord(b);
            _pos++;

            _h1 = _pos >= 1 ? (uint)_buf[_pos - 1] : 0;
            _wordCtx = _wordHash * 1000003u + _prevWordHash;
        }

        /// <summary>
        /// Compute P(bit=1) from a 256-way byte distribution, conditioned on partial byte.
        /// partial uses sentinel-bit encoding: 1, 1b7, 1b7b6, ...
        /// </summary>
        private static int MarginalP1(float[] dist, int partial)
        {
            int bitsKnown = 0;
            int temp = partial;
            while (temp > 1) { temp >>= 1; bitsKnown++; }

            int bitPos = 7 - bitsKnown;

            int knownMask = 0;
            int knownValue = 0;
            for (int k = 0; k < bitsKnown; k++)
            {
                int bitIdx = 7 - k;
                knownMask |= 1 << bitIdx;
                int bitVal = (partial >> (bitsKnown - 1 - k)) & 1;
                knownValue |= bitVal << bitIdx;
            }

            float sum1 = 0, sum0 = 0;
            for (int s = 0; s < 256; s++)
            {
                if ((s & knownMask) != knownValue) continue;
                if (((s >> bitPos) & 1) != 0)
                    sum1 += dist[s];
                else
                    sum0 += dist[s];
            }

            float total = sum1 + sum0;
            if (total < 1e-10f) return 32768;

            return Math.Clamp((int)(sum1 / total * 65534 + 1), 1, 65534);
        }

        private int PredictMatch(int partial)
        {
            if (!_matching || _matchStart + _matchLen >= _pos)
                return 32768;

            byte matchedByte = _buf[_matchStart + _matchLen];

            int bitsEncoded = 0;
            int temp = partial;
            while (temp > 1) { temp >>= 1; bitsEncoded++; }

            for (int k = 0; k < bitsEncoded; k++)
            {
                int actualBit = (partial >> (bitsEncoded - 1 - k)) & 1;
                int matchBit = (matchedByte >> (7 - k)) & 1;
                if (actualBit != matchBit)
                    return 32768;
            }

            int predictedBit = (matchedByte >> (7 - bitsEncoded)) & 1;
            float conf = Math.Clamp(0.85f + (_matchLen - 4) * 0.02f, 0.85f, 0.98f);
            return predictedBit != 0
                ? Math.Clamp((int)(conf * 65535), 1, 65534)
                : Math.Clamp((int)((1 - conf) * 65535), 1, 65534);
        }

        private void UpdateMatch(byte b)
        {
            if (_matching)
            {
                if (_matchStart + _matchLen < _pos && _buf[_matchStart + _matchLen] == b)
                    _matchLen++;
                else
                    _matching = false;
            }

            if (_pos >= 3)
            {
                uint hash = ComputeMatchHash(_pos - 3);
                if (!_matching && _hashTable.TryGetValue(hash, out int prevStart))
                {
                    bool valid = true;
                    for (int i = 0; i < 4; i++)
                    {
                        if (prevStart + i >= _pos - 3 + i) { valid = false; break; }
                        if (_buf[prevStart + i] != _buf[_pos - 3 + i])
                        { valid = false; break; }
                    }
                    if (valid && prevStart + 4 <= _pos)
                    {
                        _matchStart = prevStart;
                        _matchLen = 4;
                        _matching = true;
                    }
                }
                _hashTable[hash] = _pos - 3;
            }
        }

        private void UpdateWord(byte b)
        {
            if (IsWordChar(b))
                _wordHash = _wordHash * 997 + b;
            else
            {
                _prevWordHash = _wordHash;
                _wordHash = 0;
            }
        }

        private static bool IsWordChar(byte b) =>
            (b >= (byte)'a' && b <= (byte)'z') ||
            (b >= (byte)'A' && b <= (byte)'Z') ||
            (b >= (byte)'0' && b <= (byte)'9') ||
            b == (byte)'\'';

        private uint ComputeMatchHash(int start)
        {
            uint h = 2166136261u;
            for (int i = 0; i < 4 && start + i <= _pos; i++)
            {
                h ^= _buf[start + i];
                h *= 16777619u;
            }
            return h;
        }
    }
}
