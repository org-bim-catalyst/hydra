# Phase 0 Research: Conversational Agent Runtime

**Feature**: 045-conversational-agent-runtime | **Date**: 2026-09-08

All Technical Context unknowns are resolved below. No `NEEDS CLARIFICATION` remains.

---

## Decision 1 — A second, streaming orchestrator; not a reuse of `AgentExecutionOrchestrator`

**Decision**: Add `ConversationTurnOrchestrator` in `Application/Conversations/Runtime`, request-scoped and streaming, invoked from `SendChatMessageCommandHandler`. It **shares** `AgentBudgetGuard`, `AgentDuplicateToolCallDetector`, `AgentPolicyEvaluator`, `AgentToolCatalog`, `IAgentAuditLogRepository` and the `AgentExecution*` record model with the existing runtime, but is a separate orchestration type.

**Rationale**: `AgentExecutionOrchestrator` is structurally wrong for a chat turn in three ways that cannot be configured away:
- It runs inside a Hangfire job with no HTTP context and deliberately avoids `ICurrentUserAccessor`; a chat turn is a request with a live user.
- It is non-streaming — it completes a plan then writes messages. FR-003/FR-004/FR-005a require output *during* the work.
- Its pause-for-approval semantics (`WaitingForApproval` + re-enqueue) suspend an execution across processes. A streaming turn cannot suspend; per spec Assumptions, a capability needing approval is surfaced as an action the user confirms *in the conversation*.

Sharing the guards and record model keeps DRY where the logic is genuinely identical (§2.III: a rule expressed once), while keeping two orchestration shapes that genuinely differ.

**Alternatives considered**:
- *Extend `AgentExecutionOrchestrator` with a streaming mode* — rejected: it would fork nearly every method on a mode flag, violating SRP and putting the background runtime's tested paths at risk.
- *Build a wholly independent runtime* — rejected: it would re-implement four guards and an audit model that already exist and are tested.

---

## Decision 2 — `IConversationCapability : IAgentTool`, a new narrow interface

**Decision**: Introduce

```csharp
public interface IConversationCapability : IAgentTool
{
    string Label { get; }             // user-facing, short — "Highlight the site boundary"
    string OfferDescription { get; }  // one line, shown under the label; never spoken
    bool IsAvailable(TurnContext context);
}
```

`ConversationCapabilityCatalog` filters `AgentToolCatalog.All` down to implementers that return `true` for the current `TurnContext`.

**Rationale**: `IAgentTool` has twelve-plus implementers (`KnowledgeSearchTool`, `MemoryWriteTool`, `SiteBoundaryResolverTool`, `McpToolAdapter`, `FakeHighRiskTool`, …). Adding three members to it breaks every one and forces meaningless labels onto tools that are not conversational. A separate opt-in interface satisfies ISP and OCP: a tool becomes conversational by implementing one more interface, and the orchestrator changes not at all (FR-012).

MCP tools reach conversation through `McpToolAdapter` implementing `IConversationCapability`, deriving `Label` from the server-declared tool title and `IsAvailable` from "the server is currently active" — so FR-015's "no relaxation for being reached from chat" holds for third-party tools too.

**Alternatives considered**:
- *Widen `IAgentTool`* — rejected: breaks all implementers, ISP violation.
- *A side-table of metadata keyed by tool name* — rejected: metadata drifts from the tool it describes; availability is behaviour, not data.

---

## Decision 3 — Structured JSON decision, not provider-native tool calling

**Decision**: The decide step sends one non-streaming completion asking for a JSON decision document, parsed with one corrective retry — the exact idiom `AgentPlanner.CreatePlanAsync` already uses. `GenerationParametersDto(JsonMode: true)` is passed when the model reports support. The prompt carries the **Tier 1 capability index** only (D13), never full input schemas.

**Amended 2026-09-08**: the decision document no longer carries an `acknowledgement` string. The acknowledgement is templated from the selected capability's own wording (D15), so nothing the user sees first depends on a model call completing.

**Rationale**: `IAIProvider` exposes `ChatAsync`/`StreamChatAsync` only; it has no tool/function-calling surface. Adding one would mean designing a vendor-neutral tool-call abstraction and implementing it across OpenAI, Anthropic, Gemini and OpenRouter — a large change to the provider layer that this feature does not need (§2.III YAGNI). The JSON idiom is already proven in this codebase and keeps `IAIProvider` vendor-neutral (§9 provider abstraction).

**Alternatives considered**:
- *Provider-native tool calling* — deferred, not rejected on merit; recorded as the natural follow-up once a second consumer needs it.
- *Keyword/heuristic routing* — rejected: it is exactly the deterministic behaviour this feature exists to remove.

---

