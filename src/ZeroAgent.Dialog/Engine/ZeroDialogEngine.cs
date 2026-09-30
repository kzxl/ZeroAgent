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

        /// <summary>
        /// Two-Tier cognitive escalation handler.
        /// Invoked when Reflex NLU confidence is low (< 0.45) or intent is unresolved, delegating to ReAct deliberation.
        /// </summary>
        public Func<DialogueSession, WorkingMemory, UserProfile, string, Task<DialogResponse?>>? CognitiveEscalationHandler { get; set; }

        public void SetCognitiveEscalationBridge(CognitiveEscalationBridge bridge)
        {
            if (bridge == null) throw new ArgumentNullException(nameof(bridge));
            CognitiveEscalationHandler = bridge.EscalateAsync;
        }

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

            // Step 2.5: Check Semantic Response Cache (Short-circuit NLU/LLM if similarity >= 0.95 and session is idle/completed)
            if (session.State == SessionState.Idle || session.State == SessionState.Completed)
            {
                if (Memory.ResponseCache.TryGet(queryEmbedding, resolvedMessage, minSimilarity: 0.95f, out var cachedEntry) && cachedEntry != null)
                {
                    // Synchronize active slots from query so working memory stays updated on cache hits
                    Dst.AdvanceSession(session, resolvedMessage, null);
                    foreach (var kvp in session.Slots)
                    {
                        workingMemory.SetSlot(kvp.Key, kvp.Value);
                    }

                    workingMemory.AddTurn(userMessage, cachedEntry.ResponseText, cachedEntry.IntentName ?? "SEMANTIC_CACHE_HIT");
                    return new DialogResponse(
                        cachedEntry.ResponseText,
                        SessionState.Completed,
                        intentName: cachedEntry.IntentName ?? "SEMANTIC_CACHE_HIT",
                        confidence: cachedEntry.Similarity);
                }
            }

            // Step 3: Explicit Analytical Deliberation (Escalation to Tier 2 ReAct when analytical reasoning requested)
            bool isAnalyticalDeliberation =
                resolvedMessage.IndexOf("phân tích", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("tại sao", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("nguyên nhân", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("đối chiếu", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("tổng hợp", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("analyze", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("explain", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isAnalyticalDeliberation && CognitiveEscalationHandler != null)
            {
                var escalated = await CognitiveEscalationHandler(session, workingMemory, profile, resolvedMessage).ConfigureAwait(false);
                if (escalated != null)
                {
                    workingMemory.AddTurn(userMessage, escalated.Text, escalated.IntentName ?? "COGNITIVE_DELIBERATION_REACT");
                    return escalated;
                }
            }

            // Step 4: Check Semantic Memory (SOPs / FAQ manuals)
            bool isDocQuery = resolvedMessage.IndexOf("quy trình", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("hướng dẫn", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("sop", StringComparison.OrdinalIgnoreCase) >= 0
                || resolvedMessage.IndexOf("tài liệu", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isDocQuery)
            {
                var faqMatches = Memory.Semantic.Query(queryEmbedding, topK: 1, minScore: 0.20f);
                if (faqMatches.Count > 0)
                {
                    var doc = faqMatches[0].Item;
                    string faqAnswer = $"📖 [Tài liệu {doc.Category} - {doc.Title}]:\n{doc.Content}";
                    workingMemory.AddTurn(userMessage, faqAnswer, "KNOWLEDGE_RETRIEVAL");
                    return new DialogResponse(faqAnswer, SessionState.Idle, intentName: "KNOWLEDGE_RETRIEVAL", confidence: faqMatches[0].Similarity);
                }
            }

            // Step 5: Check Episodic Memory (Historical incidents)
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
            if (score < 0.25f)
            {
                detectedIntent = null;
            }

            // Step 6: Advance Dialogue State
            Dst.AdvanceSession(session, resolvedMessage, detectedIntent);

            foreach (var kvp in session.Slots)
            {
                workingMemory.SetSlot(kvp.Key, kvp.Value);
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
                foreach (var kvp in session.Slots)
                {
                    workingMemory.SetSlot(kvp.Key, kvp.Value);
                }

                // Record successful action into slots for template rendering
                session.SetSlot("output", actionOutput);

                string finalResponse = Generator.FormatResponse(intent.ResponseTemplates, session.Slots, actionOutput);
                workingMemory.AddTurn(userMessage, finalResponse, intent.Name);

                // Populate semantic response cache for idempotent queries (do NOT cache state-mutating actions)
                if (intent.Name != null
                    && !intent.Name.StartsWith("SET_", StringComparison.OrdinalIgnoreCase)
                    && !intent.Name.StartsWith("WRITE_", StringComparison.OrdinalIgnoreCase)
                    && !intent.Name.StartsWith("STOP_", StringComparison.OrdinalIgnoreCase)
                    && !intent.Name.Contains("EMERGENCY"))
                {
                    Memory.ResponseCache.Store(queryEmbedding, resolvedMessage, finalResponse, intent.Name);
                }

                session.State = SessionState.Completed;
                return new DialogResponse(finalResponse, SessionState.Completed, intent.Name, session.Slots, true, score);
            }

            // Step 8: Cognitive Escalation Bridge (Two-Tier Deliberation)
            if (CognitiveEscalationHandler != null)
            {
                var escalated = await CognitiveEscalationHandler(session, workingMemory, profile, resolvedMessage).ConfigureAwait(false);
                if (escalated != null)
                {
                    workingMemory.AddTurn(userMessage, escalated.Text, escalated.IntentName ?? "COGNITIVE_DELIBERATION_REACT");
                    return escalated;
                }
            }

            // Step 8.5: Fallback Knowledge Retrieval from Semantic Memory for Unmatched Queries
            var fallbackFaq = Memory.Semantic.Query(queryEmbedding, topK: 1, minScore: 0.50f);
            if (fallbackFaq.Count > 0)
            {
                var doc = fallbackFaq[0].Item;
                string faqAnswer = $"📖 [Tài liệu {doc.Category} - {doc.Title}]:\n{doc.Content}";
                workingMemory.AddTurn(userMessage, faqAnswer, "KNOWLEDGE_RETRIEVAL");
                return new DialogResponse(faqAnswer, SessionState.Idle, intentName: "KNOWLEDGE_RETRIEVAL", confidence: fallbackFaq[0].Similarity);
            }

            // Step 9: Fallback
            string fallback = "Xin lỗi, tôi chưa hiểu rõ yêu cầu. Bạn có thể hỏi về nhiệt độ, áp suất máy, kiểm tra PLC, hoặc tra cứu quy trình sự cố.";
            workingMemory.AddTurn(userMessage, fallback, "FALLBACK");
            return new DialogResponse(fallback, SessionState.Idle, null, session.Slots, false, 0.0f);
        }
    }
}
