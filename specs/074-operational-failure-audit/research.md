# Research: Admin Operational Failure Audit Trail

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Date**: 2026-09-25

Each decision lists what was chosen, why, and the alternatives that were rejected. Every codebase
fact below was checked against the tree on 2026-09-25 (`a47f689c`).

---

## D1 — One cross-cutting store behind a recorder abstraction

**Decision.** Add one new store (incidents + occurrences) in a new `OperationalFailures` feature
folder in each layer. Engines depend only on the Application interface
`IOperationalFailureRecorder`. The existing trails are linked, not copied: provider health columns,
`VoiceProviderFailoverEvents`, and the eight per-module audit logs. The full reasoning is in the
spec's Design Decision section and is not repeated here.

**Rejected.**
- A union view over the eight audit logs. Those logs have no severity, kind, provider/model or
  triage state, and chat, voice, embeddings and jobs have no audit log at all.
- Adding triage columns to each audit log. That breaks their append-only guarantee (§8).

## D2 — Recording never blocks: a bounded in-memory channel plus a background writer

**Decision.** `IOperationalFailureRecorder.Record(OperationalFailureReport)` is **synchronous,
`void`, and never throws**. The Infrastructure implementation, `ChannelOperationalFailureRecorder`,
is a singleton. It:

1. Captures the ambient correlation id (D6) and the time. It does this on the caller's thread,
   because both are lost once the work leaves the request.
2. Calls `TryWrite` on a `Channel.CreateBounded<OperationalFailureReport>` with capacity 10,000,
   `FullMode = Wait`, `SingleReader = true`. `TryWrite` never waits: when the channel is full it
   returns `false`.
3. When the write is refused, logs `OperationalFailureDropped` (Warning) with correlation id,
   engine and kind, and returns. The original exception is untouched (FR-021).
4. Wraps everything in `try/catch (Exception)` that logs `OperationalFailureRecordingFailed` and
   returns. It never rethrows into the caller.

`OperationalFailureWriterService` (a `BackgroundService`) reads batches of up to 100 reports. For
each batch it creates a fresh DI scope and calls the Application ingestor (D3). A failed batch is
logged per report (correlation id, engine, kind) and the loop continues. On shutdown it drains
for at most 5 seconds and then logs how many reports were abandoned.

**No recursion (FR-021).** The writer and ingestor never call `IOperationalFailureRecorder`.
Their own failures go to the log only.

**Why.**
- `TryWrite` is an in-memory enqueue measured in microseconds, far inside SC-004's 5% / 20 ms
  budget.
- Its own DI scope means the writer never touches the request's `DbContext`. That was the cause of
  the concurrent-DbContext 500 recorded in memory, and the workflow Parallel-branch issue has the
  same shape.
- The repo already uses `Channel<T>` (`SubAgentDelegator`) and `BackgroundService`
  (`ProviderHealthCheckHostedService`). Nothing new is added.

**Accepted trade-off.** Reports still in the channel are lost if the process crashes. They are
already in the server log, because every recording site logs before it records. The admin trail
is a triage aid, not the system of record for the log.

**Rejected.**
- Awaiting the write inside the request. It violates FR-020 and ties the request's latency to the
  store's health.
- `Task.Run` fire-and-forget per failure. It is unbounded under a storm, and a rejected task is
  an unobserved exception.
- A Hangfire job per failure. That is a SQL round-trip on the request path (the enqueue itself),
  and it floods Hangfire storage during a storm.
- MediatR notifications. They are dispatched in-process on the caller's thread and scope, which
  gives the same blocking and DbContext-sharing problems.

## D3 — Ingestion: one upsert per report, race-safe by a filtered unique index

**Decision.** `OperationalFailureIngestor` (Application) runs inside the writer's scope and, for
each report:

1. **Sanitises** the reason (D8).
2. **Derives** the kind and severity (D4, D5).
3. **Computes** `GroupingKey` and `RootCauseKey`: SHA-256 hex over the normalised,
   `|`-joined parts (FR-018, FR-026a).
4. **Upserts** the open incident through `IOperationalFailureStore.AppendAsync`. That Persistence
   method:
   - finds the open incident for the key;
   - if there is none, inserts one, marking it a recurrence when a resolved incident with the same
     key exists;
   - bumps the counters with `ExecuteUpdateAsync`:
     `OccurrenceCount + 1`, `LastSeenUtc = max(...)`,
     `HighestSeverity = max(...)`, and `StoredOccurrenceCount + 1` only while under the cap;
   - inserts the occurrence row only while under the cap (FR-022);
   - inserts the participant rows (D10) if absent and increments the distinct counts only when a
     row was actually inserted.

