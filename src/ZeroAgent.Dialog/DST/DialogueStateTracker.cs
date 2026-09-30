using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ZeroAgent.Dialog.Embedding;
using ZeroAgent.Dialog.Neural;
using ZeroVector.Core.Metrics;

namespace ZeroAgent.Dialog.DST
{
    /// <summary>
    /// Dialogue State Tracker (DST).
    /// Extracts slots, resolves pending requests, and tracks conversation state across multiple turns.
    /// Supports hybrid intent classification (Deterministic fast-path + Deep Neural MLP + Lexical Cosine).
    /// </summary>
    public sealed class DialogueStateTracker
    {
        private readonly List<DialogueIntent> _intents = new List<DialogueIntent>();
        private readonly LexicalSemanticEmbedder _embedder;
        private readonly List<(DialogueIntent Intent, float[] SampleEmbedding)> _sampleEmbeddings = new List<(DialogueIntent, float[])>();

        /// <summary>
        /// Gets or sets the optional deep neural intent classifier.
        /// </summary>
        public INeuralIntentClassifier? NeuralClassifier { get; set; }

        public DialogueStateTracker(LexicalSemanticEmbedder embedder)
        {
            _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
        }

        public void RegisterIntent(DialogueIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            _intents.Add(intent);

            foreach (var sample in intent.SampleUtterances)
            {
                var vec = _embedder.Embed(sample);
                _sampleEmbeddings.Add((intent, vec));
            }
        }

        /// <summary>
        /// Automatically trains and attaches the deep neural intent classifier on all registered intents.
        /// </summary>
        public void EnableNeuralClassifier(int epochs = 80, float learningRate = 0.05f)
        {
            var neural = new NeuralIntentClassifier(_embedder);
            neural.Train(_intents, epochs, learningRate);
            NeuralClassifier = neural;
        }

