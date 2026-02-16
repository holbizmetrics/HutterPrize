using System.Runtime.CompilerServices;

namespace HutterLab.Core.Coding;

/// <summary>
/// Range decoder — symmetric counterpart to RangeEncoder.
///
/// Usage pattern:
///   1. Call GetFreq(total) to get the cumulative frequency
///   2. Use the model to find which symbol has that cumulative frequency
///   3. Call Update(cumFreq, freq) to advance the decoder state
///
/// The two-step decode is necessary because the decoder needs to know
/// which interval to narrow to, and only the model knows that.
/// </summary>
public sealed class RangeDecoder : IDisposable
{
    private const uint TOP = 1u << 24;  // Must match encoder

    private uint _low;
    private uint _code;     // Value read from compressed stream
    private uint _range = uint.MaxValue;
    private readonly Stream _input;

    public RangeDecoder(Stream input)
    {
        _input = input;

        // Read initial 5 bytes into code (matches encoder's Flush of 5 bytes)
        for (int i = 0; i < 5; i++)
            _code = (_code << 8) | ReadByte();
    }

    /// <summary>
    /// Get the cumulative frequency of the encoded symbol.
    /// After calling this, use the model to find the symbol, then call Update().
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint GetFreq(uint total)
    {
        _range /= total;
        uint offset = (_code - _low) / _range;
        // Clamp to valid range (safety against rounding)
        return offset < total ? offset : total - 1;
    }

    /// <summary>
    /// Update decoder state after determining the symbol.
    /// cumFreq and freq must match what the encoder used.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(uint cumFreq, uint freq)
    {
        // Note: _range was already divided by total in GetFreq()
        _low += cumFreq * _range;
        _range *= freq;

        // Normalize (must be identical to encoder)
        while (_range < TOP)
        {
            _code = (_code << 8) | ReadByte();
            _low <<= 8;
            _range <<= 8;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ReadByte()
    {
        int b = _input.ReadByte();
        return b < 0 ? 0u : (uint)b;
    }

    public void Dispose() { }
}