**Concurrency.** A filtered unique index on `GroupingKey WHERE TriageState <> 'Resolved'` allows
exactly one open incident per key. A duplicate-key `DbUpdateException` on insert is caught *in
Persistence*, and the method re-reads and joins the winner. Application never references EF Core;
see the memory note on that rule. The writer is single-reader, so this race only arises across
processes, which cannot happen on today's single IIS instance. It is still enforced, because
Web.Tests run the host in parallel.

**Acknowledged-not-resolved** incidents keep the key, so a recurrence joins them without being
re-flagged (US2 scenario 5). **Resolved** incidents are outside the filtered index, so a recurrence
opens a new row with `RecurrenceOfIncidentId` set (US2 scenario 4).

**Rejected.**
- Computing the grouping at query time (`GROUP BY` over occurrences). That does not scale to
  SC-007's 100k rows with 2 s latency, and it cannot carry per-incident triage state.
- A time-window grouping ("same key within N minutes"). It splits one long outage into many
  incidents. The spec groups by open/resolved state, not by time.

## D4 — Failure kind vocabulary: a new Domain enum that mirrors the provider kinds by name

**Decision.** `OperationalFailureKind` (Domain) has these members:

- **The nine `AiProviderFailureKind` names, unchanged:** `CredentialRejected`,
  `CredentialUnreadable`, `NotConfigured`, `QuotaExhausted`, `RateLimited`, `UsageRestricted`,
  `Unavailable`, `RequestInvalid`, `ResponseNotUnderstood`.
- **Non-provider kinds:** `UnexpectedError`, `TimedOut`, `DependencyUnreachable`,
  `ValidationFailed`, `JobFailedAfterRetries`, `SignInRefused`, `TwoFactorRefused`,
  `AccountLocked`, `AccessDenied`.

`OperationalFailureKinds.FromProvider(AiProviderFailureKind)` is a total `switch`. A unit test
asserts that every `AiProviderFailureKind` member maps to the same-named member, so adding a
provider kind breaks the build's tests rather than silently mapping to `UnexpectedError`. Kinds are
stored as strings, as the enum string converter requires API-wide (memory note).

`IFailureClassifier` (Application, pure) maps an exception to a kind:
- `AiProviderException` → its `Kind`;
- `TimeoutException` / `TaskCanceledException` that was *not* caused by the caller's token →
  `TimedOut`;
- `HttpRequestException` / `SocketException` → `DependencyUnreachable`;
- anything else → `UnexpectedError`, with the exception **type name** as the reason (spec Edge
  Cases).

**Rejected.** Adding the non-provider kinds to `AiProviderFailureKind`. That enum is persisted on
`AIProvider.HealthFailureKind` and drives the provider-health UI; access or job kinds do not
belong there.

## D5 — Severity is derived, never passed

**Decision.** Callers pass an **outcome**: `Failed`, `DegradedServed` (failover or fallback served
the user), or `RecoveredByRetry`. They never pass a severity.
`OperationalFailureSeverityPolicy.Classify(engine, kind, outcome)` is a pure Domain function that
implements FR-009:

- `Access` engine → Warning.
- The Critical kinds → Critical, **whatever the outcome**. A rejected ElevenLabs key is Critical
  even though the fallback served the user (spec Assumptions).
- `RateLimited` → Warning.
- `DegradedServed` or `RecoveredByRetry` → Warning.
- Everything else → Error.

## D6 — Correlation id: one accessor for requests and jobs

**Decision.** There is no correlation abstraction today; `CorrelationIdMiddleware` only writes
`HttpContext.Items` and the Serilog `LogContext`. Add `ICorrelationIdAccessor` (Application)
with a `string? Current` property. `CorrelationIdAccessor` (Infrastructure) resolves it in this
order:

1. an `AsyncLocal<string?>` set by the Hangfire filter (D7);
2. otherwise `IHttpContextAccessor.HttpContext.Items[...]`, via the key the middleware already
   uses (exposed as a constant);
3. otherwise `null`. The ingestor then stores a freshly generated id and logs it on the
   `OperationalFailureRecorded` Debug line, so every record still has an id that appears in the
   log.

The recorder reads the accessor **on the caller's thread** (D2 step 1).

