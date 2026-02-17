namespace HutterLab.Core.Coding.BitLevel;

/// <summary>
/// FPAQ-style binary arithmetic decoder. Mirrors BinaryEncoder exactly.
/// </summary>
public sealed class BinaryDecoder : IDisposable
{
    private uint _x1;
    private uint _x2;
    private uint _x;
    private readonly Stream _in;

    public BinaryDecoder(Stream input)
    {
        _in = input;
        _x1 = 0;
        _x2 = 0xFFFFFFFF;
        _x = 0;
        for (int i = 0; i < 4; i++)
        {
            int b = _in.ReadByte();
            _x = (_x << 8) | (uint)(b < 0 ? 0 : b);
        }
    }

    /// <summary>
    /// Decode a single bit. prob = P(bit=1) in [1, 65534].
    /// Must use the same probability the encoder used.
    /// </summary>
    public int Decode(int prob)
    {
        uint xmid = _x1 + (uint)(((ulong)(_x2 - _x1) * (uint)prob) >> 16);
        int bit = (_x <= xmid) ? 1 : 0;  // bit=1 if code is in lower range
        if (bit != 0)
            _x2 = xmid;
        else
            _x1 = xmid + 1;

        while ((_x1 ^ _x2) < 0x01000000u)
        {
            _x1 <<= 8;
            _x2 = (_x2 << 8) | 0xFF;
            int b = _in.ReadByte();
            _x = (_x << 8) | (uint)(b < 0 ? 0 : b);
        }

        return bit;
    }

    public void Dispose() { }
}
