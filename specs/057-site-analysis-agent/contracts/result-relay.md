# Contract: Specialist → Parent Result Relay

**Spec**: SPEC-057 | **Requirements**: FR-005, FR-006, FR-007, FR-011, FR-022, FR-024

The internal contract every specialist uses to report to the coordinating agent. This is the **only** route by
which a finding can reach a user — a specialist that pushes a panel or a message itself violates FR-007.

## Interface

`AskLucy.Application.SiteAnalysis.ISiteAnalysisResultRelay`

```csharp
Task ReportSuccessAsync(
    Guid siteAnalysisId,
    SiteAnalysisType analysisType,
    SiteAnalysisResultMetadata metadata,
    JsonDocument content,          // panel content-block document
    Guid? documentId,              // set when the specialist produced a file
    CancellationToken cancellationToken = default);

Task ReportFailureAsync(
    Guid siteAnalysisId,
    SiteAnalysisType analysisType,
    string failureReason,          // diagnostic detail, not user-facing copy
    Exception? cause,
    CancellationToken cancellationToken = default);
```

`SiteAnalysisResultMetadata`:

```csharp
public sealed record SiteAnalysisResultMetadata(
    SiteAnalysisType AnalysisType,
    string SiteName,
    string DataSource,
    SiteAnalysisConfidenceLevel ConfidenceLevel,
    DateTime GeneratedAtUtc,
    IReadOnlyList<string> Notes);
```

## Call timing (FR-005)

A specialist MUST call the relay as the **last step of its own `ExecuteAsync`**, before returning its
`AgentToolResult`.

It MUST NOT rely on the workflow engine's node-completion events to deliver its result:
`WorkflowExecutionOrchestrator.ExecuteParallelAsync` batches those until every branch has settled, which would
deliver all findings at once and violate FR-005. See research D3.

## Validation gate (FR-006)

`ReportSuccessAsync` validates before persisting or delivering anything. A result failing **any** check is
persisted with status `Rejected` and a `FailureReason`, and is **not** delivered.

| Check | Rule |
|---|---|
| Analysis exists | `siteAnalysisId` resolves to a non-deleted `SiteAnalysis` |
| Not already settled | No existing result for this (`siteAnalysisId`, `analysisType`) |
| Analysis still running | Parent status is `Running` |
| Content shape | Parses as a content-block document; block count within the vocabulary limit; every block matches a known kind |
| Content non-empty | At least one block carrying user-visible substance — not solely dividers |
| Provenance present | `DataSource` non-empty; `ConfidenceLevel` set; `GeneratedAtUtc` set |
| File reference | When the analysis type declares a file output, `documentId` is non-null and resolves to a document owned by the analysis's user |
| No external URLs | No block carries an external address in place of a platform file id |

Content is **model-influenced and therefore untrusted** (constitution §8 prompt injection): it is validated as
data here and never interpreted as instructions.

## Delivery on success

In order, within one unit-of-work commit where possible:

1. Persist the result as `Completed`.
2. Persist the chat notice as a real assistant `Message` row (FR-008) — not only a transient push;
   a reload must still show it via the conversation's ordinary message history, in the same commit
   as the result itself.
3. Push `SiteAnalysisResultReceived` (see `site-analysis-hub-events.md`) for immediate, low-latency
   rendering — the persisted message is what survives a reload, the push is what makes it appear
   without one.
4. Push the panel — existing `IPanelNotifier.PanelRequestedAsync(userId, PanelRequestDto.ForContent(...))`.
5. If this was the last outstanding specialist, settle the analysis and deliver the closing outcome
   (also persisted as a message when it carries text — FR-024's quiet stays when it does not).

Notifications are fired **after** the commit, so a delivered finding is always retrievable (FR-018). A
notification failure MUST NOT roll back the persisted result — the finding survives and rehydrates; the failure
is logged (FR-022a).

## Behavior on failure (FR-022, FR-024)

1. Persist the result as `Failed` with its `FailureReason` and the originating error detail.
2. Log via `ILogger<T>` with structured properties: analysis id, site name, analysis type, user id, exception.
3. Push **nothing** — no notice, no panel.
4. If this was the last outstanding specialist, settle the analysis and deliver the closing outcome.

Per constitution §2 VIII the failure is captured and diagnosable; user silence is deliberate and compliant
(research D13).

## Closing outcome (FR-025, FR-026, FR-027)

When the final specialist settles, the relay calls `TryClaimClosingOutcome`. Only the caller that wins the claim
emits the outcome — this is what makes "exactly once" hold when branches finish simultaneously.

| Situation | Analysis status | Message |
|---|---|---|
| All specialists succeeded | `Completed` | No additional message — the findings speak for themselves |
| Some succeeded, some did not | `Completed` | One brief line: finished, with *n* of *m* completed |
| None succeeded | `Failed` | One brief line: the analysis could not be completed |

Copy is composed server-side and delivered as `SiteAnalysisCompleted`.

## Concurrency

Specialists run concurrently (`Task.WhenAll`), so the relay MUST be safe under simultaneous calls: each writes a
distinct result row, contention is on the parent, and the closing-outcome claim is atomic. A
`DbUpdateConcurrencyException` is handled by re-reading and conceding, never surfaced as a 500 (constitution §5).
