using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZeroAgent.Core.Engine;
using ZeroAgent.Dialog.Generator;

namespace ZeroAgent.Dialog.Learning
{
    /// <summary>
    /// Represents an agentic, supervised fine-tuning (SFT) training sample
    /// containing thought deliberation, tool invocation, and factual natural response.
    /// </summary>
    public sealed class ErpSftSample
    {
        public string SampleId { get; set; } = Guid.NewGuid().ToString("N");
        public string Domain { get; set; } = string.Empty;
        public string UserQuery { get; set; } = string.Empty;
        public string Thought { get; set; } = string.Empty;
        public string? ToolCall { get; set; }
        public string? ToolResult { get; set; }
        public string Response { get; set; } = string.Empty;

        /// <summary>
        /// Renders this sample into the canonical Micro-SLM token format using special token delimiters.
        /// </summary>
        public string ToFormattedText()
        {
            var sb = new StringBuilder();
            sb.Append("<bos>");
            sb.Append("<system>Bạn là trợ lý AI thông minh chuyên trách giải pháp quản trị doanh nghiệp ERP.</system>");
            if (!string.IsNullOrWhiteSpace(Domain))
            {
                sb.Append("<expert>").Append(Domain).Append("</expert>");
            }
            sb.Append("<user>").Append(UserQuery).Append("</user>");
            if (!string.IsNullOrWhiteSpace(Thought))
            {
                sb.Append("<thought>").Append(Thought).Append("</thought>");
            }
            if (!string.IsNullOrWhiteSpace(ToolCall))
            {
                sb.Append("<tool_call>").Append(ToolCall).Append("</tool_call>");
            }
            if (!string.IsNullOrWhiteSpace(ToolResult))
            {
                sb.Append("<tool_result>").Append(ToolResult).Append("</tool_result>");
            }
            sb.Append("<response>").Append(Response).Append("</response>");
            sb.Append("<eos>");
            return sb.ToString();
        }

        public string ToJsonLine()
        {
            return $"{{\"id\":\"{SampleId}\",\"domain\":\"{EscapeJson(Domain)}\",\"query\":\"{EscapeJson(UserQuery)}\",\"thought\":\"{EscapeJson(Thought)}\",\"tool_call\":\"{EscapeJson(ToolCall ?? "")}\",\"tool_result\":\"{EscapeJson(ToolResult ?? "")}\",\"response\":\"{EscapeJson(Response)}\"}}";
        }

        private static string EscapeJson(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }

    /// <summary>
    /// Distiller generating synthetic multi-turn SFT datasets from teacher LLMs or rule-grounded generators
    /// to train and incubate internal Vietnamese Micro-SLMs with zero hallucination.
    /// </summary>
    public sealed class ErpDialogueDistiller
    {
        private readonly ILlmClient _teacherLlm;

        public ErpDialogueDistiller(ILlmClient teacherLlm)
        {
            _teacherLlm = teacherLlm ?? throw new ArgumentNullException(nameof(teacherLlm));
        }

        /// <summary>
        /// Distills a complete, structured SFT dialogue trajectory for a given domain fact set.
        /// </summary>
        public async Task<ErpSftSample> DistillTrajectoryAsync(
            string domain,
            string userQuery,
            string toolName,
            string toolArgs,
            string toolOutput,
            CancellationToken cancellationToken = default)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Hệ thống: Bạn là chuyên gia kiến trúc dữ liệu huấn luyện Micro-SLM cho giải pháp ERP Tiếng Việt.");
            prompt.AppendLine($"Phân hệ nghiệp vụ: {domain}");
            prompt.AppendLine($"Dữ liệu thực tế từ hệ thống: Tác vụ='{toolName}', Tham số='{toolArgs}', Kết quả='{toolOutput}'");
            prompt.AppendLine($"Câu hỏi của người vận hành: \"{userQuery}\"");
            prompt.AppendLine("Nhiệm vụ của bạn:");
            prompt.AppendLine("Hãy trả lời người vận hành trực tiếp, ngắn gọn, đưa số liệu thực tế vào thẻ <response>câu trả lời</response>.");
            prompt.AppendLine("Quy tắc: Không bịa thông tin ngoài dữ liệu thực tế.");

            string teacherOutput = await _teacherLlm.CompleteAsync(prompt.ToString(), cancellationToken).ConfigureAwait(false);

            string thought = ExtractTag(teacherOutput, "thought");
            string response = ExtractTag(teacherOutput, "response");

            int endThoughtIdx = teacherOutput.IndexOf("</thought>", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(response) && endThoughtIdx >= 0)
            {
                response = teacherOutput.Substring(endThoughtIdx + "</thought>".Length).Trim();
            }

            if (string.IsNullOrWhiteSpace(thought))
            {
                thought = $"Cần gọi hàm {toolName} để tra cứu thông tin cho yêu cầu '{userQuery}'.";
            }
            if (string.IsNullOrWhiteSpace(response))
            {
                response = teacherOutput.Replace("<thought>", "").Replace("</thought>", "").Trim();
                if (string.IsNullOrWhiteSpace(response))
                {
                    response = $"Kết quả xử lý: {toolOutput}";
                }
            }

            return new ErpSftSample
            {
                Domain = domain,
                UserQuery = userQuery,
                Thought = thought,
                ToolCall = $"{toolName}({toolArgs})",
                ToolResult = toolOutput,
                Response = response
            };
        }

        /// <summary>
        /// Exports a collection of distilled SFT samples into a UTF-8 JSONL file for model incubation.
        /// </summary>
        public static async Task ExportDatasetJsonlAsync(IEnumerable<ErpSftSample> samples, string filePath)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            foreach (var sample in samples)
            {
                await sw.WriteLineAsync(sample.ToJsonLine()).ConfigureAwait(false);
            }
        }

        private static string ExtractTag(string text, string tag)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string startTag = $"<{tag}>";
            string endTag = $"</{tag}>";

            int start = text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return string.Empty;
            start += startTag.Length;

            int end = text.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) return text.Substring(start).Trim();

            return text.Substring(start, end - start).Trim();
        }
    }
}
