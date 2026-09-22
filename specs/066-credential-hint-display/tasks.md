---

description: "Task list for 066-credential-hint-display"

---

# Tasks: Credential Hint Display

**Input**: Design documents from `/specs/066-credential-hint-display/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/admin-ai-providers-hint.md, quickstart.md

**Tests**: Constitution §10 (Testing Standards) and this repo's established convention (see spec 065) pair every behavior change with a test-update task in the same phase. Included below at every layer touched (Domain, Application, frontend).

## Format: `[ID] [P?] [Story] Description`

## Path Conventions

Existing Clean Architecture solution: `src/AskLucy.Domain`, `src/AskLucy.Application`, `src/AskLucy.Persistence`, `src/AskLucy.Web` (API + `ClientApp/` React SPA), with matching `tests/AskLucy.*.Tests` projects.

---

## Phase 1: Setup

**Purpose**: Project initialization and basic structure

No tasks — no new package, tool, or scaffolding is required; this feature extends existing Domain/Application/Persistence/Web projects already wired up.

---

## Phase 2: Foundational

**Purpose**: Blocking prerequisites shared by both user stories — the persisted hint field, its computation, and its plumbing through every layer up to the API response. Neither user story can be demonstrated until this phase is complete, since both depend on `credentialHint` actually existing on the wire.

- [X] T001 [P] Create `CredentialHintFormatter` (pure static `Format(string plaintextApiKey)` per data-model.md: `>=8` chars → `first4...last4`; `<8` chars → `****`) in `src/AskLucy.Application/Ai/CredentialHintFormatter.cs`.
- [X] T002 [P] Add `tests/AskLucy.Application.Tests/Ai/CredentialHintFormatterTests.cs` covering: a typical key (e.g. `sk-ant-api03-C5fxxxxxxxxygAA` → `sk-a...ygAA`), the exact 8-character boundary (non-overlapping halves), and a key shorter than 8 characters (→ `****`).
- [X] T003 Add `CredentialHint` property to `src/AskLucy.Domain/Ai/AIProvider.cs`; change `SetCredential(string ciphertext, string actor)` to `SetCredential(string ciphertext, string? hint, string actor)` storing both together; update `ClearCredential` to also null `CredentialHint`.
- [X] T004 Update the 4 existing `provider.SetCredential("ciphertext", "admin-1")` call sites in `tests/AskLucy.Domain.Tests/Ai/AIProviderTests.cs` to pass a hint argument, and add an assertion that `ClearCredential` nulls `CredentialHint` alongside `CredentialCiphertext`.
- [X] T005 [P] In `src/AskLucy.Persistence/Configurations/AIProviderConfiguration.cs`, add `builder.Property(p => p.CredentialHint).HasMaxLength(20);` with a comment noting it — unlike `CredentialCiphertext` — is safe to project into read DTOs.
- [X] T006 Add an EF Core migration `AddAiProviderCredentialHint` (nullable `CredentialHint nvarchar(20)` column on `AIProviders`) via `dotnet ef migrations add` in `src/AskLucy.Persistence`; verify the generated `Down` drops the column.
- [X] T007 [P] Add `CredentialHint` to `AdminAiProviderDto` in `src/AskLucy.Application/Ai/AdminAiProviderDto.cs` (record parameter + `FromEntity` mapping), updating its doc comment to clarify the hint is deliberately returned while the raw credential value never is.
- [X] T008 Update `src/AskLucy.Application/Ai/Commands/SetAiProviderCredential/SetAiProviderCredentialCommandHandler.cs` to compute `CredentialHintFormatter.Format(request.ApiKey)` before encrypting, and pass it into `provider.SetCredential(ciphertext, hint, actorUserId)`.
- [X] T009 [P] Update the `provider.SetCredential(...)` call site in `src/AskLucy.Web/DevSeed/DevAiProviderSeeder.cs` to also compute and pass a hint via `CredentialHintFormatter`.
- [X] T010 Create an idempotent startup backfill step (e.g. `src/AskLucy.Web/StartupTasks/CredentialHintBackfillService.cs`) that queries `AIProvider` rows where `CredentialCiphertext IS NOT NULL AND CredentialHint IS NULL`, decrypts each via `IAiCredentialProtector.Unprotect` inside a per-row try/catch (log and skip on `CryptographicException`, never throw/block startup), computes and saves the hint via `CredentialHintFormatter`; register it to run once at startup in `src/AskLucy.Web/Program.cs`.
- [X] T011 [P] Add `credentialHint: string | null` to the `AdminAiProvider` interface in `src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiProvidersApi.ts`.
- [X] T012 Update the `providers` fixture array in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.a11y.test.tsx` to include a `credentialHint` value (or `null`) on every fixture provider, keeping the file compiling against the now-required field.

**Checkpoint**: `credentialHint` exists end-to-end (DB column → DTO → API response → frontend type) and is correctly computed on set/replace/clear, with all pre-existing tests still passing. Neither user story's UI is wired up yet.

---

## Phase 3: User Story 1 - Confirm at a glance whether a provider has a key set (Priority: P1) 🎯 MVP

**Goal**: The AI Providers table's Provider column always shows, next to each provider's name, either its credential hint or a clear "no key set" indicator — never a blank cell.

**Independent Test**: Load the table with a mix of providers (some with a credential, some without) and confirm each row's Provider cell clearly distinguishes "has a key" from "has no key," next to the provider name.

### Implementation for User Story 1

