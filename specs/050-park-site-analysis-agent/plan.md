# Implementation Plan: Conversational Park Site Analysis Agent

**Branch**: `050-park-site-analysis-agent` | **Date**: 2026-08-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/050-park-site-analysis-agent/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Wire four already-implemented-but-previously-unwired Ask Lucy systems together — the Agent Engine
(`specs/020`), the MCP Tool Engine (`specs/021`), the Immersive Viewer (`specs/027`), and the AI
Floating Panel framework (`specs/028`) — so that a chat conversation can conversationally, turn by
turn, verify a real-world park site, bootstrap or link a digital Project in the external
**TheDigitalCore** platform, resolve and render the site's boundary, and run cited Recreation/Social
scoring analyses, without introducing a second tool-execution framework or a new persisted copy of
TheDigitalCore's own data. The `park-redesign` repository's existing, validated Python GIS pipeline
(notebooks) is wrapped — not rewritten — as five new MCP tools on a new external MCP server
(research.md Decision 4). A small, feature-scoped router is the one genuinely new mechanism this
plan introduces: it recognizes qualifying chat messages in a site-linked conversation and starts a
new, short `AgentExecution` (existing-conversation mode, `specs/020` FR-051/FR-052) against one
pre-published "Site Analysis Agent" (research.md Decisions 1-2) — resolving Clarifications Q1
without any Agent Engine change. TheDigitalCore is integrated as a direct, Application-owned HTTP
client under a single service-account credential (Clarifications Q2, research.md Decision 3), never
as an MCP server and never via per-user TheDigitalCore accounts. Exactly one new database table is
added (`SiteAnalysisProjectLink`); category results live on `AgentExecution.FinalOutputJson`
(research.md Decision 6), keeping FR-025's "no competing copy of TheDigitalCore's data" trivially
true.

## Technical Context

**Language/Version**: C# on .NET 10 (backend, all five existing `AskLucy.*` projects); TypeScript on
React 19 + Vite (frontend, `src/AskLucy.Web/ClientApp`) — no new language/runtime in this repository.
Python 3.11+ for the new MCP server in the sibling `park-redesign` repository (its existing dev
environment runs 3.14; the notebooks impose no lower-version requirement beyond modern
`osmnx`/`rasterio`/`geopandas`).

**Primary Dependencies**: Existing (reused, no new usage pattern): MediatR (CQRS), FluentValidation,
AutoMapper, Entity Framework Core (SQL Server), ASP.NET Core Identity/JWT, `IHttpClientFactory`,
the existing `McpServersController`/`IMcpClient` admin-registration flow (`specs/021`, zero changes),
the existing `AgentExecutionOrchestrator`/`AgentPlanner`/`StartAgentExecutionCommand`
(`specs/020`, zero changes), the existing `IPanelNotifier`/`PanelHub` (`specs/028`, zero changes),
the existing Immersive Viewer command API (`specs/027`, zero changes), the existing RAG/Knowledge
Base pipeline (`specs/016`) for the new Site Analysis Knowledge Base. **New**: the official Python
`mcp` SDK in `park-redesign` (research.md Decision 4) — no new NuGet package is required on the
.NET side; TheDigitalCore integration uses the already-present `IHttpClientFactory` pattern with a
new named client, not a new package.

**Storage**: SQL Server via EF Core Code-First migrations against the existing `AskLucyDbContext`;
exactly one new entity, `SiteAnalysisProjectLink` (data-model.md), inheriting the same `BaseEntity`
conventions (Guid v7 key, audit columns, soft delete, `RowVersion`) as every other entity in this
codebase. Category score results are **not** newly persisted — they reuse `AgentExecution`'s existing
`FinalOutputJson` column (research.md Decision 6). TheDigitalCore's own Project/Attachment/
SiteAnalysis/DesignConcept/DesignRecommendation storage is out of this repository's scope entirely
(FR-025) — a separate system, a separate schema, not touched by this feature's migrations.

**Testing**: xUnit for `AskLucy.Domain.Tests`/`AskLucy.Application.Tests`/`AskLucy.Persistence.Tests`/
`AskLucy.Infrastructure.Tests`/`AskLucy.Web.Tests`; Playwright (`AskLucy.E2E.Tests`) for the deep-link
entry and turn-by-turn chat flow — matches every existing feature's test-folder convention, extended
with a new `SiteAnalysis`-named subfolder per project. `pytest` for the new Python MCP server in
`park-redesign` (that repository's own test convention, not this one's).

