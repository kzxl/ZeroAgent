using System;
using System.Threading.Tasks;

namespace ZeroAgent.Core.Tools
{
    /// <summary>
    /// Represents an invokable tool binding available to the AI agent.
    /// </summary>
    public sealed class AgentTool
    {
        public string Name { get; }
        public string Description { get; }
        public string ParameterSignature { get; }
        public Func<string, Task<string>> Invoker { get; }

        public AgentTool(string name, string description, string parameterSignature, Func<string, Task<string>> invoker)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
            ParameterSignature = parameterSignature ?? string.Empty;
            Invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        }

        public async Task<string> ExecuteAsync(string argument)
        {
            try
            {
                return await Invoker(argument).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return $"Tool execution failed with error: {ex.Message}";
            }
        }
    }
}
