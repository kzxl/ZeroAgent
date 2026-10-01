using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZeroAgent.Core.Context;
using ZeroAgent.Core.Engine;

namespace ZeroAgent.Core.Swarm
{
    /// <summary>
    /// Hierarchical Swarm Supervisor and Meta-Orchestrator.
    /// Decomposes multifaceted user objectives into domain subtasks, delegates work to specialized agents
    /// in the swarm via a shared blackboard, and synthesizes an authoritative final response.
    /// </summary>
    public sealed class SupervisorAgent
    {
        public string Name { get; }
        public string Role { get; }
        public AgentSwarm Swarm { get; }
        public ILlmClient Llm { get; }

        public SupervisorAgent(string name, string role, AgentSwarm swarm, ILlmClient llm)
        {
            Name = name ?? "SwarmSupervisor";
            Role = role ?? "Chief Systems Orchestrator";
            Swarm = swarm ?? throw new ArgumentNullException(nameof(swarm));
            Llm = llm ?? throw new ArgumentNullException(nameof(llm));
        }

        public readonly struct SubTaskAssignment
        {
            public string AgentName { get; }
            public string SubGoal { get; }

            public SubTaskAssignment(string agentName, string subGoal)
            {
                AgentName = agentName;
                SubGoal = subGoal;
            }
        }

        /// <summary>
        /// Executes a complex hierarchical multi-agent mission end-to-end.
        /// </summary>
        public async Task<AgentResponse> ExecuteHierarchicalTaskAsync(string complexGoal, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(complexGoal))
                throw new ArgumentNullException(nameof(complexGoal));

            var sw = Stopwatch.StartNew();
            var history = new List<AgentMessage>();

            // Phase 1: Task Decomposition
            var assignments = await PlanSubTasksAsync(complexGoal, cancellationToken).ConfigureAwait(false);

            if (assignments.Count == 0)
            {
                // Fallback: If no subtasks identified, attempt to delegate to first agent or synthesize directly
                var firstAgent = System.Linq.Enumerable.FirstOrDefault(Swarm.Agents);
                if (firstAgent != null)
                {
                    var ctx = new AgentContext(complexGoal, maxSteps: 6);
                    return await Swarm.DelegateAsync(firstAgent.Name, ctx, cancellationToken).ConfigureAwait(false);
                }

                sw.Stop();
                return AgentResponse.Failed("No available agents registered in the swarm to handle the mission.", 0, sw.Elapsed, history);
            }

            // Phase 2: Collaborative Multi-Agent Delegation
            var findings = new List<string>();
            int totalSteps = 0;

            foreach (var task in assignments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var subContext = new AgentContext(task.SubGoal, maxSteps: 6);
                var subResponse = await Swarm.DelegateAsync(task.AgentName, subContext, cancellationToken).ConfigureAwait(false);

                totalSteps += subResponse.TotalSteps;

                if (subResponse.Success)
                {
                    findings.Add($"[{task.AgentName}] Sub-task '{task.SubGoal}': {subResponse.Output}");
                }
                else
                {
                    findings.Add($"[{task.AgentName}] Sub-task '{task.SubGoal}' (Error): {subResponse.ErrorMessage}");
                }
            }

            // Phase 3: Final Synthesis
            string finalSynthesis = await SynthesizeFindingsAsync(complexGoal, findings, cancellationToken).ConfigureAwait(false);

            sw.Stop();
            history.Add(new AgentMessage(AgentRole.Assistant, finalSynthesis));

            return AgentResponse.Succeeded(finalSynthesis, Math.Max(1, totalSteps), sw.Elapsed, history);
        }

        private async Task<List<SubTaskAssignment>> PlanSubTasksAsync(string complexGoal, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are {Name}, acting as {Role}.");
            sb.AppendLine("Decompose the following complex objective into 1 to 4 distinct sub-tasks for the specialized agents in your team:");
            sb.AppendLine($"Objective: {complexGoal}");
            sb.AppendLine();
            sb.AppendLine("Available Specialists in your Swarm:");
            foreach (var agent in Swarm.Agents)
            {
                sb.AppendLine($"- {agent.Name} (Role: {agent.Role}, Tools: {agent.Tools.Count})");
            }
            sb.AppendLine();
            sb.AppendLine("Respond with assignments using the exact line format:");
            sb.AppendLine("Delegate: [AgentName] | Task: <clear subtask instruction>");

            string planningOutput = await Llm.CompleteAsync(sb.ToString(), cancellationToken).ConfigureAwait(false);

            return ParseAssignments(planningOutput);
        }

        private List<SubTaskAssignment> ParseAssignments(string planningText)
        {
            var list = new List<SubTaskAssignment>();
            if (string.IsNullOrWhiteSpace(planningText)) return list;

            var matches = Regex.Matches(planningText, @"Delegate:\s*\[?([a-zA-Z0-9_\-]+)\]?\s*\|\s*Task:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                string agentName = m.Groups[1].Value.Trim();
                string subGoal = m.Groups[2].Value.Trim();

                if (Swarm.FindAgent(agentName) != null && !string.IsNullOrEmpty(subGoal))
                {
                    list.Add(new SubTaskAssignment(agentName, subGoal));
                }
            }

            return list;
        }

        private async Task<string> SynthesizeFindingsAsync(string originalGoal, List<string> findings, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are {Name}, the team supervisor ({Role}).");
            sb.AppendLine($"Original Goal: {originalGoal}");
            sb.AppendLine();
            sb.AppendLine("Team Findings from Specialists:");
            foreach (var f in findings)
            {
                sb.AppendLine($"- {f}");
            }
            sb.AppendLine();
            sb.AppendLine("Synthesize a cohesive, comprehensive, and authoritative response addressing the original goal.");

            return await Llm.CompleteAsync(sb.ToString(), cancellationToken).ConfigureAwait(false);
        }
    }
}