## Decision 4 — `AiCapability.TurnOrchestration` for the decide step; sub-agents use the user's chat model

**Decision**: Add one enum member, `TurnOrchestration`, resolved through the existing `AiCapabilityProviderResolver`. Sub-agent reasoning and all user-facing narration use the provider/model the user selected for the conversation.

**Rationale**: §9 forbids hardcoding a model and requires asking for a capability. The decide step is short, structured and latency-critical — an administrator will want it on a fast, cheap model, exactly as they already do for `LocationIntent`. Narration is user-facing prose and must sound like the model the user chose, so it stays on their selection.

`AiCapabilityProviderResolver` already falls back to the platform default with a logged warning when a capability is unassigned, so the new member needs no seeding to work.

**Alternatives considered**:
- *Reuse `AiCapability.LocationIntent`* — rejected: the decision step supersedes location classification (D11), so reusing its assignment would silently repurpose an administrator's setting.
- *A hardcoded small model* — rejected outright by §9.

---

## Decision 5 — Beats reuse `StartsNewMessage`/`PendingLabel`; add `__ACTIONS__` and an SSE keep-alive

**Decision**:
- Each beat is an existing `StartsNewMessage: true` chunk carrying a `PendingLabel` naming the work, exactly as specs/044 already does for the boundary step. No new beat mechanism.
- One new field, `SuggestedActions`, on `ChatStreamChunk`; one new SSE sentinel, `__ACTIONS__`, following the established distinguishable-prefix convention.
- `AiController` writes a `: keep-alive` SSE comment every 10 s while a beat is pending.

**Rationale**: specs/044 built beat separation for exactly this reason and its ordering guarantees are hard-won (FR-001a/FR-001b). Reusing it means the boundary flow FR-046a describes is the mechanism already in production, just driven by the orchestrator instead of a hardcoded branch. The keep-alive addresses SC-004a's real failure mode on the shared production host: a proxy buffering or dropping an idle SSE connection during a 45 s boundary lookup would make the progress indication disappear even though the server is working.

**Alternatives considered**:
- *A generic `__PROGRESS__` event with periodic label updates* — rejected as unnecessary: `PendingLabel` already names the work, and a static, accurate label satisfies FR-005/SC-004a. Revisit only if a capability gains genuine sub-steps worth reporting.
- *WebSocket/SignalR for the turn* — rejected: the chat transport is SSE and changing it is out of scope.

---

## Decision 6 — The offer is stored on the assistant message; the selection on the next user message

**Decision**: `Message` gains three nullable columns: `SuggestedActionsJson` (set on the assistant message that made the offer) and `SelectedActionKey` + `SelectedActionArgumentsJson` (set on the *user* message that a selection creates).

**Rationale**: `Message` is documented as immutable and append-only — "no rename, no edit". Recording a selection by updating the assistant message would break that invariant. Writing it onto the user message the selection produces preserves append-only, keeps the transcript strictly chronological, and makes FR-026/SC-009 replay a straight read with no join.

**Alternatives considered**:
- *A separate `SuggestedActionOffer` table* — rejected: an offer has exactly one owning message and is never queried independently; a child table adds a join and a lifecycle for no gain (§5 aggregate rules).
- *Mutating the assistant message on selection* — rejected: breaks the aggregate's stated immutability.

---

## Decision 7 — Selection re-enters the existing stream endpoint

**Decision**: `SendChatMessageCommand`/`SendChatMessageRequest` gain an optional `SelectedAction { OfferedByMessageId, ActionKey, ArgumentsJson }`. When present, the orchestrator skips the decide step and goes straight to that capability, after re-checking availability (FR-028).

**Rationale**: A separate dispatch endpoint would have to duplicate SSE streaming, assistant-message persistence, usage recording, voice output and the viewer payload path. Re-entering the one streaming endpoint means a selection is just a turn whose decision was made by the user, and every downstream guarantee applies unchanged.

`OfferedByMessageId` is what makes FR-029 enforceable: a selection naming anything other than the newest assistant message with an offer is refused as stale.

**Alternatives considered**:
- *`POST /chats/{id}/actions/{key}`* — rejected: duplicates the streaming pipeline.
- *Sending the label as a normal user message* — rejected: that is exactly today's "reply 1" failure mode (US3 rationale).

---

## Decision 8 — The turn record is an `AgentExecution`

