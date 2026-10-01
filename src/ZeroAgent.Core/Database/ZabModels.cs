using System;
using System.Collections.Generic;
using ZeroVector.Core.Quantization;

namespace ZeroAgent.Core.Database
{
    /// <summary>
    /// Metadata manifest describing the agent identity, role, and capabilities.
    /// </summary>
    public sealed class ZabManifest
    {
        public string AgentId { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "ZeroAgent";
        public string Description { get; set; } = string.Empty;
        public string Role { get; set; } = "Assistant";
        public string SystemPrompt { get; set; } = string.Empty;
        public List<string> RegisteredTools { get; set; } = new List<string>();
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Verified domain knowledge, synonym, or business invariant record.
    /// </summary>
    public sealed class ZabKnowledgeRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public float Confidence { get; set; } = 1.0f;
        public string Status { get; set; } = "Verified";
        public string Author { get; set; } = "Admin";
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
        public string? ConflictDetails { get; set; }
    }

    /// <summary>
    /// Quantized high-dimensional vector record (SQ8 compressed, 4x memory savings).
    /// Supports direct SIMD inner-product evaluation without uncompressing to FP32.
    /// </summary>
    public sealed class ZabVectorRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Label { get; set; } = string.Empty;
        public int Dimension { get; set; }
        public float Scale { get; set; }
        public float Offset { get; set; }
        public sbyte[] QuantizedData { get; set; } = Array.Empty<sbyte>();

        /// <summary>
        /// Dequantizes the SQ8 bytes back into exact FP32 continuous floats for display or inspection.
        /// </summary>
        public float[] Dequantize()
        {
            if (QuantizedData == null || QuantizedData.Length == 0) return Array.Empty<float>();
            float[] result = new float[QuantizedData.Length];
            BinaryQuantizer.DequantizeSQ8(QuantizedData, result, Scale, Offset);
            return result;
        }

        /// <summary>
        /// Evaluates Cosine Similarity directly against an uncompressed query vector using SIMD Affine Hoisting.
        /// Zero allocations, zero decompression latency.
        /// </summary>
        public unsafe float ComputeSimilarity(ReadOnlySpan<float> queryVector, float querySum)
        {
            if (queryVector.Length != Dimension || QuantizedData.Length != Dimension) return 0.0f;

            fixed (float* qPtr = queryVector)
            fixed (sbyte* tPtr = QuantizedData)
            {
                return BinaryQuantizer.CosineSimilaritySq8Hoisted(qPtr, tPtr, Dimension, Scale, Offset, querySum);
            }
        }
    }

    /// <summary>
    /// Self-reflective failure analysis and episodic lesson record.
    /// </summary>
    public sealed class ZabReflexionRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Goal { get; set; } = string.Empty;
        public string FailureReason { get; set; } = string.Empty;
        public string Lesson { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Cached verified trajectory plan record for sub-0.1ms instant solution replay.
    /// </summary>
    public sealed class ZabPlanRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Goal { get; set; } = string.Empty;
        public string Solution { get; set; } = string.Empty;
        public int StepsCount { get; set; }
        public float Confidence { get; set; } = 1.0f;
        public int HitCount { get; set; }
        public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;

        // Optional SQ8 embedding for fast semantic plan lookup
        public int VectorDim { get; set; }
        public float Scale { get; set; }
        public float Offset { get; set; }
        public sbyte[]? GoalVectorSq8 { get; set; }

        public unsafe float ComputeSimilarity(ReadOnlySpan<float> queryVector, float querySum)
        {
            if (GoalVectorSq8 == null || GoalVectorSq8.Length == 0 || queryVector.Length != VectorDim) return 0.0f;

            fixed (float* qPtr = queryVector)
            fixed (sbyte* tPtr = GoalVectorSq8)
            {
                return BinaryQuantizer.CosineSimilaritySq8Hoisted(qPtr, tPtr, VectorDim, Scale, Offset, querySum);
            }
        }
    }

    /// <summary>
    /// Storage compression and health metrics summary for the sovereign database container.
    /// </summary>
    public sealed class ZabStorageStats
    {
        public long RawSizeEstimatedBytes { get; set; }
        public long ActualFileSizeBytes { get; set; }
        public double CompressionRatio => ActualFileSizeBytes > 0 ? (double)RawSizeEstimatedBytes / ActualFileSizeBytes : 1.0;
        public int KnowledgeCount { get; set; }
        public int VectorCount { get; set; }
        public int ReflexionCount { get; set; }
        public int PlanCount { get; set; }
        public bool HasNeuralPolicy { get; set; }
    }

