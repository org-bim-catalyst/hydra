# Quickstart: Admin AI Provider Configuration UI

Validates the feature end-to-end once implemented. No new backend setup is required — the
API this UI calls already exists (`005-multi-provider-ai-engine`).

## Prerequisites

- Backend (`AskLucy.Web`) and frontend (`ClientApp`) running locally.
- A logged-in account with the `Administrator` or `Super User` role.
- A real credential for at least one provider (e.g., an OpenAI API key) if you want to see
  a genuinely `Healthy` status after the next periodic health check; a syntactically
  plausible but fake string is enough to exercise every UI flow below except the eventual
  health flip to `Healthy`.

## Scenario 1 — Enable a provider for the first time (User Story 1)

1. Sign in as an administrator, open **Admin Dashboard**, click **Manage AI providers**.
2. Confirm the page lists all providers (including disabled ones), each showing enabled/
   disabled and "credential configured" state, with no credential value anywhere.
3. Pick a disabled provider with no credential. Choose **Set credential**, enter a value,
   confirm. Expect: the row now shows "credential configured"; the value is never shown
   again anywhere on the page.
4. Try **Enable** on a *different* disabled provider that still has no credential.
   Expect: rejected with an explanation naming the missing credential, not a generic
   error — and no confirmation dialog is even reached (FR-003).
5. Choose **Enable** on the provider from step 3, confirm. Expect: it now shows enabled.
6. Open `GET /api/v1/ai/providers` (or the chat provider picker, or Settings → AI
   Providers) as an end user. Expect: the newly enabled provider now appears.

## Scenario 2 — Review status at a glance (User Story 2)

1. With providers in a mix of enabled/disabled states, reload the AI providers page.
2. Expect every row to show its enabled state and its most recent health status
   (`Unknown`/`Healthy`/`Unhealthy`) plus roughly when that was last checked — no
   additional click needed, no log/tool outside the product consulted.

## Scenario 3 — Disable, and rotate/clear a credential (User Story 3)

1. On the provider enabled in Scenario 1, choose **Disable**, confirm. Expect: it
   immediately disappears from the end-user catalog (`GET /api/v1/ai/providers`); any
   past conversation that used it still shows that provider/model as its attribution
   (unchanged).
2. Re-enable it (Scenario 1 steps), then choose **Set credential** again with a different
   value, confirm. Expect: it stays enabled throughout, now under the new credential.
3. Choose **Clear credential**. Expect: the confirmation dialog explicitly states this
   will also disable the provider; after confirming, the row shows both "no credential"
   and "disabled" at once.

## Scenario 4 — Access control

1. Sign in as a non-administrator user. Confirm `/admin/ai-providers` is not reachable
   (same denial behavior as `/admin/users`), and no provider data is returned to that
   session by any means.

## Automated coverage

- `AiProviderActionsMenu.test.tsx` (11 tests) — every action gated behind its confirmation
  dialog (or, for Enable with no credential, the immediate client-side explanation with no
  dialog/API call at all); canceling never calls the API; confirming does; the submitted
  credential value never reappears in rendered output afterward; the "Clear credential"
  item is disabled with nothing to clear; "Replace credential" replaces "Set credential"
  once a credential exists. Mirrors `UserActionMenu.test.tsx`'s existing assertion style.
- `AdminAiProvidersPage.a11y.test.tsx` — automated a11y check (axe), per constitution §10.

## Verification results (2026-07-31)

Ran as part of `/speckit-implement`:

- **Scenarios 1–3 (logic)**: exercised via the automated component tests above — every
  branch each scenario describes (set/replace credential, enable gated on credential,
  disable, clear-credential-forces-disable) has a corresponding passing assertion.
- **Scenario 4 (access control)**: verified without new code — `AdminRoute` (client) and
  the existing `[Authorize(Policy = "AdministratorOrSuperUser")]` on
  `AdminAiProvidersController` (server) already cover this route; the 12 existing tests in
  `tests/AskLucy.Web.Tests/Ai/AdminAiProvidersControllerTests.cs` (401 anonymous / 403
  non-admin / pass-through for admin, across all four endpoints) all pass.
- **Full live-browser walkthrough with a real database**: **not performed** in this
  session — this sandbox has no SQL Server/Docker available (the same limitation that
  keeps `AskLucy.Persistence.Tests` from running here). Scenario 1 step 6's "confirm the
  provider now appears in the end-user catalog" cross-feature check in particular has not
  been observed live and should be confirmed manually before this ships.
- Full test suite re-run after implementation: backend 268/268 runnable tests pass
  (Persistence.Tests excluded — Docker-only); frontend 80/84 pass, the 4 failures being
  the pre-existing, unrelated `AdminUsersPage.test.tsx` issue (present before this feature,
  not touched by it).