**Decision**: A turn that invokes at least one capability creates an `AgentExecution` row (`AgentId` = the Lucy orchestrator, `UserChatId` = the conversation, `Objective` = the user's message), with one `AgentExecutionStep` per beat, one `AgentToolCall` per capability invocation, `AgentExecutionError` rows for failures, and `Usage`/`Cost` populated at the end. Fast-path turns create no row.

**Rationale**: This satisfies FR-038 and FR-020 with tables, repositories, a query API and an admin UI that already exist — `AgentExecution` already carries `UserChatId` precisely because spec 020 anticipated conversation-linked executions. Inventing a parallel `ConversationTurn` table would duplicate all of it and split "what did an agent do" across two places.

**Alternatives considered**:
- *A new `ConversationTurn` aggregate* — rejected on DRY grounds above.
- *Log-only records* — rejected: §14 requires business events to be dashboardable, not discoverable only via log search, and FR-038 requires retrieval.

---

## Decision 9 — System agents: `SystemKey` + nullable model binding + an idempotent provisioner

**Decision**:
- `Agent` gains `SystemKey` (nullable, unique when non-null), `IsSystemOwned` (bool) and `ModelCapability` (nullable `AiCapability`). `OwnerId` for system agents is the sentinel `"system"`.
- `AgentVersion.ModelProviderId`/`ModelId` become **nullable**; null means "resolve at run time from the owning agent's `ModelCapability`".
- `SystemAgentProvisioner` runs as a hosted service at startup, upserting the five definitions from `SystemAgentDefinitions` by `SystemKey`, publishing a new `AgentVersion` only when the definition's content hash differs from the newest published version.
- Existing agent mutation commands reject any agent with `IsSystemOwned == true`.

**Rationale**: The provisioner cannot bind a concrete provider/model at startup — the AI provider catalog is administrator-configured and may legitimately be empty on a fresh deployment, and pinning whatever happened to be default at provision time would record a fact that immediately becomes false when the administrator reassigns the capability. Nullable model columns make "resolved by capability" an honest, first-class state instead of a stale snapshot. Content-hash-gated publishing gives FR-035 its upgrade semantics while preserving history (`AgentVersion` is append-only) and keeping restarts idempotent.

There is no production seeder in this repository today (`DevAdminSeeder` is development-only) and migrations are applied out of band with a readiness health check, so the provisioner must tolerate a not-yet-migrated database: it logs a warning and retries on the next startup rather than crashing the host — the same degradation the existing dev seed and hosted services already use.

**Alternatives considered**:
- *Keep the model columns required and bind at provision time* — rejected: records a value that silently goes stale, and fails outright on an environment with no provider configured.
- *Keep system agent definitions out of the database entirely* — rejected: FR-033/FR-034 require them visible in the catalog, and the user's own diagnosis ("the agents table is empty") is precisely the gap being closed.
- *EF Core `HasData` seeding in the migration* — rejected: `HasData` cannot express content-hash-gated re-publishing across releases, and requires literal Guids in migrations.

---

## Decision 10 — Grounding is a server-side filter, never a prompt promise

**Decision**: `SuggestedActionGrounder` takes the model's proposed actions and the turn's available capability set, and drops any action whose key is not in that set or whose arguments fail the capability's `InputSchemaJson`. Every discard is logged with the proposed key and reason. The prompt also constrains the model, but the filter is what the guarantee rests on.

**Rationale**: SC-002 demands *zero* ungrounded options across a benchmark. A prompt instruction cannot deliver zero — the "360° images" case in the spec is precisely a model inventing a plausible capability. A deterministic post-filter can, and it degrades safely: worst case the offer is shorter than intended, never wrong.

**Alternatives considered**:
- *Trust the prompt* — rejected: cannot meet SC-002.
- *Repair invalid actions by re-prompting* — rejected: adds a round trip at the latency-sensitive end of the turn for an option the user may not want anyway.

---

## Decision 11 — The decide step replaces the location intent classifier

**Decision**: `LocationResolutionService` keeps geocoding, confidence scoring and outcome shaping, and **loses** its own `LocationIntentClassificationPromptV1` model call. Location intent becomes one possible outcome of the turn decision. `ViewerZoomDetector` is deleted; zoom becomes `AdjustViewerFocusCapability`. `LocationConfirmationTemplates` survives as FR-008 fallback wording only.

**Rationale**: Without this, a location turn would make two classification calls — the orchestrator's and the location service's — for the same question. Folding them makes the feature roughly cost-neutral on the location turns that dominate this product's usage, and it removes the specs/037 FR-008 race that is the root cause of the flat narration (the classifier's verdict arriving after the reply was already written).

**Cost impact, stated honestly**: a plain conversational turn that today makes one model call will make two (decide + reply). The decide call is short (≈200 input tokens, ≈40 output) on a model the administrator chooses for speed and price. Location turns stay at two calls, as today. FR-020 and SC-008 bound the result.

**Alternatives considered**:
- *Keep both classifiers* — rejected: two model calls answering the same question, and the specs/037 race survives.
- *Skip the decide call when the turn context has no capabilities* — kept as a genuine optimisation but not the primary path, since `ResolveLocationCapability` is available in essentially every conversation.

