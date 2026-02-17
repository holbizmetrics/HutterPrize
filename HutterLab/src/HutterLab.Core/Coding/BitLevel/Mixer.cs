namespace HutterLab.Core.Coding.BitLevel;

/// <summary>
/// Logistic mixer: combines multiple bit predictions in log-odds (logit) space.
///
/// P_mix = squash(Σ w_i * stretch(p_i))
///
/// Weights updated by gradient descent on cross-entropy loss.
/// This is the key innovation of PAQ-style compressors.
/// Uses precomputed lookup tables for stretch/squash.
/// </summary>
public sealed class Mixer
{
    private readonly float[] _weights;
    private readonly float[] _stretched;
    private float _logitSum;
    private readonly int _n;

    private const float LEARN_RATE = 0.003f;

    // Precomputed lookup tables
    private static readonly float[] StretchLUT;
    private static readonly int[] SquashLUT;

    static Mixer()
    {
        // Stretch: probability [0,65535] → log-odds float
        StretchLUT = new float[65536];
        for (int i = 1; i < 65535; i++)
            StretchLUT[i] = MathF.Log(i / (65535f - i));
        StretchLUT[0] = StretchLUT[1];
        StretchLUT[65535] = StretchLUT[65534];

        // Squash: quantized logit → probability [1, 65534]
        // Maps logit range [-16, 16] across 4097 entries
        SquashLUT = new int[4097];
        for (int i = 0; i <= 4096; i++)
        {
            float x = (i - 2048) / 128f;
            SquashLUT[i] = Math.Clamp((int)(65535f / (1f + MathF.Exp(-x)) + 0.5f), 1, 65534);
        }
    }

    public Mixer(int numModels)
    {
        _n = numModels;
        _weights = new float[numModels];
        _stretched = new float[numModels];
        Array.Fill(_weights, 1.0f / numModels);
    }

    /// <summary>
    /// Mix N predictions (each in [1, 65534]) into one prediction.
    /// </summary>
    public int Mix(ReadOnlySpan<int> predictions)
    {
        _logitSum = 0;
        for (int i = 0; i < _n; i++)
        {
            _stretched[i] = Stretch(predictions[i]);
            _logitSum += _weights[i] * _stretched[i];
        }
        return Squash(_logitSum);
    }

    /// <summary>
    /// Update weights by gradient descent after observing actual bit.
    /// </summary>
    public void Update(int bit)
    {
        float p = Squash(_logitSum) / 65535f;
        float err = (bit - p) * LEARN_RATE;
        for (int i = 0; i < _n; i++)
            _weights[i] += err * _stretched[i];
    }

    public static float Stretch(int p)
    {
        return StretchLUT[Math.Clamp(p, 0, 65535)];
    }

    public static int Squash(float logit)
    {
        int idx = (int)(logit * 128f + 2048f + 0.5f);
        return SquashLUT[Math.Clamp(idx, 0, 4096)];
    }
}
