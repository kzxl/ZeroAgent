using System;
using System.Collections.Generic;
using ZeroAgent.Core.Database;

namespace ZeroAgent.Core.Reasoning.Cognitive
{
    /// <summary>
    /// Privacy-Preserving Federated Weight Aggregator (FedAvg) for Distributed Agent Swarms.
    /// Aggregates decentralized System 1 INT8 reflex weights from multiple edge agents
    /// into a unified global policy network without transmitting raw user dialogue or proprietary data.
    /// </summary>
    public static class ZabFederatedAveraging
    {
        /// <summary>
        /// Combines multiple agent neural policies into a single synchronized swarm policy.
        /// </summary>
        public static ZabNeuralPolicy Aggregate(
            IReadOnlyList<ZabNeuralPolicy> policies,
            string aggregatedModelName = "SwarmGlobalPolicy")
        {
            if (policies == null || policies.Count == 0)
                throw new ArgumentException("Policies collection cannot be null or empty.", nameof(policies));

            if (policies.Count == 1)
            {
                return policies[0];
            }

            var baseline = policies[0];
            int inputDim = baseline.InputDim;
            int classCount = baseline.OutputClasses.Count;
            int totalWeights = inputDim * classCount;

            // Validate homogeneous architecture across all agents
            for (int p = 1; p < policies.Count; p++)
            {
                var pol = policies[p];
                if (pol.InputDim != inputDim || pol.OutputClasses.Count != classCount)
                {
                    throw new InvalidOperationException($"Policy at index {p} has mismatched architecture. Expected Dim: {inputDim}, Classes: {classCount}.");
                }
            }

            sbyte[] aggregatedWeights = new sbyte[totalWeights];
            float[] aggregatedBiases = new float[classCount];
            float m = policies.Count;

            // 1. Average INT8 Weights with safe numerical bounds
            for (int w = 0; w < totalWeights; w++)
            {
                double sum = 0;
                for (int p = 0; p < policies.Count; p++)
                {
                    sum += policies[p].WeightsInt8[w];
                }
                int avg = (int)Math.Round(sum / m);
                aggregatedWeights[w] = (sbyte)Math.Max(-127, Math.Min(127, avg));
            }

            // 2. Average Floating-point Biases
            for (int c = 0; c < classCount; c++)
            {
                double bSum = 0;
                for (int p = 0; p < policies.Count; p++)
                {
                    if (c < policies[p].Biases.Length)
                    {
                        bSum += policies[p].Biases[c];
                    }
                }
                aggregatedBiases[c] = (float)(bSum / m);
            }

            // Calculate averaged scale
            float scaleSum = 0;
            for (int p = 0; p < policies.Count; p++) scaleSum += policies[p].Scale;
            float avgScale = scaleSum / m;

            var globalPolicy = new ZabNeuralPolicy
            {
                ModelName = aggregatedModelName,
                InputDim = inputDim,
                OutputClasses = new List<string>(baseline.OutputClasses),
                Scale = avgScale,
                WeightsInt8 = aggregatedWeights,
                Biases = aggregatedBiases
            };

            return globalPolicy;
        }
    }
}
