# Quickstart: Validating Cookie Consent & Privacy Management

**Feature**: [spec.md](./spec.md) | **Contracts**: [contracts/cookie-consent-api.md](./contracts/cookie-consent-api.md)

Manual/scripted validation scenarios proving the feature works end-to-end, mapped to the
spec's user stories and success criteria. Run after implementation, before marking the
feature done (constitution §19 Definition of Done).

## Prerequisites

- Solution built and running locally (`dotnet run` for `AskLucy.Web`, which hosts both
  the API and the built `ClientApp`), against a local SQL Server instance with this
  feature's migration applied (`dotnet ef database update`).
- Two test user accounts with no prior `CookieConsentRecord` rows (fresh accounts, or
  accounts with existing rows deleted directly for test setup).
- `appsettings.Development.json` configured with a known `CookiePolicyOptions:CurrentVersion`
  (e.g. `"2026-07-30.1"`) so version-bump scenarios can be simulated by editing this value.

## Scenario 1 — First-login blocking banner (User Story 1 / SC-001, SC-002)

1. Log in with a fresh account (no prior consent record).
2. Confirm the consent banner appears immediately on the main app page, and confirm
   clicking/tapping anywhere else in the app (nav, chat input) has no effect while the
   banner is open (FR-020).
3. Select "Accept All" → confirm the banner closes and the app becomes interactive.
4. Log out and log back in → confirm the banner does **not** reappear.

**Pass condition**: banner blocks all other interaction until a decision is made; no
non-essential activity is possible before that decision (FR-019); banner never reappears
after an explicit decision under the same policy version (SC-002).

## Scenario 2 — Reject and Customize paths (User Story 1)

1. With a second fresh account, open the banner and select "Reject Non-Essential" →
   confirm `GET /api/v1/users/me/cookie-consent` now returns `functional: false,
   analytics: false, marketing: false` and the app remains fully usable (core
   functionality unaffected, spec.md Edge Cases).
2. With a third fresh account, open the banner, choose "Customize," toggle Functional on
   and leave Analytics/Marketing off, confirm the Essential toggle is locked on and
   cannot be switched off, then save → confirm the response reflects exactly that
   combination.

**Pass condition**: all three banner paths (Accept All, Reject Non-Essential, Customize)
persist the expected per-category state; Essential can never be toggled off anywhere.

## Scenario 3 — Manage preferences from Settings (User Story 2 / FR-011..FR-014, FR-017)

1. As a user with an existing consent decision, open Settings > Cookies.
2. Confirm the current per-category toggles and a "last updated" timestamp matching the
   value from `GET /api/v1/users/me/cookie-consent` are displayed.
3. Toggle Analytics on and save → confirm the change is reflected immediately (no reload
   needed) and the "last updated" timestamp refreshes to the new save time.
4. Simulate a save failure (e.g., stop the API or block the request in dev tools) and
   attempt to save again → confirm a visible error message appears and the previously
   known preferences remain in effect (constitution §2.VIII, spec.md FR-017).

**Pass condition**: preferences are viewable/editable at any time from Settings; changes
take effect immediately; failures are never silent.

## Scenario 4 — Privacy Page reachability and content (User Story 3 / FR-008..FR-010, SC-006)

1. Without logging in, navigate directly to `/privacy` → confirm the page loads (no
   redirect to login) and displays cookie categories, their purposes, third-party
   services, and data retention information, plus the current policy version/effective
   date (from the anonymous `GET /api/v1/cookie-policy`).
2. From the consent banner, click the privacy link → confirm it opens the Privacy Page.
3. From the global footer (visible on both the authenticated app and the Privacy Page
   itself), click the Privacy link → confirm it opens/stays on the Privacy Page.
4. As a logged-in user on the Privacy Page, click "manage your preferences" → confirm it
   navigates to Settings > Cookies.
5. From Settings > Cookies (Cookie Preferences), click the Privacy link → confirm it
   opens the Privacy Page (FR-010, SC-006).

**Pass condition**: Privacy Page is reachable in 1 click from banner, footer, and
Settings; loads without authentication; displays the live policy version, not a hardcoded
string.

## Scenario 5 — Policy version bump triggers re-consent for everyone (Edge Cases / FR-007)

1. With both test accounts from Scenario 1/2 already holding a saved decision, change
   `CookiePolicyOptions:CurrentVersion` in configuration (simulating a real policy
   update) and restart the app.
2. Log in as each account → confirm the banner reappears and again blocks interaction
   until a new decision is recorded, even though each account previously had a decision
   under the old version.
3. Confirm the Privacy Page's displayed version/effective date also reflects the new
   value.

**Pass condition**: 100% of previously-consented users are re-prompted after a policy
version change; no stale version is shown anywhere.

## Scenario 6 — Cross-cutting: ownership and silent-failure checks

1. As User A, attempt `GET`/`PUT /api/v1/users/me/cookie-consent` with User B's bearer
   token replaced by User A's — confirm the response always reflects the token's own
   user, never another account's data (no id is ever accepted from the client, per the
   contract).
2. Call `GET /api/v1/cookie-policy` with no `Authorization` header at all → confirm
   `200 OK` (anonymous access works).
3. Call either `me` endpoint with no `Authorization` header → confirm `401`.
4. Trigger a validation failure on `PUT` (omit a required boolean field) → confirm `400`
   Problem Details, not a generic/ad hoc error shape.

**Pass condition**: consent data is always scoped to the caller's own token; the public
endpoint truly requires no auth; every failure path returns RFC 7807 Problem Details.