**Target Platform**: ASP.NET Core Web API (`AskLucy.Web`) hosting the existing React SPA — this
feature adds no new deployable unit to `hydra` itself. The new Python MCP server is a separate
deployable service, owned by `park-redesign`, registered into Ask Lucy the same way any other MCP
server is (endpoint + transport, admin-activated).

**Project Type**: Web application — extends the existing modular monolith's five backend projects
plus the existing frontend SPA; no new project/solution entry in `hydra`. One new external service
(the Python MCP server) exists outside this repository's solution.

**Performance Goals**: SC-001 ("within a single conversational exchange") — `resolve_site_boundary`
MUST return within the existing chat/agent turn-around budget the platform already targets for a
short (1-2 step) `AgentExecution`; no new performance target beyond what `specs/020`'s own execution
policy defaults already enforce (`AgentExecutionPolicy.MaxExecutionDurationSeconds`). SC-007 (100%
relay success, zero silent failures) — the TheDigitalCore relay call is retried per this repo's
existing HTTP-client retry convention and, on final failure, surfaces a visible chat/UI error rather
than swallowing it (constitution §2.VIII).

**Constraints**: No Agent Engine, MCP Tool Engine, Immersive Viewer, or Floating Panel *core*
mechanism changes (research.md Decisions 1-2, 9-11 — each is additive-only: new data, new renderer,
new tool rows). No second, parallel tool-execution framework (FR-012, `specs/021`'s own constraint).
No individual Ask Lucy user may be required to hold a TheDigitalCore account (FR-027a). The
TheDigitalCore service-account credential is server-side only, never reaching a browser (FR-027).
TheDigitalCore remains the sole system of record for Project/Company/Attachment/SiteAnalysis/
DesignConcept/DesignRecommendation (FR-025) — this feature's own new persistence is limited to one
link table (research.md Decision 7).

**Scale/Scope**: A vertical slice, not the full pipeline — 2 scoring categories (Recreation, Social),
5 new MCP tools, 1 new pre-published Agent, 1 new database table, 1 new Floating Panel renderer, 1
new thin controller (deep-link entry), 1 new feature-scoped chat-turn router. 27 functional
requirements (FR-001-FR-027a, spec.md), 6 user stories, 8 success criteria.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Section | Status | Notes |
|---|---|---|
| I. Clean Architecture & Dependency Rule | **PASS** | New `Application/SiteAnalysis` (router, bootstrap-flow handlers, TheDigitalCore-relay handlers) depends only on `Domain` and `Application`-owned interfaces (`ITheDigitalCoreClient`). `Infrastructure/TheDigitalCore` (the concrete `HttpClient` implementation) depends on `Application`/`Domain` only, never the reverse. The Python MCP server lives entirely outside this solution's dependency graph — Ask Lucy only ever sees it through the existing `IMcpClient` abstraction (`specs/021`), unchanged. |
| II. SOLID | **PASS** | New MCP tools are new `McpTool` rows plus the existing `McpToolAdapter` (`specs/021`) — zero new adapter classes, zero `AgentExecutionOrchestrator`/`AgentPlanner` changes (OCP). `ITheDigitalCoreClient` is narrow (3 methods, research.md Decision 3) — no god interface. |
| III. Simplicity First (DRY/KISS/YAGNI) | **PASS** | Decision 5 (one boundary tool serves two purposes) and Decision 6 (no new result table) explicitly choose not to add tools/entities a less careful design would have doubled up. Decision 1's router is deliberately scoped to this feature's own conversations, not a platform-wide intent classifier (YAGNI). |
| IV. Composition over Inheritance | **PASS** | `ITheDigitalCoreClient` is injected via constructor into the router/bootstrap handlers; no inheritance hierarchy introduced. |
| V. Dependency Inversion & Testability | **PASS** | `ITheDigitalCoreClient` is Application-defined and fully fakeable in unit tests; the chat-turn router, bootstrap handlers, and the panel/viewer-triggering completion handler are all unit-testable with `IAgentExecutionRunner`/`IPanelNotifier`/`IViewerCommandDispatcher`-equivalent dependencies mocked, no DB/network required. |
| VI. Separation of Concerns | **PASS** | The new `SiteAnalysisController` (deep-link entry) stays thin — one action, delegates to a Command/Query, matches every existing controller in this codebase. |
| VII. Convention over Configuration | **PASS** | Reuses the existing MCP admin-registration flow verbatim (no new registration UX, per spec Assumptions), the existing `StartAgentExecutionCommand`/`ConversationIntegrationMode.ExistingConversation` verbatim, the existing `IPanelNotifier`/Floating Panel registry verbatim, the existing Immersive Viewer command API verbatim. The one new pattern (a service-account-authenticated external HTTP client) has no existing in-repo precedent to reuse — justified in Complexity Tracking. |
| VIII. No Silent Failures (NON-NEGOTIABLE) | **PASS, enforced by design** | FR-015/FR-016 (explicit data-gap indications, never fabricated/omitted values), FR-018 (conflicts pause for approval, never silently resolved), SC-007 (relay failures visibly surfaced, never dropped) are all first-class functional requirements this plan implements via existing, already-audited mechanisms (`AgentExecutionError`, `AgentApproval`) — no new place for a failure to go unobserved. |
| §3 Architecture Rules | **PASS** | No `Domain`→`Application`/`Infrastructure` reference; new `Infrastructure/TheDigitalCore` implements an `Application`-defined interface, same shape as every other `Infrastructure.*` integration in this repo. |
| §3 CQRS rules | **PASS** | New bootstrap-flow (verify/search/create-or-link Project) and relay-result operations are each a MediatR command with one handler; the chat-turn router itself is a pipeline behavior/handler addition inside the existing `SendMessage` flow, not a bypass of MediatR. |
| §3 Domain events | **PASS** | `SiteAnalysisProjectLinked`/`SiteAnalysisProjectLinkFailed` domain events raised from the new `SiteAnalysisProjectLink` aggregate, dispatched post-commit — matches this codebase's existing domain-event convention. |
| §5 Database Principles | **PASS** | `SiteAnalysisProjectLink` follows `BaseEntity` conventions exactly (Guid v7 key, audit columns, soft delete query filter, `RowVersion`), with a unique index on `UserChatId` (data-model.md) and an index on the external `ProjectId` reference for the reverse lookup. Zero changes to any existing table/schema. RAG/vector storage: the new Site Analysis Knowledge Base uses the existing per-knowledge-base `IVectorStore` abstraction (`specs/016`, ADR-0007) unchanged — no new vector database introduced by this feature. |
| §6 API Standards | **PASS** | New `POST /api/v1/site-analysis/project-links` (deep-link entry) is nouns/plural/kebab, `/api/v1/...`, Problem Details on failure, `[Authorize]` by default — matches every existing controller. No new controller is needed for the chat-turn/agent/panel/viewer wiring (it rides on existing `SendMessage`, `StartAgentExecutionCommand`, `PanelHub`, viewer command paths). |
| §7 UI Principles | **PASS** | The one new frontend piece with real UI is the Floating Panel renderer (`ClientApp/src/features/site-analysis`, MUI theme, no bespoke styling) plus the minimal deep-link-entry route — both follow the existing feature-folder shape (`specs/028`'s own convention). |
| §8 Security | **PASS, this feature's central concern alongside `specs/021`** | Service-account credential (Clarifications Q2, FR-027) is never exposed to a browser and is stored via the same secret-manager/`IOptions<T>` pattern the constitution's §4 Configuration rule already mandates — no new credential-storage mechanism invented. Least privilege: the service account is scoped only to the Project search/create/attach operations this feature needs, not a general TheDigitalCore admin credential (documented as an explicit assumption on TheDigitalCore's side in contracts/thedigitalcore-integration-api.md, since that scoping is enforced by TheDigitalCore, not this repository). MCP tool credentials (mapping/imagery/vision providers) reuse the existing MCP credential-storage mechanism (FR-009) — no new pattern. |
| §9 AI Principles | **PASS** | The Site Analysis Agent's tool set is explicit and bounded (5 new MCP tools + existing Knowledge Search) — no implicit capability beyond what its task grants. Every tool invocation remains authorized/logged/bounded via the unchanged `AgentExecutionOrchestrator`/`AgentPolicy` machinery. |
| §10 Testing Standards | **PASS (planned)** | Test-folder plan mirrors the `Agents`/`Mcp`/`Panels` features exactly (new `SiteAnalysis`-named subfolders per project); unit tests for the chat-turn router's message-classification logic (no DB), integration tests for `ITheDigitalCoreClient` against a stubbed HTTP handler, Playwright for the deep-link-entry → conversation → boundary-render → category-analysis → panel end-to-end flow (quickstart.md). |
| §13 Documentation | **Action required** | Two new ADRs are warranted per §17 (new cross-cutting infrastructure dependency): (1) the new external Python MCP server as a second-repository dependency, and (2) the TheDigitalCore service-account integration pattern. Both are recorded in Complexity Tracking below rather than blocking this plan — to be authored during the implementation phase per constitution §13. |
| §11-§19 (Git/CI/CD/Observability/Performance/Quality Gates/AI Agent Rules/DoD) | **PASS** | No deviation requested; `docs/ARCHITECTURE.md`, `docs/ENTITY_MODEL.md`, `docs/DATABASE.md` will be updated during implementation (documentation-is-part-of-implementation, constitution §13), not before. |

Both new-dependency additions (the Python MCP server as a second-repository dependency, and the
TheDigitalCore service-account HTTP integration pattern) are recorded in Complexity Tracking below
per constitution §17's ADR trigger — not because either is a violation, but because each is exactly
the class of decision §17 asks to be recorded with alternatives considered, which research.md
Decisions 3 and 4 already do.

## Project Structure

### Documentation (this feature)

```text
specs/050-park-site-analysis-agent/
├── plan.md                                     # This file (/speckit-plan command output)
├── research.md                                 # Phase 0 output (/speckit-plan command)
├── data-model.md                               # Phase 1 output (/speckit-plan command)
├── quickstart.md                               # Phase 1 output (/speckit-plan command)
├── contracts/                                  # Phase 1 output (/speckit-plan command)
│   ├── thedigitalcore-integration-api.md
│   ├── site-analysis-mcp-tools.md
│   ├── site-analysis-category-result.md
│   └── chat-to-agent-routing.md
├── checklists/
│   └── requirements.md
└── tasks.md                                    # Phase 2 output (/speckit-tasks command — NOT created by /speckit-plan)
```

### Source Code (repository root)

This is the existing Ask Lucy Clean Architecture modular monolith (`docs/ARCHITECTURE.md`). This
feature extends five existing backend projects and the existing frontend SPA — it introduces zero
new projects into the `hydra` solution. It also touches a second, sibling repository
(`park-redesign`) for the new Python MCP server, which is outside this solution but is part of this
feature's overall delivery.

```text
src/
├── AskLucy.Domain/
│   └── SiteAnalysis/                                  # NEW — data-model.md
│       └── SiteAnalysisProjectLink.cs                 # + SiteAnalysisProjectLinkSource enum
│
├── AskLucy.Application/
│   ├── Abstractions/                                  # EXTENDED — existing flat convention
│   │   ├── ITheDigitalCoreClient.cs                    # research.md Decision 3
│   │   └── ISiteAnalysisProjectLinkRepository.cs
│   └── SiteAnalysis/                                   # NEW feature folder — mirrors Agents/Mcp/Panels shape
│       ├── Commands/
│       │   ├── VerifyAndLinkDigitalCoreProject/         # FR-001a-FR-001g bootstrap flow (research.md Decision 8)
│       │   ├── CreateSiteAnalysisProjectLinkFromDeepLink/# FR-024(a), research.md Decision 12
│       │   └── RelayCategoryScoreResult/                # FR-026, research.md Decision 3/6
│       ├── Queries/
│       │   └── GetSiteAnalysisProjectLink/
│       ├── Routing/
│       │   └── SiteAnalysisChatTurnRouter.cs            # research.md Decision 1 — the one new mechanism
│       └── AgentExecutionCompletion/
│           └── SiteAnalysisAgentExecutionCompletionHandler.cs  # triggers IPanelNotifier + viewer command (Decisions 10-11)
│
├── AskLucy.Infrastructure/
│   └── TheDigitalCore/                                 # NEW
│       ├── TheDigitalCoreClient.cs                      # implements ITheDigitalCoreClient (named HttpClient)
│       └── TheDigitalCoreIntegrationOptions.cs          # IOptions<T>, service-account credential (Clarifications Q2)
│
├── AskLucy.Persistence/
│   ├── Configurations/SiteAnalysis/
│   │   └── SiteAnalysisProjectLinkConfiguration.cs
│   └── Migrations/
│       └── <timestamp>_AddSiteAnalysisProjectLink.cs
│
└── AskLucy.Web/
    ├── Controllers/v1/
    │   └── SiteAnalysisController.cs                    # POST /api/v1/site-analysis/project-links (Decision 12)
    └── ClientApp/src/features/site-analysis/
        ├── DeepLinkEntry.tsx                             # thin route, calls the controller above, redirects into chat
        └── panels/
            └── SiteAnalysisCategoryResultPanel.tsx       # new Floating Panel renderer (Decision 10), + registration

tests/
├── AskLucy.Domain.Tests/SiteAnalysis/
├── AskLucy.Application.Tests/SiteAnalysis/
├── AskLucy.Infrastructure.Tests/SiteAnalysis/
├── AskLucy.Persistence.Tests/SiteAnalysis/
├── AskLucy.Web.Tests/SiteAnalysis/
└── AskLucy.E2E.Tests/
    └── SiteAnalysisDeepLinkAndBoundary.spec.ts
```

```text
# Sibling repository — outside this solution, part of this feature's overall delivery
park-redesign/
└── mcp_server/                                          # NEW — research.md Decision 4
    ├── server.py                                        # official `mcp` SDK, Streamable HTTP transport
    ├── tools/
    │   ├── resolve_site_boundary.py                     # wraps notebook Module 01
    │   ├── collect_recreation_data_layers.py             # wraps notebook Module 02 (Recreation-scoped)
    │   ├── collect_social_data_layers.py                 # wraps notebook Module 02 (Social-scoped)
    │   ├── score_recreation.py                           # wraps notebook Module 06 (Recreation)
    │   └── score_social.py                               # wraps notebook Module 06 (Social)
    └── tests/
```

**Structure Decision**: Web application (existing modular monolith) plus one new external service
in a sibling repository. No new `hydra` project is created; the Python MCP server is deliberately
kept out of this .NET solution (research.md Decision 4's "Alternatives considered").

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution Check item above is a violation — every gate passes. The table below records the
two new cross-cutting dependencies §17 requires an ADR for, per the Constitution Check's §13 row
(this is a documentation obligation, not a violation):

| New dependency | Why needed | Simpler alternative rejected because |
|---|---|---|
| New external Python MCP server (`park-redesign/mcp_server`) | The notebooks' geospatial analysis (`osmnx`/`rasterio`/`geopandas`/Earth Engine/Gemini-vision) has no .NET equivalent, and rewriting it would duplicate already-validated logic and risk behavior drift from the known-good `pipeline_results.json` reference output. | Porting the pipeline to C# — rejected: no equivalent geospatial ecosystem in .NET, and would silently change what the analysis computes, contradicting "migrate ... instead of relying on the notebook" (the pipeline's *behavior* is being preserved, not reimplemented). |
| TheDigitalCore service-account HTTP integration (`ITheDigitalCoreClient`) | This feature cannot function without a way to search/create/relay into TheDigitalCore's own Project data (FR-025's system-of-record boundary), and Clarifications Q2 explicitly rules out per-user TheDigitalCore accounts. | Modeling TheDigitalCore as an MCP server — rejected per research.md Decision 3 (forces admin-activation-lifecycle semantics onto a mandatory, non-optional integration, and TheDigitalCore is not a discoverable third-party tool source). |

Both are recorded here as the constitution §17 ADR trigger requires ("introduces a new... cross-
cutting infrastructure dependency"); authoring the actual ADRs (`docs/adr/0008-...`,
`docs/adr/0009-...`) is deferred to the implementation task list (`/speckit.tasks`), consistent with
how `specs/021-mcp-integration/plan.md` handled its own two new-dependency additions.

## Post-Design Constitution Check

*Re-checked after Phase 1 (data-model.md, contracts/, quickstart.md).* No new violation introduced
by the detailed design. Two things worth re-confirming post-design:

1. **`SiteAnalysisProjectLink` staying a pure reference, never a data mirror** (data-model.md) — the
   entity carries only `UserChatId`, the external `TheDigitalCoreProjectId`, `LinkSource`, and audit
   columns; no Project name/description/attachment fields were added during detailed design, which
   would have quietly reintroduced the "competing copy" FR-025 forbids. Confirmed still true.
2. **The chat-turn router's scope** — during Phase 1 design (contracts/chat-to-agent-routing.md), the
   router's trigger-phrase set was kept to exactly the phrases FR-001/FR-010/FR-011 and the User
   Scenarios describe (a site description, "analyze recreation," "analyze social," and a bootstrap
   confirmation), resisting the temptation to generalize it into a broader classifier now that its
   shape is concrete — matches research.md Decision 1's YAGNI rationale.

Gate: **PASS**.
