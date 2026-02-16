using HutterLab.Core.Coding.PPM;

namespace HutterLab.Core.Coding.Mixing;

/// <summary>
/// Wraps a PPMModel as an IBytePredictor for use in context mixing.
/// </summary>
public sealed class PPMPredictor : IBytePredictor
{
    private readonly PPMModel _model;

    public PPMPredictor(int order)
    {
        _model = new PPMModel(order);
    }

    public void Predict(float[] probs)
    {
        _model.PredictDistribution(probs);
    }

    public void Update(byte symbol)
    {
        _model.UpdateModel(symbol);
    }
}
