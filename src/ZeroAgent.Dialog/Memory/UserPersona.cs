using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZeroAgent.Dialog.Memory
{
    public enum CommunicationTone
    {
        Formal,
        Respectful,
        Friendly,
        Casual,
        Direct
    }

    /// <summary>
    /// Long-term User Persona & Behavioral Personalization Memory.
    /// Tracks communication pronouns, formality, frequent topics, and domain affinities
    /// across conversations (similar to ChatGPT Custom Instructions & Context History).
    /// </summary>
    public sealed class UserPersona
    {
        private static readonly Regex TaoMayRegex = new Regex(@"\b(tao|mày|may|thằng này|con bot này)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnhEmRegex = new Regex(@"\b(anh|anh ơi|em ơi|chú|bác|chị|chị ơi)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ToiBanRegex = new Regex(@"\b(tôi|bạn|toi|ban|mình)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string UserPronoun { get; set; } = "bạn";
        public string BotPronoun { get; set; } = "tôi";
        public CommunicationTone Tone { get; set; } = CommunicationTone.Formal;

        public ConcurrentDictionary<string, int> TopicFrequencies { get; } = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, int> EntityFrequencies { get; } = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public DateTime LastUpdatedUtc { get; private set; } = DateTime.UtcNow;

        public string? DominantDomain
        {
            get
            {
                if (TopicFrequencies.IsEmpty) return null;
                return TopicFrequencies.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key;
            }
        }

        public void RecordUtterance(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage)) return;

            LastUpdatedUtc = DateTime.UtcNow;

            // 1. Detect casual/brusque: tao - mày
            if (TaoMayRegex.IsMatch(userMessage))
            {
                UserPronoun = "tao";
                BotPronoun = "mày";
                Tone = CommunicationTone.Casual;
                return;
            }

            // 2. Detect respectful/elder: anh/chị - em
            var anhEmMatch = AnhEmRegex.Match(userMessage);
            if (anhEmMatch.Success)
            {
                string matchVal = anhEmMatch.Value.ToLowerInvariant();
                if (matchVal.Contains("chị"))
                {
                    UserPronoun = "chị";
                    BotPronoun = "em";
                    Tone = CommunicationTone.Respectful;
                }
                else
                {
                    UserPronoun = "anh";
                    BotPronoun = "em";
                    Tone = CommunicationTone.Respectful;
                }
                return;
            }

            // 3. Detect standard formal: tôi - bạn / mình
            if (ToiBanRegex.IsMatch(userMessage) && Tone == CommunicationTone.Formal)
            {
                UserPronoun = "tôi";
                BotPronoun = "bạn";
                Tone = CommunicationTone.Formal;
            }
        }

        public void RecordInteraction(string? intentName, IReadOnlyDictionary<string, string>? slots = null)
        {
            if (!string.IsNullOrEmpty(intentName))
            {
                TopicFrequencies.AddOrUpdate(intentName!, 1, (_, count) => count + 1);
            }

            if (slots != null)
            {
                foreach (var kvp in slots)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        string entityKey = $"{kvp.Key}:{kvp.Value}";
                        EntityFrequencies.AddOrUpdate(entityKey, 1, (_, count) => count + 1);
                    }
                }
            }
        }

        public string? GetPreferredEntity(string slotKey)
        {
            if (EntityFrequencies.IsEmpty || string.IsNullOrEmpty(slotKey)) return null;

            string prefix = $"{slotKey}:";
            var candidate = EntityFrequencies
                .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(kvp => kvp.Value)
                .FirstOrDefault();

            if (candidate.Key == null) return null;
            return candidate.Key.Substring(prefix.Length);
        }

        public string FormatPersonalizedResponse(string rawResponse, string? userName = null)
        {
            if (string.IsNullOrWhiteSpace(rawResponse)) return string.Empty;

            string displayName = !string.IsNullOrWhiteSpace(userName) && userName != "DefaultOperator" && userName != "Operator"
                ? " " + userName
                : string.Empty;

            switch (Tone)
            {
                case CommunicationTone.Respectful:
                    if (rawResponse.StartsWith("Chào bạn", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"Dạ chào {UserPronoun}{displayName}, {BotPronoun} " + rawResponse.Substring(8).TrimStart(',', ' ');
                    }
                    if (!rawResponse.StartsWith("Dạ", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"Dạ {UserPronoun}{displayName}, " + rawResponse;
                    }
                    break;

                case CommunicationTone.Casual:
                    string casual = rawResponse.Replace("Chào bạn, ", string.Empty)
                                               .Replace("Chào bạn", string.Empty)
                                               .Replace("của bạn", "của mày");
                    return casual.TrimEnd('.', '!') + " nhé!";

                case CommunicationTone.Formal:
                default:
                    break;
            }

            return rawResponse;
        }

        public string GetPersonaPromptSummary(string? userName = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[USER PERSONA & HISTORICAL PREFERENCES]");
            if (!string.IsNullOrWhiteSpace(userName))
            {
                sb.AppendLine($"- User Name: {userName}");
            }
            sb.AppendLine($"- Communication Style: User addresses self as '{UserPronoun}', refers to agent as '{BotPronoun}' (Tone: {Tone})");
            if (!string.IsNullOrEmpty(DominantDomain))
            {
                sb.AppendLine($"- Dominant Inquired Topic: {DominantDomain} (Queried {TopicFrequencies[DominantDomain!]} times)");
            }

            var topEntities = EntityFrequencies.OrderByDescending(kvp => kvp.Value).Take(3).ToList();
            if (topEntities.Count > 0)
            {
                sb.AppendLine($"- Frequently Mentioned Entities: {string.Join(", ", topEntities.Select(e => $"{e.Key} ({e.Value}x)"))}");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
