using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZeroAgent.Core.Context;
using ZeroAgent.Core.Engine;

namespace ZeroAgent.Core.Swarm
{
    /// <summary>
    /// Multi-Agent Swarm coordinator.
    /// Manages a collective of specialized agents, orchestrates goal delegation, and handles collaborative handoffs.
    /// </summary>
    public sealed class AgentSwarm
    {
        private readonly Dictionary<string, ReActAgent> _agents = new Dictionary<string, ReActAgent>(StringComparer.OrdinalIgnoreCase);

        public int AgentCount => _agents.Count;
        public IEnumerable<ReActAgent> Agents => _agents.Values;

        public void RegisterAgent(ReActAgent agent)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            _agents[agent.Name] = agent;
        }

        public async Task<AgentResponse> DelegateAsync(string agentName, AgentContext context, CancellationToken cancellationToken = default)
        {
            if (!_agents.TryGetValue(agentName, out var agent))
            {
                return AgentResponse.Failed($"Agent '{agentName}' is not registered in this swarm.", 0, TimeSpan.Zero, context.History);
            }

            return await agent.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
