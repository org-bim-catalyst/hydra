# Quickstart: Admin Panel Layout & Polish Pass

Manual verification steps, one per user story. Run the frontend dev server (`npm run dev` in `src/AskLucy.Web/ClientApp`) and sign in as a built-in admin.

## Prerequisites

- At least one AI provider configured with a `healthStaleAfterUtc` in the past (to see the staleness state) and one without (to see the normal state).
- At least one existing Workflow Policy and one existing Agent Policy (to verify the table still renders correctly post-conversion).
- Browser window resized to both a tall desktop viewport and a short viewport (~700px tall) to check stretching behavior at different heights.

## US1 — AI Providers staleness column

1. Navigate to Admin → AI providers.
2. Confirm the table has a distinct column for the staleness indicator, separate from the Health column.
3. Confirm the "Possibly out of date" chip (when present) no longer wraps onto a second line within the Health cell, and its tooltip no longer visually overlaps the row below.
4. Confirm a provider with a recent health check shows no staleness indicator in that column (empty/neutral, not an error state).

## US2 — Full-height admin pages

1. Navigate to Admin → Default models (or any admin page).
2. Confirm the sidebar nav and the tab content both extend to fill the full viewport height, with no dead/empty gap below either, matching Account Settings' behavior.
3. Resize the browser to a short viewport; confirm both panes scroll independently within their own bounds rather than the whole page scrolling or content being clipped.
4. Repeat on at least 2-3 other admin pages to confirm the fix is shell-level, not page-specific.

## US3 — Hint anchored under the table

1. Navigate to Admin → Default models (short table, few providers).
2. Confirm the info hint sits directly beneath the table with no visible gap, and just above the page's bottom edge — not at the top of the content area.
3. Navigate to Admin → AI capabilities.
4. Confirm the same hint-under-table placement, regardless of how many rows the table has.

## US4 — Policy creation as a modal

1. Navigate to Admin → Workflow policies.
2. Confirm there is no inline "New Policy" form; instead, a button (e.g. "New policy") opens a modal.
3. Open the modal, fill in required fields, submit; confirm the new policy appears in the table and the modal closes.
4. Open the modal again and cancel/close without submitting; confirm no partial policy is created and the table is unaffected.
5. Confirm the policies table now occupies the full available page height (per US2).
6. Repeat steps 1-5 on Admin → Agent policies.

## US5 — Sidebar reorder + divider

1. Open any admin page and inspect the sidebar nav order.
2. Confirm the order is: Dashboard, Users, Roles, Role assignments, System agents, [divider], AI providers, Default models, AI capabilities, Agent policies, Workflow policies, MCP servers, Jobs.
3. Confirm a horizontal divider renders between "System agents" and "AI providers", and no divider appears anywhere else in the list.
4. Confirm "System agents" still navigates correctly and retains its existing permission gating (not visible to admins lacking the required permission).

## Regression checks

- Keyboard navigation through the sidebar (Tab/Shift+Tab, Enter to activate) still works end-to-end, including across the new divider.
- Existing `AdminShell.a11y.test.tsx`-style checks still pass (no new axe violations).
- Sidebar collapse/expand toggle still works and persists via localStorage as before.
