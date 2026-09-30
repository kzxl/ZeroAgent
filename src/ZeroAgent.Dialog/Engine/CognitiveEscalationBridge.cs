using System;
using System.Threading.Tasks;
using ZeroAgent.Core.Context;
using ZeroAgent.Core.Engine;
using ZeroAgent.Dialog.DST;
using ZeroAgent.Dialog.Memory;

namespace ZeroAgent.Dialog.Engine
{
    /// <summary>
    /// Two-Tier Cognitive Escalation Bridge.
    /// Bridges Tier 1 (Sub-millisecond Reflex NLU) to Tier 2 (Generative Deliberation ReAct Agent).
    /// When Tier 1 confidence is insufficient or an open-ended multi-step query is detected,
    /// packages active working memory and delegates reasoning to ReActAgent.
    /// </summary>
    public sealed class CognitiveEscalationBridge
    {
        private readonly ReActAgent _reActAgent;

        public ReActAgent ReActAgent => _reActAgent;

        public CognitiveEscalationBridge(ReActAgent reActAgent)
        {
            _reActAgent = reActAgent ?? throw new ArgumentNullException(nameof(reActAgent));
        }

        public async Task<DialogResponse?> EscalateAsync(
            DialogueSession session,
            WorkingMemory workingMemory,
            UserProfile profile,
            string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            // Build AgentContext with active subject and operator profile
            var context = new AgentContext(query, maxSteps: 8);

            // 1. Seed active entities from Working Memory
            if (!string.IsNullOrEmpty(workingMemory.CurrentSubject))
            {
                context.AddMessage(AgentRole.System, $"Active target entity: {workingMemory.CurrentSubject}. Operator: {profile.Name} (Role: {profile.Role})");
            }
            if (!string.IsNullOrEmpty(workingMemory.CurrentMetric))
            {
                context.AddMessage(AgentRole.System, $"Active metric of interest: {workingMemory.CurrentMetric}");
            }

            // 2. Seed recent dialogue history (up to 4 recent turns)
            var turns = workingMemory.Turns;
            int start = Math.Max(0, turns.Count - 4);
            for (int i = start; i < turns.Count; i++)
            {
                context.AddMessage(AgentRole.User, turns[i].UserMessage);
                context.AddMessage(AgentRole.Assistant, turns[i].BotResponse);
            }

            // 3. Execute deliberation loop via ReActAgent
            var agentResponse = await _reActAgent.ExecuteAsync(context).ConfigureAwait(false);
            if (agentResponse.Success)
            {
                session.State = SessionState.Completed;
                return new DialogResponse(
                    agentResponse.Output,
                    SessionState.Completed,
                    "COGNITIVE_DELIBERATION_REACT",
                    session.Slots,
                    isActionExecuted: agentResponse.TotalSteps > 1,
                    confidence: 0.95f);
            }

            return null; // Allow fallback if ReAct was unable to complete
        }
    }
}
