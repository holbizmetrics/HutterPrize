namespace HutterLab.Core.Coding.Mixing;

/// <summary>
/// Geometric mixer for byte-level probability models.
///
/// Combines predictions using weighted geometric mean in log space:
///   P_mix(s) ∝ Π P_i(s)^w_i
///
/// Key advantage: uniform predictions (from inactive models like match model)
/// contribute a constant in log space, which cancels in normalization.
/// This means inactive models are invisible — only models with actual
/// predictions influence the mix.
///
/// Weight update: exponential smoothing of normalized per-byte scores.
/// Prevents weight death that plagued multiplicative updates.
/// </summary>
public sealed class ByteMixer
{
    private readonly IBytePredictor[] _models;
    private readonly float[] _weights;
    private readonly float[][] _predictions;
    private readonly float[] _logMixed; // log-domain mixed values
    private readonly float[] _mixed;    // final probabilities
    private readonly int[] _freqs;
    private readonly int[] _cumFreqs;

    private const float LEARN_RATE = 0.005f;
    private const float LOG_FLOOR = -20f; // floor for log(prob) to avoid -inf

    /// <summary>
    /// Total for quantized frequency table. Must be &lt; 2^16 for range coder.
    /// </summary>
    public const int FREQ_TOTAL = 65280;

    public ByteMixer(IBytePredictor[] models, float[]? initialWeights = null)
    {
        _models = models;
        _weights = new float[models.Length];
        if (initialWeights != null && initialWeights.Length == models.Length)
            Array.Copy(initialWeights, _weights, models.Length);
        else
            Array.Fill(_weights, 1.0f / models.Length);

        _predictions = new float[models.Length][];
        for (int i = 0; i < models.Length; i++)
            _predictions[i] = new float[256];

        _logMixed = new float[256];
        _mixed = new float[256];
        _freqs = new int[256];
        _cumFreqs = new int[257];
    }

    /// <summary>
    /// Compute the mixed probability distribution and quantize.
    /// </summary>
    public void Predict()
    {
        // Get predictions from all models
        for (int m = 0; m < _models.Length; m++)
            _models[m].Predict(_predictions[m]);

        // Geometric mixing in log domain:
        // log P_mix(s) = Σ w_i * log P_i(s) + const
        float maxLogP = float.NegativeInfinity;

        for (int s = 0; s < 256; s++)
        {
            float logP = 0;
            for (int m = 0; m < _models.Length; m++)
            {
                float p = _predictions[m][s];
                float lp = p > 1e-10f ? MathF.Log(p) : LOG_FLOOR;
                logP += _weights[m] * lp;
            }
            _logMixed[s] = logP;
            if (logP > maxLogP) maxLogP = logP;
        }

        // Softmax normalization (subtract max for numerical stability)
        float sumExp = 0;
        for (int s = 0; s < 256; s++)
        {
            _mixed[s] = MathF.Exp(_logMixed[s] - maxLogP);
            sumExp += _mixed[s];
        }
        float invSum = 1.0f / sumExp;
        for (int s = 0; s < 256; s++)
            _mixed[s] *= invSum;

        Quantize();
    }

    public void GetEncodeInfo(byte symbol, out uint cumFreq, out uint freq, out uint total)
    {
        total = (uint)FREQ_TOTAL;
        cumFreq = (uint)_cumFreqs[symbol];
        freq = (uint)_freqs[symbol];
    }

    public byte DecodeByte(uint cumValue, out uint cumFreq, out uint freq)
    {
        int lo = 0, hi = 255;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_cumFreqs[mid] <= (int)cumValue)
                lo = mid;
            else
                hi = mid - 1;
        }

        cumFreq = (uint)_cumFreqs[lo];
        freq = (uint)_freqs[lo];
        return (byte)lo;
    }

    /// <summary>
    /// Update weights and models after observing actual symbol.
    /// Uses exponential smoothing: slow enough to be stable, fast enough to adapt.
    /// </summary>
    public void Update(byte symbol)
    {
        // Score each model: how well did it predict the actual symbol?
        float maxScore = 0;
        for (int m = 0; m < _models.Length; m++)
        {
            float score = _predictions[m][symbol];
            if (score > maxScore) maxScore = score;
        }

        // Exponential smoothing of normalized scores
        if (maxScore > 1e-10f)
        {
            float sum = 0;
            for (int m = 0; m < _models.Length; m++)
            {
                float normalizedScore = _predictions[m][symbol] / maxScore;
                _weights[m] = (1 - LEARN_RATE) * _weights[m] + LEARN_RATE * normalizedScore;
                sum += _weights[m];
            }

            // Normalize
            float invSum = 1.0f / sum;
            for (int m = 0; m < _models.Length; m++)
                _weights[m] *= invSum;
        }

        // Update all component models
        for (int m = 0; m < _models.Length; m++)
            _models[m].Update(symbol);
    }

    private void Quantize()
    {
        int sum = 0;
        int maxIdx = 0;

        for (int s = 0; s < 256; s++)
        {
            _freqs[s] = Math.Max(1, (int)(_mixed[s] * FREQ_TOTAL + 0.5f));
            sum += _freqs[s];
            if (_mixed[s] > _mixed[maxIdx]) maxIdx = s;
        }

        _freqs[maxIdx] += FREQ_TOTAL - sum;
        if (_freqs[maxIdx] < 1) _freqs[maxIdx] = 1;

        _cumFreqs[0] = 0;
        for (int s = 0; s < 256; s++)
            _cumFreqs[s + 1] = _cumFreqs[s] + _freqs[s];
    }
}
