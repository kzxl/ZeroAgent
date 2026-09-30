# 🤖 ZeroAgent: Sovereign Pure C# Cognitive Agent & Multi-Agent Swarm Framework

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET Multi-Targeting](https://img.shields.io/badge/.NET-8.0%20%7C%204.6.2%20%7C%20Standard%202.0-purple.svg)](https://dotnet.microsoft.com/)
[![Zero External Dependencies](https://img.shields.io/badge/Dependencies-0%20(Pure%20C%23)-brightgreen.svg)]()
[![Performance](https://img.shields.io/badge/Latency-<5ms%20Reflex-orange.svg)]()
[![Concurrency](https://img.shields.io/badge/Concurrency-100%20Users%20%7C%202900%2B%20turns%2Fsec-success.svg)]()

**ZeroAgent** is an enterprise-grade, deterministic AI Agent and Multi-Agent Swarm framework engineered in **100% pure C#** for the .NET ecosystem. Operating in **Tier 5 (Presentation & Orchestration)** of the [ZeroPlatform](https://github.com/kzxl/ZeroPlatform) ecosystem, ZeroAgent provides autonomous ReAct execution loops, zero-reflection tool calling protocols, sub-5ms task-oriented dialogue tracking, 4-tier agentic memory, and CSP-based multi-agent coordination—completely independent of external heavy Python runtimes or cloud-locked SDKs.

---

## 🏛️ The 5 Pillars of ZeroAgent Architecture

ZeroAgent is engineered around 5 foundational architectural pillars designed for high throughput, sub-millisecond predictability, and verifiable safety:

```
                      ┌───────────────────────────────────────────────┐
                      │              ZeroAgent Core Engine            │
                      └──────────────────────┬────────────────────────┘
                                             │
             ┌───────────────────────────────┼───────────────────────────────┐
             │                               │                               │
             ▼                               ▼                               ▼
    ┌─────────────────┐             ┌─────────────────┐             ┌─────────────────┐
    │    Pillar 1:    │             │    Pillar 2:    │             │    Pillar 3:    │
    │ Zero-Reflection │             │ Two-Tier Bridge │             │ KV-Cache Layout │
    │  Tool Protocol  │             │ Reflex <-> ReAct│             │ Prefix Stability│
    └─────────────────┘             └─────────────────┘             └─────────────────┘
             │                               │
             ▼                               ▼
    ┌─────────────────┐             ┌─────────────────┐
    │    Pillar 4:    │             │    Pillar 5:    │
    │ Knapsack Budget │             │ Fluent Builder  │
    │ Context Packer  │             │ Type-Safe DSL   │
    └─────────────────┘             └─────────────────┘
```

### 1. Zero-Reflection Tool Protocol (`IAgentTool`)
- High-performance tool registration through strongly-typed delegates and explicit schema contracts (`JsonSchemaConstraint`).
- Zero reflection overhead during invocation, enabling sub-microsecond tool dispatch.
- Native Human-in-the-Loop (`HitlSafetyGate`) gating for destructive, safety-critical, or high-privilege actions with cryptographic audit logging.

### 2. Two-Tier Cognitive Escalation Bridge (`CognitiveEscalationBridge`)
- **Tier 1 (Reflex Fast Path)**: Pure C# CPU-based NLU and Dialogue State Tracking (DST). Handles slot filling, state machines, and routine operational inquiries in sub-5ms with **0 GPU / LLM cost**.
- **Tier 2 (Deliberative ReAct Engine)**: Activated automatically when analytical reasoning is demanded (*"tại sao"*, *"phân tích"*, *"đối chiếu"*) or when NLU confidence drops below threshold ($< 0.45$). Executes autonomous multi-step reasoning, tool observation cycles, and self-correction.

### 3. KV-Cache Friendly Prompt Layout (`PromptLayout`)
- Structurally partitions prompt templates into **Static Prefix** (System Role, Safety Directives, Tool Schemas) and **Dynamic Suffix** (Working Memory, Slots, Contextual turns).
- Guarantees maximum prefix KV-cache reuse on local inference engines (`ZeroInference` / vLLM / llama.cpp), cutting TTFT (Time-To-First-Token) by up to 70%.

### 4. Knapsack Context Budget Engine (`ContextBudgetManager`)
- Algorithmic token budgeting applying greedy/knapsack optimization to pack message histories, dynamic tool schemas, and episodic recollections into strict context windows.
- Prevents context overflow and token thrashing under prolonged multi-turn conversations.

### 5. Fluent Builder DSL (`ZeroAgentBuilder`)
- Unified, type-safe builder interface to declaratively compose Memory Engines, Cognitive Escalation Bridges, Safety Gates, Tools, and Model Backends.

---

## 🧠 4-Tier Agentic Memory System

ZeroAgent features a multi-tiered cognitive memory hierarchy that mimics human operational cognition:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        4-Tier Agentic Memory                           │
├───────────────────┬────────────────────────────────────────────────────┤
│ Tier              │ Description & Scope                                │
├───────────────────┼────────────────────────────────────────────────────┤
│ Working Memory    │ Active conversation turns, slot tracking, and      │
│                   │ deterministic Anaphora / Coreference Resolution    │
│                   │ (resolves "nó", "máy này" -> active equipment ID). │
├───────────────────┼────────────────────────────────────────────────────┤
│ Semantic Memory   │ SOPs, operational manuals, and domain guidelines   │
│                   │ vector-indexed via ZeroVector for instant recall.  │
├───────────────────┼────────────────────────────────────────────────────┤
│ Episodic Memory   │ Historical incidents, past failures, and verified  │
│                   │ resolutions stored as semantic episodes.           │
├───────────────────┼────────────────────────────────────────────────────┤
│ Response Cache    │ High-confidence (similarity >= 0.95) vector cache  │
│                   │ for idempotent queries, bypassing NLU/LLM cycles.  │
└───────────────────┴────────────────────────────────────────────────────┘
```

---

## 🛠️ Industrial Tool Suite & Partial Modularity

The framework is partitioned into modular, single-responsibility components and partial classes:

- **`DynamicDatabaseQueryTool`**:
  - `DynamicDatabaseQueryTool.cs`: Schema metadata catalog and unified query dispatch.
  - `DynamicDatabaseQueryTool.LiveSql.cs`: Direct live SQL Server pushdown with connection pooling and schema introspection.
  - `DynamicDatabaseQueryTool.DataFrame.cs`: In-memory tabular queries and vector search over `ZeroData.DataFrame`.
- **`TsdbQueryTool`**:
  - `TsdbQueryTool.cs`: Rolling telemetry metrics (avg, min, max, count, latest).
  - `TsdbQueryTool.Anomalies.cs`: Statistical Z-score outlier and anomaly detection.
  - `TsdbDataPoint.cs`: Dedicated time-series data model.
- **`DialogueStateTracker`**:
  - `DialogueStateTracker.cs`: Diacritic-tolerant intent recognition and neural classifier binding.
  - `DialogueStateTracker.Entities.cs`: Multi-slot extraction and FSM state transition engine.

---

## 📊 Stress & Concurrency Benchmarks

ZeroAgent has undergone rigorous stress testing under enterprise multi-tenant workloads:

```
================================================================================
CONCURRENT MULTI-CONTEXT STRESS BENCHMARK RESULTS
================================================================================
Concurrent Active Users:    100
Distinct Question Patterns: 20
Total Processed Turns:      300
Wall-Clock Execution Time:  103 ms
Throughput Rate:            ~2,912.62 turns/sec
Average Turn Latency:       0.34 ms
Cross-Talk / State Leaks:   0 (0.00%)
Test Suite Status:          58 / 58 Passed (100%)
================================================================================
```

### Highlights:
- **Zero Cross-Talk**: Complete context isolation across 100 simultaneous user sessions.
- **Sub-Millisecond Execution**: Core Reflex path executes in $< 1$ ms on standard multi-core CPUs.
- **Deterministic Slot-Filling**: Diacritic-tolerant NLU reliably extracts parameters regardless of Vietnamese accent variations (e.g., *"ap suat"*, *"áp suất"*, *"ap-suat"*).

---

## 🚀 Quick Start

### 1. Fluent Agent Construction (`ZeroAgentBuilder`)

```csharp
using ZeroAgent.Core.Builder;
using ZeroAgent.Tools.Data;
using ZeroAgent.Tools.Storage;

var agent = ZeroAgentBuilder.Create()
    .WithName("FactorySupervisor")
    .WithRole("Chief Autonomous Plant Dispatcher")
    .WithMemory(dimension: 128)
    .WithTokenBudget(maxTokens: 4096)
    .WithTool(new AgentTool("read_sensor", "Reads telemetry", "sensorId: string", (arg) => Task.FromResult("75.2 C")))
    .WithHitlSafetyGate(timeoutSeconds: 30)
    .Build();
```

### 2. Sub-5ms Task Dialogue Engine (`ZeroDialogEngine`)

```csharp
using ZeroAgent.Dialog.Engine;
using ZeroAgent.Dialog.Memory;

var engine = new ZeroDialogEngine();

// First turn: Inquire about a piece of equipment
var res1 = await engine.ChatAsync("session_user_01", "Kiểm tra nhiệt độ máy nén C-102");
Console.WriteLine(res1.Text);
// Output: "Nhiệt độ của thiết bị C-102 hiện tại là 78.4°C (Bình thường)."

// Second turn: Coreference resolution ("nó" -> "C-102")
var res2 = await engine.ChatAsync("session_user_01", "Áp suất của nó thế nào?");
Console.WriteLine(res2.Text);
// Output: "Áp suất hiện tại của C-102 là 6.2 bar."
```

### 3. Deliberative ReAct Escalation

```csharp
// Asking for deep analytical reasoning escalates from Reflex to ReAct Deliberation
var res3 = await engine.ChatAsync("session_user_01", "Tại sao áp suất C-102 tăng đột biến?");
Console.WriteLine(res3.Text);
// [Escalated to Tier 2 ReAct Engine]
// Output includes structured Thought -> Action (TSDB Anomaly Scan) -> Observation -> Analytical Explanation.
```

---

## 📦 Solution Architecture

```
ZeroAgent/
├── src/
│   ├── ZeroAgent.Core/          # ReAct loops, context managers, Knapsack budgeting, tools protocol
│   ├── ZeroAgent.Dialog/        # ZeroDialogEngine, DST (FSM), 4-Tier Memory, Anaphora resolution
│   └── ZeroAgent.Tools/         # DynamicDatabaseQueryTool, TsdbQueryTool, HitlSafetyGate
└── tests/
    └── ZeroAgent.Tests/         # 58 comprehensive unit, integration, DST, and 100-user stress tests
```

---

## 🌐 Multi-Target Support

| Target Framework | Status | Runtime Notes |
| :--- | :---: | :--- |
| **.NET 8.0+** | ✅ Active | Hardware intrinsics, `Span<T>`, modern async pipeline |
| **.NET Standard 2.0** | ✅ Active | Cross-platform integration (.NET Core 2.0+, Unity, Mono) |
| **.NET Framework 4.6.2** | ✅ Active | Legacy industrial SCADA, WinForms, and WPF compatibility |

---

## 📄 License

Architected and developed by **Phong Võ** (`kzxl`) for the **ZeroUniverse / ZeroPlatform** ecosystem. Released under the **MIT License**.
