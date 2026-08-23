# Research: Conversational Park Site Analysis Agent

**Feature**: [spec.md](./spec.md) | **Date**: 2026-08-22

This feature has an unusually large "consult existing platform docs before designing" burden — it
composes four already-implemented but previously-unwired systems (Agent Engine, MCP Tool Engine,
Immersive Viewer, Floating Panels) with one external platform (TheDigitalCore) and one external
Python codebase (the `park-redesign` notebooks). Every decision below is grounded in what already
exists in this repo (or the sibling `TheDigitalCore`/`park-redesign` repos) rather than invented.

## Decision 1: Chat-to-Agent turn routing (the mechanism behind Clarifications Q1/FR-012)

**Decision**: Add a small, feature-scoped router in the Chat Engine's message-send pipeline: when a
message is sent in a `UserChat` that has a `SiteAnalysisProjectLink` (data-model.md), and the message
text matches one of this feature's narrow intents (site description, "analyze recreation," "analyze
social," or a bootstrap-flow confirmation), the handler calls the existing
`StartAgentExecutionCommand` (`AgentId` = the one pre-published "Site Analysis Agent",
`ConversationIntegrationMode = ExistingConversation`, `UserChatId` = the current chat, `Objective` =
the qualifying message plus a short, structured summary of already-known conversation state: the
resolved `SiteBoundary`, which categories already have a result, and the linked
`SiteAnalysisProjectLink`) instead of (or in addition to, for a natural-language acknowledgement)
a normal streamed chat completion.

**Rationale**: `grep` across `src/AskLucy.Application/Chats/**` for any reference to `Agent` returns
zero results — there is no existing chat-message → agent-execution trigger anywhere in this codebase
today, only an explicit, user-driven "start this named agent with this objective" entry point
(`StartAgentExecutionCommand`, `AgentExecutionsController`). `specs/020-ai-agent-framework`'s own
FR-051/FR-052 already support exactly the shape this feature needs — "start an execution inside an
existing conversation, with automatic linking" — so nothing in the Agent Engine itself needs to
change (confirms Clarifications Q1's answer is achievable with zero Agent Engine changes). What is
missing, and is this feature's own responsibility to add, is the *decision* of *when* a plain chat
message should become a new `StartAgentExecutionCommand` call rather than an ordinary streamed reply.
Building a general-purpose, platform-wide chat intent classifier is explicitly out of scope (YAGNI) —
this feature's router only needs to recognize its own narrow set of triggering phrases within a
`UserChat` that is already linked to a site-analysis Project, which keeps the new code small and
testable.

**Alternatives considered**:
- **Let the AI model itself decide, via function-calling, inside the normal streamed chat
  completion** — rejected: `specs/021-mcp-integration`'s own constraint is that MCP tools (and, by the
  same reasoning, this feature's own MCP-backed capabilities) may only be invoked through the existing
  Agent Tool abstraction and Agent Runtime, "never a second, parallel tool execution framework." A
  model-driven function call from inside a plain chat completion would be exactly that second
  framework.
- **Require the user to explicitly launch the "Site Analysis Agent" from the existing Agents UI for
  every turn** — rejected: this is precisely the "one upfront batch declaration" shape Clarifications
  Q1 and FR-010/FR-011 rule out; the whole point of this feature is that ordinary chat messages
  trigger it.
- **A platform-wide, general intent-classification service** — rejected (YAGNI): no other feature
  needs this yet; scoping the router to this feature's own conversations (via
  `SiteAnalysisProjectLink`) and its own narrow phrase set is sufficient and avoids a speculative,
  broad new subsystem.

## Decision 2: One pre-published "Site Analysis Agent," not one Agent per category or per run

**Decision**: This feature provisions exactly one `Agent` (Type = `Task`, `OutputFormat = Json`,
Published) configured with the five new MCP tools (Decision 4) plus the existing built-in
"Knowledge Search" tool scoped to the new Site Analysis Knowledge Base (Decision 6). Every qualifying
chat turn (Decision 1) starts a new `AgentExecution` against this same `Agent`, with a different
`Objective` each time.

