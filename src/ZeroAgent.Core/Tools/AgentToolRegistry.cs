using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ZeroAgent.Core.Tools
{
    /// <summary>
    /// Registry for tools executable by AI agents.
    /// Provides strongly-typed registration and automatic schema description formatting.
    /// </summary>
    public sealed class AgentToolRegistry
    {
        private readonly Dictionary<string, AgentTool> _tools = new Dictionary<string, AgentTool>(StringComparer.OrdinalIgnoreCase);

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
            if (_tools.TryGetValue(name, out var tool))
            {
                return await tool.ExecuteAsync(argument).ConfigureAwait(false);
            }
            return $"Error: Tool '{name}' is not found in the registry.";
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
                sb.AppendLine($"- `{tool.Name}`({tool.ParameterSignature}): {tool.Description}");
            }
            return sb.ToString();
        }
    }
}
