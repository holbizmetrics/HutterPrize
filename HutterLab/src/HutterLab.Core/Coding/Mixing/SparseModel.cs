namespace HutterLab.Core.Coding.Mixing;

/// <summary>
/// Sparse context model: uses non-adjacent bytes as context.
/// Complementary to PPM which uses contiguous contexts.
///
/// Three patterns:
///   [-1, -3, -5] — skip-1 (captures periodic/structural patterns)
///   [-1, -2, -4] — mixed gaps (medium-range structure)
///   [-2, -4, -8] — power-of-2 (long-range structure, column alignment)
///
/// Each pattern maps a context hash to a (predicted_byte, hit_count) entry.
/// Confidence scales with hit count; decay on misprediction.
/// </summary>
public sealed class SparseModel : IBytePredictor
{
    private readonly byte[] _buf;
    private int _pos;

    private static readonly int[][] Patterns =
    [
        [1, 3, 5],
        [1, 2, 4],
        [2, 4, 8],
    ];

    private readonly Entry[][] _tables;
    private readonly int _mask;

    private struct Entry
    {
        public byte Predicted;
        public byte Count;
    }

    public SparseModel(int maxSize, int tableBits = 20)
    {
        _buf = new byte[maxSize + 16];
        int tableSize = 1 << tableBits;
        _mask = tableSize - 1;
        _tables = new Entry[Patterns.Length][];
        for (int i = 0; i < Patterns.Length; i++)
            _tables[i] = new Entry[tableSize];
    }

    public void Predict(float[] probs)
    {
        Array.Clear(probs, 0, 256);
        float weight = 1.0f / Patterns.Length;

        for (int p = 0; p < Patterns.Length; p++)
        {
            uint hash = HashPattern(p);
            ref var entry = ref _tables[p][hash & _mask];

            if (entry.Count >= 3)
            {
                // Soft prediction: small boost above uniform, scaling with confidence.
                // Keeps most probability mass uniform so geometric mixer isn't destructive.
                float boost = Math.Min(0.4f, entry.Count * 0.015f);
                float pPred = (1.0f + boost * 255) / 256;
                float pOther = (1.0f - boost) / 256;
                for (int s = 0; s < 256; s++)
                    probs[s] += (s == entry.Predicted ? pPred : pOther) * weight;
            }
            else
            {
                float u = weight / 256;
                for (int s = 0; s < 256; s++)
                    probs[s] += u;
            }
        }
    }

    public void Update(byte symbol)
    {
        _buf[_pos] = symbol;

        for (int p = 0; p < Patterns.Length; p++)
        {
            uint hash = HashPattern(p);
            ref var entry = ref _tables[p][hash & _mask];

            if (entry.Predicted == symbol && entry.Count > 0)
            {
                entry.Count = (byte)Math.Min(255, entry.Count + 1);
            }
            else if (entry.Count <= 1)
            {
                entry.Predicted = symbol;
                entry.Count = 1;
            }
            else
            {
                entry.Count >>= 1; // decay on misprediction
            }
        }

        _pos++;
    }

    private uint HashPattern(int patternIdx)
    {
        uint h = 2166136261u;
        var offsets = Patterns[patternIdx];
        for (int i = 0; i < offsets.Length; i++)
        {
            int idx = _pos - offsets[i];
            byte b = idx >= 0 ? _buf[idx] : (byte)0;
            h ^= b;
            h *= 16777619u;
        }
        return h;
    }
}
