namespace HutterLab.Core.Coding.Mixing;

/// <summary>
/// Word-boundary-aware context model for text compression.
///
/// Maintains a rolling hash of the current partial word (reset at non-word chars).
/// Two context types:
///   1. Word context: hash of partial word so far (e.g., "th" → predicts 'e')
///   2. Transition context: hash of (previous_word, current_partial_word)
///      Captures word-to-word patterns (e.g., after "the" → next word starts with...)
///
/// Effective for words longer than PPM order (captures full word context)
/// and for word-level patterns in natural language text.
/// </summary>
public sealed class WordModel : IBytePredictor
{
    private uint _wordHash;
    private uint _prevWordHash;

    private readonly Entry[] _wordTable;
    private readonly Entry[] _transTable;
    private readonly int _mask;

    private struct Entry
    {
        public byte Predicted;
        public byte Count;
    }

    public WordModel(int tableBits = 20)
    {
        int tableSize = 1 << tableBits;
        _mask = tableSize - 1;
        _wordTable = new Entry[tableSize];
        _transTable = new Entry[tableSize];
    }

    private static bool IsWordChar(byte b) =>
        (b >= (byte)'a' && b <= (byte)'z') ||
        (b >= (byte)'A' && b <= (byte)'Z') ||
        (b >= (byte)'0' && b <= (byte)'9') ||
        b == (byte)'\'';

    public void Predict(float[] probs)
    {
        ref var wEntry = ref _wordTable[_wordHash & _mask];
        ref var tEntry = ref _transTable[TransHash() & _mask];

        // Soft predictions: small boost above uniform when confident.
        // This prevents destructive interference in geometric mixer.
        float wBoost = wEntry.Count >= 3 ? Math.Min(0.4f, wEntry.Count * 0.015f) : 0;
        float tBoost = tEntry.Count >= 3 ? Math.Min(0.35f, tEntry.Count * 0.012f) : 0;

        for (int s = 0; s < 256; s++)
        {
            float pW = wBoost > 0
                ? (s == wEntry.Predicted ? (1.0f + wBoost * 255) / 256 : (1.0f - wBoost) / 256)
                : 1.0f / 256;

            float pT = tBoost > 0
                ? (s == tEntry.Predicted ? (1.0f + tBoost * 255) / 256 : (1.0f - tBoost) / 256)
                : 1.0f / 256;

            probs[s] = 0.6f * pW + 0.4f * pT;
        }
    }

    public void Update(byte symbol)
    {
        UpdateEntry(ref _wordTable[_wordHash & _mask], symbol);
        UpdateEntry(ref _transTable[TransHash() & _mask], symbol);

        if (IsWordChar(symbol))
        {
            _wordHash = _wordHash * 997 + symbol;
        }
        else
        {
            _prevWordHash = _wordHash;
            _wordHash = 0;
        }
    }

    private static void UpdateEntry(ref Entry entry, byte symbol)
    {
        if (entry.Predicted == symbol && entry.Count > 0)
        {
            entry.Count = (byte)Math.Min(255, entry.Count + 1);
        }
        else if (entry.Count <= 1)
        {
            entry.Predicted = symbol;
            entry.Count = 1;
        }
        else
        {
            entry.Count >>= 1; // decay
        }
    }

    private uint TransHash()
    {
        return _prevWordHash * 2654435761u + _wordHash;
    }
}
