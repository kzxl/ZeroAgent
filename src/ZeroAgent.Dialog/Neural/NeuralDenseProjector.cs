using System;
using ZeroNeural.Core.Autograd;
using ZeroNeural.Core.nn;
using ZeroTensor.Core;
using ZeroVector.Core.Metrics;

namespace ZeroAgent.Dialog.Neural
{
    /// <summary>
    /// Projects 128-dimensional sparse lexical embeddings into a dense non-linear latent space (e.g. 64-d).
    /// </summary>
    public sealed class NeuralDenseProjector
    {
        private readonly Sequential _projectionNet;
        public int InputDim { get; }
        public int OutputDim { get; }

        public NeuralDenseProjector(int inputDim = 128, int outputDim = 64, int seed = 42)
        {
            if (inputDim <= 0) throw new ArgumentOutOfRangeException(nameof(inputDim));
            if (outputDim <= 0) throw new ArgumentOutOfRangeException(nameof(outputDim));

            InputDim = inputDim;
            OutputDim = outputDim;

            _projectionNet = new Sequential(
                new Linear(inputDim, outputDim, seed: seed),
                new Tanh()
            );
            _projectionNet.Eval();
        }

        /// <summary>
        /// Projects an input 128-d embedding into an L2-normalized 64-d dense vector.
        /// </summary>
        public float[] Project(ReadOnlySpan<float> inputEmbedding)
        {
            if (inputEmbedding.Length != InputDim)
            {
                throw new ArgumentException($"Input embedding length {inputEmbedding.Length} must match InputDim {InputDim}.", nameof(inputEmbedding));
            }

            var xTensor = Tensor.FromArray(inputEmbedding.ToArray(), 1, InputDim);
            var xVar = new Variable(xTensor, requiresGrad: false);

            var outVar = _projectionNet.Forward(xVar);

            float[] result = new float[OutputDim];
            for (int i = 0; i < OutputDim; i++)
            {
                result[i] = outVar.Data[0, i];
            }

            VectorMetrics.NormalizeL2(result);
            return result;
        }
    }
}