---

## Decision 12 — Sub-agent delegation is bounded, in-process and sequential-with-declared-dependencies

**Decision**: The decision document may name up to `MaxDelegationsPerTurn` (default 3) sub-agent slices, each with an optional `dependsOn` index. `SubAgentDelegator` runs independent slices concurrently and dependent ones in order, passing the prior slice's result forward (FR-017). Each sub-agent is one model call scoped to its own capability subset. Failure of one slice is caught, recorded and reported without failing the others (FR-018), using the isolation pattern `ResolveBoundarySafelyAsync` established in specs/044.

**Rationale**: A bounded fan-out with declared dependencies covers the spec's motivating case ("find the site, then answer from our standards") without a general planning graph. `AgentPlanner` already models exactly this shape (`dependsOnStepIndex`), so the decision document reuses its vocabulary. Sequential-when-dependent keeps FR-017 trivially correct; concurrent-when-independent keeps SC-005's single-turn promise affordable.

**Concurrency constraint**: each concurrent slice must resolve its own `IServiceScope` rather than sharing the request's scoped `DbContext` — this repository has already been bitten by exactly that bug (an un-awaited location task and a reply stream sharing one scoped `DbContext` produced hard 500s on DB-credential providers). `ScopeIsolatedLocationResolutionService` is the existing precedent and the pattern to follow.

**Alternatives considered**:
- *Unbounded recursive delegation* — rejected by FR-002.
- *Strictly sequential delegation* — rejected: doubles latency on the independent-slice case for no correctness gain.
- *Sharing the request scope across concurrent slices* — rejected; it is a known production failure in this codebase.

---

## Decision 13 — Capabilities are a skill-style registry with progressive disclosure

**Decision**: Model the capability registry on Anthropic's Agent Skills architecture. Three tiers, each with a different audience:

| Tier | Audience | Loaded | Content |
|---|---|---|---|
| **1 — Index** | the deciding model | every turn | `key`, **what it does**, **when to use it**, argument hint. ~25 tokens per capability. |
| **2 — Guidance** | the sub-agent that runs it, and the narration step | only for a selected capability | Prose: how to use it well, what its outputs mean, how to report the result, what its failure modes look like. |
| **3 — Contract** | server-side only, never any model | never | `InputSchemaJson`, `RequiredPermissions`, `RiskLevel`, `OutputSchemaJson`. |

Tier 1 is the skill frontmatter; Tier 2 is the skill body; Tier 3 has no skill equivalent, because a skill has no privileged validator standing behind it — ours does.

**Rationale**: as originally specced, the decide prompt carried every available capability's full description *and* its JSON input schema on every turn. At seven capabilities that is ~400 tokens; once MCP servers are connected — one server can publish dozens of tools — it grows without bound. That is precisely the per-turn cost this feature was asked to contain. Anthropic's published guidance is explicit that only `name` + `description` are preloaded and the body is read on demand, and that "the context window is a public good".

**The rule that changed the interface**: the guidance requires a description to state **both what the capability does and when to use it**, in third person, with concrete trigger terms — *"Extract text and tables from PDF files… Use when working with PDF files or when the user mentions PDFs, forms, or document extraction."* The original design had `Description` (what it does, inherited from `IAgentTool`) and `OfferDescription` (user-facing offer text), and **nothing carrying trigger context for the deciding model**. That is a real gap: the model was being asked to route without being told when routing here is correct. `WhenToUse` is added as a first-class member for exactly that.

Three audiences, three strings — a decomposition the original two-string design conflated:

- `Description` + `WhenToUse` → the model choosing (Tier 1)
- `Label` + `OfferDescription` → the user reading an offer
- `InputSchemaJson` → the validator (Tier 3)

**Also adopted from the guidance**:

- **Degrees of freedom matched to fragility.** Tier 2 guidance for a fragile, expensive capability (`resolve_site_boundary`: one Overpass pass, then one vision cross-check, in that order) is prescriptive; for an open-ended one (`search_knowledge_base`) it is heuristic. The guidance's "narrow bridge versus open field" distinction.
- **Provide a default, do not enumerate options.** Reinforces the cap of four suggested actions (FR-023).
- **Evaluation-driven authoring.** Every capability ships at least three evaluation scenarios written *before* its Tier 2 guidance, so the guidance answers observed failures rather than imagined ones.
- **Fully-qualified MCP names.** Already satisfied — this codebase namespaces every MCP tool `mcp:{serverId}:{toolName}`.

**Alternatives considered**:

