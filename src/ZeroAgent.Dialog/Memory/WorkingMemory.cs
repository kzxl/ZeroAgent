using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ZeroAgent.Dialog.Memory
{
    public sealed class DialogTurn
    {
        public string UserMessage { get; }
        public string BotResponse { get; }
        public string Intent { get; }
        public DateTime TimestampUtc { get; }

        public DialogTurn(string userMessage, string botResponse, string intent)
        {
            UserMessage = userMessage;
            BotResponse = botResponse;
            Intent = intent;
            TimestampUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Working Memory (Short-Term Conversational Memory).
    /// Tracks active dialogue session state, slot accumulation, and resolves anaphora/pronoun references across turns.
    /// </summary>
    public sealed class WorkingMemory
    {
        private readonly List<DialogTurn> _turns = new List<DialogTurn>();
        private readonly Dictionary<string, string> _activeSlots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string SessionId { get; }
        public IReadOnlyList<DialogTurn> Turns => _turns;
        public IReadOnlyDictionary<string, string> ActiveSlots => _activeSlots;

        public string? CurrentSubject { get; set; }
        public string? CurrentMetric { get; set; }
        public string? CurrentArea { get; set; }

        public WorkingMemory(string sessionId)
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString("N");
        }

        public void AddTurn(string userMessage, string botResponse, string intent)
        {
            _turns.Add(new DialogTurn(userMessage, botResponse, intent));
            if (_turns.Count > 50)
            {
                _turns.RemoveAt(0);
            }
        }

        public void SetSlot(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _activeSlots[key] = value;

            if (key.Equals("machine_id", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("target", StringComparison.OrdinalIgnoreCase))
            {
                CurrentSubject = value;
            }
            else if (key.Equals("metric", StringComparison.OrdinalIgnoreCase))
            {
                CurrentMetric = value;
            }
            else if (key.Equals("area", StringComparison.OrdinalIgnoreCase))
            {
                CurrentArea = value;
            }
        }

        public bool TryGetSlot(string key, out string value)
        {
            return _activeSlots.TryGetValue(key, out value!);
        }

        public void ClearSlots()
        {
            _activeSlots.Clear();
            CurrentSubject = null;
            CurrentMetric = null;
            CurrentArea = null;
        }

        /// <summary>
        /// Resolves anaphora, pronouns, and implicit subject references based on active working memory.
        /// Replaces 'nó', 'máy đó', 'con đó', 'it', 'that machine' with the active subject name.
        /// </summary>
        public string ResolveAnaphora(string rawInput)
        {
            if (string.IsNullOrWhiteSpace(rawInput)) return string.Empty;
            if (string.IsNullOrEmpty(CurrentSubject)) return rawInput;

            string text = rawInput;

            // Vietnamese pronouns and anaphoric references
            text = Regex.Replace(text, @"\b(nó|máy đó|con đó|máy này|thiết bị đó|thiết bị này)\b", CurrentSubject, RegexOptions.IgnoreCase);

            // English pronouns
            text = Regex.Replace(text, @"\b(it|that machine|this machine|that device)\b", CurrentSubject, RegexOptions.IgnoreCase);

            // Elliptical follow-up questions: "còn áp suất thì sao" -> "kiểm tra áp suất của CNC-01"
            var ellipticalMatch = Regex.Match(text, @"^còn\s+([a-zA-Z0-9_\u00C0-\u024F\u1E00-\u1EFF\s]+)\s+(thì sao|thế nào)\??$", RegexOptions.IgnoreCase);
            if (ellipticalMatch.Success)
            {
                string metric = ellipticalMatch.Groups[1].Value.Trim();
                text = $"kiểm tra {metric} của {CurrentSubject}";
            }

            return text;
        }
    }
}
