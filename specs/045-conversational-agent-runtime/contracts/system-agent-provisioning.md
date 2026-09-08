# Contract: System Agent Provisioning

**Feature**: 045-conversational-agent-runtime | **Layers**: `Application/Conversations/SystemAgents` → `Infrastructure/Conversations`

Fills the empty agent catalog with the orchestrator and its sub-agents, keeps them upgradable across releases, and makes them un-corruptible by users. Satisfies FR-033, FR-034, FR-035, FR-036, SC-011.

---

## 1. Definitions

`SystemAgentDefinitions` is a static, versioned artifact in `Application` — the §9 "prompts are versioned artifacts, reviewed like code" requirement applied to whole agents.

```csharp
public sealed record SystemAgentDefinition(
    string SystemKey,
    string Name,
    string Description,
    AgentType AgentType,
    AiCapability ModelCapability,
    AgentInstructions Instructions,
    IReadOnlyList<string> CapabilityKeys,
    AgentExecutionPolicy ExecutionPolicy)
{
    /// <summary>SHA-256 over every field above. Gates re-publishing (FR-035).</summary>
    public string ComputeHash();
}
```

| SystemKey | Name | Type | Model capability | Capability keys |
|---|---|---|---|---|
| `lucy.orchestrator` | Lucy | `Conversational` | `TurnOrchestration` | *(none — it delegates, it does not act)* |
| `lucy.site` | Site Analyst | `Task` | `Chat` | `resolve_location`, `resolve_site_boundary`, `adjust_viewer_focus` |
| `lucy.knowledge` | Knowledge Analyst | `Knowledge` | `Chat` | `search_knowledge_base`, `open_visual_panel` |
| `lucy.memory` | Memory Keeper | `Task` | `Chat` | `search_memory` |
| `lucy.viewer` | Viewer Control | `Task` | `Chat` | `adjust_viewer_focus`, `open_visual_panel` |

The orchestrator holding **no** capability keys is deliberate: it plans and narrates, and every action goes through a sub-agent. That makes "Lucy did it herself" structurally impossible and keeps the audit trail honest about which sub-agent acted.

Scoping is the safety property from the spec's Sub-Agent entity: `lucy.knowledge` holds no viewer capability, so it cannot move the map whatever its reasoning does.

MCP tools are **not** listed here. They reach a turn through `ConversationCapabilityCatalog` at run time and are attributed to the orchestrator's own delegation record, because the set of active MCP servers is per-user and cannot be baked into a platform-wide definition.

---

## 2. Provisioner

```csharp
public interface ISystemAgentProvisioner
{
    Task<SystemAgentProvisioningResult> ProvisionAsync(CancellationToken cancellationToken);
}

public sealed record SystemAgentProvisioningResult(int Created, int Upgraded, int Unchanged, bool Deferred);
```

Driven by `SystemAgentProvisioningHostedService` at startup, following the existing `ProviderHealthCheckHostedService` pattern.

### Algorithm

For each definition, in a single transaction per agent:

1. Find the `Agent` by `SystemKey`.
2. **Absent** → create it with `OwnerId = "system"`, `IsSystemOwned = true`, `ModelCapability` from the definition, `Status = Published`; publish version 1 with `DefinitionHash = ComputeHash()` and **null** model binding.
3. **Present** → compare `ComputeHash()` with the newest `AgentVersion.DefinitionHash`.
   - Equal → no write (`Unchanged`).
   - Different → publish version N+1 with the new hash and instructions. Version N and every `AgentExecution` referencing it are untouched (FR-035).
4. Never delete. A definition removed from a future release leaves its agent in place, archived by a deliberate follow-up change, never silently dropped.

### Idempotence and failure

- Restarting with no code change performs **zero** writes.
- Two instances starting concurrently are serialised by the unique filtered index on `SystemKey`; the loser catches the uniqueness violation, re-reads, and continues.
- A database that is unreachable or has **pending migrations** → log a warning, return `Deferred: true`, and do not throw. The host starts; the next startup retries. This matches how `DevAdminSeeder` and the other hosted services already degrade, and is required because this repository applies migrations out of band with a readiness health check rather than at startup.
- A `Deferred` result is surfaced on `/health/ready` as degraded, so the gap is visible rather than silent (constitution §2.VIII).

### Model resolution at run time

A version with null `ModelProviderId`/`ModelId` resolves through `AiCapabilityProviderResolver.ResolveAsync(agent.ModelCapability)` at invocation time. That resolver already falls back to the platform default with a logged warning when a capability is unassigned, so the agents work on a fresh deployment with no administrator action (SC-011), and follow the administrator's assignment the moment one is made.

---

## 3. Immutability to users

`IsSystemOwned == true` is rejected by every user-facing mutation path, each returning `403` Problem Details `system-agent-immutable`:

`UpdateAgentCommand`, `PublishAgentCommand`, `ArchiveAgentCommand`, `RestoreAgentCommand`, `DeleteAgentCommand`, and every agent-tool / knowledge-base / policy / memory-policy mutation scoped to an agent.

Enforcement lives in a shared guard called by each handler, not duplicated per handler, and is covered by one theory test enumerating every mutation command — so a future command cannot forget it.

**Reads stay open.** System agents appear in the agent list and detail views for every user, flagged `isSystemOwned: true` so the UI can badge them "Provisioned by Ask Lucy" and hide edit affordances (FR-034). Their executions appear in execution history exactly like any other agent's, which is what makes a turn inspectable (FR-038).

---

## 4. Testing contract

| Test | Asserts |
|---|---|
| Fresh database | All five agents created, each with one published version, null model binding (SC-011) |
| Second run, no change | Zero writes; `Unchanged = 5` |
| Definition changed | Exactly one new version for the changed agent; prior version and its executions intact (FR-035) |
| Pending migrations | `Deferred = true`, warning logged, no throw |
| Concurrent provisioners | One creates, the other observes; no duplicate `SystemKey` |
| Mutation guard (theory over every command) | `403 system-agent-immutable` for a system agent; unchanged behaviour for a user agent |
| Capability scoping | `lucy.knowledge` resolves no viewer capability; `lucy.site` resolves no memory capability |
