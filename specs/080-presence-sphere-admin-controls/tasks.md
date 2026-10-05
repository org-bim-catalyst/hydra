# Tasks: Presence Sphere Admin Controls

**Input**: Design documents in `/specs/080-presence-sphere-admin-controls/` (plan.md, spec.md, research.md, data-model.md, contracts/presence-sphere-api.md, quickstart.md)

**Tests**: Included. The constitution (section 10) requires unit and integration tests, and the plan lists them.

**Organization**: By user story. US1 is the MVP; US2 and US3 each build on the US1 page and scene but can be tested alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- Paths are relative to the repository root. `ClientApp/` means `src/AskLucy.Web/ClientApp/src/`.

---

## Phase 1: Setup

**Purpose**: Confirm the open point from research D6 before building on it.

- [X] T001 Find how existing background queries report a failure that is not shown to the user (search `ClientApp/` for the project's client error-reporting or logging helper and for any server endpoint that receives client errors). Record the answer under research D6 in `specs/080-presence-sphere-admin-controls/research.md`. If none exists, the fallback in T020 is a `console.error` plus the structured server log of the failed GET, and that is noted there.

---

## Phase 2: Foundational (blocks every story)

**Purpose**: The stored settings, the permissions, and the read and write API. No story works without these.

- [X] T002 [P] Add `Appearance` to the enum in `src/AskLucy.Domain/Authorization/AdminArea.cs`.
- [X] T003 Add `AppearanceView = "admin.appearance.view"` and `AppearanceManage = "admin.appearance.manage"` constants and the two catalogue entries (area Appearance, levels View and Manage, display names "View appearance" / "Manage appearance") in `src/AskLucy.Domain/Authorization/AdminPermissionCatalog.cs`; raise the list capacity from 23 to 25. Depends on T002.
- [X] T004 [P] Update the permission counts from 23 to 25 in `tests/AskLucy.Domain.Tests/Authorization/AdminPermissionCatalogTests.cs` (two places) and `tests/AskLucy.Domain.Tests/Authorization/PermissionSetTests.cs`; add a test that both new keys exist with the right levels.
- [X] T005 [P] Mirror the two permissions in `ClientApp/features/admin/adminPermissions.ts` (catalogue entries and the `ADMIN_PERMISSIONS` constants).
- [X] T006 [P] Create the entity `src/AskLucy.Domain/Appearance/PresenceSphereSettings.cs` per data-model.md: extends `BaseEntity`, fixed `SingletonId`, range and default constants (dot 0.25 to 2.00 default 1.00; fill 40 to 95 default 75; zoom default false), `CreateDefault`, and `Update(dotSize, fillPercent, zoomEnabled, actor, utcNow)` that throws a domain error outside the ranges.
- [X] T007 [P] Domain tests for the entity in `tests/AskLucy.Domain.Tests/Appearance/PresenceSphereSettingsTests.cs`: defaults, update at each boundary, rejection just outside each boundary, `ModifiedBy`/`ModifiedAtUtc` set. Depends on T006.
- [X] T008 Define `IPresenceSphereSettingsRepository` (get the row or null, upsert) in `src/AskLucy.Application/Abstractions/IPresenceSphereSettingsRepository.cs`. Depends on T006.
- [X] T009 [P] Create `PresenceSphereSettingsDto` (dotSizeMultiplier, cardFillPercent, zoomEnabled, modifiedBy, modifiedAtUtc, isDefault) in `src/AskLucy.Application/Appearance/PresenceSphereSettingsDto.cs`.
- [X] T010 Query: `GetPresenceSphereSettingsQuery` and handler in `src/AskLucy.Application/Appearance/Queries/GetPresenceSphereSettings/`, returning stored values or the defaults with `isDefault = true`; never creates the row. Depends on T008, T009.
- [X] T011 [P] Application test for the query (no row gives defaults and `isDefault`; a row gives its values) in `tests/AskLucy.Application.Tests/Appearance/GetPresenceSphereSettingsQueryHandlerTests.cs`. Depends on T010.
- [X] T012 EF mapping `src/AskLucy.Persistence/Configurations/PresenceSphereSettingsConfiguration.cs` (table `PresenceSphereSettings`, key without generation, `decimal(4,2)`, row version, `CreatedBy`/`ModifiedBy` lengths like `DictationEngineSettingConfiguration`), the `DbSet` in `src/AskLucy.Persistence/AskLucyDbContext.cs`, and the repository `src/AskLucy.Persistence/Repositories/PresenceSphereSettingsRepository.cs` registered in `src/AskLucy.Persistence/DependencyInjection.cs`. The upsert must be safe against two first saves at once (follow the lock-and-insert pattern in `DictationEngineSettingRepository`). Depends on T006, T008.
- [X] T013 Generate the migration `AddPresenceSphereSettings` under `src/AskLucy.Persistence/Migrations/` (create table, no seed row). Keep the file style of recent migrations (BOM, `System` usings first, per the repo's format gotchas). Depends on T012.
- [X] T014 Apply the migration by hand to the shared persistence test database(s) used by CI (`PERSISTENCE_TESTS_CONNECTION_STRING` and `PERSISTENCE_TESTS_2_CONNECTION_STRING`; connection strings are in `appsettings.Development.json`). Without this CI fails. Depends on T013.
- [X] T015 Controller `src/AskLucy.Web/Controllers/v1/AppearanceController.cs` with `GET api/v1/appearance/presence-sphere` (`[Authorize]`) per contracts/presence-sphere-api.md. The `PUT` is added in T024. Depends on T010.
- [X] T016 [P] Web integration test for the GET in `tests/AskLucy.Web.Tests/Appearance/AppearanceEndpointsTests.cs`: 401 anonymous, 200 defaults for a plain user. Use the shared class fixture pattern (no `WithWebHostBuilder` per test). Depends on T015.

**Checkpoint**: the read API works for every signed-in user and returns today's look.

---

## Phase 3: User Story 1 - Tune the sphere's look from the Admin panel (P1) MVP

**Goal**: An administrator changes dot size and fill on the Appearance page with a live preview, saves, and every user's sphere uses the result.

**Independent test**: quickstart scenarios 1, 2, 3 and 6.

### Tests for US1

- [X] T017 [P] [US1] Validator and handler tests (`UpdatePresenceSphereSettingsCommand`: valid saves and logs old and new values; out-of-range is rejected with a message naming the range; actor recorded) in `tests/AskLucy.Application.Tests/Appearance/UpdatePresenceSphereSettingsCommandHandlerTests.cs`.
- [X] T018 [P] [US1] Extend `tests/AskLucy.Web.Tests/Appearance/AppearanceEndpointsTests.cs` with the PUT matrix: 401 anonymous, 403 plain user, 403 view-only, 200 manage and then GET shows the saved values, 400 for each out-of-range field and nothing saved.
- [X] T019 [P] [US1] Component tests in `ClientApp/features/chat/components/AiPresenceCard.test.tsx` and `ClientApp/features/chat/scene/` (new `ReactiveSphere` or `SceneBackground` prop tests where feasible in jsdom): the card passes the fetched settings down; on query error it passes the defaults and reports the error; fill changes the camera distance helper's input.

### Implementation for US1

- [X] T020 [US1] Hook `ClientApp/features/chat/hooks/usePresenceSphereSettings.ts`: TanStack Query on the GET, defaults while loading and on error, error reported as decided in T001, `staleTime` long (settings change rarely). API call in `ClientApp/features/admin/api/adminAppearanceApi.ts` (GET and PUT, typed to the contract). Depends on T001, T015.
- [X] T021 [US1] Put the defaults and clamp ranges in `ClientApp/features/chat/scene/sphereConstants.ts` (reuse the existing `SPHERE_CARD_FILL` as the default fill) so the scene and the admin page share one source that mirrors the backend constants.
- [X] T022 [US1] Make the scene take settings as props: in `ClientApp/features/chat/scene/SceneBackground.tsx` and `ReactiveSphere.tsx` replace the module-level fill and `PARTICLE_SIZE_SCALE` with props (`dotSizeMultiplier`, `cardFillPercent`), update the live camera position when the fill changes (not only the initial `camera` prop), and keep the defaults identical to today. Depends on T021.
- [X] T023 [US1] `ClientApp/features/chat/components/AiPresenceCard.tsx` reads the hook and passes the values to the scene; the card must render immediately with defaults (no wait on the request). Depends on T020, T022.
- [X] T024 [US1] Command `UpdatePresenceSphereSettingsCommand`, `FluentValidation` validator using the entity's range constants, and handler in `src/AskLucy.Application/Appearance/Commands/UpdatePresenceSphereSettings/`; the handler upserts through the repository and writes the structured log event (who, old and new values). Add `PUT api/v1/appearance/presence-sphere` with `[RequirePermission("admin.appearance.manage")]` to `AppearanceController.cs`. Depends on T012, T015.
- [X] T025 [US1] Admin page `ClientApp/features/admin/pages/AdminAppearancePage.tsx` inside `AdminShell`: sliders for dot size and fill with their values shown, form state in React Hook Form with a Zod schema using the shared ranges, Save with a visible success and error state (values kept on failure), "last changed by/at" line, and read-only controls when the user has view but not manage. Depends on T020, T021, T024.
- [X] T026 [US1] Live preview component used by the page: a card the same size as the chat's card rendering the real scene with the unsaved form values (idle state, no analyser). Factor the card's size rule out of `AiPresenceCard.tsx` into a shared constant so both use it. Depends on T022, T025.
- [X] T027 [US1] Warn before leaving with unsaved changes (FR-016) on the page, using the router's blocker as other admin forms do. Depends on T025.
- [X] T028 [US1] Wire the route and nav: add the lazy `AdminAppearancePage` route in `ClientApp/routes/router.tsx` (same `ProtectedRoute` and `AdminRoute` wrapper as `/admin/voice`) and the "Appearance" entry gated on `admin.appearance.view` in `ClientApp/features/admin/adminNav.tsx`. Depends on T025.
- [X] T029 [P] [US1] Page tests `ClientApp/features/admin/pages/AdminAppearancePage.test.tsx` and `AdminAppearancePage.a11y.test.tsx`: loads values, sliders update the preview props, save success and failure (values kept), read-only for view-only users, leave warning. Mock the scene (no WebGL in jsdom). Depends on T025 to T027.

**Checkpoint**: an administrator can tune dot size and fill with a preview, save, and users see it. Dot size 1.0 and fill 75 equal today's look.

---

## Phase 4: User Story 2 - Allow or prevent zooming the sphere (P2)

**Goal**: A zoom switch on the page controls whether users can zoom the sphere, within 1/4x to 2x of normal.

**Independent test**: quickstart scenario 4.

- [X] T030 [P] [US2] Tests: scene passes `enableZoom` from the setting, and `minDistance`/`maxDistance` derived from the base camera distance so zoom stays within 1/4x to 2x; zoom off leaves wheel events alone. In the scene test files from T019.
- [X] T031 [US2] Add `zoomEnabled` to the scene: `OrbitControls` in `ClientApp/features/chat/scene/SceneBackground.tsx` gets `enableZoom={zoomEnabled}` with limits computed from `sphereCameraDistance(fill)`; the limits follow a fill change. Rotation is unchanged. Depends on T022.
- [X] T032 [US2] Pass `zoomEnabled` through `AiPresenceCard.tsx` and the preview, and add the switch to `AdminAppearancePage.tsx` (including the preview responding before saving). Zoom is not persisted per user: nothing is written on zoom. Depends on T031, T025, T026.
- [X] T033 [P] [US2] Extend the page tests from T029 for the switch (preview prop changes; saved value round-trips).

**Checkpoint**: zoom on and off work for all users after a save.

---

## Phase 5: User Story 3 - Return to the default look (P3)

**Goal**: One action restores the defaults, and another discards unsaved edits.

**Independent test**: quickstart scenario 5.

- [X] T034 [US3] Add "Reset to defaults" (sets the form to the shared defaults without saving) and "Discard changes" (sets it to the last saved values; disabled when nothing changed) to `ClientApp/features/admin/pages/AdminAppearancePage.tsx`. Depends on T025.
- [X] T035 [P] [US3] Page tests for both buttons, including that neither saves and that the leave warning clears after a discard, in `AdminAppearancePage.test.tsx`.

**Checkpoint**: all three stories work together.

---

## Phase 6: Polish and cross-cutting

- [X] T036 [P] Update documentation: add the Appearance area, endpoints and permissions to the API and architecture docs, and the new table to the database docs (find the existing files under `docs/`), and add migration notes (apply by hand to test databases).
- [ ] T037 [P] Mark the `specs/080-presence-sphere-admin-controls/spec.md` status line Implemented once verified, and add the memory note for the shared-test-DB migration step if it was needed again.
- [ ] T038 Run the whole verification: `dotnet build` and `dotnet format --verify-no-changes`, all backend test projects with `PERSISTENCE_TESTS_CONNECTION_STRING` set, then in `ClientApp` `npx tsc -b --noEmit`, `npx eslint .` and the full `npx vitest run` (also with `VITE_API_BASE_URL=/api/v1` as CI does).
- [ ] T039 Walk the quickstart manual scenarios 1 to 8 on production after deploy, including the visual check that the default sphere is unchanged (screenshots before and after for the same viewport), and confirm the deploy by fetching the live bundle and searching for the new strings.

---

## Dependencies and execution order

- Phase 1 (T001) first, but it only blocks T020.
- Phase 2 blocks all stories. Inside it: T002 to T003; T006 to T008, T010, T012 to T013 to T014; T015 needs T010.
- US1 is the MVP. US2 builds on the scene props and the page from US1 (T022, T025, T026). US3 builds on the page (T025).
- Each story's tests can be written before its implementation.

```text
T002 -> T003
T006 -> T007, T008 -> T010 -> T011, T015 -> T016
T006,T008 -> T012 -> T013 -> T014
US1: T021 -> T022 -> T023
     T001,T015 -> T020 -> T025 -> T026,T027,T028 -> T029
     T012,T015 -> T024 -> T025
US2: T022 -> T031 -> T032 ; US3: T025 -> T034
```

## Parallel examples

- Foundational: T002, T005, T006, T009 can start together; then T004, T007, T008.
- US1: T017, T018, T019 (tests) in parallel; T021 can run alongside T024.
- After US1: T031 (scene zoom) and T034 (reset and discard) touch different parts and can proceed in parallel, but both edit the page, so merge T032 and T034 one after the other.

## Implementation strategy

1. **MVP**: Phase 1, Phase 2, then US1. Ship; defaults mean users see no change until an administrator saves.
2. **Increment**: US2 (zoom), then US3 (reset and discard).
3. Push to main after each phase once the local checks pass, and verify the deploy by checking the live bundle (a CI failure on a flaky test skips the deploy).

---

## Implementation notes (2026-10-05)

- **T001**: the app has no client error-reporting helper; background failures are logged with `console.error` (as in `SceneBackground`, `useTextToSpeech`). `usePresenceSphereSettings` does the same, and the failed request is also visible in the server log. No new helper was added.
- **T020**: the API module is `ClientApp/features/chat/api/presenceSphereApi.ts` (not under `features/admin/api`), because the chat reads it too; the admin page imports it.
- **T025**: the page keeps its three values in plain state rather than React Hook Form and Zod: the sliders and switch cannot produce an out-of-range value, and the server validates anyway.
- **T018**: the endpoint tests do not count calls on the unit of work, because the host also calls it on every authenticated request; the saving itself is asserted in the Application tests and through the repository.
- **T014**: applied to the dedicated persistence database (test2). The shared test database and production are not touched by this task.
