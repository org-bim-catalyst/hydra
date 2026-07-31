# Tasks: Admin AI Provider Configuration UI

**Input**: Design documents from `/specs/007-admin-ai-provider-ui/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included, not optional here — plan.md's Constitution Check already commits to a
component test (`AiProviderActionsMenu.test.tsx`) and an a11y test
(`AdminAiProvidersPage.a11y.test.tsx`) as design requirements, per this project's
constitution §10/§18 ("always update or add tests when changing observable behavior").

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P2/P2) so each
story can be implemented and demoed independently once Setup + Foundational are done.
This is a **frontend-only** feature — every task below is in `AskLucy.Web/ClientApp`; no
backend task exists because the backend capability was delivered under
`005-multi-provider-ai-engine` (verified directly, see research.md Decision 4).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unmet dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Every task names an exact file path

## Path Conventions

Existing single web app: `src/AskLucy.Web/ClientApp/src/features/admin/{api,components,pages}/`, `src/AskLucy.Web/ClientApp/src/routes/router.tsx` — see plan.md's Project Structure.

---

## Phase 1: Setup

**Purpose**: The one shared, behavior-only module every user story calls into — no UI yet.

- [X] T001 [P] Create `adminAiProvidersApi.ts` — `AdminAiProvider` interface (mirrors `AdminAiProviderDto`: `id, providerKey, displayName, isEnabled, hasCredential, credentialLastRotatedAtUtc, defaultModelId, healthStatus, healthStatusCheckedAtUtc`) plus `getProviders()`, `updateProvider(id, { isEnabled })`, `setCredential(id, apiKey)`, `clearCredential(id)`, each calling the existing endpoints per contracts/admin-ai-providers.md, in `src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiProvidersApi.ts`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The page shell, route, and nav entry every user story needs to even be reachable.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 Create `AdminAiProvidersPage.tsx` page shell — `PageHeader` (`backTo="/admin/dashboard"`, title "AI providers") + `useQuery` over `getProviders()` + an MUI `Table` with columns for display name, enabled state, and credential-configured state (health-status column added in US2; actions column added in US1), mirroring `AdminUsersPage.tsx`'s shape, in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx` (depends on T001)
- [X] T003 Add the `/admin/ai-providers` route — lazy-loaded, wrapped in `ProtectedRoute` + `AdminRoute` exactly like `/admin/users`, in `src/AskLucy.Web/ClientApp/src/routes/router.tsx` (depends on T002)
- [X] T004 [P] Add a "Manage AI providers" button to `AdminDashboardPage.tsx`'s `PageHeader` `actions` slot, linking to `/admin/ai-providers` (mirrors the existing "Manage users" button), in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminDashboardPage.tsx`

**Checkpoint**: The page is reachable from the Admin Dashboard and lists every provider
read-only (enabled/credential-configured state). No state-changing action exists yet —
that is what each user story phase below adds.

---

## Phase 3: User Story 1 - Administrator enables a provider for the first time (Priority: P1) 🎯 MVP

**Goal**: An administrator can set a credential for a disabled provider and enable it,
with each step confirm-gated (FR-010) and a specific, actionable rejection if enabling is
attempted with no credential (FR-003).

**Independent Test**: Set a credential for a disabled provider, confirm; enable it,
confirm; verify it now appears in the end-user provider catalog (quickstart Scenario 1).

### Tests for User Story 1

- [X] T005 [P] [US1] Component tests for `AiProviderActionsMenu`'s set-credential and enable actions — dialog opens on menu click, Cancel does not call the API, Confirm calls `setCredential`/`updateProvider` respectively; for a provider with `hasCredential: false`, clicking Enable shows the "needs a credential" explanation immediately with **no API call and no confirmation dialog** (FR-003); and after confirming a credential submission, assert the typed value does not appear anywhere in the rendered output and is cleared from local state (SC-002) — in `src/AskLucy.Web/ClientApp/src/features/admin/components/AiProviderActionsMenu.test.tsx` (mirrors `UserActionMenu.test.tsx`'s assertion style)

### Implementation for User Story 1

