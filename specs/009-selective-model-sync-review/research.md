# Phase 0 Research: Selective Model Sync Review

## Decision 1: Filter/selection/select-all/count are frontend-only — no backend change

**Decision**: The text filter (FR-002), per-row checkbox state (FR-001), per-side
select-all/none (FR-003/FR-004), the "selection survives filtering" rule (FR-005), and the
selected-count display (FR-006) are implemented entirely as local component state inside
`ModelSyncDialog.tsx`. `GetProviderModelSyncDiffQuery` (the diff computation) and its
`ProviderModelSyncDiffDto` shape are untouched.

**Rationale**: `ApplyProviderModelSyncCommand` already accepts an explicit
`added: ProviderModelInfo[]` / `removedFromVendor: RemovedModelDto[]` list rather than an
implicit "apply the last diff" — spec 008 built it this way specifically so the client
echoes back exactly what it reviewed (no server-side ephemeral cache). That means
"applying a subset" is already representable by the existing request shape; the client
simply omits unchecked rows when building the request. Nothing about filtering or
selecting needs a round-trip.

**Alternatives considered**:
- Server-side selection state (e.g., a `POST .../sync/select` endpoint toggling rows in a
  cached proposal) — rejected as unnecessary complexity (YAGNI, constitution §III) for a
  purely presentational concern with no cross-session persistence requirement (spec's
  Assumptions).

## Decision 2: Best-effort apply is a pre-mutation per-row check, not per-row transactions

**Decision**: `ApplyProviderModelSyncCommandHandler` performs the same staleness check
that today's `ApplyProviderModelSyncCommandValidator` performs (an `added` row whose
`modelKey` already exists in the catalog; a `removedFromVendor` row whose `id` doesn't
belong to the provider) — but does it **inside the handler, per row**, instead of as a
single validator rule that rejects the whole command. Rows that pass are mutated in memory
(`AIModel.Create`+`SetStatus` / `SetStatus`) exactly as before; rows that fail are
collected into a `Failed` list with a reason and never touch the `DbContext`. Exactly one
`SaveChangesAsync` call commits every row that passed, at the end.

**Rationale**: This satisfies FR-007a (one stale row never blocks the others) while
preserving constitution §5's "a business transaction spans exactly one SaveChanges" rule —
there is still only one commit per request, it just contains fewer rows than were
requested when some are stale. A true concurrent-write database exception occurring mid-
`SaveChangesAsync` (as opposed to a staleness precondition failure, which is checked
in-memory before any mutation) is not modeled by this feature — it's an already-accepted
gap in spec 008's original design (admin-only, low-frequency, low-concurrency usage), and
introducing per-row transactions/retries to close it would be speculative complexity not
required by any acceptance scenario in spec.md.

**Alternatives considered**:
- One `SaveChangesAsync` per row (true per-row transaction isolation) — rejected: directly
  conflicts with constitution §5 ("multi-step workflows use domain events or an outbox, not
  multiple partial commits") for a benefit (surviving a same-millisecond concurrent write)
  no acceptance scenario asks for.
- Keep the all-or-nothing validator, and have the frontend just retry with a smaller
  selection on 400 — rejected: doesn't satisfy FR-007a/FR-007b, which explicitly require
  reporting *which* rows failed in the *same* result, not a generic 400 the client has to
  bisect.

## Decision 3: Response shape changes from 204 to 200 + result body

**Decision**: `ApplyProviderModelSyncCommand` becomes `IRequest<ApplyProviderModelSyncResultDto>`
(`AppliedModelKeys: string[]`, `Failed: SyncApplyFailureDto[]` where each failure carries
`ModelKey`, `DisplayName`, and a human-readable `Reason`). The controller action returns
`200 OK` with this body instead of `204 No Content`.

**Rationale**: The constitution's CQRS rule permits a command to return "an id, a result
DTO" to let the caller confirm the write — a per-row applied/failed breakdown is exactly
that, not an unrelated read. No previous consumer depends on the empty `204` body (the
endpoint shipped in spec 008 within the same release cycle, no external API version to
preserve).

**Alternatives considered**:
- A separate `GET` polling endpoint to fetch "what happened" after a `202 Accepted` — 
  rejected: massive overkill for a synchronous, sub-second admin operation.

## Decision 4: Reason strings are handler-owned, human-readable, not error codes

**Decision**: `SyncApplyFailureDto.Reason` carries the same wording the current validator
already produces (e.g. `"'{modelKey}' already exists in the catalog — the diff is stale;
re-run the sync check."`), moved verbatim into the handler. No new error-code taxonomy is
introduced.

**Rationale**: The administrator is the only consumer of this string (rendered directly in
the Snackbar/Alert per FR-007b) — there's no second client needing to branch on an error
code, so a human-readable string is the simplest thing that satisfies the requirement
(constitution §III, KISS).

**Alternatives considered**: A structured error-code enum — rejected as premature; nothing
in the spec asks for programmatic handling of *why* a row failed, only that the
administrator can see it.
