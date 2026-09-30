using System;
using System.Collections.Generic;
using ZeroPrompt.Core.Templating;

namespace ZeroAgent.Dialog.Generator
{
    /// <summary>
    /// Generates structured, diverse, and contextual responses using ZeroPrompt templates.
    /// Eliminates rigid robot-like repetitive answers via template alternation.
    /// </summary>
    public sealed class DialogueResponseGenerator
    {
        private int _turnCounter = 0;

        /// <summary>
        /// Formats response using one of the available intent templates.
        /// </summary>
        public string FormatResponse(IReadOnlyList<string> templates, IReadOnlyDictionary<string, string> slots, string fallback)
        {
            if (templates == null || templates.Count == 0)
            {
                return fallback;
            }

            int index = Math.Abs(_turnCounter++) % templates.Count;
            string selectedTemplate = templates[index];

            var template = new PromptTemplate(selectedTemplate);
            var ctx = new PromptContext();
            foreach (var kvp in slots)
            {
                ctx.Set(kvp.Key, kvp.Value);
            }

            return template.Render(ctx);
        }

        public string FormatClarification(string slotPrompt)
        {
            return $"ℹ️ {slotPrompt}";
        }

        public string FormatPermissionDenied(string requiredPermission)
        {
            return $"⛔ Truy cập bị từ chối: Thao tác này yêu cầu quyền [{requiredPermission}]. Tài khoản hiện tại không có quyền can thiệp.";
        }
    }
}
