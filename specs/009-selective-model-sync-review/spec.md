# Feature Specification: Selective Model Sync Review

**Feature Branch**: `009-selective-model-sync-review`

**Created**: 2026-07-31

**Status**: Draft

**Input**: User description: "Selective model sync review. Context: specs/008-ai-model-catalog-management shipped the "sync from provider" diff-then-apply flow — an administrator triggers a sync, reviews a diff of models newly listed by the vendor (to be added as Unavailable) and models the catalog has that the vendor no longer lists (to be marked Unavailable), then Confirm applies the *entire* reviewed diff as one all-or-nothing batch. Live testing surfaced a real gap: for a vendor like OpenAI the "added" side of the diff can be huge (98 models in the observed case), and the administrator has no way to choose which of those models they actually want added — it's all 98 or none. There's also no way to search/filter the list by name to find a specific model in a long diff. An administrator must be able to select/deselect individual models on both sides of the diff via a checkbox per row, use select-all/select-none controls, filter/search the list by model name or key, and have Confirm apply only the currently-selected subset — deselected models are left completely alone and remain eligible for a future sync. Confirm must be blocked when nothing is selected."

## Clarifications

### Session 2026-07-31

- Q: Is the text filter a single shared search box that filters both diff sides at once, or two independent filter boxes, one per side? → A: One shared search box, inside the sync-review dialog, filtering both sides simultaneously.
- Q: If applying the selected subset partially fails server-side, should the whole confirm action fail atomically with no changes, or apply what it can and report which items failed? → A: Best-effort partial apply — models that can be applied are applied; failures are reported individually.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Administrator narrows a long diff to the models they care about (Priority: P1) 🎯 MVP

An administrator triggers a catalog sync against a vendor (e.g. OpenAI) that returns dozens
of models the catalog doesn't have yet. Instead of an all-or-nothing list, the administrator
types part of a model's name or key into a search box and the diff narrows to just the
matching rows, on both the "newly available" and "no longer listed" sides.

**Why this priority**: Without this, a large diff is unusable — the reported gap (98 models
in one screen) makes the review dialog impractical to act on at all. This is the direct fix
for what was observed live.

**Independent Test**: Trigger a sync that returns a large diff; type a partial model name
into the search box; confirm only matching rows remain visible on both diff sides, and
clearing the search restores the full list.

**Acceptance Scenarios**:

1. **Given** a sync diff with dozens of added models, **When** the administrator types a
   partial model name into the filter box, **Then** only added-side rows whose display name
   or model key contains that text (case-insensitive) remain visible.
2. **Given** a sync diff with models on the "no longer listed by vendor" side, **When** the
   administrator filters by text, **Then** that side is filtered by the same rule
   independently of the added side.
3. **Given** an active filter that matches nothing, **When** the administrator views the
   dialog, **Then** it clearly states no rows match rather than appearing broken or empty
   with no explanation.

---

### User Story 2 - Administrator selects exactly which models to apply (Priority: P1)

Having narrowed or reviewed the diff, the administrator checks the box next to each model
they actually want to add or mark unavailable, uses "select all" / "select none" to quickly
select or clear a whole side, and then confirms — only the checked rows are applied. Rows
left unchecked are untouched by this sync and remain candidates for a future one.

**Why this priority**: This is the actual capability gap — being able to see/filter a long
diff (User Story 1) is only useful if the administrator can then act on a subset of it
rather than being forced into all-or-nothing.

**Independent Test**: From a diff with several added and several removed-from-vendor
models, check only some rows on each side, confirm; verify only the checked models changed
status and the unchecked ones are unaffected; re-run a sync and confirm the unchecked models
still appear in the diff (they were never applied).

**Acceptance Scenarios**:

1. **Given** a reviewed diff, **When** the administrator checks a subset of added models and
   confirms, **Then** only those checked models are created in the catalog (as Unavailable,
   per specs/008's existing rule); unchecked added models are not created at all.
2. **Given** a reviewed diff, **When** the administrator checks a subset of removed-from-vendor
   models and confirms, **Then** only those checked models are marked Unavailable; unchecked
   ones keep their current status.
3. **Given** a reviewed diff with a mix of checked and unchecked rows on both sides,
   **When** the administrator confirms, **Then** exactly the checked rows across both sides
   are applied in a single action.
4. **Given** the administrator uses "select all" for a side, **When** they then uncheck one
   row, **Then** only that row is excluded from what "select all" had selected — the
   control does not silently re-select it.
5. **Given** a filter is active and narrows a side to a few rows, **When** the administrator
   uses "select all", **Then** only the currently-filtered/visible rows on that side are
   selected — rows hidden by the filter are not silently included.
6. **Given** a model that was deselected (left unapplied) in a prior sync, **When** the
   administrator triggers a new sync later, **Then** that model still appears in the new
   diff exactly as before, since it was never added/marked (nothing was silently remembered
   or excluded from future diffs because of the earlier deselection).
7. **Given** a confirm action where one selected row fails to apply (e.g., a conflicting
   catalog state) while the rest succeed, **When** the administrator views the result,
   **Then** the successfully applied rows are reflected in the catalog, the failed row is
   named specifically with a reason, and the failed row remains available to select again
   on a future sync.

---

### User Story 3 - Confirm is blocked when nothing would happen (Priority: P2)

The administrator opens the sync dialog, sees a diff, but doesn't select anything (or
deliberately deselects everything). The Confirm action is disabled or otherwise clearly
blocked, so no accidental no-op submission is possible and it's obvious nothing has been
chosen yet.

**Why this priority**: A safety/clarity guard rather than new capability — prevents
confusion ("I clicked Confirm, did anything happen?") once selection exists as a concept.

**Independent Test**: Open a diff, deselect every row (or select none to begin with);
confirm the Confirm control is disabled/blocked and no apply request is possible.

**Acceptance Scenarios**:

1. **Given** a freshly reviewed diff with nothing selected, **When** the administrator
   looks at the dialog, **Then** Confirm is disabled.
2. **Given** the administrator selects at least one row on either side, **When** they view
   the dialog, **Then** Confirm becomes available.
3. **Given** the administrator had rows selected and then deselects all of them, **When**
   they view the dialog, **Then** Confirm becomes disabled again.

---

### Edge Cases

- What happens when the administrator filters the list down to zero rows on both sides?
  Confirm remains blocked (nothing is selected/selectable), and the dialog states no rows
  match rather than looking identical to the "nothing to review" state from specs/008.
- What happens if the administrator selects rows, then changes the filter text so some
  selected rows are no longer visible? The underlying selections are preserved (filtering
  only changes what's shown, not what's selected) — hidden selected rows still count toward
  what Confirm will apply, and the dialog communicates that a selection exists outside the
  current filter (e.g., a visible "N selected" count) so the administrator isn't confused by
  an enabled Confirm button next to an apparently-empty visible list.
- What happens on the "select all" control when the filtered set already has some rows
  selected and some not? Selecting it selects every currently-visible row, regardless of
  their prior individual state.
- What happens when a confirm action partially fails? The rows that succeeded are applied;
  the rows that failed are reported individually with a reason and are left unmodified —
  they behave exactly like an unselected row (still eligible for a future sync), never a
  silently-dropped or silently-retried one.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The sync-review dialog MUST display an individually checkable control for
  every model row on both the "newly available at vendor" side and the "no longer listed by
  vendor" side of the diff.
- **FR-002**: The sync-review dialog MUST provide a single, shared text filter (one search
  box, not one per side) that narrows the visible rows on both diff sides simultaneously to
  those whose display name or model key contains the entered text, case-insensitively,
  updating as the administrator types.
- **FR-003**: Each diff side MUST offer a "select all" and "select none" control that acts
  on that side's currently visible (filtered) rows only.
- **FR-004**: Deselecting one or more rows after using "select all" MUST leave the remaining
  rows selected — "select all" is a one-time bulk action, not an enforced state.
- **FR-005**: Filtering MUST NOT change which rows are selected; a previously selected row
  that a new filter hides remains selected, and its selection still counts toward what
  Confirm will apply.
- **FR-006**: The dialog MUST communicate the total count of currently selected models
  (across both sides) so the administrator is never uncertain what a confirm will do,
  including when the filtered view doesn't show every selected row.