## D7 — Background jobs: a global Hangfire filter for correlation and final failure

**Decision.** `OperationalFailureJobFilter` implements `IServerFilter` and `IElectStateFilter`. It
is registered once through `config.UseFilter(...)` inside the existing `AddHangfire((sp, config) =>
…)` block, which has `sp` available to resolve the singleton recorder.

- **`OnPerforming`**:
  - reads the job parameter `CorrelationId`, or creates and stores one on the first attempt, so
    every retry of one job instance shares an id;
  - sets the `AsyncLocal` (D6);
  - pushes `LogContext.PushProperty("CorrelationId", …)`.
- **`OnPerformed`**: disposes both.
- **`OnStateElection`**: records one occurrence when `context.CandidateState is FailedState`.
  - The filter is registered with `Order` **greater than** `AutomaticRetryAttribute`'s. Hangfire
    runs election filters in ascending order, so this filter sees the state after the retry filter
    has already turned a retryable failure into `ScheduledState`. It therefore only ever sees the
    **final** failure (US4 scenario 3; retries are not recorded, per spec Assumptions).
  - The occurrence has engine `BackgroundJob` and kind `JobFailedAfterRetries`. Operation is the
    job type name; the classifier (D4) builds the reason from `FailedState.Exception`. When the job
    method's arguments carry an obvious subject (a document id or workflow execution id), it is
    **not** extracted generically; D12 covers engines that record their own subject.
- Jobs whose own code already records a richer occurrence (the document pipeline, the workflow
  orchestrator) mark the exception (D11), and the filter skips marked exceptions.

**Verified.** No `GlobalJobFilters`, `IApplyStateFilter` or `IElectStateFilter` exists in the repo
today, so there is no conflicting filter. `CustomModelDeploymentJob` uses
`[AutomaticRetry(Attempts = 0)]`, so its first failure is final. That is correct.

**Rejected.** `IApplyStateFilter.OnStateApplied` for `FailedState`. It works too, but
`OnStateElection` is where the retry decision is visible, which keeps "final only" explicit.

## D8 — Sanitisation: a pure redactor applied at ingestion

**Decision.** `FailureReasonSanitizer` (Application, pure, unit-tested against a corpus) runs in
the ingestor. It is not left to callers, so no caller can forget it. Steps:

1. Collapse whitespace to a single line.
2. Redact:
   - `Authorization:`/`Cookie:`/`Set-Cookie:` header values;
   - `Bearer <token>`;
   - JWT-shaped `xxx.yyy.zzz` base64url triples;
   - `sk-…`, `sk-ant-…`, `AIza…`, `xi-…` key shapes and any 32+ character hex or base64 run;
   - `key=`/`api_key=`/`token=`/`sig=`/`password=` query or `k=v` values;
   - `Password=`/`Pwd=`/`User ID=` connection-string segments.

   Each match becomes `[redacted]`.
3. Truncate to 500 characters with an ellipsis (FR-013).

**Callers pass system prose**, which FR-011 requires: the provider adapters already build their
`AiProviderException` messages from the classification, and the classifier uses the exception type
for unknown exceptions. The sanitizer is defence in depth for the cases where a vendor message
leaks through (`TextToSpeechStreamer` today passes `Truncate(ex.Message)`). SC-006's seeded-secret
scan runs over the stored reasons.

**No new dependency.** The redactor uses `System.Text.RegularExpressions` with
`[GeneratedRegex]` and a timeout.

## D9 — User-facing calm: two sentences, one place

**Decision.** Add `UserFacingFailureText` (Application, constants) with exactly two sentences:

- `Retry`: "Something went wrong and I couldn't finish. Please try again."
- `Later`: "This isn't available right now. Please try again later." This is used for
  `NotConfigured`, `CredentialRejected`, `CredentialUnreadable`, `QuotaExhausted` and
  `UsageRestricted`, where retrying cannot help (FR-005, consistent with specs/068).

Changes by call site:

- **`ProblemDetailsMiddleware.MapProviderFailure`**: the **non-administrator** `detail` becomes the
  sentence for its kind. The `providerFailure` extension stays administrator-only (FR-003, as
  today). Title and status do not change, so no client branching changes.
- **`AiController.DescribeTurnFailure`**: now returns only the generic sentence. It is used for the
  streamed notice, `RecordedTurnOutcome.Reason` and, through it, `RecentTurnOutcomeSummary` and the
  `__TURN_OUTCOME__` event (FR-002, US1 scenario 3). The classification moves to the recorder call
  in the same `catch`, which fixes FR-004's "unexpected error" misclassification.
