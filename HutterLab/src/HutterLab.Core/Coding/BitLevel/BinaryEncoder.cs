namespace HutterLab.Core.Coding.BitLevel;

/// <summary>
/// FPAQ-style binary arithmetic encoder.
/// Encodes one bit at a time given P(bit=1) as a 16-bit probability.
/// </summary>
public sealed class BinaryEncoder : IDisposable
{
    private uint _x1;
    private uint _x2;
    private readonly Stream _out;

    public BinaryEncoder(Stream output)
    {
        _out = output;
        _x1 = 0;
        _x2 = 0xFFFFFFFF;
    }

    /// <summary>
    /// Encode a single bit. prob = P(bit=1) in [1, 65534].
    /// </summary>
    public void Encode(int bit, int prob)
    {
        uint xmid = _x1 + (uint)(((ulong)(_x2 - _x1) * (uint)prob) >> 16);
        if (bit != 0)
            _x2 = xmid;      // bit=1 uses lower range [x1, xmid], size ∝ prob
        else
            _x1 = xmid + 1;  // bit=0 uses upper range [xmid+1, x2]

        while ((_x1 ^ _x2) < 0x01000000u)
        {
            _out.WriteByte((byte)(_x2 >> 24));
            _x1 <<= 8;
            _x2 = (_x2 << 8) | 0xFF;
        }
    }

    public void Flush()
    {
        _out.WriteByte((byte)(_x1 >> 24));
        _out.WriteByte((byte)(_x1 >> 16));
        _out.WriteByte((byte)(_x1 >> 8));
        _out.WriteByte((byte)_x1);
    }

    public void Dispose() { }
}
