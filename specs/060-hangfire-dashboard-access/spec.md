# Feature Specification: Hangfire Dashboard Access from Admin Panel

**Feature Branch**: `060-hangfire-dashboard-access`

**Created**: 2026-09-19

**Status**: Implemented (User Story 2's theme-*selection* mechanism shipped with a documented gap — see note below FR-006)

**Input**: User description: "Add Hangfire dashboard entry admin panel sidebar. Clicking it authenticates short-lived, Hangfire-only cookie via new bearer-authenticated endpoint (Administrator/Super User policy), then opens /hangfire in new browser tab (not iframe). Apply app's existing theme (palette tokens, light/dark mode) Hangfire dashboard via Hangfire's DashboardOptions.StylesheetFiles/DarkModeStylesheetFiles, light/dark selected from admin's current theme mode at link-click time."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Open the background jobs dashboard from the admin panel (Priority: P1)

An administrator browsing the admin panel wants to check on background job activity (queued, running, failed jobs, recurring schedules) without knowing a hidden URL or manually attaching credentials. They see a "Jobs" (or equivalent) entry in the admin sidebar alongside the other admin sections, click it, and the background jobs dashboard opens ready to use in a new tab — no separate sign-in step, no broken/unauthorized page.

**Why this priority**: This is the entire feature. Without it, the dashboard remains reachable only by administrators who already know it exists and can attach credentials themselves — which today effectively means it isn't reachable through the product at all.

**Independent Test**: As an Administrator or Super User, open the admin panel, click the new sidebar entry, and confirm a new tab opens showing live job data (not a login/401/403 page).

**Acceptance Scenarios**:

1. **Given** an Administrator is viewing the admin panel, **When** they click the background jobs sidebar entry, **Then** a new browser tab opens showing the background jobs dashboard already displaying current job data.
2. **Given** the dashboard tab has been open for a while, **When** the administrator interacts with it (e.g. switches between its views, lets it refresh its live counters), **Then** it continues to work for a reasonable working session rather than immediately failing as unauthorized.
3. **Given** a user without Administrator or Super User privileges is viewing the admin panel, **When** the sidebar renders, **Then** no background jobs entry is shown to them, and a direct attempt to reach the dashboard is refused.

---

### User Story 2 - Dashboard looks and feels like part of Ask Lucy (Priority: P2)

An administrator who opens the background jobs dashboard sees a look consistent with the rest of the platform — matching colors and matching light/dark mode — rather than a jarring, unrelated-looking tool bolted on the side.

**Why this priority**: Valuable for a cohesive product feel and to reduce the "did I open the right thing / is this safe" hesitation an unbranded internal tool can cause, but the dashboard is fully usable for its core purpose (User Story 1) even with its default appearance.

**Independent Test**: Open the dashboard with the OS/browser in light mode and again in dark mode, and confirm each opens using that mode's Ask Lucy palette colors rather than Hangfire's stock appearance.

**Known gap (see note below FR-006)**: Hangfire's dashboard has no API to select or persist dark mode at all — it follows only the browser/OS `prefers-color-scheme` setting. So while the *colors* always match Ask Lucy's palette, *which* mode (light/dark) is shown does not follow the admin panel's own theme toggle. Acceptance Scenarios 2 and 3 below describe the originally intended behavior and are retained as a record of scope, but are not what ships; see FR-006's note for what is actually verifiable.

**Acceptance Scenarios**:

1. **Given** the administrator's admin panel is in light mode, **When** they open the background jobs dashboard, **Then** it opens in a light appearance using colors consistent with the rest of the platform.
2. ~~**Given** the administrator's admin panel is in dark mode, **When** they open the background jobs dashboard, **Then** it opens in a dark appearance using colors consistent with the rest of the platform.~~ Not deliverable as written — see the known-gap note above; the dashboard's light/dark selection follows the OS, not the admin panel.
3. ~~**Given** the administrator switches the admin panel's theme and then opens the dashboard again, **When** the new tab loads, **Then** it reflects the theme that was active at the moment they clicked, not a stale or default one.~~ Not deliverable as written, for the same reason.

---

### Edge Cases

- What happens if the click-to-authenticate step fails (e.g. the administrator's session has expired)? The user must see a clear, visible error rather than a silently blank or stuck tab, consistent with the platform's no-silent-failures standard.
- What happens if the browser blocks the new tab as a pop-up? The administrator must get a visible, actionable message telling them to allow pop-ups, rather than nothing appearing to happen.
- What happens if an authenticated but unauthorized user (not Administrator/Super User) somehow reaches the dashboard URL directly? Access must still be refused server-side, independent of whether the sidebar entry is shown.
- What happens if the administrator leaves the dashboard tab open past the authenticated window's validity? Further activity in that tab must fail safely (e.g. prompting to reopen it from the admin panel) rather than exposing job data past the intended session.
- What happens if the administrator opens the dashboard multiple times in a row? Each click produces its own valid session; opening multiple tabs concurrently is not blocked.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The admin panel sidebar MUST include an entry for the background jobs dashboard, visible only to users in the Administrator or Super User roles, positioned consistently with the panel's other section entries.
- **FR-002**: Clicking the entry MUST establish an authenticated session for the dashboard on behalf of the current administrator before the dashboard is opened, requiring no separate manual sign-in.
- **FR-003**: The dashboard MUST open in a new browser tab, leaving the administrator's place in the admin panel undisturbed.
- **FR-004**: The dashboard session established by a click MUST remain valid for a reasonable working session (target: 30 minutes) so an administrator can review job data, including the dashboard's own periodic live-refresh activity, without being unexpectedly signed out mid-review.
- **FR-005**: A user who is not an Administrator or Super User MUST be refused access to the dashboard, whether they attempt it via the sidebar (not shown to them) or by any other means of reaching its address.
- **FR-006**: ~~The dashboard's appearance MUST reflect the admin panel's current color theme (light or dark) at the moment the administrator opens it.~~ **Shipped as**: the dashboard's light/dark *selection* follows the browser/OS `prefers-color-scheme` setting, not the admin panel's own theme state — Hangfire 1.8.24 exposes no mechanism (cookie, `localStorage`, query string) to select or persist dark mode any other way (research.md Decision 3, T025). This is an accepted, documented gap, not an oversight; a future spec could close it with a custom injected toggle script (see ADR 0013).
- **FR-007**: The dashboard's colors MUST be visually consistent with the rest of the platform's established look (matching brand colors and both light/dark palettes), not the dashboard tool's out-of-the-box appearance. — **Fully delivered**, independent of FR-006's gap: both the light and dark stylesheets use Ask Lucy's palette tokens, whichever one the OS/browser selects.
- **FR-008**: If establishing the dashboard session fails, the administrator MUST see a visible error explaining that the dashboard could not be opened, rather than a silent failure or unexplained blank tab.
- **FR-009**: If the new tab is blocked by the browser, the administrator MUST see a visible message prompting them to allow pop-ups for the site.

### Key Entities

- **Dashboard access session**: A short-lived, purpose-specific authorization that lets an administrator's browser tab interact with the background jobs dashboard, established at click time and independent of the administrator's regular sign-in session.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Administrator or Super User can go from the admin panel to a working, already-authenticated background jobs dashboard in under 5 seconds, in one click.
- **SC-002**: 100% of users outside the Administrator/Super User roles cannot reach the dashboard, whether via the admin panel UI or by direct address, verified by attempted access producing a refusal.
- **SC-003**: ~~100% of dashboard opens visually match the admin panel's active theme (light or dark) at the moment of opening~~ **Verified as shipped**: 100% of dashboard opens use Ask Lucy's palette colors for whichever mode (light/dark) the browser/OS is currently set to, with no manual re-theming step needed — see FR-006's note for why "at the moment of opening" no longer applies to *mode selection*, only to palette color accuracy.
- **SC-004**: A dashboard session remains usable for at least 30 minutes of normal review activity without requiring the administrator to reopen it.
- **SC-005**: Every failure path (expired session, blocked pop-up, unauthorized access) produces a visible, understandable message to the user — zero silent failures.

## Assumptions

- The background jobs dashboard referenced here is the existing Hangfire dashboard already running in the platform at a fixed internal address; this feature is about making it reachable and on-theme from the admin panel, not building a new dashboard.
- "Administrator or Super User" is the same authorization boundary already enforced for the dashboard today; this feature does not change who is allowed to view job data, only how they reach it.
- A 30-minute session window is a reasonable default for a working review session; it can be tuned later without changing the feature's intent.
- ~~The admin panel's current theme (light/dark) is already known to the client at the moment of the click; no new theme-detection mechanism is needed.~~ **Disproven during implementation**: this assumption held for the client, but not for Hangfire itself — the dashboard has no mechanism to *receive* that client-known theme (research.md Decision 3). The client-side assumption was correct; the missing piece was on the third-party dashboard's side.
- Visual "consistency with the platform's look" means colors/palette matching, not a pixel-perfect recreation of the admin panel's own components inside the dashboard tool.
