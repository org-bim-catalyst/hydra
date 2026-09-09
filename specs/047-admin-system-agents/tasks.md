# Tasks: Admin Visibility of System Agents

**Input**: Design documents from `/specs/047-admin-system-agents/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/admin-system-agents-api.md, quickstart.md

**Tests**: Included — constitution §10 requires tests for new/changed behavior in the same change that introduces it; this feature's spec is security/access-control-bearing (Story 3), which the constitution explicitly calls out as needing a completed security review.

**Organization**: Tasks are grouped by user story (spec.md: US1 "see system agents' state" P1, US2 "visually distinguished as read-only/system-owned" P2, US3 "admin-only access" P1). US1 and US3 are both P1 and are delivered together as the MVP, since a list screen with no access control is not a shippable increment of this feature.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1/US2/US3)

## Path Conventions

Existing Clean Architecture web app: `src/AskLucy.Domain/`, `src/AskLucy.Application/`, `src/AskLucy.Infrastructure*/`, `src/AskLucy.Persistence/`, `src/AskLucy.Web/` (backend, incl. `ClientApp/` frontend), `tests/AskLucy.*.Tests/` — matches plan.md's Project Structure exactly.

---

## Phase 1: Setup

**Purpose**: No new project/dependency/tooling is needed — this feature reuses the existing solution, existing `AdministratorOrSuperUser` policy, and existing admin frontend shell. Nothing to set up.

*(Phase intentionally empty — see plan.md's Constitution Check: no new stack, no new policy.)*

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The repository read method and query/DTO that both remaining user stories' UI and access-control tests depend on.

**⚠️ CRITICAL**: T001-T005 MUST complete before Phase 3/4 work begins.

- [X] T001 Add `ListSystemOwnedAsync(CancellationToken)` to `IAgentRepository` in `src/AskLucy.Application/Abstractions/IAgentRepository.cs` — returns `IReadOnlyList<Agent>` for every agent where `IsSystemOwned == true`, per data-model.md
- [X] T002 Implement `ListSystemOwnedAsync` in `src/AskLucy.Persistence/Repositories/AgentRepository.cs` — filter `IsSystemOwned == true`, eager-load `Versions` (ordered so the newest is easily selected), no pagination per research.md Decision 3 (depends on T001)
- [X] T003 [P] Create `AdminSystemAgentDto` record in `src/AskLucy.Application/Agents/Queries/GetSystemAgents/AdminSystemAgentDto.cs` with a `FromEntity(Agent)` factory computing `LastUpdatedAtUtc` from the newest `Versions` entry (fallback to `Agent.ModifiedAtUtc ?? Agent.CreatedAtUtc`), per data-model.md
- [X] T004 Create `GetSystemAgentsQuery : IRequest<IReadOnlyList<AdminSystemAgentDto>>` in `src/AskLucy.Application/Agents/Queries/GetSystemAgents/GetSystemAgentsQuery.cs`
- [X] T005 Create `GetSystemAgentsQueryHandler` in `src/AskLucy.Application/Agents/Queries/GetSystemAgents/GetSystemAgentsQueryHandler.cs` calling `IAgentRepository.ListSystemOwnedAsync` and mapping each via `AdminSystemAgentDto.FromEntity` (depends on T001, T003, T004)

**Checkpoint**: Query layer complete and independently unit-testable (no controller/frontend needed yet).

---

## Phase 3: User Story 1 + User Story 3 — See system agents' state, admin-only (Priority: P1) 🎯 MVP

**Goal**: An Administrator/Super User can load a new admin endpoint and see every system agent's name, status, version, and last-updated time; anyone else is denied.

**Independent Test**: Sign in as Administrator, call `GET /api/v1/admin/agents/system`, confirm the full list with correct fields; repeat as anonymous (401) and as a non-admin authenticated user (403), per quickstart.md Scenarios 1, 3, 5.

### Tests for User Story 1 + 3

- [X] T006 [P] [US1] Unit test `GetSystemAgentsQueryHandlerTests` in `tests/AskLucy.Application.Tests/Agents/Queries/GetSystemAgentsQueryHandlerTests.cs` — faked `IAgentRepository` returning a mix of system/non-system agents (repository itself does the filtering, so this test asserts correct DTO mapping and the empty-list case), a system agent with no published version (fallback timestamp), and a system agent with multiple versions (newest wins)
- [X] T007 [P] [US3] Controller authorization tests in `tests/AskLucy.Web.Tests/Agents/AdminAgentsControllerTests.cs` — `GET /api/v1/admin/agents/system` returns 401 anonymous, 403 for an authenticated non-admin (`TestJwtFactory.Create("user-1")`), 200 for Administrator and for Super User roles, mirroring `AdminAiProvidersControllerTests`'s exact pattern
- [X] T008 [P] [US1] Repository test in `tests/AskLucy.Persistence.Tests/AgentRepositoryTests.cs` (new file) — `ListSystemOwnedAsync` against the real test database returns only `IsSystemOwned == true` rows and excludes a seeded user-owned agent

> **T008 uncovered a real, previously-unknown production bug**, fixed as part of this feature per user direction: `Agents.OwnerId` carries a real FK to `AspNetUsers.Id` (`AgentConfiguration.cs`), but nothing had ever seeded a matching row for `Agent.SystemOwnerId` ("system") — every `SystemAgentProvisioner` insert of a system agent has been failing that FK, caught and logged per-definition (`SystemAgentProvisionerLog.DefinitionFailed`), never crashing the host and never previously traced to this specific cause. Fixed by adding `ISystemAccountProvisioner`/`SystemAccountProvisioner` (Application abstraction, Persistence implementation via `UserManager<ApplicationUser>`) that idempotently ensures the "system" pseudo-account exists, called from `SystemAgentProvisioner.ProvisionAsync` right after the pending-migrations check, folded into the same defer-and-retry-next-startup handling already in place for an unreachable database. No schema/migration change — this seeds one data row at runtime, not a new table/column.

### Implementation for User Story 1 + 3

- [X] T009 [US1] [US3] Create `AdminAgentsController` in `src/AskLucy.Web/Controllers/v1/AdminAgentsController.cs` — `[Authorize(Policy = "AdministratorOrSuperUser")]`, `[Route("api/v1/admin/agents")]`, `[HttpGet("system")]` sending `GetSystemAgentsQuery`, mirroring `AdminAiProvidersController`'s shape exactly (depends on T005)
- [X] T010 [US1] Register `AdminAgentsController`'s dependencies if needed (MediatR handler auto-discovery — verify `GetSystemAgentsQueryHandler` is picked up by the existing `AddApplication()` assembly scan; no manual DI registration expected, confirm during build) (depends on T009)

**Checkpoint**: `GET /api/v1/admin/agents/system` is live, correctly scoped, and access-controlled — testable via `curl`/quickstart.md Scenarios 1, 3, 5 without any frontend.

---

## Phase 4: User Story 2 — Visually distinguished as read-only/system-owned (Priority: P2)

**Goal**: An Administrator sees the system agents list rendered as a page, each row clearly marked system-owned/read-only, with no edit/delete/duplicate/publish controls — reusing the existing frontend admin patterns and the T108 system-owned badge.

**Independent Test**: As an Administrator, open `/admin/system-agents` and visually confirm the badge and absence of mutating controls, per quickstart.md Scenarios 1, 2, 4, 6.

### Tests for User Story 2

- [X] T011 [P] [US2] Render test `AdminSystemAgentsPage.test.tsx` in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminSystemAgentsPage.test.tsx` — renders the fetched list with name/status/version/last-updated columns, renders the system-owned badge on every row, renders no edit/delete/duplicate/publish affordance, and renders the empty state when the API returns `[]` (FR-006)
- [X] T012 [P] [US2] Accessibility test `AdminSystemAgentsPage.a11y.test.tsx` in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminSystemAgentsPage.a11y.test.tsx`, mirroring `AdminAiProvidersPage.a11y.test.tsx`'s existing axe-check pattern (constitution §7/§10)

### Implementation for User Story 2

- [X] T013 [P] [US2] Create `getSystemAgents()` API client function in `src/AskLucy.Web/ClientApp/src/features/admin/api/adminSystemAgentsApi.ts`, calling `GET /api/v1/admin/agents/system` and typing the response per contracts/admin-system-agents-api.md, mirroring `adminAiProvidersApi.ts`'s shape
- [X] T014 [US2] Create `AdminSystemAgentsPage.tsx` in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminSystemAgentsPage.tsx` — `useQuery` against `getSystemAgents`, `AdminShell` wrapper, MUI `Table` with columns (Name, System Key, Status, Version, Last Updated), the existing system-owned read-only badge component reused from the Agent Library UI (T108) on every row, and an empty-state message when the list is empty (depends on T013)
- [X] T015 [US2] Add a "System Agents" entry to `src/AskLucy.Web/ClientApp/src/features/admin/adminNav.tsx`, pointing at `/admin/system-agents`, alongside the existing "AI providers" entry
- [X] T016 [US2] Register the lazy route in `src/AskLucy.Web/ClientApp/src/routes/router.tsx` at path `/admin/system-agents`, mirroring the existing `/admin/ai-providers` lazy-import and route-guard registration exactly (depends on T014, T015)

