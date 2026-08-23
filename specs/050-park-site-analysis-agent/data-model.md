# Data Model: Conversational Park Site Analysis Agent

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Per research.md Decisions 6-7, this feature adds exactly **one** new persisted entity. Everything
else it needs (agent definitions, executions, tool calls, panels, viewer state) is modeled by
already-existing entities from `specs/020`, `specs/021`, `specs/027`, and `specs/028`, reused
unchanged.

## New Entity

### SiteAnalysisProjectLink (aggregate root)

The association between one Ask Lucy `UserChat` and one TheDigitalCore `Project` (spec.md's
"Project Link" Key Entity). A pure reference — never a copy of TheDigitalCore's own Project/
Company/Attachment data (FR-025).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (v7) | `BaseEntity` convention |
| `UserChatId` | `Guid` (FK → `UserChat`) | The Ask Lucy conversation this link belongs to |
| `TheDigitalCoreProjectId` | `string` | Opaque external identifier — this feature does not assume TheDigitalCore's internal key type/shape, only that it's a stable, referenceable string (contracts/thedigitalcore-integration-api.md) |
| `LinkSource` | `SiteAnalysisProjectLinkSource` enum | `InboundDeepLink` / `BootstrapCreated` / `BootstrapMatched` (FR-024, FR-001d/FR-001f) — records *how* the link was established, for audit/debugging, not behavior branching after creation |
| `SiteName` | `string` | The site name as described/matched at link time — denormalized for display only (e.g., in chat history), not a system-of-record copy; TheDigitalCore's Project name is authoritative |
| `ResolvedLatitude` / `ResolvedLongitude` | `decimal?` | The coordinates used for the geolocation-secondary match (research.md Decision 8) or bootstrap creation; nullable because a deep-link-established link (FR-024a) may arrive with the Project already fully identified and not need its own resolution |
| `CreatedAtUtc` / `CreatedBy` / `LastModifiedAtUtc` / `LastModifiedBy` | per `BaseEntity` | Populated by the existing `SaveChanges` interceptor, never set manually |
| `RowVersion` | concurrency token | Per constitution §5 |

**Business rules**:
- Exactly one `SiteAnalysisProjectLink` per `UserChatId` — enforced by a unique index (FR-001d: a
  matching Project links the conversation rather than creating a duplicate link; a conversation
  cannot represent two Projects at once in this feature's scope).
- Never hard-deleted — soft delete only (constitution §5 "Soft deletes & auditing"), consistent
  with every other user-facing, audit-relevant record in this codebase.
- Immutable once created in the fields that identify the link (`TheDigitalCoreProjectId`,
  `LinkSource`) — a conversation's Project association does not silently change; a user who wants a
  different site starts a new conversation (matches spec's FR-005 "unless the user names a different
  site" being scoped to boundary reuse, not to changing an established Project link).

**Domain events**: `SiteAnalysisProjectLinked` (raised on successful creation, whichever
`LinkSource`), `SiteAnalysisProjectLinkFailed` (raised when TheDigitalCore search/create fails after
retries — carries enough context for the no-silent-failures requirement to surface a visible error,
constitution §2.VIII).

**Indexes**: unique index on `UserChatId`; non-unique index on `TheDigitalCoreProjectId` (reverse
lookup, e.g. "which conversation(s) relate to this Project" for support/debugging).

## Reused Entities (no schema changes)

| Entity | From | Reused for |
|---|---|---|
| `UserChat` | existing Chat Engine | The conversation a `SiteAnalysisProjectLink` attaches to; the `UserChatId` passed to `StartAgentExecutionCommand` (research.md Decision 1) |
| `Agent` / `AgentVersion` | `specs/020` | The one pre-published "Site Analysis Agent" (research.md Decision 2) — `Type = Task`, `OutputFormat = Json`, tool list = the 5 new MCP tools + built-in Knowledge Search |
| `AgentExecution` / `AgentExecutionStep` / `AgentExecutionEvent` / `AgentToolCall` / `AgentExecutionError` | `specs/020` | One short `AgentExecution` per qualifying chat turn (research.md Decision 1); `FinalOutputJson` holds the Category Score Result shape (contracts/site-analysis-category-result.md, research.md Decision 6); `AgentToolCall` rows satisfy FR-023's audit requirement with zero new tables |
| `AgentApproval` / `AgentPolicy` | `specs/020` | Conflicting/ambiguous tool results pause here (research.md Decision 9, FR-018) — no new interruption entity |
| `McpServer` / `McpTool` | `specs/021` | The new Python MCP server and its 5 tools register as ordinary rows here — no schema change, no new entity type |
| `KnowledgeBase` / `Document` / `DocumentChunk` / `Embedding` | `specs/016` | The new "Site Analysis Knowledge Base" is an ordinary `KnowledgeBase` created through the existing KB Engine UI/API — no new entity |
| `PanelRequest` (transient, not persisted — `specs/028`) | `specs/028` | The Category Score Result's presentation surface (research.md Decision 10) |

## Explicitly Not Modeled (and why)

- **`CategoryAnalysisRun` / `CategoryScoreResult` / `DataGapIndication` as database tables** — these
  are spec.md Key Entities at the *business* level, but per research.md Decision 6 they are realized
  as the JSON shape of `AgentExecution.FinalOutputJson` (contracts/site-analysis-category-result.md),
  not new tables. Adding dedicated tables here would be exactly the "competing copy" of
  TheDigitalCore's `SiteAnalysis`/`DesignRecommendation` records FR-025 forbids, and would duplicate
  what `AgentExecution`/`AgentExecutionStep`/`AgentToolCall` already capture (constitution §3 DRY).
- **`Project` / `Company` / `Attachment` / `SiteAnalysis` / `DesignConcept` / `DesignRecommendation`**
  — these remain entirely TheDigitalCore's schema (FR-025); this repository's migrations never
  create or reference them directly, only the opaque `TheDigitalCoreProjectId` string on
  `SiteAnalysisProjectLink`.
- **A `SiteBoundary` table** — the resolved boundary (spec.md Key Entity) is the *output* of the
  `resolve_site_boundary` MCP tool call, already captured by that call's `AgentToolCall` row (input/
  output JSON) and by the Immersive Viewer's own in-session client state once rendered
  (research.md Decision 11) — it does not need server-side persistence beyond the audit trail
  `AgentToolCall` already provides. Re-asking `resolve_site_boundary` is cheap and idempotent
  (FR-005's "reuse" requirement is satisfied at the conversation-context level the chat-turn router
  already assembles — research.md Decision 1 — not by a dedicated boundary table).
