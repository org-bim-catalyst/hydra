# Data Model: Site Analysis Agent (SPEC-057)

**Date**: 2026-09-17 | **Plan**: [plan.md](./plan.md)

One new aggregate. No changes to existing aggregates except two additive fields on `Workflow`.

---

## Aggregate: `SiteAnalysis` (root)

One request to analyze one site, by one user, within one conversation. Owns its results; results are never
reachable through their own `DbSet` outside this aggregate's repository (constitution §5).

`AskLucy.Domain/SiteAnalysis/SiteAnalysis.cs`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK, `Guid.CreateVersion7()` |
| `UserId` | `string` | Owner. Every read is filtered by this (FR-019). |
| `UserChatId` | `Guid` | The conversation the analysis belongs to; the relay resolves the chat to notify from here, **not** from the tool execution context (research D2). |
| `SiteName` | `string` | Display name of the resolved site, used in notices and the image prompt. |
| `Latitude` / `Longitude` | `double` | The resolved site point. |
| `BoundaryGeoJson` | `string?` | Snapshot of the confirmed boundary when one was resolved. Null when only a point was confirmed. Snapshotted deliberately: a later boundary re-resolution must not retroactively change what an analysis was run against. |
| `Status` | `SiteAnalysisStatus` | See state machine below. |
| `WorkflowExecutionId` | `Guid?` | Link to the fan-out execution, for operator diagnosis. Null until dispatch succeeds. |
| `ExpectedResultCount` | `int` | How many specialists were dispatched. Lets the closing outcome (FR-025) be computed without re-reading the workflow definition. |
| `ClosingOutcomeReportedAtUtc` | `DateTime?` | Set when the single closing outcome is delivered; its presence is what enforces "exactly once" (FR-027) under concurrent branch completions. |
| `StartedAtUtc` | `DateTime` | |
| `CompletedAtUtc` | `DateTime?` | |
| + `BaseEntity` audit fields | | `CreatedAtUtc`/`CreatedBy`/`ModifiedAtUtc`/`ModifiedBy`/`DeletedAtUtc`/`DeletedBy` per §5 |

**Behavior (domain methods, not setters)**
- `Create(userId, userChatId, siteName, latitude, longitude, boundaryGeoJson, expectedResultCount, actor)`
- `RecordDispatched(workflowExecutionId, actor)` — attaches the execution id.
- `AddResult(...)` / `AddFailedResult(...)` — the only way a result enters the aggregate.
- `TryClaimClosingOutcome(actor)` → `bool` — atomically sets `ClosingOutcomeReportedAtUtc` if unset and returns
  whether this caller won. Guards FR-027 when branches finish simultaneously.
- `Complete(actor)` / `Fail(reason, actor)` — terminal transitions.

### State machine — `SiteAnalysisStatus`

```
Running ──► Completed     (all specialists settled, at least one succeeded)
   │
   └─────► Failed         (all specialists failed/rejected, or dispatch itself failed)
```

`Running` is set at creation. Both terminal states set `CompletedAtUtc`. An analysis MUST reach a terminal state
even when every specialist fails (FR-021).

---

## Entity: `SiteAnalysisResult` (child)

One specialist's outcome. Persisted for successes, failures, **and** rejections — a rejected result is evidence
(FR-022).

`AskLucy.Domain/SiteAnalysis/SiteAnalysisResult.cs`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `SiteAnalysisId` | `Guid` | FK → `SiteAnalysis`, indexed, cascade delete |
| `AnalysisType` | `SiteAnalysisType` | Which specialist produced it |
| `Status` | `SiteAnalysisResultStatus` | `Completed` / `Failed` / `Rejected` |
| `ContentJson` | `string?` | The panel content-block document. Null unless `Completed`. |
| `DataSource` | `string?` | e.g. `openai:<model>`, `osm-overpass`, `ai-interpretation` (FR-011) |
| `ConfidenceLevel` | `SiteAnalysisConfidenceLevel?` | Null unless `Completed` |
| `DocumentId` | `Guid?` | The persisted generated image, when the specialist produced one (FR-029) |
| `FailureReason` | `string?` | Required when `Failed`/`Rejected`. Carries the originating error detail needed to diagnose without reproducing (FR-022). |
| `CompletedAtUtc` | `DateTime` | When the specialist settled |

