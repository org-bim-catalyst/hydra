# Implementation Plan: Conversational Agent Runtime

**Branch**: `045-conversational-agent-runtime` | **Date**: 2026-09-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/045-conversational-agent-runtime/spec.md`

## Summary

Replace the fixed straight-line chat pipeline in `SendChatMessageCommandHandler` with a **request-scoped, streaming orchestrator** that plans each turn, delegates parts of it to system-provisioned sub-agents, narrates each beat as its own chat message, and closes with a bounded list of capability-grounded next actions the user can select.

The technical approach is **reuse, not rebuild**. Spec 020 already shipped an agent runtime with a tool abstraction, budget guard, duplicate-call detection, policy evaluation, audit logging and an `AgentExecution`/`AgentExecutionStep`/`AgentToolCall` record model — it has simply never run a conversation. This feature:

1. adds a thin **conversational capability** interface on top of `IAgentTool` (label, offer description, per-turn availability) so tools can describe themselves to a user, without touching the twelve existing implementers;
2. adds a **streaming** orchestrator alongside the existing Hangfire-backed one, sharing its guards and its record model;
3. **provisions** the Lucy orchestrator and four sub-agents as system-owned rows in the existing agent catalog, which is empty in production today;
4. extends the existing SSE + `ChatStreamChunk` contract with a suggested-actions payload, reusing the `StartsNewMessage`/`PendingLabel` beat mechanism already built for specs/044;
5. retires the parallel deterministic paths this replaces (concurrent location classification, keyword zoom detection, automatic boundary resolution).

The decision step reuses `AgentPlanner`'s proven JSON-decision-plus-corrective-retry idiom rather than introducing provider-native tool calling, keeping `IAIProvider` vendor-neutral.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript 5 + React 19 (frontend, `src/AskLucy.Web/ClientApp`)

**Primary Dependencies**: MediatR (CQRS + `IStreamRequestHandler`), FluentValidation, EF Core 10, Serilog, Hangfire (existing background agent runtime only — *not* used by this feature's turn path), MUI 7, TanStack Query, Zustand. Plus, for capability-index retrieval, the **already-referenced** `Microsoft.ML.OnnxRuntime` 1.20.1 + `Microsoft.ML.Tokenizers` behind the existing `IEmbeddingService` — no new package, and deliberately no new native binary (this repo has been broken once by colliding native DLLs).

**Deployment prerequisite**: `App_Data/embedding-models/{model.onnx,vocab.txt}` (~90 MB) must be present. `OnnxLocalEmbeddingProvider` has existed since specs/016 but the model file has never been deployed, so this feature is what finally activates it — and the local RAG embedding path with it.

**Storage**: SQL Server via EF Core code-first migrations. Reuses the existing `Agents`, `AgentVersions`, `AgentExecutions`, `AgentExecutionSteps`, `AgentToolCalls`, `AgentAuditLogs`, `Messages` and `UserChats` tables; adds nullable columns and one new preference table (see [data-model.md](./data-model.md))

**Testing**: xUnit v3 + NSubstitute + FluentAssertions (`tests/AskLucy.Application.Tests`, `.Web.Tests`, `.Persistence.Tests`); Vitest + Testing Library + axe (`ClientApp`)

**Target Platform**: ASP.NET Core web app on Windows/IIS (site4now shared host in production), single deployable serving the SPA

**Project Type**: Web application — Clean Architecture backend (`Domain`/`Application`/`Infrastructure`/`Persistence`/`Web`) plus a co-located React SPA

**Performance Goals**:
- First visible output (acknowledgement or first reply token) no later than today's first token — target p90 ≤ 2.5 s (SC-001)
- Decision step p90 ≤ 1.2 s on the model bound to the new `TurnOrchestration` capability
- Never more than 5 s of the turn without a visible indication naming current work (SC-004a)
- A "show me <place>" turn runs the full three-step flow with every step named as it starts and ends (SC-004). **Wall-clock time is not expected to improve over today** — the same work is done; the wait is narrated rather than silent. The earlier ≥30 s target was withdrawn in spec Revision 4 when the boundary became a flow step rather than an opt-in action (research.md D17).
- **Turn cost budget** (SC-008): median action turn ≤ 2 model calls (decide + narrate) plus one per sub-agent slice; median no-action turn exactly 1 — the same as today. Measured against a pre-feature baseline captured before implementation begins.

**Constraints**:
- Bounded per turn: max capability invocations, max wall-clock, max tokens/cost — all configurable, all enforced by the existing `AgentBudgetGuard`
- Existing viewer payloads (`ConfirmedLocationData`, `ConfirmedSiteBoundaryData`) are frozen contracts (FR-048)
- `Message` is append-only/immutable — the offer and the selection must be recorded without mutating a persisted message
- Shared production host: outbound HTTP needs generous timeouts (existing site4now finding), and long turns must keep the SSE connection alive through intermediary proxies
- Provider-neutral: no provider-native tool-calling API may be assumed (`IAIProvider` exposes chat/stream only)

**Scale/Scope**: ~5 new Application namespaces, ~6 capability classes, 1 EF migration, ~4 new React components, 5 provisioned system agents. Single-tenant-per-user isolation throughout; no multi-user shared turn state.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Gate | Verdict |
|---|-----------|------|---------|
| 2.I | Clean Architecture / Dependency Rule | Orchestrator, capability catalog and turn context live in `Application`, depending only on `Domain` + abstractions it owns. No EF Core, HttpClient or provider SDK reference. Geocoding/Overpass/panel work stays behind existing `Application` interfaces implemented in `Infrastructure`. | **PASS** (post-design re-check: PASS) |
| 2.II | SOLID | `IConversationCapability` is a new narrow interface, not three new members on the widely-implemented `IAgentTool` (ISP + OCP). A new capability is a new class + DI registration; the orchestrator never changes (FR-012). Each guard keeps its single reason to change. | **PASS** |
| 2.III | Simplicity / DRY / YAGNI | `AgentBudgetGuard`, `AgentDuplicateToolCallDetector`, `AgentPolicyEvaluator`, `AgentToolCatalog`, `AgentAuditLog` and the `AgentExecution` record model are reused, not reimplemented. Provider-native tool calling is deliberately *not* built (YAGNI — the JSON-decision idiom already works in `AgentPlanner`). | **PASS** |
| 2.IV | Composition over inheritance | `IConversationCapability : IAgentTool` is one level of interface refinement; capabilities compose existing Application services rather than subclassing them. | **PASS** |
| 2.V | DI & testability | Orchestrator, catalog, capabilities and the provisioner take constructor dependencies on interfaces; every one is unit-testable with fakes, no DB/network. | **PASS** |
| 2.VI | Separation of concerns | `AiController` gains only transport (one new SSE event); no orchestration logic. `MessageBubble`/`SuggestedActionCard` render, they do not decide availability. | **PASS** |
| 2.VII | Convention over configuration | Follows existing idioms: MediatR stream handler, `[LoggerMessage]` source-gen logging, `IOptions<>` for limits, `UserPanelPreference` as the template for the new per-user toggle, `AiCapability` for model policy. | **PASS** |
| 2.VIII | **No Silent Failures (non-negotiable)** | FR-039/040/041/042 are the spec's own restatement. Every orchestrator branch logs *and* yields user-visible text; capability failure is isolated per FR-040 using the `ResolveBoundarySafelyAsync` pattern already proven in specs/044. Frontend: the action-dispatch mutation has an explicit error path to a visible alert, never a bare `void promise`. | **PASS** — see [contracts/turn-stream.md](./contracts/turn-stream.md) failure matrix |
| §6 | API standards | New endpoints return Problem Details on failure; the SSE stream keeps its existing content-type and sentinel-prefix convention. | **PASS** |
| §8 | Security | Capability invocation runs through `AgentPolicyEvaluator` + `RequiredPermissions` (FR-015); sub-agents hold scoped capability sets so a knowledge sub-agent cannot reach viewer capabilities. Geocoder/Overpass output is embedded as *data* into templates and never re-fed to an LLM as instructions (existing specs/037 prompt-injection rule preserved). System agents are not user-writable. | **PASS** |
| §9 | AI principles | Model choice is a capability (`AiCapability.TurnOrchestration`), never a hardcoded vendor/model (FR-037). All orchestrator/sub-agent prompts are versioned constants in dedicated classes (FR-036), testable without a model call. Every invocation is bounded and audited (FR-015/FR-038). Streaming preserved throughout. Token/cost recorded per turn against the initiating user (FR-020). | **PASS** |
| §10 | Testing | Unit tests for orchestrator branches, capability availability, grounding filter, and the provisioner; Application integration tests for the full turn; `ClientApp` component + a11y tests for the action card; a replay test for FR-026/SC-009. | **PASS** |
| §14 | Observability | Turn records land in the existing `AgentExecution*` tables; every discarded suggestion logs its key and reason (FR-024). | **PASS** |
| §15 | Performance | Fast path (FR-006) keeps no-action turns at today's cost; boundary work removed from the critical path (FR-046) is the headline win; SSE keep-alive prevents proxy-buffered stalls. | **PASS** |

**No violations. Complexity Tracking section omitted.**

### Post-design re-evaluation (after Phase 1)

Re-checked against the artifacts actually produced. Still **PASS**, with four points worth recording because the design moved on them:

- **2.I Dependency Rule** — `SystemAgentProvisioner` lands in `Infrastructure` (it touches persistence and hosting) while `SystemAgentDefinitions` and `ISystemAgentProvisioner` stay in `Application`. The definitions are pure data + prompt text, so `Application` gains no outward dependency.
- **2.III Simplicity** — the design adds **one** migration and **zero** new aggregates. The turn record reuses `AgentExecution*` (research.md D8) and the offer lives on `Message` rather than in a child table (D6). Both were the DRY-vs-new-table call, and both went to reuse.
- **2.VIII No Silent Failures** — [contracts/turn-stream.md](./contracts/turn-stream.md) §7 enumerates all thirteen failure modes with their user-visible outcome. Two are worth flagging as deliberately *degraded, not failed*: a broken decision step still answers the user, and a dropped suggestion still completes the turn. Both log; neither is silent.
- **§9 AI principles** — the design introduces exactly one new `AiCapability` member and no hardcoded model. All three prompts are versioned classes under `Application/Conversations/Prompts`, and whole agent definitions are hashed and version-gated (contracts/system-agent-provisioning.md), which extends §9's "prompts are versioned artifacts" to agents themselves.

### Amendment 2026-09-08 — skill-style registry (research.md D13–D15)

Three decisions were added after reviewing Anthropic's Agent Skills authoring guidance, at the author's prompting. Constitution verdict is unchanged (**PASS**); the changes strengthen two gates:

- **§2.III Simplicity / §15 Performance** — the decide prompt now carries a compact Tier 1 index instead of every capability's full description plus JSON schema. Bounded per-turn prompt cost, and bounded again by embedding retrieval once MCP catalogues grow (D14).
- **§8 Security** — `InputSchemaJson` is now Tier 3 and reaches no model. The grounder's validation becomes a genuinely independent check rather than a formality, which is what SC-002's "zero ungrounded options" rests on.
- **SC-001** — the acknowledgement is templated from the capability, not returned by the decide call (D15), so the first thing the user sees is off the critical path and survives a failed decision step.

The interface gained one member the original design was missing outright: **`WhenToUse`**. The skill guidance requires a description to state both *what it does* and *when to use it*; the original had "what it does" and user-facing offer text, and nothing telling the deciding model when routing here is correct.

One risk carried into implementation rather than resolved here: **`SendChatMessageCommandHandler` currently mixes transport, retrieval, memory, location, zoom and boundary concerns in one 356-line method.** The orchestrator extraction must leave it a thin delegation, or the feature will have added a second orchestration layer on top of the first rather than replacing it. `/speckit-tasks` should sequence that extraction before the new beats are added, not after.

## Project Structure

### Documentation (this feature)

```text
specs/045-conversational-agent-runtime/
├── plan.md              # This file
├── research.md          # Phase 0 output — 12 decisions
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── conversation-capability.md
│   ├── turn-stream.md
│   ├── suggested-actions-api.md
│   └── system-agent-provisioning.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/AskLucy.Domain/
├── Agents/
│   ├── Agent.cs                                # + SystemKey, IsSystemOwned, ModelCapability
│   └── AgentVersion.cs                         # ModelProviderId/ModelId become nullable
├── Chats/
│   └── Message.cs                              # + SuggestedActionsJson, SelectedActionKey, SelectedActionArgumentsJson
└── Conversations/                               # NEW
    └── SuggestedAction.cs                      # value object

