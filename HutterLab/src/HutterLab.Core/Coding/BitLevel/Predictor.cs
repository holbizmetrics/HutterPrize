namespace HutterLab.Core.Coding.BitLevel;

/// <summary>
/// Adaptive prediction table: maps context hash → P(bit=1).
/// Each entry stores a 16-bit prediction and a count for learning rate adaptation.
/// Prediction updated toward actual bit with rate that decreases as count grows.
/// </summary>
public sealed class Predictor
{
    private readonly ushort[] _pred;
    private readonly byte[] _count;
    private readonly uint _mask;

    public Predictor(int tableBits)
    {
        int size = 1 << tableBits;
        _mask = (uint)(size - 1);
        _pred = new ushort[size];
        _count = new byte[size];
        Array.Fill(_pred, (ushort)32768);
    }

    /// <summary>
    /// Get prediction P(bit=1) in [1, 65534] for context.
    /// </summary>
    public int Predict(uint context)
    {
        return _pred[context & _mask];
    }

    /// <summary>
    /// Update prediction after observing actual bit.
    /// </summary>
    public void Update(uint context, int bit)
    {
        uint idx = context & _mask;
        int p = _pred[idx];
        int n = _count[idx];

        // Learning rate: fast at first, slows with observations
        int rate = n < 2 ? 128 : n < 8 ? 64 : n < 32 ? 32 : n < 128 ? 16 : 8;

        int target = bit != 0 ? 65534 : 1;
        p += ((target - p) * rate) >> 8;
        p = Math.Clamp(p, 1, 65534);

        _pred[idx] = (ushort)p;
        if (n < 255) _count[idx] = (byte)(n + 1);
    }
}