- [X] T006 [US1] Implement `AiProviderActionsMenu.tsx`'s **set/replace credential** action: menu item opens a dialog with a masked (`type="password"`) `TextField` holding the value in local `useState` only, Cancel clears it, Confirm calls `setCredential(id, apiKey)` and invalidates the providers query on success, in `src/AskLucy.Web/ClientApp/src/features/admin/components/AiProviderActionsMenu.tsx` (depends on T001, T002)
- [X] T007 [US1] Implement `AiProviderActionsMenu.tsx`'s **enable** action: if `hasCredential: false` (already known client-side from the fetched row), clicking Enable shows the explanation inline immediately — no API call, no confirmation dialog (FR-003). If `hasCredential: true`, clicking opens a confirm dialog whose Confirm calls `updateProvider(id, { isEnabled: true })`. In the same file (depends on T006)
- [X] T008 [US1] Wire `AiProviderActionsMenu` into each row's Actions column in `AdminAiProvidersPage.tsx`, and confirm the row's enabled/credential-configured cells refresh immediately after either action succeeds, in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx` (depends on T002, T006, T007)
- [X] T009 [US1] Ensure every action in this story surfaces clear, visible success/error feedback (Snackbar or inline `Alert`) — never a silent no-op, per constitution Principle VIII (§2) and spec FR-008 — in `AiProviderActionsMenu.tsx` (depends on T006, T007)

**Checkpoint**: User Story 1 is fully functional and independently testable — this is the
feature's MVP; it is the only story that unblocks every other multi-provider capability
already built in the product.

---

## Phase 4: User Story 2 - Administrator reviews provider status at a glance (Priority: P2)

**Goal**: Every provider's enabled/health state is visible on the page with zero extra
interaction.

**Independent Test**: With providers in varying states, reload the page and confirm every
row shows enabled state, health status, and roughly when health was last checked
(quickstart Scenario 2).

### Implementation for User Story 2

- [X] T010 [US2] Add a health-status column to `AdminAiProvidersPage.tsx`'s table: a `Chip` colored `success`/`error`/`default` for `Healthy`/`Unhealthy`/`Unknown` (matching `AdminUsersPage.tsx`'s existing status-chip color convention) plus `healthStatusCheckedAtUtc` rendered as localized date/time text next to it, in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx` (depends on T002; sequenced after T008 — same file as US1's changes)

### Tests for User Story 2

- [X] T011 [P] [US2] Automated a11y check (axe) for `AdminAiProvidersPage`, matching `AdminDashboardPage.a11y.test.tsx`'s pattern, in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.a11y.test.tsx`

**Checkpoint**: User Stories 1 and 2 both work independently — an admin can configure a
provider and monitor every provider's status from the same page.

---

## Phase 5: User Story 3 - Administrator disables a provider or rotates/removes its credential (Priority: P2)

**Goal**: An administrator can safely reverse User Story 1 — disable a provider, replace
its credential without extra steps, or clear its credential (which always also disables
it, made explicit in the confirmation copy per FR-005/FR-010).

**Independent Test**: Disable an enabled provider and confirm it disappears from the
end-user catalog; replace a credential and confirm the provider keeps working; clear a
credential and confirm the row shows both "no credential" and "disabled" at once
(quickstart Scenario 3).

### Tests for User Story 3

- [X] T012 [US3] Extend `AiProviderActionsMenu.test.tsx` (from T005) with the disable and clear-credential flows: Cancel does not call the API for either; clearing a credential's confirmation dialog text explicitly states it will also disable the provider, in `src/AskLucy.Web/ClientApp/src/features/admin/components/AiProviderActionsMenu.test.tsx` (depends on T005; same file, run after it, not in parallel)

### Implementation for User Story 3

_T013–T015 reuse the same success/error feedback plumbing built in T009 (Snackbar/inline Alert on every action) — FR-008 coverage is inherited, not re-implemented._

- [X] T013 [US3] Implement `AiProviderActionsMenu.tsx`'s **disable** action: confirm dialog, Confirm calls `updateProvider(id, { isEnabled: false })`, in `src/AskLucy.Web/ClientApp/src/features/admin/components/AiProviderActionsMenu.tsx` (depends on T007; same file)
- [X] T014 [US3] Implement `AiProviderActionsMenu.tsx`'s **clear credential** action: confirm dialog copy explicitly states clearing will also disable the provider (FR-005/FR-010), Confirm calls `clearCredential(id)` and invalidates the providers query so the row reflects both "no credential" and "disabled" immediately, in the same file (depends on T013)
- [X] T015 [US3] Verify/wire the **replace credential** path: reuse T006's set-credential dialog for a provider where `hasCredential` is already `true`, confirming the provider's `isEnabled` state is left untouched by a credential replacement alone (no separate disable/re-enable step, per FR-006), in `AiProviderActionsMenu.tsx` (depends on T006, T014)

**Checkpoint**: All three user stories are independently functional — the feature is
complete per spec.md.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T016 [P] Confirm non-administrator denial for `/admin/ai-providers` (spec FR-007, quickstart Scenario 4): this is already structurally guaranteed by reusing `AdminRoute` client-side and the existing, already-tested `[Authorize(Policy = "AdministratorOrSuperUser")]` on `AdminAiProvidersController` server-side (see `tests/AskLucy.Web.Tests/Ai/AdminAiProvidersControllerTests.cs`) — this task is a verification pass, not new code; record the result in quickstart.md
- [X] T017 Run all 4 quickstart.md scenarios end-to-end and record results — Scenario 3 step 1 is FR-009's only verification (past message attribution unaffected by later provider changes); no dedicated implementation task exists for FR-009 since this feature never touches Message/attribution storage. Results recorded in quickstart.md's "Verification results" section — logic scenarios covered by automated tests, access control confirmed via existing server tests; a full live-browser run with a real database was **not** performed (no SQL Server/Docker in this sandbox) and should be done before shipping.
- [X] T018 [P] Update `docs/ARCHITECTURE.md` (or the admin-area section of it, if one exists) to mention the AI Providers admin page, per constitution §13 — **skipped**: `docs/ARCHITECTURE.md`'s admin section only lists generic feature-folder conventions (`admin/`, `Admin/`), never naming individual pages (`AdminUsersPage`/`AdminDashboardPage` aren't named there either), so there is no existing pattern of per-page doc entries to extend

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–5)**: All depend on Foundational. US1 is the MVP and has no
  dependency on US2/US3. US2 and US3 both touch `AdminAiProvidersPage.tsx`/
  `AiProviderActionsMenu.tsx` alongside US1's changes, so — despite being conceptually
  independent per their Independent Test — they are sequenced after US1 in this task list
  to avoid two people/passes editing the same file's same region simultaneously.
