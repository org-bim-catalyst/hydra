# Quickstart: Hangfire Dashboard Access from Admin Panel

## Prerequisites

- Backend running locally (`dotnet run` from `src/AskLucy.Web`) with an Administrator or
  Super User account seeded (dev baseline seeder already provides one — see
  `web_tests_need_persistence_connection_env` memory for the connection string requirement).
- Frontend dev server running (`npm run dev` from `src/AskLucy.Web/ClientApp`).

## Validate User Story 1 (open the dashboard)

1. Sign in as an Administrator or Super User.
2. Navigate to the admin panel; confirm the new sidebar entry (e.g. "Jobs") is visible.
3. Sign out, sign in as a non-admin user with admin-panel access; confirm the entry is **not**
   visible (FR-001, FR-005, Acceptance Scenario 1.3).
4. As the Administrator again, click the entry.
   - **Expected**: a new tab opens within ~5s already showing live Hangfire job data — no login
     prompt, no 401/403 page (SC-001, Acceptance Scenario 1.1).
5. Leave the tab open and idle for a few minutes while the dashboard's own auto-refresh runs.
   - **Expected**: it keeps working without re-prompting for auth (Acceptance Scenario 1.2).
6. In a private/incognito window (no session), request `/hangfire` directly.
   - **Expected**: refused (unauthenticated) — confirms server-side enforcement is independent
     of the sidebar's visibility (Edge case: direct URL access).

## Validate User Story 2 (theme match)

**Known gap (research.md Decision 3 / T025)**: Hangfire 1.8.24's dashboard has no persistence
mechanism for its dark mode at all — no cookie, no `localStorage`, no query-string hook. It is
driven entirely by the *browser/OS* `prefers-color-scheme` media query, not by anything this app
controls. So despite the original plan, the dashboard's theme cannot actually be selected "from
the admin's current theme mode at link-click time" — there is no API surface in this Hangfire
version to do that. The steps below verify what the app *can* control (its palette colors) against
the OS's own light/dark setting, not against the SPA's in-app theme toggle. SC-003's "100% theme
match" is scoped to that — palette colors match; dark/light *selection* follows the OS, not the
SPA.

1. With the OS/browser set to light mode, click the sidebar entry.
   - **Expected**: the dashboard opens already in a light appearance using the app's palette
     colors (primary `#1F4E5E`-derived accents), not Hangfire's stock blue.
2. Switch the OS/browser to dark mode (not the admin panel's own theme toggle — it has no effect
   here), click the entry again (a fresh tab).
   - **Expected**: the new tab opens already in dark mode using the app's dark palette values —
     no flash of the wrong theme, no manual toggle needed inside the dashboard.
3. With the OS in one mode and the admin panel's own in-app theme toggle set to the *other* mode,
   click the entry.
   - **Expected**: the dashboard follows the OS setting, not the SPA's toggle — this is the
     documented gap above, not a bug.

## Validate failure paths (no silent failures)

1. Simulate an expired session (e.g. clear the access token / let it expire) and click the
   entry.
   - **Expected**: a visible error (toast/inline), not a dead or blank new tab (FR-008).
2. Block pop-ups for the site in the browser, then click the entry.
   - **Expected**: a visible message prompting to allow pop-ups (FR-009), not silence.

## Out of scope for this quickstart

- Exercising specific Hangfire job-management actions (retry/delete/trigger) inside the
  dashboard itself — that's Hangfire's own existing, unmodified behavior, not part of this
  feature.