- A test enumerates every kind as a non-administrator and asserts that no user-visible string
  contains the SC-001 word list. The test is per surface: Problem Details, stream notice, turn
  outcome and replayed context.

## D10 — Distinct users and sources survive the storm cap and erasure

**Decision.** Distinct-count inputs live in `OperationalFailureIncidentParticipants`, not in
occurrences. It has a composite key of `(IncidentId, ParticipantType, ParticipantKey)`.
`ParticipantType` is `User` or `Source` (the Access engine's source IP). The ingestor inserts a
participant only if it is absent, and increments `DistinctUserCount` or `DistinctSourceCount` only
when a row was actually inserted.

**Why a separate table:**
- The occurrence cap (FR-022) stops storing occurrences after 1,000, but distinct counts must stay
  exact.
- Erasure (FR-029a) rewrites `ParticipantKey` to `erased:{random-guid}` for that user, so the
  count is unchanged.
- The admin "filter by user" becomes an indexed `EXISTS` over this table.

## D11 — Exactly one occurrence per failure: record at the site that knows, mark the exception

**Decision.** There are two recording tiers:

1. **Site recording.** Engines that handle a failure themselves, or know more than the boundary,
   record at that point:
   - the mid-stream chat `catch`;
   - voice failover;
   - the document pipeline's `stage.Fail` and `job.Fail`;
   - workflow node or execution failure;
   - agent step failure;
   - MCP refresh or test and tool-call failure;
   - image generation;
   - the provider health check.

   If the exception then propagates, the site marks it:
   `ex.Data[OperationalFailureMarkers.Recorded] = true`, through the extension
   `ex.MarkOperationalFailureRecorded()`.
2. **Boundary recording.** `ProblemDetailsMiddleware` records any **system-side** exception that
   reaches it and is not marked. That covers `AiProviderException` on non-stream calls,
   unhandled 500s, and timeouts. The Hangfire filter (D7) does the same for jobs.

**Not recorded** at the boundary (FR-006a): FluentValidation `ValidationException`, a plain
`KeyNotFoundException`, `OperationCanceledException` when `RequestAborted` is signalled, 429 from
the platform's own rate limiter (which never reaches the middleware as an exception), and
`SecurityTokenExpiredException` / refresh flows.

## D12 — Ownership refusals vs genuine not-found: a subclass that keeps the 404

**Decision.** Add `OwnershipDeniedException : KeyNotFoundException` (Application/Common). Each of
the 14 `*OwnershipGuard` classes changes the same way:

```csharp
if (chat is null) throw new KeyNotFoundException("Chat not found.");
if (!chat.IsOwnedBy(userId)) throw new OwnershipDeniedException("Chat not found.", "Chat", chat.Id);
```

- Same message and same 404 status, so nothing about "exists but not yours" leaks.
- Every existing `catch (KeyNotFoundException)` and test still matches.
- `ProblemDetailsMiddleware` gains an arm *before* the `KeyNotFoundException` arm. It records an
  `Access / AccessDenied` occurrence with the item type (never its content) and then falls through
  to the same 404 response.

**Rejected.**
- Recording every `KeyNotFoundException`. It records genuine not-found, which FR-006a forbids.
- Returning 403 for ownership refusals. That changes the API contract and leaks existence.

## D13 — Access engine hook points

**Decision.**

- **403 (permission denied):** `PermissionDeniedAuditResultHandler` already writes
  `RoleAuditAction.AuthorizationDenied`. It additionally records `Access / AccessDenied`
  (operation = the required permission key) with the same correlation id (FR-006b).
- **Sign-in:** `LoginCommand` gains `string? SourceIp`, filled by `AuthController` from
  `HttpContext.Connection.RemoteIpAddress`, the same way `RequestPasswordResetCommand` already gets
  its IP. `IdentityService.ValidateCredentialsAsync` returns the **matched user id** on a wrong
  password internally. The client still gets the same `InvalidCredentials` with no id. The handler
  records:
  - wrong password → `SignInRefused`, user = the matched account;
  - unknown email → `SignInRefused`, no user reference (FR-012a);
  - `LockedOut` → `AccountLocked`;
  - suspended → `AccountLocked`;
  - `EmailNotConfirmed` → **not recorded**, because it is user-state guidance, not a refusal of a
    credential. This is a validation-class outcome under FR-006a.
- **2FA:** the `ValidateTwoFactorCodeAsync` failure path records `TwoFactorRefused`.
- **External login:** a refused `ResolveExternalLoginAsync` callback records `SignInRefused` with
  operation `External sign-in: {provider}`.
- Operation strings are fixed, so a password-guessing burst is **one** incident (spec Edge Cases).
  The password, code and tokens are never part of a report: the report type has no field for them.

## D14 — *View user content*: the one permission a built-in role does not get automatically

**Decision.**

- **Catalogue.** Add `AdminArea.OperationalFailures` and three keys:
  - `admin.operational-failures.view`
  - `admin.operational-failures.manage`
  - `admin.operational-failures.content.view`

  `AdminPermissionCatalog` gains `SuperUserControlledKeys = { content.view }`.
- **Resolver.**
  - `PermissionSet.Full` keeps meaning "the whole catalogue".
  - `EffectivePermissionResolver` returns Super User → `Full`.
  - Administrator → `Full` **minus** `SuperUserControlledKeys` **plus** whichever of them are stored
    on the built-in Administrator `ApplicationRole`'s permission grants.
  - Custom roles are unchanged: their stored grants still apply, so a content key stored by a Super
    User works.
- **Granting to Administrator.** `UpdateRoleCommandHandler` refuses built-in roles ("Built-in roles
  cannot be edited"), and that stays. A new command, `SetBuiltInRoleContentAccessCommand(bool
  granted)`, restricted to Super Users, writes only that key on the Administrator role, audits
  `RoleUpdated` with before/after, and evicts every Administrator's permission cache through
  `IAuthorizationCacheInvalidator`.
- **Guard.** `SuperUserControlledPermissionGuard` (Application) is called by Create, Update,
  BulkDelete, Delete, AssignRole and BulkAssignRole:
  - **Create/Update:** if the actor is not a Super User and the requested set differs from the
    stored set on any controlled key (added *or* omitted), refuse with 403 and "Only a Super User
    can grant or remove *View user content*". The role editor always sends the stored controlled
    keys back unchanged (the checkbox is disabled but keeps its state), so an Administrator saving
    other changes keeps the key (US1b scenario 3). A crafted request that drops it is refused
    rather than applied, so the key can never be stripped as a side effect (FR-016i).
    - Rejected: silently re-adding an omitted key. It hides a refused action from the caller,
      which conflicts with FR-016h's "refused … with a clear message".
  - **Assign/BulkAssign/Delete/BulkDelete:** refuse when the actor is not a Super User and the role
    being assigned, the role being removed from the user, or the role being deleted holds a
    controlled key. This mirrors `PrivilegedRoleRequiresSuperUser` in `AssignRoleCommandHandler`.
  - The Super User role cannot lose the key, because the resolver returns `Full` for it
    unconditionally.
- **Audit.** Role changes already write `RoleAuditLog` inside `RoleRepository` and
  `RoleAssignmentRepository`. Grants and revokes appear in the existing before/after JSON with no
  new action (FR-016k).
- **Frontend.** `adminPermissions.ts` mirrors the three keys and the controlled set. The role
  editor's picker shows the key disabled, with the tooltip "Only a Super User can grant this", for
  non-Super-Users. The Roles page (already `builtInOnly`) shows a Super-User-only "Administrators
  may view user content" switch that calls the new command.

## D15 — Investigation views are dedicated read-only queries, not the user endpoints

**Decision.** There are three queries under `OperationalFailures/Investigations`:
`GetChatInvestigation`, `GetWorkflowRunInvestigation` and `GetDocumentInvestigation`. Each takes
`(incidentId, itemId)` and:

1. Refuses (404) unless the incident has at least one occurrence referencing that item
   (FR-016b: reachable only from an incident). The check uses the occurrence table **or** the
   incident's subject.
2. Always returns the metadata projection.
3. Adds the content projection **only** when the caller holds `content.view`. The decision is made
   server-side in the handler through `ICurrentUserPermissions`. The client never receives the
   content otherwise (FR-016a).
4. Returns content only when the viewer is not the owner, in which case it appends a
   `UserContentAccessEvent` synchronously in the same unit of work. If that insert fails, the query
   fails: no content is returned without an audit row (FR-016c). This is the one place where an
   audit write *is* on the request path, deliberately, because the content is the privileged act.
5. Returns deleted items as `{ deleted: true }` with no content (FR-016d).

There are no write endpoints, and the frontend views render without the composer, rename, delete
or download controls (FR-016b).

**Rejected.**
- Reusing `GET /api/v1/chats/{id}` with an admin bypass. It widens a user endpoint's authorisation
  and makes FR-016b's "no general browse" unprovable.
- Recording the access event through the non-blocking recorder. An access record that could be
  dropped is not an audit trail.

## D16 — Retention and erasure

**Decision.**

**Retention job.** `OperationalFailureRetentionJob` is a Hangfire recurring job running daily at
03:30 UTC, registered in `Program.cs` next to the existing recurring jobs. It calls three set-based
`ExecuteDeleteAsync` methods on the store:
- acknowledged or resolved incidents whose last occurrence is older than 90 days;
- unacknowledged incidents whose last occurrence is older than 180 days;
- occurrences older than 90 days inside incidents that are kept.

Incidents cascade to their occurrences and participants. The job logs the counts. A failure is
rethrown, so the global filter (D7) records it as a Background job failure (FR-028).

**Options.** `OperationalFailuresOptions` holds:
- `ResolvedRetentionDays` = 90
- `UnacknowledgedRetentionDays` = 180
- `OccurrenceRetentionDays` = 90
- `MaxStoredOccurrencesPerIncident` = 1,000
- `QueueCapacity` = 10,000
- `WriterBatchSize` = 100

All have defaults and are bound with `Bind` only. There is **no `ValidateOnStart`**: a required
option with no default crashes the whole host (memory note). Out-of-range values are clamped and
logged when the options are read.

`UserContentAccessEvents` are **never** removed by retention (spec Key Entities).

**Erasure.** `DeleteMyAccountCommandHandler` already anonymises the memory audit and notification
logs before `identityService.DeleteAsync`. It gains `operationalFailureStore.AnonymizeUserAsync(userId)`
in the same place. That method, in a single transaction:
- clears user, chat, message and document references on occurrences and sets `IsUserErased`;
- rewrites participant keys (D10);
- clears `OwnerUserId` and `ItemId` on content-access events and sets `IsOwnerErased`.

`SourceIp` is cleared on that user's Access occurrences (FR-012a). An exception propagates, so the
erasure fails visibly before the user row is deleted (FR-029a). Admin `DeleteUser` is a soft
delete and is untouched.

## D17 — Frontend: one page, one badge hook, deep-link selection on target pages

**Decision.**

- **Page and route.** `AdminOperationalFailuresPage` at `/admin/operational-failures`. It shows:
  - the filter bar (time preset/custom, severity, engine, provider, user autocomplete, kind);
  - the incident table (server-paged);
  - a detail drawer with occurrences (paged), related incidents (root cause), corrective-action
    link, provider current health, and acknowledge/resolve/reopen buttons plus the all-variants.

  Filters live in URL search params, so a link can reproduce a view.
- **Badge.** `useOperationalFailureBadge()` is a TanStack Query hook with `refetchInterval: 60_000`,
  enabled only with the view permission. Every transition mutation invalidates it (FR-026).
  `AdminNavItem` gains `badgeKey?: 'operationalFailures'`, which `AdminShell` resolves. A failed
  badge fetch shows the nav entry with an error dot and a tooltip, not silence (§2.VIII).
- **Deep links (FR-015).** The target pages read `?select=<id>` once on mount and pre-select or
  scroll to it:
  - `AdminAiProvidersPage` (provider)
  - `AdminVoicePage` (voice provider)
  - `/admin/mcp-servers` (server)
  - `AdminUsersPage` (`?search=<email>`, which the page already filters on)

  An unknown id shows an inline "no longer exists" note.
- **Investigation routes.** `/admin/operational-failures/:incidentId/chats/:chatId`,
  `…/workflow-runs/:runId` and `…/documents/:documentId`. The views are read-only and reuse the
  existing markdown renderer, not the chat page.
- **Errors.** Every query and mutation has a visible error path (inline error with retry, or a
  toast). A 409 on a transition shows "This incident was changed by someone else — reloaded."
  (FR-024, FR-027).

## D18 — Future notifier seam (FR-030, not built)

**Decision.** The ingestor returns `IncidentAppendResult { IncidentId, Opened, Severity }`. When
`Opened && Severity == Critical`, it publishes an in-process MediatR notification,
`CriticalIncidentOpened`, **from the writer's scope**, never from the user's request. No handler
is registered in this feature. A future email or paging handler subscribes to it without touching
any recording caller.
