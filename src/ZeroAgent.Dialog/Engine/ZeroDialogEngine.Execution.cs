using System;
using System.Threading.Tasks;
using ZeroAgent.Dialog.DST;
using ZeroAgent.Dialog.Memory;

namespace ZeroAgent.Dialog.Engine
{
    public sealed partial class ZeroDialogEngine
    {
        /// <summary>
        /// Executes the ready intent, validates RBAC permissions, caches idempotent outputs,
        /// and formats the final personalized template response.
        /// </summary>
        private async Task<DialogResponse> ExecuteIntentAsync(
            DialogueSession session,
            WorkingMemory workingMemory,
            UserProfile userProfile,
            string userMessage,
            string resolvedMessage,
            float[] queryEmbedding,
            float score)
        {
            var intent = session.CurrentIntent!;

            // RBAC Permission Gate check
            if (!string.IsNullOrEmpty(intent.RequiredPermission) && !userProfile.CanExecute(intent.RequiredPermission))
            {
                session.State = SessionState.ActionBlockedByPermission;
                string deniedMsg = userProfile.IsGuest
                    ? Generator.FormatGuestLoginRequired(intent.RequiredPermission, intent.Name)
                    : Generator.FormatPermissionDenied(intent.RequiredPermission);
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

            userProfile.Persona.RecordInteraction(intent.Name, session.Slots);
            session.State = SessionState.Completed;
            return new DialogResponse(finalResponse, SessionState.Completed, intent.Name, session.Slots, true, score);
        }
    }
}
