namespace HutterLab.Core.Coding.BitLevel;

/// <summary>
/// Adaptive Probability Map (APM) / Secondary Symbol Estimation (SSE).
///
/// Maps (context, input_prediction) → refined_prediction.
/// Uses linear interpolation between adjacent table entries for a smooth mapping.
/// Each entry is trained online toward the actual bit value.
/// </summary>
public sealed class APM
{
    private readonly int[] _table;
    private readonly int _n;         // entries per context
    private readonly uint _ctxMask;
    private int _lastIdx;
    private int _lastWeight;

    public APM(int contextBits, int entriesPerContext = 33)
    {
        _n = entriesPerContext;
        _ctxMask = (uint)((1 << contextBits) - 1);
        int totalEntries = (1 << contextBits) * _n;
        _table = new int[totalEntries];

        // Initialize with identity mapping: entry[i] = i * 65535 / (n-1)
        for (int c = 0; c < (1 << contextBits); c++)
        {
            int baseIdx = c * _n;
            for (int i = 0; i < _n; i++)
                _table[baseIdx + i] = i * 65534 / (_n - 1) + 1;
        }
    }

    /// <summary>
    /// Map an input prediction to a refined prediction given context.
    /// prediction: [1, 65534], returns refined prediction in [1, 65534].
    /// </summary>
    public int Map(uint context, int prediction)
    {
        int baseIdx = (int)(context & _ctxMask) * _n;

        // Scale prediction to table index with fractional part
        int t = (prediction - 1) * (_n - 1);
        int idx = t / 65533;
        int weight = t % 65533;

        if (idx >= _n - 1) { idx = _n - 2; weight = 65533; }

        _lastIdx = baseIdx + idx;
        _lastWeight = weight;

        // Interpolate between adjacent entries
        int p0 = _table[_lastIdx];
        int p1 = _table[_lastIdx + 1];
        int result = p0 + (int)(((long)(p1 - p0) * weight) / 65533);
        return Math.Clamp(result, 1, 65534);
    }

    /// <summary>
    /// Update the two interpolated entries toward the actual bit.
    /// </summary>
    public void Update(int bit)
    {
        int target = bit != 0 ? 65534 : 1;
        _table[_lastIdx] += (target - _table[_lastIdx]) >> 5;
        _table[_lastIdx + 1] += (target - _table[_lastIdx + 1]) >> 5;
    }
}
