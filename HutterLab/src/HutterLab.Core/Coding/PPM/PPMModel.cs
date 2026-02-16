namespace HutterLab.Core.Coding.PPM;

/// <summary>
/// PPM (Prediction by Partial Matching) context model with PPMD escape estimation.
///
/// Uses hash-based context lookup per order (0..maxOrder).
/// Escape probability at each order uses Method D: escape_freq = max(1, distinct/2).
/// This gives more probability mass to seen symbols vs PPMC (escape = distinct).
/// Full exclusion: symbols seen at higher orders are excluded at lower orders.
/// Order -1 fallback: uniform distribution over remaining (non-excluded) symbols.
/// </summary>
public sealed class PPMModel
{
    private readonly int _maxOrder;
    private readonly Dictionary<ulong, ContextTable>[] _contexts;
    private readonly byte[] _contextBuf;
    private int _contextPos;
    private int _contextLen;
    private readonly bool[] _exclusion = new bool[256];
    private readonly bool[] _predExclusion = new bool[256]; // for PredictDistribution

    public PPMModel(int maxOrder)
    {
        _maxOrder = Math.Max(maxOrder, 0);
        _contexts = new Dictionary<ulong, ContextTable>[_maxOrder + 1];
        for (int i = 0; i <= _maxOrder; i++)
            _contexts[i] = new Dictionary<ulong, ContextTable>();
        _contextBuf = new byte[Math.Max(_maxOrder, 1)];
    }

    /// <summary>
    /// Encode a symbol using the PPM model and range encoder.
    /// </summary>
    public void Encode(byte symbol, RangeEncoder encoder)
    {
        Array.Clear(_exclusion);
        bool encoded = false;

        int maxOrd = Math.Min(_maxOrder, _contextLen);

        for (int order = maxOrd; order >= 0; order--)
        {
            ulong hash = HashContext(order);
            if (!_contexts[order].TryGetValue(hash, out var table))
                continue;

            if (!table.GetEffectiveTotals(_exclusion, out int effTotal, out int effDistinct))
                continue; // all symbols excluded at this order

            int escFreq = EscapeFreq(effDistinct);
            uint total = (uint)(effTotal + escFreq);

            if (table.TryEncode(symbol, _exclusion, out uint cumFreq, out uint freq))
            {
                encoder.Encode(cumFreq, freq, total);
                encoded = true;
                break;
            }

            // Symbol not found — encode escape and fall to lower order
            encoder.Encode((uint)effTotal, (uint)escFreq, total);
            table.AddToExclusion(_exclusion);
        }

        if (!encoded)
            EncodeOrderMinus1(symbol, encoder);

        UpdateContexts(symbol);
        AddToContext(symbol);
    }

    /// <summary>
    /// Decode a symbol using the PPM model and range decoder.
    /// </summary>
    public byte Decode(RangeDecoder decoder)
    {
        Array.Clear(_exclusion);

        int maxOrd = Math.Min(_maxOrder, _contextLen);

        for (int order = maxOrd; order >= 0; order--)
        {
            ulong hash = HashContext(order);
            if (!_contexts[order].TryGetValue(hash, out var table))
                continue;

            if (!table.GetEffectiveTotals(_exclusion, out int effTotal, out int effDistinct))
                continue;

            int escFreq = EscapeFreq(effDistinct);
            uint total = (uint)(effTotal + escFreq);
            uint cumValue = decoder.GetFreq(total);

            int result = table.Decode(cumValue, _exclusion, effTotal, escFreq,
                out uint cumFreq, out uint freq);
            decoder.Update(cumFreq, freq);

            if (result >= 0)
            {
                byte symbol = (byte)result;
                UpdateContexts(symbol);
                AddToContext(symbol);
                return symbol;
            }

            // Escape — exclude this context's symbols and try lower order
            table.AddToExclusion(_exclusion);
        }

        // Order -1: uniform over remaining symbols
        byte s = DecodeOrderMinus1(decoder);
        UpdateContexts(s);
        AddToContext(s);
        return s;
    }

