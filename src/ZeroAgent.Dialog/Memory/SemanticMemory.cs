using System;
using System.Collections.Generic;
using ZeroVector.Core.Indices;
using ZeroVector.Core.Metrics;
using ZeroVector.Core.Results;

namespace ZeroAgent.Dialog.Memory
{
    public sealed class SemanticKnowledgeItem
    {
        public int Id { get; }
        public string Title { get; }
        public string Content { get; }
        public string Category { get; }

        public SemanticKnowledgeItem(int id, string title, string content, string category)
        {
            Id = id;
            Title = title;
            Content = content;
            Category = category;
        }
    }

    /// <summary>
    /// Semantic Memory (Domain Knowledge, SOP Manuals & Technical Specifications).
    /// Indexed via ZeroVector for sub-millisecond factual lookups without LLMs.
    /// </summary>
    public sealed class SemanticMemory
    {
        private readonly IVectorIndex _vectorIndex;
        private readonly Dictionary<int, SemanticKnowledgeItem> _items = new Dictionary<int, SemanticKnowledgeItem>();
        private int _nextId = 1;
        private readonly object _lock = new object();

        public int Count => _items.Count;

        public SemanticMemory(int dimension = 128)
        {
            _vectorIndex = new FlatVectorIndex(dimension, defaultMetric: VectorMetricType.Cosine);
        }

        public SemanticKnowledgeItem Add(string title, string content, ReadOnlySpan<float> embedding, string category = "SOP")
        {
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentNullException(nameof(title));

            lock (_lock)
            {
                int id = _nextId++;
                _vectorIndex.Add(id, embedding);
                var item = new SemanticKnowledgeItem(id, title, content, category);
                _items[id] = item;
                return item;
            }
        }

        public List<(SemanticKnowledgeItem Item, float Similarity)> Query(ReadOnlySpan<float> queryEmbedding, int topK = 3, float minScore = 0.45f)
        {
            VectorSearchResult[] matches;
            lock (_lock)
            {
                matches = _vectorIndex.SearchTopK(queryEmbedding, topK);
            }

            var results = new List<(SemanticKnowledgeItem, float)>(matches.Length);
            lock (_lock)
            {
                for (int i = 0; i < matches.Length; i++)
                {
                    var m = matches[i];
                    if (m.Score >= minScore && _items.TryGetValue(m.Id, out var item))
                    {
                        results.Add((item, m.Score));
                    }
                }
            }

            return results;
        }
    }
}
