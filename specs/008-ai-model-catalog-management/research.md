# Research: Admin AI Model Catalog Management

No `NEEDS CLARIFICATION` markers remain in Technical Context — both real ambiguities were
already resolved during `/speckit-clarify`. This document records the implementation
decisions needed to act on those clarifications and to reuse existing code correctly.

## Decision 1: The diff-matching rule lives entirely in the query handler, using the existing "list all statuses" repository method

**Decision**: `GetProviderModelSyncDiffQueryHandler` computes the diff by calling
`IAIModelRepository.ListByProviderIdAsync(providerId)` (already exists — returns every
model regardless of status) and `IAIProviderResolver.Resolve(provider.ProviderKey)
.ListAvailableModelsAsync()` (already exists on all four providers), then:
- `added` = vendor models whose `ModelKey` is not present anywhere in the full catalog list
  (regardless of status) — per the first clarification, a deprecated model is never
  re-proposed even if the vendor still lists it.
- `removedFromVendor` = catalog models whose `Status == Available` and whose `ModelKey`
  the vendor no longer lists — a model already Deprecated/Unavailable is never included on
  this side either (re-flagging it would be redundant noise, per spec.md FR-006).

**Rationale**: No new repository method is needed — `ListByProviderIdAsync` already
returns exactly the "entire catalog regardless of status" set the clarified rule requires.
Keeping the comparison in the handler (Application layer) rather than pushing it into a
repository query keeps the business rule visible and unit-testable with faked
repositories/resolver, no database needed.

**Alternatives considered**: A new repository method returning only `Available` models for
comparison — rejected, that's the OLD (pre-clarification) rule and would silently
reintroduce the bug the clarification exists to prevent.

## Decision 2: Newly-synced models are created Available, then immediately set Unavailable

**Decision**: `ApplyProviderModelSyncCommandHandler` calls the existing
`AIModel.Create(...)` factory (which unconditionally sets `Status = Available`) for each
added model, then immediately calls the existing `SetStatus(AIModelStatus.Unavailable,
actor)` on the same instance before adding it to the repository — rather than modifying
`AIModel.Create` to accept an initial status.

**Rationale**: `AIModel.Create` is unchanged Domain code from spec 005, already used
elsewhere only via the migration's raw-SQL seed (which doesn't call `Create` at all).
Adding an optional status parameter to `Create` for a single new caller is a larger,
less-obviously-safe Domain change than composing two already-existing, already-tested
public methods in the (new) Application handler. Per constitution §2.III (Simplicity/
YAGNI), prefer the smaller change until a second real caller justifies widening `Create`'s
signature.

**Alternatives considered**: Add an `initialStatus` parameter to `AIModel.Create` —
rejected for now per the rationale above; revisit if a second caller ever needs a
non-Available initial status.

## Decision 3: The sync check is a Query, not a Command, despite its `POST .../actions/sync` route

**Decision**: `GetProviderModelSyncDiffQuery`/Handler is modeled as a MediatR query
(`IRequest<ProviderModelSyncDiffDto>`), invoked from a `POST` action on the controller
(matching the existing `POST .../actions/x` convention for non-CRUD actions, constitution
§6) — the HTTP verb reflects the resource-action shape, not the CQRS classification.

**Rationale**: FR-006 explicitly requires the check to never mutate the catalog. Modeling
it as a Command because it happens to be reached via `POST` would violate constitution
§3's "Queries MUST NOT mutate state / Commands MUST NOT be used to fetch unrelated data"
guidance in spirit (a Command that does nothing but return read data is really a Query
wearing a Command's name). This mirrors research.md Decision 5 from spec 005, which
already anticipated this exact split for the same feature ("Model Discovery... surfaced to
an admin as a diff and never applied to the catalog automatically").

**Alternatives considered**: A single Command that both computes and immediately applies
the diff — rejected outright by FR-007 (explicit review/confirm is mandatory, not
optional).

## Decision 4: UI shape — expand the existing provider row rather than a new page

**Decision**: `AdminAiProvidersPage.tsx` gains an expand/collapse control per provider row
(MUI `Collapse`), revealing that provider's model table + a "Sync from provider" button
inline — no new route, no new page.

**Rationale**: Constitution §7 requires composing from the existing design system/pattern
before writing something bespoke, and a model catalog is conceptually "detail of this
provider row," not an independent resource an administrator navigates to directly. Keeps
the entire admin AI experience (enable/disable, credential, now models) on one page,
consistent with how compact this admin area already is (a handful of providers).

**Alternatives considered**: A separate `/admin/ai-providers/{id}/models` route — rejected,
adds a new routing/navigation pattern for a feature that fits comfortably as a detail
expansion of an existing row, and would fragment one coherent admin task across two pages.

## Decision 5: Confirm-gating reuses `AiProviderActionsMenu.tsx`'s established idiom

**Decision**: `AiModelStatusMenu.tsx` (per-model status change) and `ModelSyncDialog.tsx`
(sync → diff → confirm/apply) each use the same `pendingAction` + `CONFIRM_COPY` +
Snackbar/Alert feedback shape already built for spec 007, per FR-010.

**Rationale**: Established, already-tested pattern in the exact same admin area;
introducing a second confirmation idiom here would violate Convention Over Configuration
(§2.VII) for no benefit.

**Alternatives considered**: None seriously — this is a direct extension of an
already-accepted pattern one feature old.
