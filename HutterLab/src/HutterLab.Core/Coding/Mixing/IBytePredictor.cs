namespace HutterLab.Core.Coding.Mixing;

/// <summary>
/// A probabilistic model that predicts the next byte.
/// Used as a component in context mixing.
/// </summary>
public interface IBytePredictor
{
    /// <summary>
    /// Fill probs[0..255] with the probability of each byte value.
    /// Sum must be approximately 1.0.
    /// </summary>
    void Predict(float[] probs);

    /// <summary>
    /// Update the model after observing the actual byte.
    /// Must be called after Predict, in the same order as the data stream.
    /// </summary>
    void Update(byte symbol);
}
