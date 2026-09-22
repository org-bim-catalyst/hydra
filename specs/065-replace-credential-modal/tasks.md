---

description: "Task list for 065-replace-credential-modal"

---

# Tasks: Replace Credential Modal Polish

**Input**: Design documents from `/specs/065-replace-credential-modal/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md (no contracts/ — no API surface change)

**Tests**: Constitution §4 (frontend error/state changes are DOM-visible behavior) and this repo's established convention (see spec 062) pair every user-visible behavior change with a test-update task in the same phase. Included below.

**Organization**: Tasks are grouped by user story. All three stories touch the same two files (`AiProviderActionsMenu.tsx` and its test file) since the dialog is one shared component — tasks within and across stories are therefore sequential (no `[P]`), not because of artificial ordering but because they are genuinely the same file.

## Format: `[ID] [P?] [Story] Description`

## Path Conventions

Single existing web app component — all paths under `src/AskLucy.Web/ClientApp/src/features/admin/components/`.

---

## Phase 1: Setup

**Purpose**: Project initialization and basic structure

No tasks — `@mui/icons-material` (source of `Visibility`/`VisibilityOff`) is already a project dependency (used elsewhere in this same file for `KeyIcon`, `MoreVertIcon`, etc.), and no new tooling, package, or scaffolding is required for this feature.

---

## Phase 2: Foundational

**Purpose**: Blocking prerequisites shared by all user stories

No tasks — each user story is an independent, additive edit to the existing dialog markup in `AiProviderActionsMenu.tsx`. None requires infrastructure the others don't already have.

---

## Phase 3: User Story 1 - Reveal/hide toggle for the typed API key (Priority: P1) 🎯 MVP

**Goal**: A show/hide toggle appears inside the API key field only once the administrator has typed a character, and toggling it reveals/re-hides the typed value without changing it.

**Independent Test**: Open the credential dialog for any provider, type a value, confirm the toggle appears and correctly reveals/re-hides the text; delete the value and confirm the toggle disappears.

### Tests for User Story 1

- [X] T001 [US1] In `src/AskLucy.Web/ClientApp/src/features/admin/components/AiProviderActionsMenu.test.tsx`, add cases: (a) no eye-toggle button is present when the API key field is empty, (b) typing a character renders the toggle, (c) clicking the toggle switches the field's `type` between `password` and `text` without changing its value, (d) clearing the field back to empty removes the toggle again. Run and confirm these fail against current code.
- [X] T001a [US1] (Added during implementation — closes analysis finding E1) Add a test asserting `showApiKey`/masking resets to default when the dialog is closed and reopened after being toggled on (FR-010).

### Implementation for User Story 1

- [X] T002 [US1] In `src/AskLucy.Web/ClientApp/src/features/admin/components/AiProviderActionsMenu.tsx`, import `Visibility`/`VisibilityOff` from `@mui/icons-material` and `InputAdornment` from `@mui/material`; add local `showApiKey` state (`useState(false)`).
- [X] T003 [US1] Wire the credential dialog's `TextField`: `type={showApiKey ? 'text' : 'password'}`, and a trailing `InputAdornment`/`IconButton` toggling `showApiKey`, rendered only when `apiKeyInput.length > 0`.
- [X] T004 [US1] Reset `showApiKey` to `false` in both `openCredentialDialog` and `closeCredentialDialog`, matching how `apiKeyInput` already resets (data-model.md).

**Checkpoint**: T001's tests pass. User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Empty field never looks pre-filled (Priority: P1)

**Goal**: The API key field always visibly reads as empty (placeholder text, not a masked value) when opened, whether or not the provider already has a credential configured.

**Independent Test**: Open "Replace credential" for a provider that already has a credential configured; confirm the field is empty with placeholder text, not a masked pre-filled value; close and reopen to confirm it resets the same way each time.

### Tests for User Story 2

- [X] T005 [US2] In `AiProviderActionsMenu.test.tsx`, add a case asserting the API key field shows placeholder text "Please insert API key here" when the dialog opens for a provider with `hasCredential: true` (reusing the existing `disabledWithCredential`/`enabledWithCredential` fixtures), and that the field's value is empty. Confirm it fails (or already documents current behavior with an explicit placeholder assertion) before implementation.

### Implementation for User Story 2

- [X] T006 [US2] In `AiProviderActionsMenu.tsx`, add `placeholder="Please insert API key here"` to the credential dialog's `TextField`.

**Checkpoint**: T005's test passes. User Stories 1 AND 2 both work independently.

---

## Phase 5: User Story 3 - Double-width dialog (Priority: P2)

**Goal**: The credential dialog renders at roughly double its previous width on desktop viewports, without overflowing on narrow viewports.

**Independent Test**: Open the credential dialog on a desktop-width viewport and confirm it renders noticeably wider than before; confirm on a narrow viewport it still fits without horizontal overflow.

### Implementation for User Story 3

- [X] T007 [US3] In `AiProviderActionsMenu.tsx`, add `maxWidth="sm" fullWidth` to the credential `Dialog` (research.md: `sm` + `fullWidth` roughly doubles the visible width while staying within MUI's responsive breakpoints).

**Checkpoint**: All three user stories are independently functional. Manually verify against `quickstart.md` US3 steps (no automated width assertion — jsdom does not lay out real pixel widths).

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation across all three stories

- [X] T008 Run the full frontend test suite (`ClientApp`) to confirm no regression outside `AiProviderActionsMenu.test.tsx` (per this repo's convention: page-level tests can catch component changes the component's own test file misses). Result: 1480/1481 passed; the 1 failure (`ChatPage.test.tsx` voice-preference hydration) is the pre-existing known full-suite-only flake (5s timeout under parallel load), unrelated to this feature — passes in isolation per prior sessions.
- [ ] T009 Run `quickstart.md` end-to-end in a browser: verify all three user stories against at least two different providers (e.g., Anthropic and OpenAI) to confirm FR-009 (identical behavior across all providers) holds, since only Anthropic fixtures are exercised by the automated tests above. **Not run this session** — no browser/screenshot tool available; deferred to manual verification.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** / **Foundational (Phase 2)**: No tasks — nothing blocks story start.
- **User Stories (Phase 3-5)**: Each can be implemented and tested independently, but all three edit the same file (`AiProviderActionsMenu.tsx`) and the same test file — apply sequentially to avoid diff conflicts, in priority order (US1 → US2 → US3).
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### Within Each User Story

- Tests before implementation (T001 before T002-T004; T005 before T006).
- US3 (T007) has no test task — width is a layout property jsdom does not compute; verified manually via `quickstart.md` in Phase 6 instead.

### Parallel Opportunities

None. Every task in this feature touches one of two files (`AiProviderActionsMenu.tsx`, `AiProviderActionsMenu.test.tsx`), so no `[P]` markers apply — this is a small, sequential, single-component change.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 3 (US1): reveal/hide toggle.
2. **STOP and VALIDATE**: confirm the toggle's appear/disappear/reveal/hide behavior via T001's tests.
3. Ship — US1 delivers the core value (verifying a typed key before submit) independent of US2/US3.

### Incremental Delivery

1. US1 (reveal/hide toggle) → ship.
2. US2 (empty-field placeholder) → ship — closes the "looks pre-filled" ambiguity Story 2 exists to fix.
3. US3 (double width) → ship — pure layout polish, lowest risk, can also be pulled forward first since it's independent of the other two.

### Notes

- No new backend/API work — all tasks are frontend-only, confined to one existing shared component.
- Commit after each user story's checkpoint (T004, T006, T007) or as one combined commit if shipping all three together, consistent with this repo's solo-developer, direct-to-main workflow.
