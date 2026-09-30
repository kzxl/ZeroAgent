using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroAgent.Core.Tools
{
    /// <summary>
    /// Ultra-fast semantic and keyword router for dynamically selecting the most relevant tools
    /// from large toolsets (50+ tools) without blowing up prompt context windows or latency.
    /// </summary>
    public sealed class ToolSemanticRouter
    {
        private sealed class ToolDescriptor
        {
            public IAgentTool Tool { get; }
            public HashSet<string> Keywords { get; }
            public string NormalizedDescription { get; }

            public ToolDescriptor(IAgentTool tool, IEnumerable<string>? triggers)
            {
                Tool = tool;
                Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Add tool name tokens
                foreach (var part in tool.Name.Split('_', '-', ' ', '.'))
                {
                    if (part.Length >= 2) Keywords.Add(part.ToLowerInvariant());
                }

                // Add explicit triggers
                if (triggers != null)
                {
                    foreach (var trigger in triggers)
                    {
                        foreach (var token in trigger.Split(' ', '_', '-'))
                        {
                            if (token.Length >= 2) Keywords.Add(token.ToLowerInvariant());
                        }
                    }
                }

                NormalizedDescription = tool.Description.ToLowerInvariant();
            }
        }

        private readonly List<ToolDescriptor> _descriptors = new List<ToolDescriptor>();

        public int Count => _descriptors.Count;

        public void RegisterTool(IAgentTool tool, IEnumerable<string>? triggers = null)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            _descriptors.Add(new ToolDescriptor(tool, triggers));
        }

        public void RegisterRegistry(AgentToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            foreach (var tool in registry.Tools)
            {
                RegisterTool(tool);
            }
        }

        /// <summary>
        /// Scores and retrieves the top-K most relevant tools for a given user utterance or query.
        /// Evaluates in less than 20 microseconds.
        /// </summary>
        public List<(IAgentTool Tool, float Score)> Route(string userQuery, int topK = 3, float minScore = 0.2f)
        {
            if (string.IsNullOrWhiteSpace(userQuery) || _descriptors.Count == 0)
                return new List<(IAgentTool, float)>();

            string lower = userQuery.ToLowerInvariant();
            var queryTokens = lower.Split(new[] { ' ', ',', '.', '?', '!', ':', ';', '(', ')', '"', '\'', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

            var scored = new List<(IAgentTool Tool, float Score)>(_descriptors.Count);

            foreach (var desc in _descriptors)
            {
                int matchCount = 0;
                float bonus = 0.0f;

                foreach (var token in queryTokens)
                {
                    if (desc.Keywords.Contains(token))
                    {
                        matchCount += 2;
                    }
                    else if (desc.NormalizedDescription.Contains(token))
                    {
                        matchCount += 1;
                    }
                }

                // Check direct substring of tool name
                if (lower.Contains(desc.Tool.Name.ToLowerInvariant()))
                {
                    bonus += 1.5f;
                }

                float totalScore = (float)matchCount / Math.Max(queryTokens.Length, 1) + bonus;
                if (totalScore >= minScore)
                {
                    scored.Add((desc.Tool, totalScore));
                }
            }

            scored.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (scored.Count > topK)
            {
                scored.RemoveRange(topK, scored.Count - topK);
            }

            return scored;
        }
    }
}