- *Flat list with full schemas each turn* — the original design; rejected on unbounded prompt growth and the missing "when to use it".
- *A literal SKILL.md filesystem per capability* — rejected: capabilities are C# classes with executable behaviour and compile-time DI registration, not user-authored folders. The tiering transfers; the filesystem does not.

---

## Decision 14 — Embedding-based Tier 1 retrieval for large catalogs

**Decision**: When the available-capability count exceeds `IndexRetrievalThreshold` (default 10), `ConversationCapabilityCatalog` embeds the user's message with the in-process ONNX MiniLM model and returns only the top-N (default 8) most similar Tier 1 entries, **plus every context-gated capability that is currently available**. Below the threshold, all entries are listed.

**Rationale**: Tier 1 keeps the per-capability cost low but not zero. A user with three MCP servers connected could reach 60+ capabilities — ~1,500 tokens of index on every turn. Embedding retrieval bounds that at a constant regardless of catalogue size, which is the standard answer to large tool catalogues.

The retriever **never decides anything**. It only chooses which Tier 1 lines the model is shown; the model still selects. A retrieval miss costs a missed capability, never a wrong action — and always including the context-gated entries means the capabilities that matter most in the current turn state (a boundary, when a location is active) cannot be retrieved away.

`OnnxLocalEmbeddingProvider` and its `Microsoft.ML.OnnxRuntime` 1.20.1 / `Microsoft.ML.Tokenizers` dependencies already exist in `Infrastructure`, alongside two other in-process model precedents on the same host (Whisper.net for transcription, Tesseract for OCR). **No new native dependency is introduced** — which matters, because this repository has already been broken once by two packages shipping colliding native binaries.

**Deployment prerequisite, and a side benefit**: `App_Data/embedding-models/` does not currently exist on disk, so the local embedding provider is coded but not deployed. This feature requires deploying the model (`model.onnx` + `vocab.txt`, ~90 MB). Doing so also activates the local embedding path for RAG, dormant since specs/016 for the same missing-file reason.

**Alternatives considered**:

- *Keyword pre-filter over Tier 1* — rejected: brittle in exactly the paraphrase-heavy and multilingual cases where retrieval matters.
- *Ask the model to page through the catalogue* — rejected: extra round trips at the latency-critical start of the turn.
- *A generative SLM in-process for the whole decide step* — rejected on measurement grounds. On shared myasp.net vCPUs with no GPU, a 0.5B INT4 model producing a ~60-token JSON decision from a ~300-token prompt is plausibly 3–10 s against a 1.2 s budget, and would make the acknowledgement slower than today's first token (SC-001). Memory is the second problem: ~400–600 MB on top of the ~150 MB Whisper model already resident, against a shared app-pool cap. An **encoder** model at ~90 MB and ~10 ms is the right size of tool for routing; a **decoder** model is not. Recorded here so the option is re-evaluated on evidence if the host changes, not re-litigated from intuition.

---

## Decision 15 — The acknowledgement is templated, not generated

**Decision**: The acknowledgement beat is composed from the selected capability's own wording (`AcknowledgementTemplate` — e.g. "OK, let me find it first." for `resolve_location`), not returned by the decide call. When no capability is selected, no acknowledgement is emitted (FR-006's fast path).

**Rationale**: the acknowledgement is the **first thing the user sees**, and SC-001 requires it no later than today's first token. Making it a model output puts it behind a network round trip and a JSON parse, and makes it the one part of the turn that a decision-step failure would silence. Templating removes it from the critical path entirely: it is emitted the instant routing resolves, costs nothing, is phrased consistently, and can be localised properly rather than depending on the model's language handling.

The trade is less phrasing variety turn to turn. Acceptable — this is a five-word status line, not the substance of the reply. FR-007's requirement that narration reflect the *real outcome* applies to the result beat, which remains model-written.

**Alternatives considered**:

- *Generated by the decide call* — the original design; rejected on the critical-path and failure-mode grounds above.
- *Generated by the main chat model in parallel* — rejected: a second concurrent call to say five words, and it would still need the routing verdict it is racing.

---

## Decision 16 — Offerable is a separate, stricter test than available

**Decision**: Split the single availability predicate in two. `IsAvailable(context)` answers "could this run" and gates the Tier 1 index and invocation. `IsOfferable(context, justCompleted)` answers "would this plausibly be wanted next" and gates the offer alone, defaulting to `IsAvailable` where the two genuinely coincide. The offer step is skipped entirely — no model call, no event — when nothing is offerable, when the turn took the fast path, when the user just declined, or when the same options were offered and ignored last turn (FR-025a).

**Rationale**: the original design gated the offer on availability alone, and stated in FR-025 that no list appears when nothing is "available or relevant" — but nothing defined or enforced relevance. Combined with `resolve_location` being specced "available: always", `AvailableFor(context)` could never return empty, so **the suppression rule could never fire and an offer would have appeared on essentially every turn**, including plain conversational ones. The spec said conditional; the mechanism said mandatory.