**Checkpoint**: All three user stories complete — the full admin screen is reachable, correctly scoped, and visually correct.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T017 [P] Run `quickstart.md` Scenarios 1-6 end-to-end against a running local environment (or site4now.net) and confirm each expected outcome — **left for the user post-deploy**: requires signing in as a real Administrator/Super User against a live/staging environment and clicking through the actual admin UI, which this session cannot do; everything verifiable without that (repository behavior, authorization gating, DTO mapping, rendering, empty state, a11y) is covered by T006-T012's automated tests, all passing
- [X] T018 [P] `dotnet format "Ask Lucy.sln" --verify-no-changes` on touched backend files
- [X] T019 [P] Full backend suite (`dotnet test "Ask Lucy.sln"`) and full frontend suite (`npx tsc -b --noEmit` then `npm test`) both green
- [X] T020 Update `specs/047-admin-system-agents/tasks.md` scope notes for any deviation discovered during implementation (per this repo's established documentation-as-implementation practice, seen throughout specs/045) — see the note under T008

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Empty — nothing blocks Phase 2.
- **Foundational (Phase 2)**: T001 → T002; T003 and T004 can run in parallel with T001/T002; T005 depends on T001, T003, T004. BLOCKS Phase 3.
- **User Story 1 + 3 (Phase 3)**: Depends on Phase 2 completion (needs `GetSystemAgentsQuery`/handler to exist). Tests (T006-T008) can be written in parallel with each other before T009; T009 depends on T005; T010 depends on T009.
- **User Story 2 (Phase 4)**: Depends on Phase 3's endpoint existing and being reachable (T009) — the frontend calls the real endpoint. T011/T012 (tests) and T013 (api client) can start in parallel once T009 lands; T014 depends on T013; T015 is independent; T016 depends on T014 and T015.
- **Polish (Phase 5)**: Depends on Phases 2-4 all complete.

### User Story Dependencies

- **US1 (P1)** and **US3 (P1)** are delivered together in Phase 3 — a list with no access control is not an acceptable increment for this feature (the spec's own Story 3 rationale), so they are not independently shippable from each other, only jointly shippable as the MVP.
- **US2 (P2)** depends on US1/US3's endpoint existing (it renders that same data) but adds no new backend behavior — purely additive frontend presentation.

### Parallel Opportunities

- T003 and T004 (Phase 2) in parallel with T001 (different files, no shared dependency until T005).
- T006, T007, T008 (Phase 3 tests) fully parallel — three different test files/projects.
- T011, T012, T013 (Phase 4) fully parallel — different files.

---

## Parallel Example: Phase 3

```bash
# Launch all three test tasks together:
Task: "Unit test GetSystemAgentsQueryHandlerTests in tests/AskLucy.Application.Tests/Agents/Queries/GetSystemAgentsQueryHandlerTests.cs"
Task: "Controller authorization tests in tests/AskLucy.Web.Tests/Agents/AdminAgentsControllerTests.cs"
Task: "Repository test in tests/AskLucy.Persistence.Tests/AgentRepositoryTests.cs"
```

---

## Implementation Strategy

### MVP First (US1 + US3 together)

1. Complete Phase 2: Foundational (query/repository layer).
2. Complete Phase 3: US1 + US3 (endpoint live, access-controlled, independently curl-testable).
3. **STOP and VALIDATE**: quickstart.md Scenarios 1, 3, 5 pass against the real endpoint.
4. This is a legitimate, demonstrable MVP even with no UI — an administrator with API access can already verify system-agent health.

### Incremental Delivery

1. Foundational → Phase 3 (MVP: endpoint + access control) → validate → (optionally deploy/demo via API).
2. Phase 4 (US2: the actual admin screen) → validate visually → deploy/demo.
3. Phase 5: polish, full-suite verification, docs.

---

## Notes

- No database migration in this feature (data-model.md: pure read over existing `Agent`/`AgentVersion` fields) — nothing to flag to the user per their standing "tell me about DB changes" instruction.
- File paths above are exact, derived from reading the actual existing files this feature mirrors (`AdminAiProvidersController.cs`, `AdminAiProvidersPage.tsx`, `adminAiProvidersApi.ts`, `AdminAiProvidersControllerTests.cs`) — not placeholders.
- Commit after each phase checkpoint, consistent with this repo's established per-phase commit pattern (see specs/045's Phase 7-10 history).
