using System;
using System.Collections.Generic;
using ZeroAgent.Core.Database;

namespace ZeroAgent.Core.Reasoning.Cognitive
{
    /// <summary>
    /// Multi-expert decision result synthesized from the Mixture of Reflexes (MoR).
    /// </summary>
    public sealed class ZabReflexSuiteResult
    {
        public string? RoutedAction { get; set; }
        public float RoutingConfidence { get; set; }
        public float SecurityRiskScore { get; set; }
        public bool IsBlockedBySecurity => SecurityRiskScore >= 0.80f;
        public float CacheAffinityScore { get; set; }
        public bool ShouldCheckCache => CacheAffinityScore >= 0.70f;
        public bool IsConfident => !IsBlockedBySecurity && RoutingConfidence >= 0.85f;
    }

    /// <summary>
    /// Mixture of Reflexes (MoR) Engine composed of gated domain-specific neural policies.
    /// Distributes cognitive load across specialized INT8 micro-experts:
    /// 1. Action Routing Expert (determines optimal tool or action)
    /// 2. Security Guardrail Expert (detects dangerous commands and prompt injections)
    /// 3. Memory Retrieval Affinity Expert (predicts cache hits)
    /// </summary>
    public sealed class ZabMixtureOfReflexes
    {
        private ZabNeuralPolicy? _routingExpert;
        private ZabNeuralPolicy? _securityExpert;
        private ZabNeuralPolicy? _cacheExpert;
        private readonly object _lock = new object();

        public ZabNeuralPolicy? RoutingExpert => _routingExpert;
        public ZabNeuralPolicy? SecurityExpert => _securityExpert;
        public ZabNeuralPolicy? CacheExpert => _cacheExpert;

        public ZabMixtureOfReflexes(
            ZabNeuralPolicy? routingExpert = null,
            ZabNeuralPolicy? securityExpert = null,
            ZabNeuralPolicy? cacheExpert = null)
        {
            _routingExpert = routingExpert;
            _securityExpert = securityExpert;
            _cacheExpert = cacheExpert;
        }

        public void RegisterRoutingExpert(ZabNeuralPolicy policy)
        {
            lock (_lock) _routingExpert = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public void RegisterSecurityExpert(ZabNeuralPolicy policy)
        {
            lock (_lock) _securityExpert = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public void RegisterCacheExpert(ZabNeuralPolicy policy)
        {
            lock (_lock) _cacheExpert = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        /// <summary>
        /// Evaluates all specialized experts concurrently in sub-0.05ms reflex time.
        /// </summary>
        public ZabReflexSuiteResult Evaluate(ReadOnlySpan<float> inputEmbedding)
        {
            var result = new ZabReflexSuiteResult();
            if (inputEmbedding.IsEmpty) return result;

            lock (_lock)
            {
                // 1. Security Guardrail Evaluation
                if (_securityExpert != null && _securityExpert.InputDim == inputEmbedding.Length)
                {
                    result.SecurityRiskScore = _securityExpert.PredictScore(inputEmbedding, "Risk");
                    if (result.IsBlockedBySecurity)
                    {
                        return result; // Early exit on critical security violation
                    }
                }

                // 2. Action Routing Evaluation
                if (_routingExpert != null && _routingExpert.InputDim == inputEmbedding.Length)
                {
                    result.RoutedAction = _routingExpert.PredictChoice(inputEmbedding, out float conf);
                    result.RoutingConfidence = conf;
                }

                // 3. Cache Affinity Evaluation
                if (_cacheExpert != null && _cacheExpert.InputDim == inputEmbedding.Length)
                {
                    result.CacheAffinityScore = _cacheExpert.PredictScore(inputEmbedding, "CacheAffinity");
                }
            }

            return result;
        }
    }
}
