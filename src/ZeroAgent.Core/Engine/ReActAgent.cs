using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZeroAgent.Core.Context;
using ZeroAgent.Core.Tools;

namespace ZeroAgent.Core.Engine
{
    /// <summary>
    /// Autonomous cognitive agent implementing the Reasoning-Action (ReAct) paradigm.
    /// Interleaves thinking, external tool execution, observation analysis, and answer formulation.
    /// </summary>
    public sealed class ReActAgent
    {
        private static readonly Regex ActionRegex = new Regex(@"Action:\s*([A-Za-z0-9_]+)\s*\((.*)\)", RegexOptions.Compiled);
        private static readonly Regex FinalAnswerRegex = new Regex(@"Final Answer:\s*(.*)", RegexOptions.Compiled | RegexOptions.Singleline);

        public string Name { get; }
        public string Role { get; }
        public AgentToolRegistry Tools { get; }
        public ILlmClient Llm { get; }

        public ReActAgent(string name, string role, AgentToolRegistry tools, ILlmClient llm)
        {
            Name = name ?? "ReActAgent";
            Role = role ?? "General Autonomous Assistant";
            Tools = tools ?? new AgentToolRegistry();
            Llm = llm ?? throw new ArgumentNullException(nameof(llm));
        }

        public async Task<AgentResponse> ExecuteAsync(AgentContext context, CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var sw = Stopwatch.StartNew();
            int currentStep = 0;

            var conversation = new StringBuilder();
            conversation.AppendLine($"User Goal: {context.Goal}");

            while (currentStep < context.MaxSteps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentStep++;

                // 1. Build prompt
                string prompt = BuildPrompt(context.Goal, conversation.ToString());

                // 2. Query LLM
                string completion = await Llm.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
                context.AddMessage(AgentRole.Assistant, completion);

                // 3. Check for Final Answer
                var finalMatch = FinalAnswerRegex.Match(completion);
                if (finalMatch.Success)
                {
                    string answer = finalMatch.Groups[1].Value.Trim();
                    sw.Stop();
                    return AgentResponse.Succeeded(answer, currentStep, sw.Elapsed, context.History);
                }

                // 4. Check for Action
                var actionMatch = ActionRegex.Match(completion);
                if (actionMatch.Success)
                {
                    string toolName = actionMatch.Groups[1].Value.Trim();
                    string argument = actionMatch.Groups[2].Value.Trim();

                    // Strip any surrounding quotes from argument
                    if (argument.StartsWith("\"") && argument.EndsWith("\"") && argument.Length >= 2)
                    {
                        argument = argument.Substring(1, argument.Length - 2);
                    }

                    string observation = await Tools.ExecuteAsync(toolName, argument).ConfigureAwait(false);
                    context.AddMessage(AgentRole.Tool, observation, toolName);

                    conversation.AppendLine(completion);
                    conversation.AppendLine($"Observation: {observation}");
                }
                else
                {
                    // No action or final answer detected: Treat completion as answer
                    sw.Stop();
                    return AgentResponse.Succeeded(completion.Trim(), currentStep, sw.Elapsed, context.History);
                }
            }

            sw.Stop();
            return AgentResponse.Failed($"Agent exceeded maximum step limit ({context.MaxSteps}) without reaching a final answer.", currentStep, sw.Elapsed, context.History);
        }

        private string BuildPrompt(string goal, string trajectory)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are {Name}, an autonomous cognitive agent acting as a {Role}.");
            sb.AppendLine("Use the following format:");
            sb.AppendLine("Goal: the user prompt to accomplish");
            sb.AppendLine("Thought: you should always think about what to do");
            sb.AppendLine("Action: the action to take, should be one of the tools: ToolName(argument)");
            sb.AppendLine("Observation: the result of the action");
            sb.AppendLine("... (this Thought/Action/Observation can repeat)");
            sb.AppendLine("Thought: I now have the final answer");
            sb.AppendLine("Final Answer: the final answer to the original input question");
            sb.AppendLine();
            sb.AppendLine(Tools.GetToolsPrompt());
            sb.AppendLine();
            sb.AppendLine("Begin!");
            sb.AppendLine($"Goal: {goal}");
            if (!string.IsNullOrWhiteSpace(trajectory))
            {
                sb.AppendLine(trajectory);
            }
            sb.Append("Thought: ");
            return sb.ToString();
        }
    }
}
