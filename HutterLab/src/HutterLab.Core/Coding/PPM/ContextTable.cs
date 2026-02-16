namespace HutterLab.Core.Coding.PPM;

/// <summary>
/// Compact frequency table for a PPM context.
/// Stores (symbol, frequency) pairs in sorted order by symbol value.
/// Used by PPMModel for each context of each order.
/// </summary>
public sealed class ContextTable
{
    private byte[] _symbols;
    private int[] _freqs;
    private int _count;
    private int _total;

    private const int MAX_TOTAL = 1 << 14; // 16384 — well under range coder's 2^16 limit

    public ContextTable()
    {
        _symbols = new byte[2];
        _freqs = new int[2];
    }

    /// <summary>
    /// Update frequency for a symbol. Inserts if new, maintaining sorted order.
    /// </summary>
    public void Update(byte symbol)
    {
        int idx = FindIndex(symbol);
        if (idx >= 0)
        {
            _freqs[idx]++;
        }
        else
        {
            int insertAt = ~idx;
            if (_count == _symbols.Length)
            {
                int newCap = Math.Min(_count * 2, 256);
                Array.Resize(ref _symbols, newCap);
                Array.Resize(ref _freqs, newCap);
            }
            if (insertAt < _count)
            {
                Array.Copy(_symbols, insertAt, _symbols, insertAt + 1, _count - insertAt);
                Array.Copy(_freqs, insertAt, _freqs, insertAt + 1, _count - insertAt);
            }
            _symbols[insertAt] = symbol;
            _freqs[insertAt] = 1;
            _count++;
        }
        _total++;

        if (_total >= MAX_TOTAL)
            Rescale();
    }

    /// <summary>
    /// Compute effective total and distinct count, excluding symbols in the exclusion array.
    /// Returns true if there are any non-excluded symbols.
    /// </summary>
    public bool GetEffectiveTotals(bool[] exclusion, out int effTotal, out int effDistinct)
    {
        effTotal = 0;
        effDistinct = 0;
        for (int i = 0; i < _count; i++)
        {
            if (exclusion[_symbols[i]]) continue;
            effTotal += _freqs[i];
            effDistinct++;
        }
        return effDistinct > 0;
    }

    /// <summary>
    /// Try to encode a symbol. Returns true if found and not excluded.
    /// Sets cumFreq and freq for the range encoder. Caller supplies total = effTotal + effDistinct.
    /// </summary>
    public bool TryEncode(byte symbol, bool[] exclusion, out uint cumFreq, out uint freq)
    {
        cumFreq = 0;
        freq = 0;

        if (exclusion[symbol])
            return false;

        for (int i = 0; i < _count; i++)
        {
            byte s = _symbols[i];
            if (exclusion[s]) continue;

            if (s == symbol)
            {
                freq = (uint)_freqs[i];
                return true;
            }

            // Symbols are sorted, so all non-excluded s < symbol contribute to cumFreq
            cumFreq += (uint)_freqs[i];
        }

        return false; // symbol not in this context
    }

    /// <summary>
    /// Decode: given cumulative value from the range decoder, find the symbol or escape.
    /// Returns symbol (0-255) or -1 for escape.
    /// cumValue must be less than effTotal + effDistinct.
    /// </summary>
    public int Decode(uint cumValue, bool[] exclusion, int effTotal, int escapeFreq,
        out uint cumFreq, out uint freq)
    {
        // Escape occupies [effTotal, effTotal + escapeFreq)
        if (cumValue >= (uint)effTotal)
        {
            cumFreq = (uint)effTotal;
            freq = (uint)escapeFreq;
            return -1;
        }

        // Symbol region [0, effTotal)
        uint cum = 0;
        for (int i = 0; i < _count; i++)
        {
            if (exclusion[_symbols[i]]) continue;

            uint f = (uint)_freqs[i];
            if (cum + f > cumValue)
            {
                cumFreq = cum;
                freq = f;
                return _symbols[i];
            }
            cum += f;
        }

        throw new InvalidOperationException("PPM decode error: symbol not found");
    }

    /// <summary>
    /// Add all symbols in this context to the exclusion set.
    /// Called after encoding/decoding an escape from this context.
    /// </summary>
    public void AddToExclusion(bool[] exclusion)
    {
        for (int i = 0; i < _count; i++)
            exclusion[_symbols[i]] = true;
    }

    /// <summary>
    /// Assign probability mass to non-excluded symbols for distribution extraction.
    /// scale = escapeProd / (effTotal + effDistinct) from the PPM walk.
    /// </summary>
    public void AssignProbabilities(float[] probs, bool[] exclusion, float scale)
    {
        for (int i = 0; i < _count; i++)
        {
            byte s = _symbols[i];
            if (exclusion[s]) continue;
            probs[s] = _freqs[i] * scale;
        }
    }

    /// <summary>
    /// Binary search for symbol in sorted array.
    /// Returns index if found, or ~insertionPoint if not found.
    /// </summary>
    private int FindIndex(byte symbol)
    {
        int lo = 0, hi = _count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            byte s = _symbols[mid];
            if (s == symbol) return mid;
            if (s < symbol) lo = mid + 1;
            else hi = mid - 1;
        }
        return ~lo;
    }

    /// <summary>
    /// Halve all frequencies (minimum 1) to prevent overflow and track recent statistics.
    /// </summary>
    private void Rescale()
    {
        _total = 0;
        for (int i = 0; i < _count; i++)
        {
            _freqs[i] = (_freqs[i] + 1) >> 1;
            _total += _freqs[i];
        }
    }
}