- **FR-007**: Confirming MUST apply only the currently selected rows: selected added models
  are created in the catalog (status Unavailable, per specs/008-ai-model-catalog-management's
  existing rule); selected removed-from-vendor models are marked Unavailable; unselected rows
  on either side are left completely unmodified.
- **FR-007a**: If one or more selected rows fail to apply (e.g., a selected row now conflicts
  with catalog state), the rows that can still be applied MUST be applied — a failure on one
  selected row MUST NOT block or roll back the other selected rows in the same confirm
  action (best-effort, per-row application).
- **FR-007b**: When a confirm action produces a mix of successes and failures, the
  administrator MUST be told, per failed row, which specific model failed and why, distinct
  from a fully successful confirm's feedback.
- **FR-008**: The Confirm control MUST be disabled whenever zero rows are selected across
  both sides, and MUST become enabled as soon as at least one row is selected.
- **FR-009**: A model left unselected (and therefore unapplied) MUST remain eligible to
  appear in a future sync's diff exactly as it would have if this sync had never run — no
  unapplied row is remembered or excluded from later diffs.
- **FR-010**: When an active filter matches zero rows on a side, that side MUST clearly
  state that no rows match the filter, distinct from specs/008's existing "nothing to
  review" empty-diff state.
- **FR-011**: Dismissing the dialog (without confirming) MUST continue to leave the catalog
  completely unchanged, regardless of any in-progress filter or selection state — consistent
  with specs/008-ai-model-catalog-management's existing Dismiss behavior.
- **FR-012**: Every apply attempt (fully successful, fully failed, or a partial mix per
  FR-007a/FR-007b) MUST surface visible feedback to the administrator, consistent with
  specs/008-ai-model-catalog-management's existing confirm-gated feedback pattern.
- **FR-013**: The apply action MUST reject a request where nothing is selected on either
  side, as a server-side backstop to FR-008's client-side Confirm-disabled guard — the two
  are independent enforcement points for the same rule (an apply with nothing selected is
  never meaningful), not separate behaviors.

### Key Entities

- **Diff row selection state**: A per-model-row checked/unchecked flag, scoped to the
  current sync-review session (not persisted once the dialog closes) and independent of
  whatever text filter is currently narrowing the visible rows.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator reviewing a 98-model diff can find a specific model by typing
  a partial name and see the matching rows in under 5 seconds, without scrolling through the
  full unfiltered list.
- **SC-002**: An administrator can apply a chosen subset of a diff (e.g., 3 of 98 added
  models) in a single confirmation, with the other 95 left completely untouched and still
  available for a later sync.
- **SC-003**: 100% of confirm attempts with zero rows selected are blocked before any
  catalog change occurs.
- **SC-004**: After applying a partial selection, re-running a sync against the same vendor
  state reproduces the previously-unselected rows in the new diff with no discrepancy.
- **SC-005**: When a confirm action partially fails, 100% of the successfully-applied rows
  are reflected in the catalog and 100% of the failed rows are named individually to the
  administrator with a reason, in the same result.

## Assumptions

- This feature extends specs/008-ai-model-catalog-management's existing sync-review dialog
  and diff/apply contract; it does not change what triggers a sync, how the diff is computed
  (research.md Decision 1's matching rule), or the Unavailable-on-create rule for newly
  added models — only how much of a reviewed diff the administrator chooses to apply.
- The existing diff/apply request already carries an explicit list of added and
  removed-from-vendor models rather than an implicit "everything," so applying a subset is a
  matter of which rows the client includes when confirming. Per-row best-effort application
  (FR-007a/FR-007b) is a new server-side behavior specs/008 didn't need, since specs/008
  always applied one all-or-nothing batch.
- Selection state lives only for the duration of one sync-review session; there is no
  requirement to save, restore, or remember a partial selection across dialog closes or
  across different sync attempts.
- Confirm-gating (an explicit confirmation step before anything changes) and per-action
  success/error feedback continue to follow the pattern already established in
  specs/008-ai-model-catalog-management and specs/007-admin-ai-provider-ui.