Availability and offerability are genuinely different questions, and conflating them produces a product that nags:

| | Available | Offerable |
|---|---|---|
| `resolve_location` | always | never — users name the place they want; suggesting "find a place" unprompted is noise |
| `adjust_viewer_focus` | whenever a location is active | never — asked for directly; a row nobody picks |
| `search_memory` | whenever memory is on | never — an internal lookup, not a user-facing choice |
| `resolve_site_boundary` | whenever a location is active and unoutlined | only in the turn that confirmed that location |

Three of the seven registered capabilities are never offered at all. That asymmetry is the design working, not a gap in it: an offer that lists everything possible is a menu, not a suggestion, and a menu shown after every message is wallpaper the user learns to ignore.

**Three suppression rules carry most of the weight.** No offer after a fast-path turn — answering a question is not a reason to ask what to do next, and this alone keeps ordinary conversation identical to today in cost, latency and appearance. No offer after a decline — a decline is an answer, and re-asking is nagging. No re-offer of options ignored last turn — the same reason.

**The offer step may also legitimately return nothing** even when offerable capabilities exist, and that answer is honoured: no padding to a minimum length, no generic substitute (FR-025c). An empty offer and a suppressed offer look identical to the user, which is correct.

**Cost consequence**, which sharpens D11's accounting: the offer call now happens only on turns that did work *and* have something worth suggesting. Plain conversational turns make **one** model call — the same as today — not two. The second call on an action turn replaces the location classifier that fires today, so action turns are unchanged too.

**Alternatives considered**:

- *Relevance decided by the offer prompt alone* — rejected: the same class of failure as ungrounded suggestions. A prompt cannot be relied on to stay quiet; a predicate can, and it costs nothing.
- *A global "offer every N turns" throttle* — rejected: arbitrary, and it would suppress a genuinely useful offer as readily as a redundant one.
- *Scoring relevance numerically and thresholding* — rejected as premature. A boolean predicate per capability is legible and testable; a score is neither until there is evidence about what to weigh.

---

## Decision 17 — Capability flows: a linear, narrated, declarative sequence

**Decision**: Introduce `IConversationFlow` — a named, ordered, declaratively registered sequence of capability steps that runs as one job inside a streaming turn. Each step declares its capability, how its arguments bind from the previous step's output, a precondition, and its announcement/report wording. Ship one flow, **Locate a place**: `resolve_location` → `adjust_viewer_focus` → `resolve_site_boundary`. Flows appear in the Tier 1 index alongside standalone capabilities, and the decide step may select one.

**Rationale**: these three steps are not independent options a user should be quizzed about — they are one job with a dependency chain. Offering them separately makes the user click twice to finish something they already asked for, and it misrepresents the causality: focusing the viewer is meaningless without a resolved location, and outlining a site is meaningless without both. The flow is the unit the user actually intends; the steps are its mechanics.

Making flows declarative rather than hardcoded is the point of the abstraction — the author explicitly noted these "may apply to other cases later". A new flow is a registration, exactly as a new capability is (FR-012, FR-050); the orchestrator never learns about locations.

**This reverses two earlier decisions, deliberately**:

| Earlier | Now |
|---|---|
| FR-046: boundary opt-in, offered as an action | Boundary is step 3 of the flow, run automatically |
| D16: boundary offerable in the turn that confirmed a location | Boundary is never offered — it is a flow step (FR-060) |
| SC-004: a "show me" turn is ≥30 s faster | **Withdrawn.** The same work is done, so the time is not saved |

The trade is explicit and worth stating plainly: the earlier design bought speed by not doing the work; this design does the work but makes the wait legible. SC-004 is rewritten to measure *whether the user always knows which step is running*, and SC-004a's five-second no-silence rule carries the weight that the time saving used to. Anyone revisiting this should know the speed was given up on purpose, not lost.

**Why not the existing Workflow Orchestration Engine (specs/022)?** It is a genuinely capable DAG engine — conditional branching, parallel/merge, loop-back edges, per-node retry and idempotency, human-approval pauses, four error strategies. It is also structurally wrong here for the same three reasons `AgentExecutionOrchestrator` was (D1): it runs as a Hangfire background job with no HTTP context, it does not stream, and it suspends across processes to wait for approval. A conversational flow must emit a message before and after every step, inside one request, and cannot suspend.

