using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroAgent.Core.Tools;
using ZeroAgent.Dialog.DST;
using ZeroAgent.Dialog.Generator;
using ZeroAgent.Dialog.Memory;
using ZeroAgent.Tools.Safety;

namespace ZeroAgent.Dialog.Engine
{
    /// <summary>
    /// Master Non-LLM Task-Oriented Dialogue Engine for Industrial & Operational Chatbots.
    /// Orchestrates 4-tier Agentic Memory, Dialogue State Tracking (DST), Tool Execution, and Template Generation.
    /// Operates entirely on CPU in sub-5ms with zero GPU/LLM dependencies.
    /// </summary>
    public sealed class ZeroDialogEngine
    {
        private readonly ConcurrentDictionary<string, DialogueSession> _sessions = new ConcurrentDictionary<string, DialogueSession>(StringComparer.OrdinalIgnoreCase);

        public AgenticMemoryEngine Memory { get; }
        public DialogueStateTracker Dst { get; }
        public AgentToolRegistry Tools { get; }
        public DialogueResponseGenerator Generator { get; } = new DialogueResponseGenerator();
        public HitlSafetyGate? SafetyGate { get; }

        public ZeroDialogEngine(AgentToolRegistry? tools = null, HitlSafetyGate? safetyGate = null, int dimension = 128)
        {
            Tools = tools ?? new AgentToolRegistry();
            SafetyGate = safetyGate;
            Memory = new AgenticMemoryEngine(dimension);
            Dst = new DialogueStateTracker(Memory.Embedder);
        }

        public DialogueSession GetOrCreateSession(string sessionId)
        {
            return _sessions.GetOrAdd(sessionId, id => new DialogueSession(id));
        }

        /// <summary>
        /// Processes a conversational message from the user and returns an action or clarification response.
        /// </summary>
        public async Task<DialogResponse> ChatAsync(string sessionId, string userMessage, UserProfile? profile = null)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return new DialogResponse("Tôi có thể giúp gì cho bạn trong việc giám sát thiết bị và vận hành nhà xưởng?", SessionState.Idle);
            }

            var workingMemory = Memory.GetWorkingMemory(sessionId);
            var session = GetOrCreateSession(sessionId);
            profile ??= Memory.Profiles.GetOrCreate(sessionId, "DefaultOperator", UserRole.Operator);

            // Step 1: Anaphora / Coreference Resolution via Working Memory
            string resolvedMessage = workingMemory.ResolveAnaphora(userMessage);

            // Step 2: Vector embedding
            var queryEmbedding = Memory.Embedder.Embed(resolvedMessage);