    /// <summary>
    /// Semi-parametric quantized neural action policy network stored within the sovereign container.
    /// Executes fast, deterministic intent routing and tool prediction in sub-0.05ms without calling external LLMs.
    /// </summary>
    public sealed class ZabNeuralPolicy
    {
        public string ModelName { get; set; } = "ActionPolicyNetwork";
        public int InputDim { get; set; }
        public List<string> OutputClasses { get; set; } = new List<string>();
        public float Scale { get; set; } = 1.0f;
        public sbyte[] WeightsInt8 { get; set; } = Array.Empty<sbyte>(); // Flattened [OutputClasses.Count * InputDim]
        public float[] Biases { get; set; } = Array.Empty<float>();

        public ZabNeuralPolicy() { }

        public ZabNeuralPolicy(string modelName, int inputDim, IReadOnlyList<string> outputClasses)
        {
            ModelName = modelName ?? "ActionPolicyNetwork";
            InputDim = inputDim;
            OutputClasses = new List<string>(outputClasses ?? Array.Empty<string>());
            Biases = new float[OutputClasses.Count];
            WeightsInt8 = new sbyte[OutputClasses.Count * InputDim];
        }

        /// <summary>
        /// Sets weights from FP32 matrix, quantizing them symmetrically to INT8.
        /// </summary>
        public void SetWeightsFp32(float[,] weights, float[]? biases = null)
        {
            int numClasses = OutputClasses.Count;
            if (weights.GetLength(0) != numClasses || weights.GetLength(1) != InputDim)
            {
                throw new ArgumentException($"Weights matrix shape must be [{numClasses}, {InputDim}]");
            }

            if (biases != null)
            {
                Biases = (float[])biases.Clone();
            }

            // Find max absolute value for symmetric scaling
            float maxAbs = 1e-7f;
            for (int c = 0; c < numClasses; c++)
            {
                for (int d = 0; d < InputDim; d++)
                {
                    float abs = Math.Abs(weights[c, d]);
                    if (abs > maxAbs) maxAbs = abs;
                }
            }

            Scale = maxAbs / 127.0f;
            float invScale = 1.0f / Scale;
            WeightsInt8 = new sbyte[numClasses * InputDim];

            for (int c = 0; c < numClasses; c++)
            {
                int rowOffset = c * InputDim;
                for (int d = 0; d < InputDim; d++)
                {
                    int q = (int)Math.Round(weights[c, d] * invScale);
                    WeightsInt8[rowOffset + d] = (sbyte)Math.Max(-127, Math.Min(127, q));
                }
            }
        }

        /// <summary>
        /// Predicts the highest-confidence action class directly from continuous input embedding using INT8 dot-product.
        /// </summary>
        public string Predict(ReadOnlySpan<float> inputEmbedding, out float confidence)
        {
            confidence = 0.0f;
            if (OutputClasses.Count == 0 || inputEmbedding.Length != InputDim || WeightsInt8.Length < OutputClasses.Count * InputDim)
            {
                return string.Empty;
            }

            int numClasses = OutputClasses.Count;
            float[] logits = new float[numClasses];
            float maxLogit = float.MinValue;

            for (int c = 0; c < numClasses; c++)
            {
                int rowOffset = c * InputDim;
                float dot = 0.0f;

                for (int d = 0; d < InputDim; d++)
                {
                    dot += WeightsInt8[rowOffset + d] * inputEmbedding[d];
                }

                float logit = (dot * Scale) + (c < Biases.Length ? Biases[c] : 0.0f);
                logits[c] = logit;
                if (logit > maxLogit) maxLogit = logit;
            }

            // Softmax for confidence
            float sumExp = 0.0f;
            int bestIdx = 0;
            for (int c = 0; c < numClasses; c++)
            {
                logits[c] = (float)Math.Exp(logits[c] - maxLogit);
                sumExp += logits[c];
                if (logits[c] > logits[bestIdx])
                {
                    bestIdx = c;
                }
            }

            confidence = sumExp > 1e-7f ? logits[bestIdx] / sumExp : 1.0f;
            return OutputClasses[bestIdx];
        }
    }
}
