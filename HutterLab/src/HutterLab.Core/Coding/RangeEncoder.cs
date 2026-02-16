using System.Runtime.CompilerServices;

namespace HutterLab.Core.Coding;

/// <summary>
/// Range encoder with carry propagation via cache technique.
/// Based on Schindler/Subbotin range coder.
///
/// Invariants:
///   - After normalization: range >= TOP (1 << 24)
///   - total passed to Encode() must be < (1 << 16) to ensure range/total > 0
///   - Encoder and decoder normalization must be symmetric
/// </summary>
public sealed class RangeEncoder : IDisposable
{
    private const uint TOP = 1u << 24;  // Normalization threshold

    private ulong _low;               // 64-bit to detect carries
    private uint _range = uint.MaxValue;
    private readonly Stream _output;
    private byte _cache;               // Pending byte that might need carry
    private int _cacheSize;            // Count of pending 0xFF bytes after cache
    private long _bytesWritten;

    public RangeEncoder(Stream output)
    {
        _output = output;
    }

    public long BytesWritten => _bytesWritten;

    /// <summary>
    /// Encode a symbol with cumulative frequency [cumFreq, cumFreq + freq) out of total.
    /// Requires: freq > 0, cumFreq + freq <= total, total < 65536.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Encode(uint cumFreq, uint freq, uint total)
    {
        _range /= total;
        _low += (ulong)cumFreq * _range;
        _range *= freq;

        while (_range < TOP)
        {
            ShiftLow();
            _range <<= 8;
        }
    }

    /// <summary>
    /// Flush remaining bytes. Must be called after encoding all symbols.
    /// </summary>
    public void Flush()
    {
        // Output 5 bytes to ensure all state is flushed
        for (int i = 0; i < 5; i++)
            ShiftLow();
    }

    /// <summary>
    /// Output a byte from the top of low, handling carry propagation.
    ///
    /// The cache technique:
    /// - When the top byte of low is 0xFF, a future carry could affect it.
    /// - We buffer 0xFF bytes in a counter instead of outputting them.
    /// - When we see a non-0xFF byte (or a carry), we can flush the buffer.
    /// </summary>
    private void ShiftLow()
    {
        // Check if we can flush (either carry happened, or byte is not 0xFF)
        if ((uint)(_low >> 32) != 0 || (_low & 0xFF000000u) != 0xFF000000u)
        {
            byte carry = (byte)(_low >> 32);

            // Output the cached byte (with carry added)
            WriteByte((byte)(_cache + carry));

            // Flush all pending 0xFF bytes (carry propagates through them)
            while (_cacheSize > 0)
            {
                WriteByte((byte)(0xFF + carry));
                _cacheSize--;
            }

            // New cache = top byte of lower 32 bits
            // Must cast to uint first — _low is 64-bit and carry bit would corrupt the value
            _cache = (byte)((uint)_low >> 24);
        }
        else
        {
            // Top byte is 0xFF — buffer it (might need carry later)
            _cacheSize++;
        }

        // Shift low left by 8, keep only lower 32 bits
        _low = (_low << 8) & 0xFFFFFFFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteByte(byte b)
    {
        _output.WriteByte(b);
        _bytesWritten++;
    }

    public void Dispose() { }
}
