# Quickstart: Selective Model Sync Review

Validates this feature end-to-end once implemented. Assumes spec 008's quickstart
prerequisites are already met (backend + frontend running, a database, a logged-in
administrator, at least one enabled provider with a working credential).

## Scenario 1 — Filter a long diff (User Story 1)

1. Open the AI Providers admin page, expand a provider row, click "Sync from provider" for
   a provider whose vendor lists many models not yet in the catalog (e.g. OpenAI).
2. Type part of a model's name into the dialog's search box. Expect: both the "newly
   available" and "no longer listed" lists narrow to matching rows only, live as you type.
3. Clear the search box. Expect: the full diff reappears.
4. Type something that matches nothing. Expect: a clear "no rows match" message, distinct
   from spec 008's "nothing to review" (empty-diff) state.

## Scenario 2 — Select a subset and apply only that subset (User Story 2)

1. With a diff open, check a few rows on the "newly available" side and a few on the
   "no longer listed" side, leaving most rows unchecked. Note the selected-count shown by
   the dialog.
2. Use "select all" on one side, then uncheck one row. Expect: only that row is excluded;
   the rest of "select all"'s selection remains.
3. Filter the list, then use "select all" again. Expect: only the currently-visible
   (filtered) rows are selected — hidden rows are untouched.
4. Clear the filter. Expect: any previously selected row that was hidden by the filter is
   still shown as selected, and the selected-count already included it.
5. Confirm. Expect: only the checked models change in the catalog (added ones as
   Unavailable; removed-from-vendor ones marked Unavailable) — everything else is
   untouched.
6. Trigger a sync again. Expect: the models you left unchecked still appear in the new diff
   exactly as before.

## Scenario 3 — Confirm is blocked with nothing selected (User Story 3)

1. Open a diff, select nothing (or select then deselect everything). Expect: Confirm is
   disabled.
2. Check one row anywhere. Expect: Confirm becomes enabled.
3. Uncheck it again. Expect: Confirm becomes disabled again.

## Scenario 4 — Partial apply failure (best-effort behavior)

1. Open a diff and select several rows, including one that will conflict (e.g., trigger a
   second administrator/tab to independently apply the same row first, or otherwise cause
   one selected row's precondition to fail before you confirm).
2. Confirm. Expect: the rows that could still be applied are applied and visible in the
   catalog; the conflicting row is named specifically in the feedback with a reason; a
   follow-up sync still shows that row as needing attention (it was never silently dropped
   or retried).

## Automated coverage

- Extended `ApplyProviderModelSyncCommandHandlerTests.cs` — a mixed request (one stale
  entry, one valid entry) results in the valid entry being applied and committed, and the
  stale entry appearing in `Failed` with a reason, in a single `SaveChangesAsync` call.
- Extended `ModelSyncDialog.test.tsx` — filter narrows both sides; select-all/none acts on
  visible rows only; deselecting after select-all doesn't re-select; Confirm is
  disabled/enabled based on selection count; Confirm sends only the selected subset; a
  mixed applied/failed result renders both outcomes.