            // Step 3: Check Semantic Memory (SOPs / FAQ manuals)
            bool isDocQuery = resolvedMessage.IndexOf("quy trình", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("hướng dẫn", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("sop", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("tài liệu", StringComparison.OrdinalIgnoreCase) >= 0;

            float semMinScore = isDocQuery ? 0.20f : 0.65f;
            var faqMatches = Memory.Semantic.Query(queryEmbedding, topK: 1, minScore: semMinScore);
            if (faqMatches.Count > 0 && (isDocQuery || session.State == SessionState.Idle || session.State == SessionState.Completed))
            {
                var doc = faqMatches[0].Item;
                string faqAnswer = $"📖 [Tài liệu {doc.Category} - {doc.Title}]:\n{doc.Content}";
                workingMemory.AddTurn(userMessage, faqAnswer, "KNOWLEDGE_RETRIEVAL");
                return new DialogResponse(faqAnswer, SessionState.Idle, intentName: "KNOWLEDGE_RETRIEVAL", confidence: faqMatches[0].Similarity);
            }

            // Step 4: Check Episodic Memory (Historical incidents)
            bool isHistoryQuery = resolvedMessage.IndexOf("trước", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("lần trước", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("lịch sử", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("sự cố", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isHistoryQuery)
            {
                float epMinScore = 0.20f;
                var pastIncidents = Memory.Episodic.Recall(queryEmbedding, topK: 1, minScore: epMinScore);
                if (pastIncidents.Count > 0)
                {
                    var ep = pastIncidents[0].Episode;
                    string epAnswer = $"📜 [Ghi nhận sự cố trước đây]:\n- Vấn đề: {ep.Issue}\n- Xử lý: {ep.Resolution}\n- Kết quả: {(ep.Success ? "Thành công" : "Chưa hoàn tất")}";
                    workingMemory.AddTurn(userMessage, epAnswer, "HISTORICAL_EPISODE");
                    return new DialogResponse(epAnswer, SessionState.Idle, intentName: "HISTORICAL_EPISODE", confidence: pastIncidents[0].Similarity);
                }
            }

            // Step 5: Intent Recognition
            var (detectedIntent, score) = Dst.MatchIntent(queryEmbedding, resolvedMessage);

            // Step 6: Advance Dialogue State
            Dst.AdvanceSession(session, resolvedMessage, detectedIntent);

            if (session.TryGetSlot("machine_id", out var currentMachineId))
            {
                workingMemory.SetSlot("machine_id", currentMachineId);
            }

            // Step 7: Handle dialogue state outcomes
            if (session.State == SessionState.CollectingSlots && !string.IsNullOrEmpty(session.PendingRequiredSlot))
            {
                string pending = session.PendingRequiredSlot!;
                string prompt = session.CurrentIntent?.SlotClarificationPrompts.TryGetValue(pending, out var p) == true
                    ? p
                    : $"Vui lòng cung cấp thông tin cho [{pending}]:";

                string clarText = Generator.FormatClarification(prompt);
                workingMemory.AddTurn(userMessage, clarText, session.CurrentIntent?.Name ?? "UNKNOWN");
                return new DialogResponse(clarText, SessionState.CollectingSlots, session.CurrentIntent?.Name, session.Slots, false, score);
            }

            if (session.State == SessionState.ReadyToExecute && session.CurrentIntent != null)
            {
                var intent = session.CurrentIntent;

                // RBAC Permission Gate check
                if (!string.IsNullOrEmpty(intent.RequiredPermission) && !profile.CanExecute(intent.RequiredPermission))
                {
                    session.State = SessionState.ActionBlockedByPermission;
                    string deniedMsg = Generator.FormatPermissionDenied(intent.RequiredPermission);
                    workingMemory.AddTurn(userMessage, deniedMsg, intent.Name);
                    return new DialogResponse(deniedMsg, SessionState.ActionBlockedByPermission, intent.Name, session.Slots, false, score);
                }

                // Execute action
                string actionOutput;
                if (intent.ActionHandler != null)
                {
                    actionOutput = await intent.ActionHandler(session).ConfigureAwait(false);
                }
                else
                {
                    actionOutput = $"Tác vụ '{intent.Name}' đã được xác nhận thực thi.";
                }

                // Update Working Memory active entities
                if (session.TryGetSlot("machine_id", out var machineId))
                {
                    workingMemory.SetSlot("machine_id", machineId);
                }
                if (session.TryGetSlot("metric", out var metric))
                {
                    workingMemory.SetSlot("metric", metric);
                }

                // Record successful action into slots for template rendering
                session.SetSlot("output", actionOutput);

                string finalResponse = Generator.FormatResponse(intent.ResponseTemplates, session.Slots, actionOutput);
                workingMemory.AddTurn(userMessage, finalResponse, intent.Name);

                session.State = SessionState.Completed;
                return new DialogResponse(finalResponse, SessionState.Completed, intent.Name, session.Slots, true, score);
            }

            // Fallback
            string fallback = "Xin lỗi, tôi chưa hiểu rõ yêu cầu. Bạn có thể hỏi về nhiệt độ, áp suất máy, kiểm tra PLC, hoặc tra cứu quy trình sự cố.";
            workingMemory.AddTurn(userMessage, fallback, "FALLBACK");
            return new DialogResponse(fallback, SessionState.Idle, null, session.Slots, false, 0.0f);
        }
    }
}
