# Quickstart: Validating Bulk Select-All

**Feature**: `056-bulk-select-all` — proves the spec's three user stories end-to-end. Contracts: [contracts/bulk-actions-api.md](./contracts/bulk-actions-api.md).

## Prerequisites

- Same as specs/055-role-management's quickstart: a local SQL Server connection, `PERSISTENCE_TESTS_CONNECTION_STRING` set. Accounts: **SU** (Super User), **AD** (Administrator, not Super User), and enough regular users/custom roles to exceed one page (≥25 recommended, given a 20-row default page size).

## 1. Build, migrate, test

```powershell
dotnet build
dotnet test tests/AskLucy.Application.Tests
dotnet test tests/AskLucy.Web.Tests
cd src/AskLucy.Web/ClientApp; npx tsc -b --noEmit; npx vitest run
```

No new migration — this feature adds no persisted schema.

## 2. Scenarios

| # | As | Do | Expect | Spec |
|---|---|---|---|---|
| S1 | AD | Users screen, select nothing | Lock/Unlock, Force 2FA reset, Delete all disabled | FR-002, US1-AS4 |
| S2 | AD | Click "select all" with 20 regular users + AD's own row + 1 locked user on the page | AD's own row and the locked user are not selected; header checkbox shows the page count selected | US1-AS1 |
| S3 | AD | With a full-page selection, manually deselect one row | Header checkbox goes indeterminate | FR-006, US1-AS3 |
| S4 | AD | With every selected user currently locked, view the Lock/Unlock button | Reads "Unlock selected" | FR-002, US1-AS5 |
| S5 | AD | Select all on page 1 of a 40-user filtered search, click Lock | Confirmation shows "20 selected on this page" and "40 match your search" | FR-007, US1-AS6 |
| S6 | AD | Choose "this page" in the confirmation, confirm | Only the 20 page rows are locked; result summary lists 0 skipped (assuming none were already locked) | FR-009 |
| S7 | AD | Repeat S5 but choose "all 40 matching" | All 40 eligible users are locked in one action, without visiting page 2 | SC-002 |
| S8 | AD | Select a mix including one already-locked user, click Lock, confirm | Result summary shows that one skipped with reason "already locked" | US1-AS5 edge case |
| SU | SU | Bulk-lock a selection including the only other active Super User along with regular users | The regular users are locked; the Super User row is skipped with reason "last Super User" — batch does not abort | Edge case, FR-010 |
| S9 | AD | Roles screen, select all custom roles (2 built-in roles have no checkbox at all) | Only custom roles selected; "Delete selected" enabled | US2-AS1/AS2 |
| S10 | AD | Click "Delete selected" with roles held by users | Confirmation states page vs. total-matching counts and the total users who will be left with no role | US2-AS3 |
| S11 | AD | Confirm | Selected roles deleted, their holders now show "No role", built-in roles untouched | US2-AS4 |
| S12 | AD | Role assignments screen, pick a role, select all (2 privileged-role holders present) | Privileged-role holders excluded from selection | US3-AS1 |
| S13 | AD | Trigger bulk-assign, choose "all matching", confirm | Every eligible user (not just the page) now holds the role; result count matches exactly | US3-AS2/AS3, SC-002 |
| S14 | — | Query `RoleAuditLogs`/structured logs after S6/S7/S11/S13 | One audit entry per successfully affected row, same as the single-row actions already produce | Consistency with specs/055 |

## 3. Accessibility & responsiveness

Keyboard-only: tab to the header checkbox, space to toggle, tab through row checkboxes, tab to an action button, Enter to open the confirmation, tab through its scope choice, Enter to confirm. Resize to 400px — the toolbar and confirmation dialog must not overflow horizontally.

## 4. Performance spot-check

With 500 eligible rows matching a search (SC-002), `GET .../actions/bulk-eligible-ids` and the subsequent bulk action both complete without the client ever requesting a second page.
