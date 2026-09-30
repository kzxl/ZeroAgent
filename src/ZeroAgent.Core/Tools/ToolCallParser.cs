using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZeroAgent.Core.Tools
{
    /// <summary>
    /// Robust, zero-allocation parser for extracting tool invocations and final answers from LLM completions.
    /// Supports both standard ReAct syntax and native JSON structured function-calling formats.
    /// </summary>
    public static class ToolCallParser
    {
        private static readonly Regex ActionFuncRegex = new Regex(
            @"Action:\s*([A-Za-z0-9_]+)\s*\((.*)\)",
            RegexOptions.Compiled);

        private static readonly Regex ActionJsonRegex = new Regex(
            @"Action:\s*(\{.+\})",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex CodeblockJsonRegex = new Regex(
            @"```(?:json)?\s*(\{.+\})\s*```",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex FinalAnswerRegex = new Regex(
            @"Final Answer:\s*(.*)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Attempts to parse a tool invocation request from the model's raw completion output.
        /// </summary>
        public static bool TryParseToolCall(string? completion, out ToolCallRequest request)
        {
            request = null!;
            if (string.IsNullOrWhiteSpace(completion)) return false;

            string text = completion.Trim();

            // 1. Try Classic ReAct: Action: ToolName(arguments)
            var funcMatch = ActionFuncRegex.Match(text);
            if (funcMatch.Success)
            {
                string toolName = funcMatch.Groups[1].Value.Trim();
                string rawArgs = funcMatch.Groups[2].Value.Trim();

                // Strip outer quotes if single string
                if (rawArgs.StartsWith("\"") && rawArgs.EndsWith("\"") && rawArgs.Length >= 2)
                {
                    rawArgs = rawArgs.Substring(1, rawArgs.Length - 2);
                }

                request = new ToolCallRequest(toolName, rawArgs);
                return true;
            }

            // 2. Try ReAct with JSON block: Action: {"tool": "...", "arguments": ...}
            var actJsonMatch = ActionJsonRegex.Match(text);
            if (actJsonMatch.Success && TryParseJsonPayload(actJsonMatch.Groups[1].Value, out request))
            {
                return true;
            }

            // 3. Try Markdown Codeblock JSON: ```json { "tool": "...", ... } ```
            var codeMatch = CodeblockJsonRegex.Match(text);
            if (codeMatch.Success && TryParseJsonPayload(codeMatch.Groups[1].Value, out request))
            {
                return true;
            }

            // 4. Try Direct Raw JSON string starting with '{'
            if (text.StartsWith("{") && text.EndsWith("}") && TryParseJsonPayload(text, out request))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to parse a final answer from the model's completion output.
        /// </summary>
        public static bool TryParseFinalAnswer(string? completion, out string finalAnswer)
        {
            finalAnswer = string.Empty;
            if (string.IsNullOrWhiteSpace(completion)) return false;

            var match = FinalAnswerRegex.Match(completion);
            if (match.Success)
            {
                finalAnswer = match.Groups[1].Value.Trim();
                return true;
            }

            return false;
        }

        private static bool TryParseJsonPayload(string json, out ToolCallRequest request)
        {
            request = null!;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? toolName = null;
                if (root.TryGetProperty("tool", out var tProp)) toolName = tProp.GetString();
                else if (root.TryGetProperty("name", out var nProp)) toolName = nProp.GetString();
                else if (root.TryGetProperty("tool_name", out var tnProp)) toolName = tnProp.GetString();

                if (string.IsNullOrWhiteSpace(toolName)) return false;

                string argsJson = "{}";
                if (root.TryGetProperty("arguments", out var aProp))
                {
                    argsJson = aProp.ValueKind == JsonValueKind.String ? aProp.GetString()! : aProp.GetRawText();
                }
                else if (root.TryGetProperty("parameters", out var pProp))
                {
                    argsJson = pProp.ValueKind == JsonValueKind.String ? pProp.GetString()! : pProp.GetRawText();
                }

                string? callId = null;
                if (root.TryGetProperty("id", out var idProp)) callId = idProp.GetString();
                else if (root.TryGetProperty("call_id", out var cidProp)) callId = cidProp.GetString();

                request = new ToolCallRequest(toolName!, argsJson, callId);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
