using System;
using System.Collections.Generic;
using ZeroVector.Core.Indices;
using ZeroVector.Core.Metrics;
using ZeroVector.Core.Results;

namespace ZeroAgent.Dialog.Memory
{
    public sealed class IncidentEpisode
    {
        public int Id { get; }
        public string Issue { get; }
        public string Resolution { get; }
        public bool Success { get; }
        public DateTime TimestampUtc { get; }

        public IncidentEpisode(int id, string issue, string resolution, bool success)
        {
            Id = id;
            Issue = issue;
            Resolution = resolution;
            Success = success;
            TimestampUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Episodic Memory (Experience & Historical Incidents Memory).
    /// Stores past diagnostics, equipment faults, and human-verified resolutions backed by ZeroVector.
    /// </summary>
    public sealed class EpisodicMemory
    {
        private readonly IVectorIndex _vectorIndex;
        private readonly Dictionary<int, IncidentEpisode> _episodes = new Dictionary<int, IncidentEpisode>();
        private int _nextId = 1;
        private readonly object _lock = new object();

        public int Count => _episodes.Count;

        public EpisodicMemory(int dimension = 128)
        {
            _vectorIndex = new FlatVectorIndex(dimension, defaultMetric: VectorMetricType.Cosine);
        }

        public IncidentEpisode Record(string issue, string resolution, ReadOnlySpan<float> embedding, bool success = true)
        {
            if (string.IsNullOrWhiteSpace(issue)) throw new ArgumentNullException(nameof(issue));

            lock (_lock)
            {
                int id = _nextId++;
                _vectorIndex.Add(id, embedding);
                var ep = new IncidentEpisode(id, issue, resolution, success);
                _episodes[id] = ep;
                return ep;
            }
        }

        public List<(IncidentEpisode Episode, float Similarity)> Recall(ReadOnlySpan<float> queryEmbedding, int topK = 3, float minScore = 0.4f)
        {
            VectorSearchResult[] matches;
            lock (_lock)
            {
                matches = _vectorIndex.SearchTopK(queryEmbedding, topK);
            }

            var results = new List<(IncidentEpisode, float)>(matches.Length);
            lock (_lock)
            {
                for (int i = 0; i < matches.Length; i++)
                {
                    var m = matches[i];
                    if (m.Score >= minScore && _episodes.TryGetValue(m.Id, out var ep))
                    {
                        results.Add((ep, m.Score));
                    }
                }
            }

            return results;
        }
    }
}