- [X] T013 [US1] In `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx`, add a dedicated "Credential hint" column as the table's rightmost column, rendering `provider.credentialHint` when present and a "Not set" indicator when `credentialHint` is `null` (corrected 2026-09-22: the original merged-into-Provider-cell rendering did not match the user's explicit "separate column" requirement; moved to its own `TableCell`, positioned after Actions).

### Tests for User Story 1

- [X] T014 [US1] Create `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.test.tsx` with cases: a provider fixture with `credentialHint: 'sk-p...33IA'` renders that hint next to its display name; a provider fixture with `credentialHint: null` renders "Not set" instead.

**Checkpoint**: T014's tests pass. User Story 1 is independently functional and testable — an administrator can tell, per row, whether a key is configured.

---

## Phase 4: User Story 2 - Recognize which key is configured (Priority: P1)

**Goal**: The displayed hint is a correct, vendor-style fingerprint (`first4...last4`) of the actual configured key, and stays in sync as credentials are replaced or cleared.

**Independent Test**: Configure a known key, confirm the displayed hint matches the vendor-style pattern exactly; replace it and confirm the hint updates; clear it and confirm the hint disappears.

### Tests for User Story 2

- [X] T015 [US2] Create `tests/AskLucy.Application.Tests/Ai/SetAiProviderCredentialCommandHandlerTests.cs` (flat `Ai/` folder, matching this project's existing test layout rather than tasks.md's literal `Ai/Commands/` path) asserting: the persisted `CredentialHint` matches `CredentialHintFormatter.Format(apiKey)` for a representative key after `Handle`; and calling `Handle` again with a different key overwrites the previous hint (FR-005).
- [X] T016 [US2] In `AdminAiProvidersPage.test.tsx`, add a case asserting the displayed hint text is rendered verbatim as returned by the API (exact `first4...last4` string), confirming the frontend performs no reformatting of the backend-computed hint.

**Checkpoint**: All Foundational + US1 + US2 tests pass. An administrator can both confirm presence and recognize which specific key is active, matching vendor console conventions (SC-002).

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Final validation across both stories

- [X] T017 Run the full backend test suite (`dotnet test`) to confirm the `SetCredential`/`ClearCredential` signature change and new `AdminAiProviderDto` field introduce no regressions across Domain/Application/Persistence/Web test projects. Result: Domain 307, Application 1489, Infrastructure 400, Web.Tests 499 — 2695 passed, 0 failed.
- [X] T018 Run the full frontend test suite (`ClientApp`) to confirm no regressions, including the updated `AdminAiProvidersPage.a11y.test.tsx` fixtures and the new `AdminAiProvidersPage.test.tsx`. Result: 1606/1608 passed; the 2 failures (`ChatPage.a11y.test.tsx`, `ChatPage.test.tsx`) are a known pre-existing full-suite-only flake unrelated to this feature — both pass in isolation.
- [ ] T019 Manually verify `quickstart.md` Scenario 5 (backfill of a pre-existing credential) against a dev DB with a credential row predating this feature, and Scenario 6 (DevTools network inspection confirming no full key ever appears in the response) — neither is exercisable by unit/component tests alone. **Not run** — requires an interactive browser session against a running dev backend; left for the user to perform per quickstart.md.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No tasks.
- **Foundational (Phase 2)**: No dependencies — start immediately. **Blocks all of Phase 3 and Phase 4** (both stories need `credentialHint` to exist end-to-end first).
- **User Story 1 (Phase 3)**: Depends only on Phase 2. Independently shippable as the MVP once complete.
- **User Story 2 (Phase 4)**: Depends on Phase 2; T016 also depends on T013 (the same rendering code it's asserting against). Can be developed in parallel with Phase 3's T014 test file once T013 lands, but T015 (backend) has no frontend dependency and can start as soon as Phase 2 is done.
- **Polish (Phase 6)**: Depends on Phases 3 and 4 both being complete.

### Within Each Phase

- Phase 2: T001 before T002 (implementation before its test can meaningfully run — or write T002 first per TDD preference); T003 before T004; T005/T006 sequential (migration must reflect the configuration change); T007 before T008; T010 depends on T001 and T003 (needs the formatter and the entity's new property) existing first.
- Phase 3: T013 before T014.
- Phase 4: T015 has no ordering dependency on Phase 3; T016 depends on T013.

### Parallel Opportunities

- T001, T002, T005, T007, T009, T011 touch distinct files with no interdependency and can proceed in parallel once their prerequisites (if any) are met.
- T015 (backend, Application test project) can proceed in parallel with T013/T014 (frontend) — different stacks, different files.

---

## Implementation Strategy

### MVP First (Foundational + User Story 1 Only)

1. Complete Phase 2 (Foundational): the hint exists end-to-end but nothing renders it yet.
2. Complete Phase 3 (US1): the table shows hint-or-"Not set" next to each provider's name.
3. **STOP and VALIDATE**: run T014's tests, and manually confirm against quickstart.md Scenarios 1–3.
4. Ship — this alone satisfies the user's stated primary need ("make sure I inserted a key before or not").

### Incremental Delivery

1. Foundational → ship nothing visible yet, but unblocks both stories.
2. US1 (presence indicator next to name) → ship — MVP.
3. US2 (format correctness, update-on-replace, remove-on-clear guarantees) → ship — closes the "hint of what this key was" half of the original request.

### Notes

- Every task in Phase 2 that touches `SetCredential`'s signature (T003, T004, T008, T009) must land together in the same commit/PR — the signature change is not independently compilable otherwise.
- Commit after each checkpoint (T012, T014, T016, T019), or as one combined commit if shipping the whole feature together, consistent with this repo's solo-developer, direct-to-main workflow.
