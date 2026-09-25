# Data Model: Admin Operational Failure Audit Trail

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Research**: [research.md](research.md)

There are four new tables in one additive migration, `AddOperationalFailureAudit`. None of them has
a foreign key to a user, chat, workflow, document or other module table. They hold soft references,
following the `McpAuditLog` precedent, so deleting those items never cascades into the trail and
the trail never blocks their deletion. The only foreign keys are internal to the feature:

- `Incident → Occurrence`, cascade.
- `Incident → Participant`, cascade.

There is no cascade diamond. `UserContentAccessEvents` has no foreign keys at all.

Enums are stored as strings (`HasConversion<string>()`, max length 40). Times are `datetime2` UTC.

---

## Enums (Domain, `AskLucy.Domain/OperationalFailures/`)

| Enum | Members |
|---|---|
| `OperationalFailureEngine` | `Chat`, `AiProvider`, `Voice`, `Embeddings`, `DocumentProcessing`, `ImageGeneration`, `Agent`, `Workflow`, `Mcp`, `BackgroundJob`, `Access` |
| `OperationalFailureKind` | The 9 `AiProviderFailureKind` names (same spelling) + `UnexpectedError`, `TimedOut`, `DependencyUnreachable`, `ValidationFailed`, `JobFailedAfterRetries`, `SignInRefused`, `TwoFactorRefused`, `AccountLocked`, `AccessDenied` (research D4) |
| `OperationalFailureSeverity` | `Warning` = 1, `Error` = 2, `Critical` = 3. Ordered so that `max()` gives the highest. |
| `OperationalFailureOutcome` | `Failed`, `DegradedServed`, `RecoveredByRetry`. Input only, never stored. |
| `IncidentTriageState` | `Open`, `Acknowledged`, `Resolved` |
| `IncidentParticipantType` | `User`, `Source` |
| `InvestigatedItemType` | `Chat`, `WorkflowRun`, `Document` |

`OperationalFailureSeverityPolicy.Classify(engine, kind, outcome)` implements FR-009 (research D5).

---

## `OperationalFailureIncidents` (aggregate root, the only mutable part)

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `GroupingKey` | `char(64)` | SHA-256 hex of `engine\|provider\|model\|kind\|operation\|subjectType\|subjectId`, with each part trimmed and lower-cased, and empty when it does not apply (FR-018) |
| `RootCauseKey` | `char(64)` | Provider kinds use `provider\|model\|kind`. All other kinds use `engine\|kind\|operation` (FR-026a). |
| `Engine` | `nvarchar(40)` | |
| `Operation` | `nvarchar(120)` | For example "Chat reply", "Index document", "Job: MemoryExtractionJob", "Sign-in" |
| `ProviderId` | `uniqueidentifier` null | Soft reference to `AIProviders`, used for the health column (FR-017) and the deep link |
| `ProviderName` | `nvarchar(100)` null | Denormalised, so it survives provider removal |
| `Model` | `nvarchar(200)` null | |
| `Kind` | `nvarchar(40)` | |
| `SubjectType` | `nvarchar(40)` null | `Workflow`, `Agent`, `Document` or `McpServer` |
| `SubjectId` | `uniqueidentifier` null | |
| `SubjectLabel` | `nvarchar(200)` null | Name at the time it was recorded, for a readable list even after the subject is deleted |
| `HighestSeverity` | `nvarchar(40)` | The maximum of the occurrences' severities |
| `FirstSeenUtc` / `LastSeenUtc` | `datetime2` | |
| `OccurrenceCount` | `int` | Every occurrence, including those beyond the cap |
| `StoredOccurrenceCount` | `int` | ≤ `MaxStoredOccurrencesPerIncident` (FR-022: "showing 1,000 of 12,408") |
| `DistinctUserCount` / `DistinctSourceCount` | `int` | Maintained from the participants table (research D10) |
| `RecoveryCount` | `int` | Voice `RecoveredToPrimary` markers (US2 scenario 3) |
| `LatestReason` | `nvarchar(500)` | The sanitised reason of the newest occurrence, shown in the list |
| `LatestCorrelationId` | `nvarchar(64)` | |
| `TriageState` | `nvarchar(40)` | Default `Open` |
| `AcknowledgedByUserId` / `AcknowledgedAtUtc` | `nvarchar(450)` null / `datetime2` null | |
| `ResolvedByUserId` / `ResolvedAtUtc` / `ResolutionNote` | null / null / `nvarchar(500)` null | |
| `RecurrenceOfIncidentId` | `uniqueidentifier` null | A soft reference with no foreign key, because retention may already have removed the earlier incident |
| `RowVersion` | `rowversion` | Optimistic concurrency for transitions (FR-024) |