**Rationale**: `Agent` is already modeled as a reusable, versioned definition (`specs/020`
data-model.md) — creating a new `Agent` per category or per conversation would duplicate
configuration (tool list, knowledge base, risk policy) that is identical across every use of this
feature, violating DRY for no benefit. One agent, many short executions, is the same shape
`specs/020`'s own examples use for reusable task agents.

**Alternatives considered**:
- **A distinct Agent per category (one for Recreation, one for Social)** — rejected: the tool sets
  overlap (both need `resolve_site_boundary` indirectly via conversation state, both use the
  Knowledge Search tool against the same knowledge base) and the only real difference is *which*
  scoring/data-collection tool the objective asks for — that's a runtime objective detail, not a
  configuration-time one.

## Decision 3: TheDigitalCore integration is a direct Application-owned HTTP client, not an MCP tool

**Decision**: Add `ITheDigitalCoreClient` (Application/Abstractions) with methods
`FindProjectAsync(siteName, coordinates, ct)`, `CreateProjectAsync(siteName, coordinates, ct)`, and
`RelayCategoryScoreResultAsync(externalProjectId, result, ct)`, implemented in
`Infrastructure/TheDigitalCore` using a named `IHttpClientFactory` client authenticated with a single
service-account credential (Clarifications Q2). This is called directly from new Application command/
query handlers (Decision 1's router, and the bootstrap-flow handlers) — **not** exposed as an MCP tool.

**Rationale**: `specs/021-mcp-integration`'s own framing is that MCP is "the sanctioned tool/data
connector surface" for *external, admin-registered, third-party* systems an agent discovers and calls
dynamically. TheDigitalCore is not a third-party data source being connected to an agent's toolbox —
it is this feature's own, fixed, first-class integration partner (the system of record this whole
feature exists to write results back into), authenticated with a single owned service account rather
than a per-server credential a server administrator configures generically. Modeling it as an MCP
tool would force awkward admin-activation-lifecycle semantics (specs/021 FR: "every discovered tool
starts inactive until an administrator activates it") onto something that isn't optional or
discoverable — this feature cannot function at all without it. A direct, purpose-built Infrastructure
client mirrors how every other fixed external dependency in this codebase is modeled (e.g., `IAIProvider`
implementations, `AiCredentialProtector`), not how MCP servers are modeled.

**Alternatives considered**:
- **Model TheDigitalCore as an MCP server like any other** — rejected per rationale above.
- **Call TheDigitalCore directly from Infrastructure/Mcp's Python server** — rejected: the Python MCP
  server's job (Decision 4) is geospatial/scoring logic only; giving it its own TheDigitalCore
  credential would duplicate the trust boundary and scatter the "one service account" invariant
  (Clarifications Q2) across two codebases instead of one.

## Decision 4: The Python geospatial pipeline becomes a new MCP server in the `park-redesign` repository, using the official Python MCP SDK

**Decision**: The five in-scope pipeline stages become five MCP tools, implemented as a new MCP
server package inside the existing `park-redesign` repository (not inside this `hydra` repository,
which is a .NET solution). The server uses the official Python `mcp` SDK (matching
`specs/021-mcp-integration`'s own precedent of using the *official* SDK for the C# side, never a
hand-rolled protocol implementation) exposed over the Streamable HTTP transport, so it can be
registered as a normal network-reachable MCP server through Ask Lucy's existing, unmodified
`McpServersController` admin registration flow (per spec's Assumption: "this feature does not
introduce new registration UX").

**Tools** (one per FR-007 pipeline stage, wrapping the existing notebook modules verbatim rather than
rewriting their logic):

| MCP tool | Wraps notebook module(s) | Notes |
|---|---|---|
| `resolve_site_boundary` | Module 01 (User Input / boundary resolution) | Serves **both** FR-001a/FR-001g's location and built-asset-status check and FR-002's boundary resolution — a successful resolution reports location plus a `builtAssetConfirmed` signal (Decision 5); avoids a second, redundant "does this exist" tool. |
| `collect_recreation_data_layers` | Module 02 (Data Collection), filtered to Recreation-relevant layers | Returns raw/derived layer data plus an explicit data-gap marker per FR-015 for any layer whose entire fallback chain fails. |
| `collect_social_data_layers` | Module 02, filtered to Social-relevant layers | Same data-gap contract as above. |
| `score_recreation` | Module 06 (Scoring Engine), Recreation category only | Consumes `collect_recreation_data_layers`'s output; static thresholds/weights per spec Assumptions. |
| `score_social` | Module 06, Social category only | Consumes `collect_social_data_layers`'s output. |

Module 03 (Preprocessing) stays internal to the tool implementations (not separately exposed), per
the pre-existing `FLUMERIA-STUDIO-INTEGRATION-ARCHITECTURE.md` mapping this decision reuses.
Modules 05/08/09/10 and the other six scoring categories are out of scope for this feature
(spec Assumptions) and are not wrapped as tools here.

**Rationale**: The notebooks depend on `osmnx`/`rasterio`/`geopandas`/Earth Engine/Gemini-vision —
none of which have a C#/.NET equivalent, and rewriting them would both duplicate already-working
logic (violates DRY) and risk behavior drift from the validated pipeline (`pipeline_results.json`'s
known-good Recreation/Social output is the reference this feature's vertical slice targets).
Wrapping, not rewriting, is the only option consistent with "migrate ... instead of relying on the
notebook" (original user request) without also silently changing what the analysis computes.

**Alternatives considered**:
- **Host the Python server inside this `hydra` repository** — rejected: this repo's solution,
  `Directory.Build.props`, and CI are entirely .NET; introducing a Python subtree here would mix
  build/deploy concerns for no benefit, when `park-redesign` already holds every dependency
  (`.venv`, the notebooks, `tools/`) this server needs.
  the pipeline needs.
- **stdio transport instead of Streamable HTTP** — rejected: `McpServersController`'s registration
  model (endpoint + transport, health-checked, network-reachable) assumes a running network service,
  not a locally-spawned subprocess; Streamable HTTP is the modern MCP transport built for exactly this
  registered-network-server shape.

## Decision 5: One boundary-resolution tool serves both the existence check and the boundary render

**Decision**: `resolve_site_boundary`'s successful result (`resolved: true`) is treated as satisfying
FR-002 (boundary resolution for the Immersive Viewer) and, together with its separate
`builtAssetConfirmed` signal, FR-001a/FR-001g (existence/status verification) — there is no separate
"does this place exist" tool call. `resolved: true` confirms the *location* is real and resolvable;
`builtAssetConfirmed` (`true`/`false`) separately tells the assistant whether a built park/facility
was confirmed there versus the site being planned/proposed/under construction (spec.md FR-001g,
Clarifications' reconciliation with the source product document's User Story 07/08) — both values of
`builtAssetConfirmed` proceed identically to TheDigitalCore search; only an unresolvable location
(`resolved: false`) blocks.

**Rationale**: The reference mockup (`urban-copilot-2.html`'s `STEPS` config) shows the map/boundary
preview appearing in the same screen transition as the "Existing physical asset detected" checklist
item — the platform's own UX precedent treats resolving a boundary as (at least a first pass at) the
existence proof, not two separate steps. Introducing a second, functionally-overlapping tool would
violate DRY/YAGNI for no requirement this spec actually states; adding one extra boolean field to the
same tool's output is enough to also capture the built-vs-planned distinction the source product
document's User Story 07/08 requires, without a second tool call.

**Alternatives considered**: A dedicated `verify_site_exists` tool distinct from
`resolve_site_boundary` — rejected as redundant per above; if resolution ever needs a materially
cheaper "just check existence, don't fully resolve" mode, that is a parameter on the same tool, not a
new one.

## Decision 6: Category Score Results are not a new database table — they live on `AgentExecution.FinalOutputJson`

**Decision**: No new `CategoryScoreResult`/`CategoryAnalysisRun` persistence is added. The Site
Analysis Agent's `OutputFormat` is `Json`; each `AgentExecution`'s existing `FinalOutputJson` column
(`specs/020` data-model.md) holds the category's score/findings/recommendations/citations/data-gaps
in the shape defined by contracts/site-analysis-category-result.md. `AgentExecutionStep`/
`AgentToolCall` (already-existing entities) already record which MCP tools ran and with what
input/output, satisfying FR-023's audit requirement with zero new tables.

**Rationale**: FR-025 forbids Ask Lucy from persisting its own competing copy of TheDigitalCore's
SiteAnalysis/DesignConcept/DesignRecommendation records. A new, Ask-Lucy-owned "category result"
table would be exactly that — a shadow copy of data whose system of record is TheDigitalCore. Reusing
`AgentExecution`'s already-modeled, already-audited output field keeps this feature's new persisted
surface to a single table (`SiteAnalysisProjectLink`, Decision 7) instead of two.

**Alternatives considered**: A dedicated `CategoryScoreResult` entity — rejected per FR-025 and DRY;
would duplicate what `AgentExecution.FinalOutputJson` + `AgentExecutionStep`/`AgentToolCall` already
capture.

## Decision 7: One new entity — `SiteAnalysisProjectLink` — is the only new persisted table

**Decision**: A single new entity maps `UserChatId` (unique) → TheDigitalCore's external `ProjectId`
(opaque string/Guid, whichever TheDigitalCore's own id type is — treated as an opaque string in this
feature's own schema to avoid assuming TheDigitalCore's internal key type), plus how the link was
established (`InboundDeepLink` / `BootstrapCreated` / `BootstrapMatched` — FR-024) and audit columns.
See data-model.md.

**Rationale**: Every `StartAgentExecutionCommand` call and every relay-to-TheDigitalCore call
(Decision 3) needs to resolve "which TheDigitalCore Project does this conversation belong to" — that
mapping does not exist anywhere else in this codebase and is not itself a competing copy of Project
data (FR-025), just a reference, matching the spec's own "Project Link" Key Entity.

## Decision 8: TheDigitalCore project matching — name search first, then geolocation (Clarifications Q3)

**Decision**: `ITheDigitalCoreClient.FindProjectAsync` first searches TheDigitalCore by the resolved
site's name (or close textual match); if that search is inconclusive (zero or multiple candidates),
it retries using the site's resolved coordinates (proximity search) as a secondary signal. If either
search still yields more than one plausible candidate, the calling Application handler surfaces the
candidates to the user for confirmation (edge case added during `/speckit.clarify`) rather than
picking one — consistent with this spec's existing no-silent-decisions guardrail (FR-018).

**Rationale**: Directly implements Clarifications Q3's resolved answer. This is a consumer-driven
contract requirement on TheDigitalCore's API (contracts/thedigitalcore-integration-api.md) — the
actual search implementation lives in TheDigitalCore, not this repository.

## Decision 9: Conflicting/ambiguous tool results reuse `AgentPolicy` risk-gating — no new interruption mechanism

**Decision**: A data-collection or scoring tool call whose result set contains materially conflicting
signals returns a distinguished "requires review" outcome; the Site Analysis Agent's tool
configuration marks that outcome as High-risk for the purpose of `AgentPolicy` evaluation, so
`AgentExecutionOrchestrator` pauses the execution in the existing `WaitingForApproval` state
(`specs/020` FR-025) exactly as it would for any other high-risk tool call — the user reviews and
either approves (accepting one source) or rejects (execution ends without a result for that step).

**Rationale**: Directly reuses `specs/020`'s existing approval/audit machinery (`AgentApproval`,
`AgentPolicy`) rather than inventing a second pause-and-ask-the-user mechanism, per this spec's own
Assumptions ("no second, parallel tool-execution or interruption framework is introduced").

## Decision 10: Floating Panel — one new renderer, registered under a new type key

**Decision**: A new panel type key, `site-analysis-category-result`, is registered with the existing
Floating Panel renderer registry (`specs/028`). When a `AgentExecution` for this feature completes,
the Application-layer handler that observes completion calls the already-defined but previously
unused `IPanelNotifier.PanelRequestedAsync(userId, request)` (`specs/028` contracts/panel-hub-events.md)
with `typeKey = "site-analysis-category-result"` and `data` shaped per
contracts/site-analysis-category-result.md.

**Rationale**: `specs/028`'s own `panel-hub-events.md` contract explicitly scopes itself to the
*receiving* side and states "no existing 'AI decides to show a panel' reasoning step exists yet in the
chat/agent pipeline" — this feature is the first real caller of that already-built, previously-unwired
mechanism, exactly as `docs/AGENTIC_AI_ENGINE_SPEC.md` anticipated ("agent wiring to that API is not
yet built" — flagged as the primary near-term integration point). No change to `PanelHub`, the
frontend panel-management core, or the cascade/eviction logic is needed — only the new renderer
component and its registration entry (spec 028's own extensibility contract).

## Decision 11: Immersive Viewer boundary rendering reuses the existing command/event API — no new viewer surface

**Decision**: Once `resolve_site_boundary` returns a boundary, the same completion-observing handler
calls the Immersive Viewer's existing programmatic command API (`specs/027`) to add/replace a GIS
overlay layer showing the boundary outline or marker, using whatever content-layer command the viewer
already exposes for GIS layers — no new command is added to the viewer.

**Rationale**: `specs/027`'s own spec was explicitly built "so that later Ask Lucy AI-agent features
can call it" and `docs/AGENTIC_AI_ENGINE_SPEC.md` names this exact wiring as unbuilt-but-anticipated.
This feature is that wiring, for one narrow case (a boundary overlay), not a reason to extend the
viewer's command surface itself.

## Decision 12: Deep-link entry point (FR-024) is one new, thin endpoint + SPA route

**Decision**: A new `POST /api/v1/site-analysis/project-links` endpoint (new, small
`SiteAnalysisController`) accepts an opaque TheDigitalCore project reference (passed as a query
parameter on the deep link the user follows from TheDigitalCore), validates it against
TheDigitalCore via `ITheDigitalCoreClient` (Decision 3), creates a new `UserChat` if needed, writes a
`SiteAnalysisProjectLink` row (`LinkSource = InboundDeepLink`), and returns the chat id so the SPA can
navigate straight into that conversation. A matching, minimal SPA route
(`ClientApp/src/features/site-analysis`) performs this call on load and redirects into the normal chat
UI — no bespoke chat/viewer UI is built for this entry path (spec Assumptions).

**Rationale**: Smallest possible surface that satisfies FR-024(a) without duplicating any existing
chat/viewer frontend code — matches the constitution's API/versioning conventions (`/api/v1/...`,
Problem Details on failure) and this repo's existing thin-controller convention.

## Technology Summary

| Concern | Choice | Notes |
|---|---|---|
| Chat→Agent routing | New, feature-scoped Application-layer router (Decision 1) | No Agent Engine changes |
| Agent definition | One pre-published `Agent` (Decision 2) | Reuses spec 020 entities as-is |
| TheDigitalCore integration | `ITheDigitalCoreClient` + named `HttpClient`, service-account auth (Decision 3, Clarifications Q2) | New `Infrastructure/TheDigitalCore` folder |
| Geospatial pipeline | New Python MCP server in `park-redesign` repo, official `mcp` SDK, Streamable HTTP transport (Decision 4) | Registered via existing, unmodified MCP admin UI |
| Category results storage | `AgentExecution.FinalOutputJson` (Decision 6) — no new result table | Keeps FR-025 (no competing copies) trivially true |
| New persisted entity | `SiteAnalysisProjectLink` only (Decision 7) | See data-model.md |
| Project matching | Name-first, geolocation-secondary (Decision 8, Clarifications Q3) | Implemented in TheDigitalCore, contract-only here |
| Conflict handling | Existing `AgentPolicy`/`AgentApproval` risk-gating (Decision 9) | No new interruption mechanism |
| Result presentation | New Floating Panel renderer + type key (Decision 10) | First real caller of spec 028's `IPanelNotifier` |
| Boundary rendering | Existing Immersive Viewer command API (Decision 11) | First real caller of spec 027's command surface |
| Deep-link entry | One new controller + SPA route (Decision 12) | No new chat/viewer UI |