src/AskLucy.Application/
├── Conversations/                               # NEW — the conversational runtime
│   ├── Runtime/
│   │   ├── ConversationTurnOrchestrator.cs     # decide → acknowledge → act → report → offer
│   │   ├── IConversationTurnOrchestrator.cs
│   │   ├── TurnDecision.cs                     # parsed decision document
│   │   ├── TurnDecisionParser.cs               # JSON + one corrective retry (AgentPlanner idiom)
│   │   ├── SubAgentDelegator.cs                # runs one sub-agent's slice of the turn
│   │   ├── SuggestedActionGrounder.cs          # FR-024 discard-and-log filter
│   │   └── TurnRecorder.cs                     # writes AgentExecution/Step/ToolCall rows
│   ├── Capabilities/
│   │   ├── IConversationCapability.cs          # : IAgentTool + 3-tier skill-style disclosure
│   │   ├── ConversationCapabilityCatalog.cs    # availability filter + Tier 1 index builder
│   │   ├── CapabilityIndexRetriever.cs         # embedding narrowing for large catalogs (D14)
│   │   ├── TurnContext.cs
│   │   ├── ResolveLocationCapability.cs
│   │   ├── ResolveSiteBoundaryCapability.cs
│   │   ├── AdjustViewerFocusCapability.cs
│   │   ├── SearchKnowledgeBaseCapability.cs
│   │   ├── SearchMemoryCapability.cs
│   │   └── OpenVisualPanelCapability.cs
│   ├── Flows/                                   # spec Revision 4 — capability flows (D17)
│   │   ├── IConversationFlow.cs                # + FlowVariant, FlowStep, FlowStepContext
│   │   ├── ConversationFlowCatalog.cs          # flows appear in the Tier 1 index too
│   │   ├── FlowRunner.cs                       # dependency-ordered steps, N+1 narration
│   │   └── LocateAPlaceFlow.cs                 # resolve → focus → outline; focus/full variants
│   ├── Prompts/
│   │   ├── TurnDecisionPrompt.cs               # v1, versioned artifact (§9)
│   │   ├── TurnNarrationPrompt.cs              # v1
│   │   └── SuggestedActionPrompt.cs            # v1
│   └── SystemAgents/
│       ├── SystemAgentDefinitions.cs           # the 5 definitions + definition version
│       └── ISystemAgentProvisioner.cs
├── Ai/Commands/SendChatMessage/
│   ├── SendChatMessageCommand.cs               # + SelectedAction
│   ├── SendChatMessageCommandHandler.cs        # delegates to the orchestrator
│   └── ChatStreamChunk.cs                      # + SuggestedActions
├── Chats/Preferences/                           # NEW — mirrors Panels preference pattern
│   ├── Commands/SaveConversationPreference/
│   └── Queries/GetConversationPreference/
├── Locations/                                   # LocationResolutionService keeps geocoding,
│                                                # loses its own intent classifier (D11)
└── Options/
    └── ConversationRuntimeOptions.cs           # NEW — per-turn caps

src/AskLucy.Infrastructure/
└── Conversations/
    └── SystemAgentProvisioner.cs               # idempotent hosted-service-driven provisioning

src/AskLucy.Persistence/
├── Configurations/                              # Message + Agent + AgentVersion config updates
└── Migrations/                                  # ONE new migration

src/AskLucy.Web/
├── Controllers/v1/AiController.cs               # + __ACTIONS__ event, SSE keep-alive
├── Controllers/v1/ChatsController.cs            # + conversation preference endpoints
└── ClientApp/src/features/chat/
    ├── api/aiApi.ts                             # + 'actions' stream event, selectedAction field
    ├── components/SuggestedActionCard.tsx       # NEW — the option card
    ├── components/MessageBubble.tsx             # renders the card for the live offer
    ├── hooks/useChatStream.ts                   # carries actions through to messages
    └── pages/ChatPage.tsx                       # dispatch + voice-string assembly
```

**Structure Decision**: Web application layout, using the repository's existing Clean Architecture projects unchanged. The feature's new code concentrates in a single new `Application/Conversations` namespace so the conversational runtime has one nameable home, distinct from `Application/Agents` (the background agent runtime it borrows from) and from `Application/Ai` (the provider-facing chat path it plugs into).