- **Polish (Phase 6)**: Depends on US1–US3 all being complete.

### User Story Dependencies

- **US1 (P1)**: Foundational only. Fully independent of US2/US3 — the feature's MVP.
- **US2 (P2)**: Foundational only for its *Independent Test* to hold; sequenced after US1
  in this list purely for same-file conflict avoidance, not a real functional dependency.
- **US3 (P2)**: Foundational + reuses the enable/set-credential dialog mechanics US1
  builds (T006, T007) rather than re-implementing them — genuinely sequenced after US1.

### Within Each User Story

- Tests written first where included, confirmed failing, then implementation.
- Story complete and independently checkpointed before moving to the next.

### Parallel Opportunities

- T001 (Setup) and T004 (Foundational nav button) touch files nothing else in their phase
  touches — parallel-safe.
- T005 (US1 test file) and T011 (US2 a11y test file) are different, new files — parallel-safe
  with each other, though T011 is sequenced in a later phase here.
- Everything else in `AiProviderActionsMenu.tsx`/`AdminAiProvidersPage.tsx` is
  intentionally sequential (same file, building on the same confirm-dialog mechanics) —
  this is a small feature where forcing parallelism across those tasks would create more
  merge conflicts than it would save time.

---

## Parallel Example: Setup + Foundational

```bash
# T001 (new api file) and T004 (existing but different file, dashboard nav button)
# can be done together once T002/T003 aren't yet blocking:
Task: "Create adminAiProvidersApi.ts in src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiProvidersApi.ts"
Task: "Add 'Manage AI providers' button in src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminDashboardPage.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (blocks everything).
3. Complete Phase 3 (US1) — this alone unblocks every other multi-provider capability
   already built in the product (chat picker, Settings default tab).
4. **STOP and VALIDATE**: run quickstart.md Scenario 1.
5. Deploy/demo if ready.

### Incremental Delivery

1. Setup + Foundational → page reachable, read-only.
2. US1 → MVP → validate → deploy/demo.
3. US2 (status at a glance) → validate → deploy/demo.
4. US3 (disable/rotate/clear) → validate → deploy/demo.
5. Polish (Phase 6) → access-control verification, doc update, full quickstart pass.

---

## Notes

- `[P]` tasks touch different files with no unmet dependency on an incomplete task.
- `[Story]` labels trace every task back to spec.md for scope/priority audits.
- This feature adds **zero backend tasks** — verified directly against the already-shipped
  `AdminAiProvidersController` (research.md Decision 4) before writing this list.
- Commit after each task or logical group; stop at any checkpoint to validate a story
  independently before continuing.
- Avoid: vague tasks, two tasks editing the same file marked `[P]`, cross-story
  dependencies that would break a story's independent testability.
