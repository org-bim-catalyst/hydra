# Quickstart: validating Presence Sphere Admin Controls

## Prerequisites
- Backend and frontend running locally, or the production site.
- The migration applied (locally by the app, and by hand to the shared persistence test database for tests).
- An administrator holding `admin.appearance.manage`, and a plain user.

## Automated
1. Backend: `dotnet test` for Domain.Tests (catalogue now 25 permissions), Application.Tests (query defaults, command validation) and Web.Tests (set `PERSISTENCE_TESTS_CONNECTION_STRING` from `appsettings.Development.json`).
2. Frontend: `npx tsc -b --noEmit`, `npx eslint`, then the **full** `npx vitest run` (page-level tests such as ChatPage have assertions independent of a component's own tests).

## Manual scenarios (mapped to the spec)
1. **Defaults (FR-018, SC-004)**: with no saved row, open the chat as a plain user. The sphere looks as it does today (75% fill, no zoom).
2. **Live preview (US1, SC-001)**: as admin, open Admin > Appearance and drag the dot-size and fill sliders. The preview follows immediately and nobody else sees the change.
3. **Save (US1, SC-003)**: save, then as another user reload the chat. The sphere matches the preview.
4. **Zoom (US2)**: switch zoom on and save. As a user, scroll over the sphere: it zooms but stops at 2x and 1/4x, and a reload returns it to normal. Switch zoom off: scrolling does nothing.
5. **Reset and discard (US3)**: change values; "Discard changes" returns to the saved values; "Reset to defaults" shows 1.0x / 75% / off without saving.
6. **Permissions (FR-017, SC-005)**: a user with only View sees read-only controls; a user with neither gets "not authorised"; a direct PUT without Manage returns 403; an out-of-range PUT returns 400 and changes nothing.
7. **Failure paths (FR-014, FR-015)**: block the GET and the sphere still appears with the defaults, with the failure logged; block the PUT and an error is shown while the form keeps its values.
8. **Leaving with unsaved changes (FR-016)**: a warning appears.
