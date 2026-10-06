# Quickstart: validating voids and drawing shapes

## Prerequisites

- The `AddSiteBoundaryVoids` migration is applied: production applies it at startup, and the test2 database (`PERSISTENCE_TESTS_2_CONNECTION_STRING`) must be migrated by hand before CI.
- A chat with a resolved site (for example Al Safa Park 2), opened in the outline editor.

## Automated

1. Backend: `dotnet test` for Domain, Application, Infrastructure, Persistence (test2) and Web (`PERSISTENCE_TESTS_CONNECTION_STRING` set).
2. Frontend: `npx tsc -b --noEmit`, `npx eslint .`, and the full `npx vitest run` (also with `VITE_API_BASE_URL=/api/v1`).

## Manual scenarios

| # | Steps | Expected |
|---|---|---|
| 1 | Cut circle, drawn wholly inside the site | A hole appears; the area drops by about the circle's area (US1, SC-002) |
| 2 | Cut rectangle, inside the site | A rectangular hole appears; one Undo removes it (US2) |
| 3 | Square tool, drag | The preview stays square; applying it works for Add and for Cut |
| 4 | Free polygon: click 5 corners, then double-click | Applied. Backspace removes the last corner first; Escape cancels; a crossing polygon is refused with a message |
| 5 | Drag a void corner, then try to drag it across the outer edge | It moves; the edge crossing is refused and snaps back (US3) |
| 6 | Shift+] to a void, then Remove void from a corner's right-click menu | The hole is filled; Undo brings it back |
| 7 | Cut a rectangle across the site | It splits into two parts; [ and ] move between them; each void stays with its piece (US4) |
| 8 | Done, then reload the page and open another chat about the same site | The voids are still there (SC-003) |
| 9 | Ask Lucy "what is the site area?" | It matches the editor's area, and Lucy mentions the voids when asked (US5) |
| 10 | Reset | The voids are gone along with the hand edits |
| 11 | A site saved before this feature | Opens and saves unchanged (SC-006) |
| 12 | Keyboard: the shape dialog with Rectangle 20 × 10 m, Cut; then the polygon tool with Space, arrows and Enter | Both work without a mouse (SC-005) |
| 13 | Edit ribbon: open the Circle group and choose Cut circle; then choose Corners, then Delete corner | The Circle button shows Cut circle and is highlighted, then loses its highlight but keeps the icon; 11 buttons in all (US6, SC-008) |
| 14 | Reopen the editor | Each group shows the tool last chosen from it |
| 15 | Keyboard on a group button: Down arrow, arrows, Enter, Escape | The menu opens, choosing works, and focus returns to the button |