**Indexes**
- `UX_Incidents_GroupingKey_Unresolved`: unique on `GroupingKey`, filtered `[TriageState] <> N'Resolved'`
  (research D3).
- `IX_Incidents_State_LastSeen` on `(TriageState, LastSeenUtc DESC)` including `HighestSeverity`,
  `Engine`, `ProviderName` and `Kind`. This serves the default list.
- `IX_Incidents_LastSeen` on `(LastSeenUtc DESC)`. This serves the filtered list and retention.
- `IX_Incidents_RootCause` on `(RootCauseKey, TriageState)`. This serves related incidents and the
  bulk transitions.
- `IX_Incidents_Badge` on `(HighestSeverity, TriageState, RootCauseKey)`, filtered
  `[TriageState] = N'Open'`. The badge is `COUNT(DISTINCT RootCauseKey) WHERE HighestSeverity =
  'Critical' AND TriageState = 'Open'`.

**State transitions** (domain methods; each sets who/when and returns a result rather than
throwing on a no-op):

```text
Open ──acknowledge──▶ Acknowledged ──resolve(note?)──▶ Resolved
  └──────────────resolve(note?)────────────────────────▲    │
                                                            └──reopen──▶ Open  (clears Ack/Resolve fields)
```

- `Acknowledge` on `Acknowledged` or `Resolved` returns `AlreadyInState`, which the bulk
  transitions skip (FR-026b).
- `Reopen` is allowed only when no other unresolved incident holds the same `GroupingKey`, because
  the filtered unique index would reject it. The UI shows "A newer incident for this failure is
  already open", with a link.
- A new occurrence never changes `TriageState`: acknowledged stays acknowledged (US2 scenario 5).
  After `Resolved`, new occurrences open a new incident (US2 scenario 4).

---

## `OperationalFailureOccurrences` (append-only; retention and erasure are the only writers after insert)

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `IncidentId` | `uniqueidentifier` FK → Incidents, cascade | |
| `OccurredAtUtc` | `datetime2` | Taken on the caller's thread (research D2) |
| `Severity` / `Kind` / `Engine` | `nvarchar(40)` | |
| `Operation` | `nvarchar(120)` | |
| `ProviderName` / `Model` | null | |
| `Reason` | `nvarchar(500)` | Sanitised, single line (FR-011–FR-013) |
| `CorrelationId` | `nvarchar(64)` | Never null (research D6) |
| `IsFailover` | `bit` | Voice (FR-007) |
| `UserId` | `nvarchar(450)` null | Cleared on erasure |
| `ChatId`, `MessageId`, `WorkflowId`, `WorkflowExecutionId`, `WorkflowExecutionNodeId`, `DocumentId`, `KnowledgeBaseId`, `AgentId`, `AgentExecutionId`, `McpServerId` | `uniqueidentifier` null | References only (FR-012) |
| `JobId` | `nvarchar(100)` null | Hangfire job id |
| `SourceIp` | `nvarchar(45)` null | Access engine only (FR-012a); cleared on erasure |
| `IsUserErased` | `bit` | Shows "erased user" (FR-029a) |

**Indexes**
- `IX_Occurrences_Incident_OccurredAt` on `(IncidentId, OccurredAtUtc DESC)`. This serves the
  occurrence list (FR-019) and the per-incident retention trim.
