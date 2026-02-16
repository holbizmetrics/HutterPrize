namespace HutterLab.Core.Coding.Mixing;

/// <summary>
/// Match model: finds the longest match of the current context in previous data
/// and predicts the byte that followed the match.
///
/// Complementary to PPM: PPM models statistical patterns (frequency-based),
/// match model finds exact repetitions (content-based). Very effective on
/// Wikipedia due to templates, boilerplate, and repeated structures.
/// </summary>
public sealed class MatchModel : IBytePredictor
{
    private readonly byte[] _buf;
    private int _pos;
    private readonly int _hashOrder;
    private readonly Dictionary<uint, int> _table; // context hash → start position
    private int _matchStart;
    private int _matchLen;
    private bool _matching;

    public MatchModel(int maxSize, int hashOrder = 4)
    {
        _buf = new byte[maxSize + 1];
        _hashOrder = hashOrder;
        _table = new Dictionary<uint, int>(Math.Min(maxSize, 1 << 20));
    }

    public void Predict(float[] probs)
    {
        if (_matching && _matchStart + _matchLen < _pos)
        {
            byte predicted = _buf[_matchStart + _matchLen];
            // Confidence scales with match length: longer match = stronger prediction
            float conf = Math.Clamp(0.2f + (_matchLen - _hashOrder) * 0.12f, 0.2f, 0.97f);

            float other = (1.0f - conf) / 255;
            for (int s = 0; s < 256; s++)
                probs[s] = other;
            probs[predicted] = conf;
        }
        else
        {
            // No active match — uniform distribution
            float uniform = 1.0f / 256;
            for (int s = 0; s < 256; s++)
                probs[s] = uniform;
        }
    }

    public void Update(byte symbol)
    {
        _buf[_pos] = symbol;

        // Check if active match continues
        if (_matching)
        {
            if (_matchStart + _matchLen < _pos && _buf[_matchStart + _matchLen] == symbol)
            {
                _matchLen++;
            }
            else
            {
                _matching = false;
            }
        }

        _pos++;

        // Update hash table and try to find new match
        if (_pos >= _hashOrder)
        {
            // Hash the context ending at _pos - 1 (last _hashOrder bytes)
            uint hash = ComputeHash(_pos - _hashOrder);

            // Try to find new match if not currently matching
            if (!_matching && _table.TryGetValue(hash, out int prevStart))
            {
                // Verify the hash match is real
                bool valid = true;
                for (int i = 0; i < _hashOrder; i++)
                {
                    if (_buf[prevStart + i] != _buf[_pos - _hashOrder + i])
                    { valid = false; break; }
                }

                if (valid && prevStart + _hashOrder < _pos)
                {
                    _matchStart = prevStart;
                    _matchLen = _hashOrder;
                    _matching = true;
                }
            }

            // Store current context position (overwrites previous for this hash)
            _table[hash] = _pos - _hashOrder;
        }
    }

    private uint ComputeHash(int start)
    {
        uint h = 2166136261u; // FNV-1a 32-bit
        for (int i = 0; i < _hashOrder; i++)
        {
            h ^= _buf[start + i];
            h *= 16777619u;
        }
        return h;
    }
}
