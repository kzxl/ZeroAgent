using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZeroAgent.Core.Context;
using ZeroAgent.Core.Tools;
using ZeroPrompt.Core.Caching;

namespace ZeroAgent.Core.Engine
{
    /// <summary>
    /// Autonomous cognitive agent implementing the Reasoning-Action (ReAct) paradigm.
    /// Interleaves thinking, external tool execution, observation analysis, and answer formulation.
    /// </summary>
    public sealed class ReActAgent
    {
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
                if (ToolCallParser.TryParseFinalAnswer(completion, out string answer))
                {
                    sw.Stop();
                    return AgentResponse.Succeeded(answer, currentStep, sw.Elapsed, context.History);
                }

                // 4. Check for Action (supports ReAct syntax, JSON objects, and markdown codeblocks)
                if (ToolCallParser.TryParseToolCall(completion, out var toolCall))
                {
                    var response = await Tools.ExecuteCallAsync(toolCall).ConfigureAwait(false);
                    string observation = response.Success ? response.Content : $"Error: {response.ErrorMessage}";
                    context.AddMessage(AgentRole.Tool, observation, toolCall.ToolName);

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
            var optimizer = new PromptLayoutOptimizer();

            // 1. Static System Instruction
            string systemInstruction = $"You are {Name}, an autonomous cognitive agent acting as a {Role}.\n" +
                "Use the following format:\n" +
                "Goal: the user prompt to accomplish\n" +
                "Thought: you should always think about what to do\n" +
                "Action: the action to take, should be one of the tools: ToolName(argument)\n" +
                "Observation: the result of the action\n" +
                "... (this Thought/Action/Observation can repeat)\n" +
                "Thought: I now have the final answer\n" +
                "Final Answer: the final answer to the original input question";

            optimizer.AddSystem(systemInstruction, "react_system_instruction");

            // 2. Static Tool Definitions
            optimizer.AddTools(Tools.GetToolsPrompt(), "react_tool_definitions");

            // 3. User Goal
            optimizer.AddUserQuery($"Begin!\nGoal: {goal}", "react_goal");

            // 4. Dynamic Trajectory
            if (!string.IsNullOrWhiteSpace(trajectory))
            {
                optimizer.AddHistory(trajectory, "react_trajectory");
            }

            var layout = optimizer.Optimize();
            return layout.FullPrompt + "\nThought: ";
        }
    }
}
