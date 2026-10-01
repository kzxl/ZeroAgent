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
    }
}
