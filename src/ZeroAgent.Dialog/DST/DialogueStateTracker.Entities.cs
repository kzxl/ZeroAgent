using System;
using System.Text.RegularExpressions;

namespace ZeroAgent.Dialog.DST
{
    /// <summary>
    /// Partial implementation of DialogueStateTracker focusing on entity extraction,
    /// slot validation, and multi-turn session progression.
    /// </summary>
    public sealed partial class DialogueStateTracker
    {
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
