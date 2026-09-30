# 🤖 ZeroAgent: Sovereign Pure C# Cognitive Agent & Multi-Agent Swarm Framework

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET Multi-Targeting](https://img.shields.io/badge/.NET-8.0%20%7C%204.6.2%20%7C%20Standard%202.0-purple.svg)](https://dotnet.microsoft.com/)
[![Zero External Dependencies](https://img.shields.io/badge/Dependencies-0%20(Pure%20C%23)-brightgreen.svg)]()

**ZeroAgent** is an enterprise-grade, deterministic AI Agent framework engineered in 100% pure C# for .NET. It resides in **Tier 5 (Presentation & Orchestration)** of the [ZeroPlatform](https://github.com/kzxl/ZeroPlatform) ecosystem, providing autonomous ReAct execution loops, zero-reflection tool calling, semantic episodic memory via `ZeroVector`, and CSP-based multi-agent coordination.

---

## ⚡ Key Capabilities

- **Deterministic ReAct Execution Loop**:
  - Implements the Reasoning-Action cognitive cycle ($\text{Thought} \to \text{Action} \to \text{Observation} \to \text{Final Answer}$).
  - Built-in iteration limits, timeout cancellation, and tool error recovery.
- **Fast Tool Calling (`AgentToolRegistry`)**:
  - Strongly typed delegate registration for C# methods.
  - Sub-microsecond tool dispatch without heavy reflection overhead.
- **Episodic & Working Memory**:
  - Seamlessly integrates with **`ZeroVector`** (Tier 2) to store and recall long-term semantic embeddings in sub-millisecond time.
  - Integrates with **`ZeroTokenizer`** (Tier 3) to enforce strict context window limits.
- **Multi-Agent Swarm (`AgentSwarm`)**:
  - Peer-to-peer agent messaging via `ZeroConcurrency.ZeroChannel`.
  - Supervisor / Worker hierarchy with structured task handoffs.
- **Zero External Dependencies & Multi-Targeting**:
  - Pure C# implementation compatible with `.NET 8.0+`, `.NET Framework 4.6.2+`, and `.NET Standard 2.0`.

---

## 🚀 Quick Start

### 1. Register Tools and Run ReAct Agent

```csharp
using ZeroAgent.Core.Context;
using ZeroAgent.Core.Engine;
using ZeroAgent.Core.Tools;

// Register domain tools
var tools = new AgentToolRegistry();

tools.Register("GetMotorVibration", "Queries sensor vibration (mm/s) for a given motor ID.", (int motorId) =>
{
    return $"Motor #{motorId} vibration: 8.4 mm/s (Warning Threshold: 7.0 mm/s)";
});

tools.Register("TriggerShutdown", "Shuts down a machine node safely.", (int motorId) =>
{
    return $"Motor #{motorId} successfully shut down.";
});

// Configure Agent
var agent = new ReActAgent("Diagnostician", "Industrial maintenance specialist", tools);

// Execute goal
var context = new AgentContext("Analyze Motor #3 vibration and shut it down if it exceeds threshold.");
AgentResponse response = await agent.ExecuteAsync(context);

Console.WriteLine($"Agent Final Response: {response.Output}");
```

---

## 📄 License

Architected and developed by **Phong Võ** (`kzxl`). Released under the **MIT License**.