    /// <summary>
    /// Extract the full 256-way probability distribution without encoding.
    /// Used by the context mixer to get PPM predictions for blending.
    /// </summary>
    public void PredictDistribution(float[] probs)
    {
        Array.Clear(probs, 0, 256);
        Array.Clear(_predExclusion, 0, 256);
        float escapeProd = 1.0f;

        int maxOrd = Math.Min(_maxOrder, _contextLen);

        for (int order = maxOrd; order >= 0; order--)
        {
            ulong hash = HashContext(order);
            if (!_contexts[order].TryGetValue(hash, out var table))
                continue;

            if (!table.GetEffectiveTotals(_predExclusion, out int effTotal, out int effDistinct))
                continue;

            int escFreq = EscapeFreq(effDistinct);
            float total = effTotal + escFreq;

            // Each non-excluded symbol at this order gets:
            // escapeProd * freq[s] / (effTotal + escapeFreq)
            table.AssignProbabilities(probs, _predExclusion, escapeProd / total);

            // Escape probability for this order (PPMD: halved escape)
            escapeProd *= escFreq / total;
            table.AddToExclusion(_predExclusion);
        }

        // Order -1: distribute remaining probability uniformly
        int remaining = 0;
        for (int s = 0; s < 256; s++)
            if (!_predExclusion[s]) remaining++;

        if (remaining > 0)
        {
            float perSymbol = escapeProd / remaining;
            for (int s = 0; s < 256; s++)
                if (!_predExclusion[s])
                    probs[s] = perSymbol;
        }
    }

    /// <summary>
    /// Update model after observing a symbol (without encoding/decoding).
    /// Used by the context mixer after the mixed distribution was encoded.
    /// </summary>
    public void UpdateModel(byte symbol)
    {
        UpdateContexts(symbol);
        AddToContext(symbol);
    }

    /// <summary>
    /// Order -1: uniform distribution over all non-excluded symbols (1/remaining each).
    /// Always succeeds — every possible byte value is covered.
    /// </summary>
    private void EncodeOrderMinus1(byte symbol, RangeEncoder encoder)
    {
        int remaining = 0;
        for (int i = 0; i < 256; i++)
            if (!_exclusion[i]) remaining++;

        uint cumFreq = 0;
        for (int s = 0; s < symbol; s++)
            if (!_exclusion[s]) cumFreq++;

        encoder.Encode(cumFreq, 1, (uint)remaining);
    }

    private byte DecodeOrderMinus1(RangeDecoder decoder)
    {
        int remaining = 0;
        for (int i = 0; i < 256; i++)
            if (!_exclusion[i]) remaining++;

        uint cumValue = decoder.GetFreq((uint)remaining);

        uint cum = 0;
        for (int s = 0; s < 256; s++)
        {
            if (_exclusion[s]) continue;
            if (cum == cumValue)
            {
                decoder.Update(cum, 1);
                return (byte)s;
            }
            cum++;
        }

        throw new InvalidOperationException("PPM order -1 decode failed");
    }

    /// <summary>
    /// Update all context tables (order 0 through current max) with the encoded/decoded symbol.
    /// Must be called identically by encoder and decoder.
    /// </summary>
    private void UpdateContexts(byte symbol)
    {
        int maxOrd = Math.Min(_maxOrder, _contextLen);
        for (int order = 0; order <= maxOrd; order++)
        {
            ulong hash = HashContext(order);
            if (!_contexts[order].TryGetValue(hash, out var table))
            {
                table = new ContextTable();
                _contexts[order][hash] = table;
            }
            table.Update(symbol);
        }
    }

    /// <summary>
    /// Add a byte to the circular context buffer.
    /// </summary>
    private void AddToContext(byte symbol)
    {
        if (_maxOrder > 0)
        {
            _contextBuf[_contextPos] = symbol;
            _contextPos = (_contextPos + 1) % _contextBuf.Length;
        }
        if (_contextLen < _maxOrder)
            _contextLen++;
    }

    /// <summary>
    /// PPMD escape frequency: half the distinct count, minimum 1.
    /// Gives more probability to seen symbols vs PPMC (which uses full distinct count).
    /// </summary>
    private static int EscapeFreq(int effDistinct) => Math.Max(1, effDistinct >> 1);

    /// <summary>
    /// FNV-1a hash of the last 'order' bytes in the context buffer.
    /// Order 0 always returns 0 (empty context).
    /// </summary>
    private ulong HashContext(int order)
    {
        if (order == 0) return 0;

        ulong h = 14695981039346656037UL; // FNV-1a offset basis
        for (int i = 0; i < order; i++)
        {
            int idx = (_contextPos - order + i + _contextBuf.Length) % _contextBuf.Length;
            h ^= _contextBuf[idx];
            h *= 1099511628211UL; // FNV-1a prime
        }
        return h;
    }
}
