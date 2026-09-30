using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ZeroPrompt.Core.Grammar;

namespace ZeroAgent.Core.Tools
{
    /// <summary>
    /// Registry for tools executable by AI agents.
    /// Provides strongly-typed registration, automatic schema description formatting,
    /// and Human-in-the-Loop (HITL) safety policy enforcement.
    /// </summary>
    public sealed class AgentToolRegistry
    {
        private readonly Dictionary<string, AgentTool> _tools = new Dictionary<string, AgentTool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Global or context-specific Human-in-the-Loop approval callback.
        /// Invoked when a tool with <see cref="AgentTool.RequiresApproval"/> is executed.
        /// Return true to allow execution, or false to reject.
        /// </summary>
        public Func<AgentTool, string, Task<bool>>? ApprovalHandler { get; set; }

        public int Count => _tools.Count;
        public IEnumerable<AgentTool> Tools => _tools.Values;

        public void Register(AgentTool tool)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            _tools[tool.Name] = tool;
        }

        public void Register(string name, string description, Func<string, string> func)
        {
            Register(new AgentTool(name, description, "string argument", arg => Task.FromResult(func(arg))));
        }

        public void Register(string name, string description, Func<string, Task<string>> asyncFunc)
        {
            Register(new AgentTool(name, description, "string argument", asyncFunc));
        }

        public void Register(string name, string description, Func<int, string> func)
        {
            Register(new AgentTool(name, description, "int id", arg =>
            {
                int.TryParse(arg?.Trim(), out int val);
                return Task.FromResult(func(val));
            }));
        }

        public bool TryGetTool(string name, out AgentTool tool)
        {
            return _tools.TryGetValue(name, out tool!);
        }

        public async Task<string> ExecuteAsync(string name, string argument)
        {
            if (!_tools.TryGetValue(name, out var tool))
            {
                return $"Error: Tool '{name}' is not found in the registry.";
            }

            // HITL Safety Gate Check
            if (tool.RequiresApproval)
            {
                if (ApprovalHandler == null)
                {
                    return $"Safety Policy Violation: Tool '{name}' requires Human-in-the-Loop (HITL) approval, but no ApprovalHandler is configured.";
                }

                bool approved = await ApprovalHandler(tool, argument).ConfigureAwait(false);
                if (!approved)
                {
                    return $"Action rejected: Execution of tool '{name}' was denied by operator safety gate.";
                }
            }

            return await tool.ExecuteAsync(argument).ConfigureAwait(false);
        }

        /// <summary>
        /// Generates a markdown description of available tools for injection into the agent's system prompt.
        /// </summary>
        public string GetToolsPrompt()
        {
            if (_tools.Count == 0) return "No external tools available.";

            var sb = new StringBuilder();
            sb.AppendLine("You have access to the following tools:");
            foreach (var tool in _tools.Values)
            {
                string sensitiveFlag = tool.RequiresApproval ? " [REQUIRES OPERATOR APPROVAL]" : string.Empty;
                sb.AppendLine($"- `{tool.Name}`({tool.ParameterSignature}): {tool.Description}{sensitiveFlag}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Generates an OpenAI-compatible JSON Schema definition for all registered tools.
        /// </summary>
        public string GetToolsJsonSchema()
        {
            if (_tools.Count == 0) return "[]";

            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (var tool in _tools.Values)
            {
                if (!first) sb.Append(",");
                first = false;

                sb.Append("{\"name\":\"").Append(tool.Name).Append("\",");
                sb.Append("\"description\":\"").Append(tool.Description.Replace("\"", "\\\"")).Append("\",");
                sb.Append("\"parameters\":{");
                sb.Append("\"type\":\"object\",");

                if (tool.Schema != null && tool.Schema.Properties.Count > 0)
                {
                    sb.Append("\"properties\":{");
                    bool firstProp = true;
                    foreach (var prop in tool.Schema.Properties)
                    {
                        if (!firstProp) sb.Append(",");
                        firstProp = false;
                        sb.Append("\"").Append(prop.Key).Append("\":{\"type\":\"")
                          .Append(prop.Value.ToString().ToLowerInvariant()).Append("\"}");
                    }
                    sb.Append("},");

                    sb.Append("\"required\":[");
                    bool firstReq = true;
                    foreach (var req in tool.Schema.RequiredProperties)
                    {
                        if (!firstReq) sb.Append(",");
                        firstReq = false;
                        sb.Append("\"").Append(req).Append("\"");
                    }
                    sb.Append("]");
                }
                else
                {
                    sb.Append("\"properties\":{},\"required\":[]");
                }

                sb.Append("}}");
            }
            sb.Append("]");
            return sb.ToString();
        }
    }
}