        /// <summary>
        /// Matches the most semantically relevant intent for the given query vector.
        /// Supports Hybrid routing: Deterministic fast-path -> Deep Neural MLP -> Lexical Cosine fallback.
        /// Resilient against unaccented Vietnamese, typos, and keyword occurrences.
        /// </summary>
        public (DialogueIntent? Intent, float Score) MatchIntent(ReadOnlySpan<float> queryEmbedding, string rawText)
        {
            string cleanRaw = rawText.Trim();
            string rawNormalized = NormalizeDiacritics(cleanRaw);

            // 1. Exact phrase / keyword match (Deterministic Fast Path)
            for (int i = 0; i < _sampleEmbeddings.Count; i++)
            {
                var item = _sampleEmbeddings[i];
                for (int s = 0; s < item.Intent.SampleUtterances.Count; s++)
                {
                    string sample = item.Intent.SampleUtterances[s];
                    if (string.Equals(cleanRaw, sample, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rawNormalized, NormalizeDiacritics(sample), StringComparison.OrdinalIgnoreCase))
                    {
                        return (item.Intent, 1.0f);
                    }
                }
            }

            // 2. Neural Deep Learning Inference (if trained and confident)
            if (NeuralClassifier != null && NeuralClassifier.IsTrained)
            {
                var (neuralIntent, confidence, _) = NeuralClassifier.Predict(queryEmbedding);
                if (neuralIntent != null && confidence >= 0.70f)
                {
                    // Gatekeeper against closed-world Softmax overconfidence:
                    // Verify the query actually has positive semantic proximity to the intent's sample utterances.
                    float maxSampleSim = GetMaxSampleSimilarity(queryEmbedding, neuralIntent);
                    if (maxSampleSim >= 0.28f)
                    {
                        return (neuralIntent, confidence);
                    }
                }
            }

            // 3. Fallback to Lexical Cosine Similarity matching
            DialogueIntent? bestIntent = null;
            float maxScore = -1.0f;

            for (int i = 0; i < _sampleEmbeddings.Count; i++)
            {
                var item = _sampleEmbeddings[i];
                float sim = VectorMetrics.CosineSimilarity(queryEmbedding, item.SampleEmbedding);

                // Exact phrase / keyword boost (supporting both accented and unaccented Vietnamese)
                for (int s = 0; s < item.Intent.SampleUtterances.Count; s++)
                {
                    string sample = item.Intent.SampleUtterances[s];
                    if (cleanRaw.IndexOf(sample, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        rawNormalized.IndexOf(NormalizeDiacritics(sample), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sim += 0.35f;
                        break;
                    }
                }

                if (sim > maxScore)
                {
                    maxScore = sim;
                    bestIntent = item.Intent;
                }
            }

            if (maxScore < 0.25f)
            {
                return (null, maxScore);
            }

            return (bestIntent, maxScore);
        }

        private static string NormalizeDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string normalizedString = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalizedString.Length);

            for (int i = 0; i < normalizedString.Length; i++)
            {
                char c = normalizedString[i];
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
        }

        private float GetMaxSampleSimilarity(ReadOnlySpan<float> queryEmbedding, DialogueIntent intent)
        {
            float max = -1.0f;
            for (int i = 0; i < _sampleEmbeddings.Count; i++)
            {
                if (ReferenceEquals(_sampleEmbeddings[i].Intent, intent))
                {
                    float sim = VectorMetrics.CosineSimilarity(queryEmbedding, _sampleEmbeddings[i].SampleEmbedding);
                    if (sim > max) max = sim;
                }
            }
            return max;
        }

        /// <summary>
        /// Advances the dialogue state: extracts entities/slots from input and updates the session.
        /// </summary>
        public void AdvanceSession(DialogueSession session, string resolvedText, DialogueIntent? detectedIntent)
        {
            // 1. If session was waiting for a specific slot, check if user provided it directly
            if (!string.IsNullOrEmpty(session.PendingRequiredSlot))
            {
                string pendingSlot = session.PendingRequiredSlot!;
                string extracted = ExtractSlotValue(resolvedText, pendingSlot);
                if (!string.IsNullOrEmpty(extracted))
                {
                    session.SetSlot(pendingSlot, extracted);
                    session.PendingRequiredSlot = null;
                }
                else
                {
                    // If short answer (e.g. single word/token), assume it is the direct slot value
                    string clean = resolvedText.Trim();
                    if (clean.Length > 0 && clean.Length < 30 && !clean.Contains(" "))
                    {
                        session.SetSlot(pendingSlot, clean);
                        session.PendingRequiredSlot = null;
                    }
                }
            }

            // 2. If new intent detected with high confidence, set as current intent
            if (detectedIntent != null && (session.CurrentIntent == null || session.State == SessionState.Completed || session.State == SessionState.Idle))
            {
                session.CurrentIntent = detectedIntent;
            }
            else if (detectedIntent == null && (session.State == SessionState.Completed || session.State == SessionState.Idle))
            {
                session.CurrentIntent = null;
            }

            // 3. Extract common entity slots from resolved text
            ExtractAllEntities(resolvedText, session);

            // 4. Verify required slots for the active intent
            if (session.CurrentIntent != null)
            {
                session.PendingRequiredSlot = null;
                bool allSatisfied = true;
                foreach (var req in session.CurrentIntent.RequiredSlots)
                {
                    if (!session.HasSlot(req))
                    {
                        session.PendingRequiredSlot = req;
                        session.State = SessionState.CollectingSlots;
                        allSatisfied = false;
                        break;
                    }
                }

                if (allSatisfied)
                {
                    session.State = SessionState.ReadyToExecute;
                }
            }
        }

        private static void ExtractAllEntities(string text, DialogueSession session)
        {
            // Machine ID pattern: CNC-01, PRESS-03, F-01, ROBOT-ARM-01, etc.
            var machineMatch = Regex.Match(text, @"\b(CNC-\d+|PRESS-\d+|ROBOT-ARM-\d+|CONVEYOR-\d+|F-\d+|D-\d+)\b", RegexOptions.IgnoreCase);
            if (machineMatch.Success)
            {
                session.SetSlot("machine_id", machineMatch.Value.ToUpperInvariant());
            }

            // Metric pattern: nhiệt độ, áp suất, độ rung, lỗi, công suất
            if (Regex.IsMatch(text, @"\b(nhiệt độ|nhiệt|nóng|temperature|temp)\b", RegexOptions.IgnoreCase))
            {
                session.SetSlot("metric", "temperature");
            }
            else if (Regex.IsMatch(text, @"\b(áp suất|áp lực|pressure|psi)\b", RegexOptions.IgnoreCase))
            {
                session.SetSlot("metric", "pressure");
            }
            else if (Regex.IsMatch(text, @"\b(rung|độ rung|vibration|bearing)\b", RegexOptions.IgnoreCase))
            {
                session.SetSlot("metric", "vibration");
            }
            else if (Regex.IsMatch(text, @"\b(lỗi|khuyết tật|defect|error|fault)\b", RegexOptions.IgnoreCase))
            {
                session.SetSlot("metric", "defects");
            }

            // Target value pattern: e.g. "xuống 80%", "bằng 100", "value: 42"
            var valueMatch = Regex.Match(text, @"\b(\d+(\.\d+)?)\s*(%|c|f|psi|bar|rpm)?\b", RegexOptions.IgnoreCase);
            if (valueMatch.Success && !machineMatch.Success) // Avoid capturing machine digits as value
            {
                session.SetSlot("value", valueMatch.Groups[1].Value);
            }

            // Area pattern
            var areaMatch = Regex.Match(text, @"\b(xưởng đúc|xưởng ép|xưởng cnc|dây chuyền 1|dây chuyền 2)\b", RegexOptions.IgnoreCase);
            if (areaMatch.Success)
            {
                session.SetSlot("area", areaMatch.Value.ToLowerInvariant());
            }

            // Table name pattern
            var tableMatch = Regex.Match(text, @"\b(factory_machines|SampleData|production_lines|telemetry_logs)\b", RegexOptions.IgnoreCase);
            if (tableMatch.Success)
            {
                session.SetSlot("tableName", tableMatch.Value);
            }
        }

        private static string ExtractSlotValue(string text, string slotName)
        {
            if (slotName.Equals("machine_id", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(text, @"\b(CNC-\d+|PRESS-\d+|ROBOT-ARM-\d+|CONVEYOR-\d+|F-\d+|D-\d+)\b", RegexOptions.IgnoreCase);
                if (m.Success) return m.Value.ToUpperInvariant();
                // Match words like "máy 1", "lò 2"
                var mAlt = Regex.Match(text, @"(máy|lò|băng chuyền)\s*([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
                if (mAlt.Success) return mAlt.Groups[2].Value.ToUpperInvariant();
            }
            else if (slotName.Equals("tableName", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(text, @"\b(factory_machines|SampleData|production_lines|telemetry_logs|[a-zA-Z0-9_]+)\b", RegexOptions.IgnoreCase);
                if (m.Success) return m.Value;
            }
            return string.Empty;
        }
    }
}