It is also far more machinery than the shape needs. A conversational flow is a **line, not a graph**: no branches, no merges, no loops. Reusing a DAG engine to walk a straight line would mean authoring node/connection rows, an execution graph and an expression language for what is a short ordered list with simple argument binding. That inverts the constitution's simplicity rule — and the two remain complementary: a *workflow* is authored by a user in a designer for repeatable automation; a *flow* is a platform-declared conversational sequence. Should a flow ever genuinely need branching, promoting it to a real workflow is the right answer, not growing `IConversationFlow` into a second DAG engine.

What *is* reused: the same capabilities, the same permission and policy checks, the same budget guard, and the same `AgentExecution`/step record model, so a flow is inspectable exactly like any other turn (FR-061).

**Brief steps should not be over-announced.** `adjust_viewer_focus` completes in well under a second. Announcing it, then reporting it, produces two messages about work that finished before either could be read — ceremony, not progress. Each step therefore declares whether it is announced separately; a brief step folds into the preceding report ("I found it and focused the viewer. Now outlining the site."). The author's requested cadence is honoured for every step that takes long enough for the cadence to mean something.

**Alternatives considered**:

- *Keep the steps as independent offered options* — the previous design; rejected as described above: it quizzes the user about a job they already asked for.
- *Hardcode the sequence in the orchestrator* — rejected: works for exactly one case, and the author asked for generality.
- *Model flows as sub-agent slices (D12)* — rejected: slices are how one turn's work is split across specialists, and are chosen per turn by a model. A flow is a fixed, reviewable sequence chosen by the platform. Conflating them would make the boundary step's execution depend on a model's judgement every single time.
- *Let the user disable the flow's later steps in settings* — rejected as premature. FR-058's request scoping ("just find it") and interruption cover the need without a setting nobody may want.

---

## Decision 18 — Intent decides whether a flow runs or is offered

**Decision**: The decide step classifies the user's intent toward a mentioned place into three, and that classification — not availability, not state — determines what happens:

| Intent | Example | Behaviour |
|---|---|---|
| **Navigational** | "Show me Al Safa Park 2", "Take me to X", "Centre on X" | Run the flow automatically, in full |
| **Informational** | "Do you know Al Safa Park 2?", "What is X?" | Answer briefly, then **offer** the flow's variants |
| **Passing mention** | "I read that Al Safa Park was renovated" | Neither run nor offer; the viewer does not move |

A flow declares named **variants** — contiguous prefixes of its steps offered as distinct choices. `locate_a_place` declares *focus only* (steps 1–2) and *focus and outline* (steps 1–3). Accepting a variant runs it from step 1, with the same narration and dependency rules as a navigational request.

**Rationale**: D17 made the flow unconditional, which was right for "show me X" and wrong for "do you know X?". Running a three-step job — including a thirty-second boundary lookup and a viewer move — because someone asked a *question* about a place is the same overreach the whole feature set out to remove; it just moved the unwanted work from a hidden pipeline into a declared flow. The distinction the user actually draws is between an instruction and a question, and it is exactly the distinction specs/037 already drew between a navigation request and a passing mention (its User Story 2) — this adds the third case that sat between them and was never modelled.

**Why variants rather than offering individual steps**: offering "focus the viewer" and "outline the boundary" as separate rows invites the user to assemble the job themselves, and lets them pick step 3 without step 1 — which cannot run. A variant is a whole outcome the user can want; a step is machinery they should not have to think about. This also keeps FR-060 clean: steps are never offerable, variants are.

**Erring direction**: borderline intent resolves to **informational**. Offering when Lucy should have acted costs one click; acting when she should have offered moves the user's viewer uninvited and spends thirty seconds doing it. The asymmetry is not close.

**Alternatives considered**:

- *Always run the flow* (D17 as first written) — rejected: overreaches on questions, which is what the author objected to.
- *Always offer the flow* — rejected: it asks permission for something the user plainly already requested, which is the failure D17 was created to fix.
- *Decide by keyword ("show"/"take me" versus "do you know")* — rejected: the same brittleness as the zoom keyword detector this feature retires. The decide call already reads the message; it can classify intent at no extra cost.

---

## Decision 19 — A stopped flow names the cause, not the consequence

**Decision**: When a flow stops, Lucy states what went wrong in the failing step and nothing more. She does not enumerate the steps that consequently did not run. The unattempted steps and their reason are still written to the turn record (FR-061) for diagnosis.

**Rationale**: D17 specified *"I couldn't find a place matching that name, so I haven't focused the viewer or outlined anything."* The second clause restates the definition of a dependent sequence. A user who has just been told the place was not found does not need telling that it was therefore not outlined — and being told reads as either padding or, worse, as defensiveness.

The diagnostic value is real but belongs in the record, where it costs the user nothing and answers "why was the boundary never drawn?" without inference.

**Alternatives considered**:

