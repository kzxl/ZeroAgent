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
        public ContextBudgetManager BudgetManager { get; set; } = new ContextBudgetManager();
        public int MaxContextTokens { get; set; } = 4096;
        public Func<ReActLoopGuard> CreateLoopGuard { get; set; } = () => new ReActLoopGuard();

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
            var loopGuard = CreateLoopGuard();

            var conversation = new StringBuilder();

            while (currentStep < context.MaxSteps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentStep++;

                // 1. Budget enforcement on context history
                if (BudgetManager != null && MaxContextTokens > 0)
                {
                    BudgetManager.PruneContextToBudget(context, MaxContextTokens);
                }

                // 2. Build prompt
                string prompt = BuildPrompt(context, conversation.ToString());

                // 3. Query LLM
                string completion = await Llm.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
                context.AddMessage(AgentRole.Assistant, completion);

                // 4. Check for Final Answer
                if (ToolCallParser.TryParseFinalAnswer(completion, out string answer))
                {
                    sw.Stop();
                    return AgentResponse.Succeeded(answer, currentStep, sw.Elapsed, context.History);
                }

                // 5. Check for Action (supports ReAct syntax, JSON objects, and markdown codeblocks)
                if (ToolCallParser.TryParseToolCall(completion, out var toolCall))
                {
                    var guardStatus = loopGuard.EvaluateCall(toolCall.ToolName, toolCall.ArgumentsJson, out string guardFeedback);

                    if (guardStatus == LoopGuardStatus.CircuitBreak)
                    {
                        // Circuit breaker triggered: block duplicate execution and inject instruction
                        string observation = guardFeedback;
                        context.AddMessage(AgentRole.Tool, observation, toolCall.ToolName);
                        conversation.AppendLine(completion);
                        conversation.AppendLine($"Observation: {observation}");
                    }
                    else
                    {
                        var response = await Tools.ExecuteCallAsync(toolCall).ConfigureAwait(false);
                        string rawObservation;
                        if (response.Success && (response.Content == null || !response.Content.StartsWith("Tool execution failed with error:")))
                        {
                            rawObservation = response.Content ?? string.Empty;
                            if (guardStatus == LoopGuardStatus.WarnAndReflect && !string.IsNullOrEmpty(guardFeedback))
                            {
                                rawObservation += $"\n{guardFeedback}";
                            }
                        }
                        else
                        {
                            string err = (!response.Success && !string.IsNullOrEmpty(response.ErrorMessage))
                                ? response.ErrorMessage
                                : response.Content;
                            rawObservation = $"Error: {err}\n[SELF-CORRECTION DIRECTIVE] The tool execution encountered an error. Reflect on why the error occurred, inspect the argument format, or select an alternate tool.";
                        }

                        string observation = ObservationCompactor.Compact(toolCall.ToolName, rawObservation);
                        context.AddMessage(AgentRole.Tool, observation, toolCall.ToolName);

                        conversation.AppendLine(completion);
                        conversation.AppendLine($"Observation: {observation}");
                    }
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

        private string BuildPrompt(AgentContext context, string trajectory)
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

            // 1.1 Injected System Directives & Context Summaries
            if (context != null)
            {
                for (int i = 0; i < context.History.Count; i++)
                {
                    var msg = context.History[i];
                    if (msg.Role == AgentRole.System && !string.IsNullOrWhiteSpace(msg.Content))
                    {
                        optimizer.AddSystem(msg.Content, $"system_context_{i}");
                    }
                }
            }

            // 2. Static Tool Definitions
            optimizer.AddTools(Tools.GetToolsPrompt(), "react_tool_definitions");

            // 3. Prior Dialogue History
            if (context != null)
            {
                var historySb = new StringBuilder();
                for (int i = 0; i < context.History.Count; i++)
                {
                    var msg = context.History[i];
                    if (msg.Role == AgentRole.User && msg.Content != context.Goal)
                    {
                        historySb.AppendLine($"User: {msg.Content}");
                    }
                    else if (msg.Role == AgentRole.Assistant)
                    {
                        historySb.AppendLine($"Assistant: {msg.Content}");
                    }
                }
                if (historySb.Length > 0)
                {
                    optimizer.AddHistory(historySb.ToString().TrimEnd(), "prior_dialog_history");
                }
            }

            // 4. User Goal
            optimizer.AddUserQuery($"Begin!\nGoal: {context?.Goal ?? string.Empty}", "react_goal");

            // 5. Dynamic Trajectory
            if (!string.IsNullOrWhiteSpace(trajectory))
            {
                optimizer.AddHistory(trajectory, "react_trajectory");
            }

            var layout = optimizer.Optimize();
            return layout.FullPrompt + "\nThought: ";
        }
    }
}