**Invariant**: exactly one of (`ContentJson` + `DataSource` + `ConfidenceLevel`) or `FailureReason` is populated,
enforced in the factory methods — the same "exactly one of" shape `AgentToolResult` already uses.

**Uniqueness**: one settled result per (`SiteAnalysisId`, `AnalysisType`). A specialist reports once.

---

## Enums

```
SiteAnalysisStatus            : Running | Completed | Failed
SiteAnalysisResultStatus      : Completed | Failed | Rejected
SiteAnalysisConfidenceLevel   : High | Medium | Low          // ordered, rule-assigned (research D10)
SiteAnalysisType              : SchematicImage               // this release
                              // deferred: SiteGeometry, UrbanContext, Connectivity,
                              //           EnvironmentalContext, Character
```

`SiteAnalysisType` is the extension point for FR-031: a new specialist adds one member here, one tool class, and
one provisioner branch entry.

**Serialization note**: enums serialize as strings API-wide (an existing `Program.cs` converter). Do not
re-introduce numeric enum serialization for these — every frontend comparison is string-based.

### Confidence assignment rule (FR-013)

| Level | Condition |
|---|---|
| `High` | Produced from data that directly and specifically describes the analyzed site |
| `Medium` | Produced from sparse, approximate, or nearby-only data, or requiring interpolation |
| `Low` | No grounding data available; output is general-knowledge or model inference |

The schematic image specialist reports `Medium`: the site and its coordinates are exact, but the rendering is a
generative interpretation rather than a survey.

---

## Modified: `Workflow` (existing aggregate)

Two additive fields plus a factory, mirroring `Agent.cs:102-124,338-377` exactly (research D12):

| Field | Type | Notes |
|---|---|---|
| `SystemKey` | `string?` | Stable identity of a platform-provisioned workflow. Null for every user-created workflow; **unique** among those that are not, so concurrent instances cannot double-provision. |
| `IsSystemOwned` | `bool` | Defaults `false`. Existing rows are unaffected. |

`Workflow.CreateSystemProvisioned(systemKey, name, description, workflowType, actor)` sets `OwnerId` to the
existing `"system"` convention.

---

## Persistence

**Configurations** (`AskLucy.Persistence/Configurations/`)

- `SiteAnalysisConfiguration` — owns the `SiteAnalysisResult` collection; global soft-delete query filter (§5).
- `SiteAnalysisResultConfiguration` — FK + cascade delete.

**Indexes** (§5: every `WHERE`/`JOIN`/`ORDER BY` column covered)

| Index | Serves |
|---|---|
| `(UserId, UserChatId, StartedAtUtc DESC)` | Rehydrating a conversation's analyses on open (FR-017) |
| `(SiteAnalysisId)` on results | Loading an analysis's findings |
| `(SiteAnalysisId, AnalysisType)` unique | One settled result per specialist |
| `(SystemKey)` unique filtered on `Workflow` | Provisioner upsert; mirrors the agent `SystemKey` index |

**Migration**: one logical change — create both tables, add the two `Workflow` columns and the filtered unique
index. Reversible `Down`. No destructive step; nothing is dropped.

**Concurrency**: branches settle concurrently and each writes its own result row, so contention is on the
**parent row**, not the children. `TryClaimClosingOutcome` plus a concurrency token on `SiteAnalysis` guards
FR-027; `DbUpdateConcurrencyException` is handled explicitly at the Application layer (§5), by re-reading and
conceding the claim, never surfacing as a 500.

---

## Relationships

```
UserChat 1 ──── * SiteAnalysis 1 ──── * SiteAnalysisResult
                      │                        │
                      │ WorkflowExecutionId     │ DocumentId
                      ▼ (diagnostic link)       ▼ (generated image)
              WorkflowExecution            Document
```

Both outward links are **soft references by id**, not navigation properties — `SiteAnalysis` must not pull the
Workflows or Documents aggregates into its own object graph (§5 aggregate boundaries).