- `IX_Occurrences_OccurredAt` on `(OccurredAtUtc)`. This serves retention.
- `IX_Occurrences_UserId` on `(UserId)`, filtered `UserId IS NOT NULL`. This serves erasure.
- `IX_Occurrences_ChatId`, `…_WorkflowExecutionId` and `…_DocumentId`, each filtered to non-null.
  These serve the investigation "is this item referenced by this incident" check (research D15).

---

## `OperationalFailureIncidentParticipants`

| Column | Type | Notes |
|---|---|---|
| `IncidentId` | FK → Incidents, cascade | |
| `ParticipantType` | `nvarchar(10)` | `User` or `Source` |
| `ParticipantKey` | `nvarchar(450)` | A user id or IP. Erasure rewrites it to `erased:{guid}`. |
| `FirstSeenUtc` | `datetime2` | |

The PK is `(IncidentId, ParticipantType, ParticipantKey)`. `IX_Participants_Key` on
`(ParticipantKey, ParticipantType)` includes `IncidentId`. It serves the admin "filter by user"
(`EXISTS`) and erasure.

---

## `UserContentAccessEvents` (immutable audit; never purged)

| Column | Type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `OccurredAtUtc` | `datetime2` | |
| `ViewerUserId` | `nvarchar(450)` | Who opened it |
| `OwnerUserId` | `nvarchar(450)` null | Whose content it was. Cleared on the owner's erasure. |
| `ItemType` | `nvarchar(40)` | `InvestigatedItemType` |
| `ItemId` | `uniqueidentifier` null | Cleared on the owner's erasure |
| `IncidentId` | `uniqueidentifier` | A soft reference to the incident it was reached from. It survives the incident's retention. |
| `CorrelationId` | `nvarchar(64)` | |
| `IsOwnerErased` | `bit` | |

Indexes: `(OwnerUserId)` filtered to non-null, which serves erasure; and `(OccurredAtUtc DESC)`.
The only update is erasure anonymisation (FR-029a), and no delete path exists. The repository
interface exposes `AddAsync` and `AnonymizeOwnerAsync` only.

---

## Application-level types (not persisted)

**`OperationalFailureReport`** is the recorder input. It is a record with init-only properties:

- **Required:** `Engine`, `Operation`, `Kind`, `Outcome`, `Reason` (system prose).
- **Optional:**
  - `Exception?`. Only its type name is ever used, as a fallback reason.
  - `ProviderId?`, `ProviderName?`, `Model?`
  - `Subject? (Type, Id, Label)`
  - `UserId?`
  - chat, message, workflow, execution, node, document, knowledge base, agent, agent execution and
    MCP server ids
  - `JobId?`, `SourceIp?`, `IsFailover`

There is deliberately **no** field for request or response bodies, prompt text, file names,
passwords, codes or tokens (FR-012, FR-012a). `CorrelationId` and `OccurredAtUtc` are stamped by
the recorder.

**`VoiceRecoveryReport`** carries `ProviderName`, `Model`, `Kind`, `Operation` and `UserId`. It
increments `RecoveryCount` on the *open* incident with the matching key, and is ignored when there
is none (spec Edge Cases).

