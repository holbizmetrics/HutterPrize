using System.Runtime.CompilerServices;

namespace HutterLab.Core.Coding;

/// <summary>
/// Order-0 adaptive frequency model for arithmetic/range coding.
///
/// Maintains symbol frequencies and cumulative frequencies.
/// Rescales when total reaches MAX_TOTAL to maintain range coder precision.
///
/// For N symbols, cumulative frequency array has N+1 entries:
///   cumFreq[0] = 0
///   cumFreq[i] = sum of freq[0..i-1]
///   cumFreq[N] = total
///
/// Constraint: total must stay below 2^16 (65536) for range coder precision.
/// With MAX_TOTAL = 2^14, we have headroom.
/// </summary>
public sealed class AdaptiveModel
{
    private readonly int[] _freq;
    private readonly int[] _cumFreq;
    private int _total;
    private readonly int _symbolCount;

    // Rescale threshold. Must be < 2^16 (range coder requires range/total > 0
    // when range >= 2^24, so total < 2^16 ensures range/total >= 2^8).
    private const int MAX_TOTAL = 1 << 14; // 16384

    public AdaptiveModel(int symbolCount)
    {
        _symbolCount = symbolCount;
        _freq = new int[symbolCount];
        _cumFreq = new int[symbolCount + 1];

        // Initialize: each symbol has frequency 1
        for (int i = 0; i < symbolCount; i++)
            _freq[i] = 1;
        _total = symbolCount;

        RebuildCumFreq();
    }

    public int SymbolCount => _symbolCount;
    public int Total => _total;

    /// <summary>
    /// Get encoding info for a symbol: (cumulative frequency, frequency, total).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetEncodeInfo(int symbol, out uint cumFreq, out uint freq, out uint total)
    {
        cumFreq = (uint)_cumFreq[symbol];
        freq = (uint)_freq[symbol];
        total = (uint)_total;
    }

    /// <summary>
    /// Find the symbol corresponding to a cumulative frequency value.
    /// Used by decoder: cumValue = decoder.GetFreq(total), then symbol = model.GetSymbol(cumValue).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetSymbol(uint cumValue)
    {
        // Binary search: find largest i such that cumFreq[i] <= cumValue
        int lo = 0, hi = _symbolCount - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_cumFreq[mid] <= (int)cumValue)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    /// <summary>
    /// Update model after encoding/decoding a symbol.
    /// Must be called identically by encoder and decoder.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(int symbol)
    {
        _freq[symbol]++;
        _total++;

        // Incremental cumFreq update: only entries after this symbol change
        for (int i = symbol + 1; i <= _symbolCount; i++)
            _cumFreq[i]++;

        if (_total >= MAX_TOTAL)
            Rescale();
    }

    /// <summary>
    /// Halve all frequencies (keeping minimum of 1) to adapt to changing statistics.
    /// This prevents any single symbol from dominating and allows the model
    /// to track local statistics rather than global averages.
    /// </summary>
    private void Rescale()
    {
        _total = 0;
        for (int i = 0; i < _symbolCount; i++)
        {
            _freq[i] = (_freq[i] + 1) >> 1; // Halve, minimum 1
            _total += _freq[i];
        }
        RebuildCumFreq();
    }

    private void RebuildCumFreq()
    {
        _cumFreq[0] = 0;
        for (int i = 0; i < _symbolCount; i++)
            _cumFreq[i + 1] = _cumFreq[i] + _freq[i];
    }
}
