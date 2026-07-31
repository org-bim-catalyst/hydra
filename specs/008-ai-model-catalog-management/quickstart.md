# Quickstart: Admin AI Model Catalog Management

Validates the feature end-to-end once implemented.

## Prerequisites

- Backend and frontend running locally; a database available (this is the one remaining
  manual gap flagged in spec 007's own quickstart — confirm that's resolved before
  starting here too).
- A logged-in administrator account.
- At least one provider enabled with a working credential (spec 007), so a sync check has
  a real vendor to call.

## Scenario 1 — Review a provider's model catalog (User Story 1)

1. Open the AI Providers admin page, expand a provider's row.
2. Confirm every model for that provider is listed — Available, Deprecated, and
   Unavailable alike — each showing its capabilities, pricing (or "unknown"), and status.

## Scenario 2 — Manually curate a model's status (User Story 2)

1. Mark an Available model Deprecated, confirming the action. Expect: it disappears from
   what end users can newly select (check the chat provider/model picker or Settings → AI
   Providers), while any past conversation that already used it is unaffected.
2. Mark it Available again, confirming. Expect: it's selectable again immediately.

## Scenario 3 — Sync from the vendor (User Story 3)

1. Trigger "Sync from provider" for an enabled provider. Expect: a diff appears (or a
   clear "nothing to review" result if the vendor's list already matches); the catalog is
   unchanged either way until confirmed.
2. Confirm the diff. Expect: each added model appears in the catalog as **Unavailable**
   (not yet end-user-selectable — per this spec's clarification, activating it is a
   separate manual step, Scenario 2); each no-longer-listed model is marked Unavailable,
   never removed.
3. Trigger a sync again immediately after. Expect: models just added/marked Unavailable by
   the previous apply are **not** re-proposed as new additions (the clarified "compare
   against the entire catalog" rule) — the diff should now show no changes for those.
4. Dismiss a diff without confirming. Expect: the catalog is completely unchanged.

## Scenario 4 — Access control

1. Sign in as a non-administrator. Confirm none of the four new endpoints (models list,
   status change, sync, sync/apply) are reachable — same denial behavior already verified
   for the rest of `AdminAiProvidersController` in spec 007.

## Automated coverage

- Application-layer unit tests for `GetProviderModelSyncDiffQueryHandler` (the
  diff-matching rule, faked repositories/resolver — no database) — including the
  regression case for Scenario 3.
- `AiModelStatusMenu.test.tsx` / `ModelSyncDialog.test.tsx` — confirm-gating, matching
  `AiProviderActionsMenu.test.tsx`'s style.