**`CorrectiveAction`** is `{ Text, AdminRoute?, AdminAction? }`. It comes from
`CorrectiveActionCatalog.For(engine, kind, providerId, subject)` (Application, code-owned per the
spec's Key Entities) and is computed at read time, never stored. The mapping:

| Engine / kind | Text | Route |
|---|---|---|
| Provider kinds `CredentialRejected`, `CredentialUnreadable` (Chat, AiProvider, Embeddings, ImageGeneration) | Replace the API key | `/admin/ai-providers?select={providerId}` |
| Voice + credential kinds | Replace the API key | `/admin/voice?select={providerId}` |
| `NotConfigured` | Enable or configure the capability | `/admin/ai-capabilities` |
| `QuotaExhausted`, `UsageRestricted` | Raise the quota in the vendor console, or switch the default model | `/admin/default-models` |
| `RequestInvalid`, `ResponseNotUnderstood` with a model | Check the model is still offered; switch the default model if retired | `/admin/default-models` |
| `RateLimited`, `Unavailable`, `TimedOut`, `DependencyUnreachable` | No action needed unless this persists | *(none)* (FR-014) |
| Mcp | Check the server's connection and credentials | `/admin/mcp-servers?select={serverId}` |
| BackgroundJob | Inspect the failed job in the jobs dashboard | No route. `AdminAction = OpenJobsDashboard` reuses the nav's existing action-triggered Hangfire entry (specs/060), which mints a session and opens a new tab |
| Access `SignInRefused` / `AccountLocked` | Review the account if attempts continue | `/admin/users?search={email}` |
| Access `AccessDenied` | No action needed unless this persists | *(none)* |
| Workflow / Agent / DocumentProcessing / others | Open the item to see which step failed | Investigation route |

---

## Permission catalogue changes (Domain)

- `AdminArea.OperationalFailures`.
- `admin.operational-failures.view` (Read).
- `admin.operational-failures.manage` (Write). It implies view, following the existing
  view/manage pairs.
- `admin.operational-failures.content.view` (Read). It is listed in
  `AdminPermissionCatalog.SuperUserControlledKeys`.

Storage for the Administrator grant: the existing role-permission grants of the built-in
Administrator `ApplicationRole`. Only `content.view` is ever stored there; the resolver ignores
every other stored key on a built-in role (research D14).

---

## Audit columns

- Incidents, occurrences and content-access events derive from `BaseEntity`, so the existing
  SaveChanges interceptor stamps `CreatedAtUtc`/`CreatedBy` and `ModifiedAtUtc`/`ModifiedBy`
  (constitution §5).
- `OperationalFailureIncidentParticipants` (`IncidentParticipant`) is deliberately a plain child row, not a `BaseEntity`: it is keyed by
  `(IncidentId, ParticipantType, ParticipantKey)`, inserted set-based (`INSERT … WHERE NOT
  EXISTS`) and never tracked, updated through the change tracker or soft-deleted, so a surrogate
  id, row version and audit columns would never be written. `FirstSeenUtc` is its timestamp.
- Rows the background writer inserts have no request user: `CreatedBy = "system"`.
- Set-based writes (occurrence upsert counters, recovery increment, retention, anonymisation) are
  bookkeeping. They bypass the interceptor and deliberately leave `ModifiedAtUtc`/`ModifiedBy`
  untouched, as all existing `ExecuteUpdateAsync` uses in the repo do. `LastSeenUtc` records
  incident activity. `IsUserErased`/`IsOwnerErased` record anonymisation.
- Append-only rows (occurrences, participants, content-access events) keep `ModifiedAtUtc` null.
- Retention hard-deletes by design (FR-029), using `ExecuteDeleteAsync`, which bypasses the
  interceptor's soft-delete conversion. This is operational telemetry, not a user-facing record,
  so the constitution's "hard delete only for GDPR erasure" rule doesn't apply. None of the four
  configurations adds a soft-delete query filter.
- Triage transitions go through the tracked aggregate, so the interceptor stamps the acting admin.

---

## Migration notes

- One migration, `AddOperationalFailureAudit`. It creates the four tables and indexes above. There
  is no data backfill and no seed; the permissions are code-catalogued and need no rows.
- Repo conventions (memory note):
  - BOM-less file;
  - `System.*` usings first;
  - generated and snapshot-checked with `dotnet ef migrations has-pending-model-changes`.
- **Apply to both databases by hand**: the shared test DB (`appsettings.Development.json`) and
  production (`appsettings.Production.json`). They are different catalogs.
- Cascade check: the only cascades are `Incident → Occurrence` and `Incident → Participant`, a
  single parent with no diamond. SQL Server error 1785 cannot arise.
- The filtered unique index uses the string value `N'Resolved'`, which matches the string enum
  converter.
- The same migration adds a nullable `CorrelationId nvarchar(64)` column, with a nonclustered
  index, to `RoleAuditLogs` and `McpAuditLogs` (FR-006b). Both entities implement `ICorrelated`,
  and the existing SaveChanges audit interceptor stamps it from `ICorrelationIdAccessor.Current` on
  insert when it is null. Existing rows stay null; there is no backfill.
