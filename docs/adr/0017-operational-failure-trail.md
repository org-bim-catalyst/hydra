# ADR 0017: One Cross-Cutting Operational Failure Trail, Written Off the Request Path

**Status:** Accepted

**Date:** 2026-09-25

**Feature:** [specs/074-operational-failure-audit](../../specs/074-operational-failure-audit/spec.md)

## Context

When something fails for a user — a chat reply, a provider credential, a voice failover, document
indexing, an agent or workflow step, a background job — the user must see a calm, generic sentence,
while an administrator needs a precise, actionable record of the same event.

Before this feature the evidence was scattered: eight per-module audit logs (agent, MCP, workflow,
document, knowledge base, memory, prompt, role), the `AIProviders` health columns, the
`VoiceProviderFailoverEvents` table and free-text server log lines. None of them grouped a burst of
identical failures, and several copied the vendor's message verbatim into text the user could see.

Recording a failure must never fail or slow the user's request (FR-020, FR-021).

## Decision

### 1. One store behind one seam

Every module reports through `IOperationalFailureRecorder` (Application). One store groups reports
into incidents (`OperationalFailureIncidents`), keeps the individual occurrences, and tracks the
distinct users and sources affected. The existing module audit logs and health columns stay as
they are; the trail links to them by correlation id rather than duplicating or replacing them.

### 2. A bounded channel and a background writer

`Record` is `void`, synchronous and never throws. It stamps the correlation id and time on the
caller's thread and does a `TryWrite` into a bounded channel. A hosted `OperationalFailureWriterService`
drains the channel in batches, each in its own DI scope, and the ingestor sanitises, classifies
severity, computes the grouping keys and appends to the store.

When the channel is full, the report is **dropped and logged** (`OperationalFailureDropped`) rather
than making the caller wait. A write failure is logged with the correlation id
(`OperationalFailureRecordingFailed`), so the server log still carries the evidence.

The one write that stays synchronous is the `UserContentAccessEvent` an investigation view writes
before it shows another user's content (research D15). That record is the permission's audit
guarantee: if it can't be written, the content must not be shown, so it cannot be fire-and-forget.

### 3. The built-in Administrator no longer implies the whole catalogue

Until now, a built-in role always resolved to `PermissionSet.Full`. The new
`admin.operational-failures.content.view` permission lets its holder read other users' chats,
workflow runs and documents. It is listed in `AdminPermissionCatalog.SuperUserControlledKeys`:
the Administrator role resolves to `Full` **minus** those keys, plus whichever of them a Super User
has explicitly granted to the Administrator role. Only a Super User can grant, revoke, assign or
remove anything that carries a controlled key. This is the first exception to "built-in ⇒ full
catalogue".

## Consequences

* A module adds one `Record(...)` call at its failure site (log first, then record). Nothing on a
  request or job path awaits the store.
* Under an extreme failure storm, reports beyond the channel capacity are lost from the trail, but
  never from the server log.
* The trail is eventually consistent: an incident appears within about a second, not
  transactionally with the failure.
* Retention hard-deletes incidents and occurrences (FR-029). Content-access events are never purged,
  only anonymised on account erasure.
* Anyone reasoning about built-in role permissions must account for `SuperUserControlledKeys`.

## Alternatives considered

* **Union the eight module audit logs in a query.** Rejected: they have different shapes, no common
  severity or kind, no grouping, and several store vendor text that must not be shown.
* **A Serilog sink that writes failures to the database.** Rejected: log events are unstructured
  for this purpose, can't carry the typed links (user, chat, workflow run), and would couple the
  admin feature to the logging configuration.
* **A synchronous write on the request path.** Rejected: it adds latency to every failing request,
  and a store failure could replace the original error.
* **An unbounded channel.** Rejected: a failure storm could exhaust memory.

## Trade-off accepted

We accept losing trail entries under overload, and eventual rather than transactional consistency,
in exchange for a guarantee that recording can never hurt the request it describes.