- *Name cause and consequence* — the D17 text; rejected by the author as unnecessary, correctly.
- *Drop the unattempted steps from the record too* — rejected: it is the one place the information genuinely helps, and FR-038 already requires a turn's decisions to be inspectable.

---

## Decision 20 — A mixed offer, with follow-ups composed rather than registered

**Decision**: An offer may contain four kinds of row: **flow variants**, **capability actions**, **conversational follow-ups**, and the **decline** row. Follow-ups are **composed for the situation** — not chosen from a registered set — drawing on what Lucy has learned from comparable turns (the memory subsystem), on the need to clarify an ambiguous request, on likely successors implied by the capability index, and on her own judgement about what is worth recommending. Cap: 5 rows, at most 4 substantive.

> **Revised 2026-09-08.** The first version of this decision made follow-ups a registered set (`IConversationFollowUp`) and added a "Something else" row. Both were corrected by the author: follow-ups are exactly the part that should be open-ended and learned, and "Something else" was an illustration in their example rather than a requirement — the composer is always live, so the row was redundant. The registry argument below is what replaced it.

**Rationale for composing them**: a registry can only contain the follow-ups someone thought of in advance, which forfeits the whole value. The useful follow-up after a boundary comes back at low confidence is *"ask whether you meant the adjacent parcel"* — situational, unlistable, and exactly what an assistant that learns from comparable situations should produce. Registration would have reduced follow-ups to three generic verbs.

**How grounding survives without a registry.** This is the hard part, and the answer is to change *what* is guaranteed rather than to pretend the old guarantee still holds:

| Row kind | Guarantee | Mechanism |
|---|---|---|
| Flow variant, capability | **Absolute** — zero ungrounded rows | Key must be in the turn's available set; arguments must satisfy the schema (FR-024.1) |
| Conversational follow-up | **Structural** — selecting one invokes zero capabilities, ever | Dispatch for this kind has no capability path at all (FR-021c) |
| Conversational follow-up | **Best-effort** — ≤2% promise platform work | Offer prompt carries the capability index so anything requiring *doing* is proposed as a capability row instead (FR-024a); a validator rejects doing-phrasing; every discard logged |

The structural guarantee is what makes the softer one acceptable. A badly-composed follow-up cannot cause the platform to act — the worst case is Lucy saying something she cannot follow through on, which is a quality problem, not a correctness one. That is a genuinely different class of failure from an action row that promises to draw a boundary and cannot.

SC-002 is split accordingly: absolute for action rows, structural for follow-up dispatch, and a measured ceiling for follow-up phrasing. Recording it as three criteria rather than one "100%" is the honest move — the previous single criterion would have been quietly unenforceable for the new kind.

**Clarification as a follow-up is a bonus.** Making "ask the user which one they meant" a first-class follow-up gives a home to the multi-candidate disambiguation case that spec 035 (User Story 2) described and spec 037 explicitly left out of scope — ambiguous place names can now be resolved by offering the candidates instead of refusing.

**Alternatives considered**:

- *A registered follow-up set* — the first version of this decision; rejected by the author, and rightly: it makes the open-ended kind closed.
- *Free-form with no validation at all* — rejected: the structural guarantee is cheap and the phrasing validator catches the common case, so accepting neither would be carelessness rather than pragmatism.
- *Model every follow-up as a capability* — rejected: a capability that runs no code and touches no data is a fiction requiring a schema, permissions and a risk level it does not have.

---

## Decision 21 — Uniform step announcements, with completion and next announcement in one message

**Decision**: Every flow step is announced before it runs, regardless of duration — `AnnounceSeparately` is removed. Each message after the first pairs the completion of the finished step with the announcement of the next, so an N-step flow produces N+1 messages:

```text
"Looking for Al Safa Park 2."
"Location found. Now focusing the viewer on it."
"Site focused. Now highlighting the boundary."
"Boundary highlighted — about 4.2 hectares."
```

**Rationale**: D17 folded sub-second steps into the neighbouring report to avoid announcing work that finishes before its own announcement can be read. The author asked for uniformity instead, and supplied the phrasing that makes uniformity cheap. Pairing completion with the next announcement is the key move: separate "done" and "starting" messages would produce 2N messages and give a 200 ms step two of its own about work already finished. Combined, every step is named exactly once starting and once ending, in four messages rather than seven.

Uniformity also removes a judgement call from every future flow author — "is this step long enough to announce?" — replacing it with a rule that needs no calibration. That is worth more than the handful of words it costs.

**Alternatives considered**:

- *Per-step `AnnounceSeparately`* — D17's design; rejected by the author in favour of uniformity, and the combined phrasing makes the cost negligible.
- *Separate completion and announcement messages* — rejected: 2N messages, and a sub-second step gets two of them about nothing pending.
