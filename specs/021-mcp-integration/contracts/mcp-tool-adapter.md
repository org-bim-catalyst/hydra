# Contract: `McpToolAdapter` and the Dynamic Tool Registry

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decisions 1, 3, 4, 6) | **Extends**: `specs/020-ai-agent-framework/contracts/agent-tool-contract.md`

This contract extends spec 020's `IAgentTool`/`AgentToolCatalog` contract — it does not replace or duplicate it. Everything spec 020's `contracts/agent-tool-contract.md` documents (the five runtime-enforced steps: input validation, permission check, approval gate, output validation, duplicate-call detection) applies to `McpToolAdapter` unchanged.

## `IMcpToolRegistry`

```csharp
public interface IMcpToolRegistry
{
    IReadOnlyCollection<IAgentTool> ActiveTools { get; }   // synchronous, in-memory (research.md Decision 1)
    void Invalidate();                                     // called after activation/deactivation, capability refresh, server enable/disable/removal
}
```

`AgentToolCatalog` (spec 020, `src/AskLucy.Application/Agents/Tools/AgentToolCatalog.cs`) constructor changes from `(IEnumerable<IAgentTool> tools)` to `(IEnumerable<IAgentTool> nativeTools, IMcpToolRegistry mcpToolRegistry)`, and merges `nativeTools` with `mcpToolRegistry.ActiveTools` into the same `_toolsByName` dictionary. `Find`/`All` are otherwise unchanged — no caller (`AgentExecutionOrchestrator`, `AgentPlanner`) needs any modification beyond this one constructor signature.

**Invariant**: an `IAgentTool.Name` collision between a native tool and an MCP tool is structurally impossible — native tool names are plain identifiers (`FileReadTool`, `KnowledgeSearchTool`, ...) and every MCP tool's name is namespaced `mcp:{serverId}:{toolName}` (research.md Decision 3), so the two sets are disjoint by construction; `AgentToolCatalog`'s dictionary build does not need a collision-handling branch.

## `McpToolAdapter : IAgentTool`

```csharp
public sealed class McpToolAdapter(McpTool tool, IMcpClientFactory clientFactory, IMcpRateLimiter rateLimiter, IJsonSchemaValidator schemaValidator) : IAgentTool
{
    public string Name => tool.NamespacedName;
    public string Description => tool.Description;
    public AgentToolRiskLevel RiskLevel => tool.EffectiveRiskLevel;                 // never ServerDeclaredRiskLevel directly
    public IReadOnlyList<AgentToolPermission> RequiredPermissions => /* deserialized from tool.RequiredPermissionsJson */;
    public string InputSchemaJson => tool.InputSchemaJson;
    public string OutputSchemaJson => tool.OutputSchemaJson;

    public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken)
    {
        // 1. rateLimiter.AcquireAsync((tool.McpServerId, tool.NamespacedName, context.UserId, context.AgentId), ct) — FR-052/FR-053
        // 2. client = await clientFactory.GetOrCreateAsync(tool.McpServerId, ct) — reuses a pooled connection where the transport allows (FR performance: "avoid reconnecting for every tool invocation")
        // 3. result = await client.CallToolAsync(tool.ToolName, input, timeout: McpRuntimeOptions.MaxCallDurationSeconds, ct) — FR-051
        // 4. schemaValidator.Validate(tool.OutputSchemaJson, result) — a second output check on top of the Agent Runtime's own (defense in depth for the untrusted external response, FR-026)
        // 5. return AgentToolResult.Success(...) / .Failure(...) — never throws for an ordinary MCP-side failure (FR-032); only a programming-error-class exception propagates
    }
}
```

**What the Agent Runtime's existing pipeline already covers, unchanged** (spec 020's `contracts/agent-tool-contract.md`): input-schema validation before `ExecuteAsync` is called at all, permission checking against `RequiredPermissions`, the `High`/`Critical` approval gate (via `AgentPolicy`/`AgentApproval` — matched against `tool.NamespacedName`, research.md Decision 3), output-schema validation after the call returns, and duplicate-call detection.

**What `McpToolAdapter` adds on top** (MCP-specific, not present for native tools): the rate/concurrency limiter (step 1 — native tools have no external system to protect), connection acquisition through `IMcpClientFactory` (step 2 — native tools call in-process services directly), and a defense-in-depth output re-check (step 4 — native tools trust their own hand-written serialization; an MCP tool's output crosses a genuine trust boundary).

## `McpResourceReadTool : IAgentTool` (built-in, singular)

One adapter class handles every MCP resource, not one class per resource (unlike `McpToolAdapter`, which is instantiated once per discovered tool) — its `ExecuteAsync` takes a `resourceUri` input parameter and dispatches to `IMcpClient.ReadResourceAsync`. `RiskLevel` is fixed `Low` (read-only by MCP protocol definition); `RequiredPermissions` is `[ReadExternalData]`. This is how FR-037 ("an agent MUST be able to retrieve the content of an MCP resource... subject to the same permission and approval rules that govern MCP tool calls") is satisfied without a second, resource-specific runtime path — a resource fetch *is* a tool call, from the orchestrator's point of view.

## Untrusted-content framing (FR-030/FR-035)

`McpToolAdapter`'s `AgentToolResult.Output` is passed back into the orchestrator exactly like any other tool's output — through the same `RetrievalPromptFraming`-style structural separation spec 020's research.md (§8 Constitution Check row) already established for RAG/tool content (wrapped as clearly-delimited data, never concatenated into a position a model would read as an instruction). No MCP-specific prompt-framing code is introduced; this is the existing mechanism applied to a new content source, per FR-030's "never elevated to instructions."
