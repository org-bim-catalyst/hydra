# Tasks: Studio HUD Top Row

**Input**: Design documents from `/specs/073-studio-hud-top-row/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution (§10) requires tests for new behaviour in the same change, and axe checks for changed UI.

**Organization**: Grouped by user story (US1 row → US2 compact card → US3 confidence tone), so each story can be implemented and verified on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story the task belongs to

## Path Conventions

All source paths are relative to `src/AskLucy.Web/ClientApp/src/` (abbreviated `ClientApp/src/`). Run commands from `src/AskLucy.Web/ClientApp`.

---

## Phase 1: Setup

**Purpose**: Confirm a green baseline, so later failures can be attributed to this change

- [X] T001 Record the baseline: run `npx tsc -b --noEmit` and `npx vitest run` from `src/AskLucy.Web/ClientApp`, and note any failures that already exist (e.g. the known `ChatPage.test.tsx` flakes under full-suite load) in `specs/073-studio-hud-top-row/tasks.md` under "Baseline notes" below

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared primitives and the framework extension point that every story builds on (research D2, D3, D6)

**⚠️ CRITICAL**: No user story work can start until this phase is complete

### `FloatingToolbar` inline placement (D2)

- [X] T002 [P] Add tests for `placement="inline"` (no `position: absolute`, no anchor offsets, no outer margin, no `maxWidth` calc; `direction`/`flexWrap`/`dataAttributes` unchanged) and for the default `'anchored'` staying identical to today, in `ClientApp/src/components/workspace-shell/FloatingToolbar.test.tsx`
- [X] T003 Add the `placement?: 'anchored' | 'inline'` prop (default `'anchored'`) to `ClientApp/src/components/workspace-shell/FloatingToolbar.tsx`. `'inline'` drops `position`, `m`, `maxWidth`, and the anchor offsets. Extend the doc comment to explain when `'inline'` applies (a parent owns the positioning)

### `HudCard` shared surface (D6)

- [X] T004 [P] Write tests for contract H1–H3 (40 px height, row layout with content vertically centred, `CIRCULAR_ACTION_CHROME` surface in both light and dark themes, no stripe or accent border, `pointerEvents` defaults to `'none'` and accepts `'auto'`, `role`/`aria-label` passed through, `maxWidth` honoured) in `ClientApp/src/components/workspace-shell/HudCard.test.tsx`
- [X] T005 Create `HudCard` per [contracts/studio-hud-row.md](contracts/studio-hud-row.md) § HudCard in `ClientApp/src/components/workspace-shell/HudCard.tsx`. Use only `CIRCULAR_ACTION_CHROME` tokens, `borderRadius: 2`, `backdropFilter: 'blur(12px)'`, and shadow `0 2px 10px rgba(0,0,0,0.28)`, with no mode branches

### `hudItem` extension contribution (D3)

- [X] T006 Add `| { kind: 'hudItem'; extensionId: string; component: ComponentType }` to the `Contribution` union in `ClientApp/src/viewer/extensions/ViewerExtension.ts`, and extend the union's doc comment
- [X] T007 Add `contributeHudItem(component)` to the `ExtensionContext` interface and to `createExtensionContext` in `ClientApp/src/viewer/extensions/context.ts`. Record it through `addContribution`, the same way `contributeOverlay` does (contract X1), and give it a doc comment matching [contracts/extension-context-hud-item.md](contracts/extension-context-hud-item.md)
- [X] T008 [P] Add tests: `contributeHudItem` records a `hudItem` contribution, and `removeContributionsFor` withdraws it (X1, X3), in `ClientApp/src/viewer/extensions/context.test.ts`
- [X] T009 [P] Write tests for `ExtensionHudItemHost`: it renders `hudItem` contributions in order with no wrapper element (X5, X6); it ignores `overlay` and unknown kinds (X4); a contribution made before mount still renders (X2); the item disappears when the extension stops (X3); a throwing item is contained (X7). File: `ClientApp/src/viewer/extensions/components/ExtensionHudItemHost.test.tsx`
- [X] T010 [P] Write tests for `ContributionErrorBoundary` (contract X7, research D3a): a child that throws while rendering calls `setLifecycle(extensionId, 'failed', 'Render failed: …')`, renders `null`, and leaves a sibling boundary's child and the parent tree mounted; no DOM wrapper is added. File: `ClientApp/src/viewer/extensions/components/ContributionErrorBoundary.test.tsx`
- [X] T011 Create `ContributionErrorBoundary` (a class component following `SceneErrorBoundary` in `ClientApp/src/features/chat/scene/SceneBackground.tsx`; props `extensionId` + `children`; `componentDidCatch` records `setLifecycle(extensionId, 'failed', 'Render failed: ' + message)` via `useViewerExtensionStore.getState()`; renders `null` once failed) in `ClientApp/src/viewer/extensions/components/ContributionErrorBoundary.tsx`
- [X] T012 Create `ExtensionHudItemHost` in `ClientApp/src/viewer/extensions/components/ExtensionHudItemHost.tsx`, mirroring `ExtensionOverlayHost` (subscribes to the store, filters `kind === 'hudItem'`, `extensionId:ordinal` keys) but returning a fragment rather than a positioned `Box` (X6), with each item wrapped in `ContributionErrorBoundary` (X7)
- [X] T013 Wrap each overlay in `ContributionErrorBoundary` in `ClientApp/src/viewer/extensions/components/ExtensionOverlayHost.tsx` (X7; the same one-line change closes the gap that already exists there), and add a test "a throwing overlay marks its extension failed without unmounting the viewer" to `ClientApp/src/viewer/extensions/extensibility.test.tsx`
- [X] T014 Confirm that `ExtensionOverlayHost` ignores `hudItem` (X4). Add an assertion to the "hosts ignore a kind they don't recognise" test in `ClientApp/src/viewer/extensions/extensibility.test.tsx`, and render `ExtensionHudItemHost` in its `renderExtensionHosts()` helper so both hosts are exercised

**Checkpoint**: The primitives exist and are tested. No visible change in the app yet.

---

## Phase 3: User Story 1 — Single-row workspace header (Priority: P1) 🎯 MVP

**Goal**: Home ● → [Flumeria Studio] → [weather] → [site card] on one left-anchored row, 40 px, sharing a centreline with the top-right cluster, wrapping without overlap (FR-001–FR-005, FR-013)

**Independent Test**: Open `/studio` with a resolved location and an active boundary. The four items sit on one row in order, and resizing to 390 px wraps them without overlapping the top-right buttons (quickstart §2 steps 1, 2, 4)

### Tests for User Story 1

- [X] T015 [P] [US1] Add `WorkspaceOverlay` tests for W1–W6 in `ClientApp/src/components/workspace-shell/WorkspaceOverlay.test.tsx`: `topStart` renders inside a start group carrying `RESERVED_ATTRIBUTE` that comes before the top cluster in one top bar; the cluster renders inline with `RESERVED_ATTRIBUTE`; omitting `topStart` leaves the existing assertions passing unchanged; `right-stack` and `bottom-end` are unaffected
- [X] T016 [P] [US1] Update `ClientApp/src/features/chat/components/HomeProjectCard.test.tsx`: it renders the Home button and the "Flumeria Studio" title as sibling elements (a fragment), with no self-positioning; Home still navigates to `/` with `VIEW_LANDING_STATE`
- [X] T017 [P] [US1] Update `ClientApp/src/features/viewer/components/LocationWeatherWidget.test.tsx` for the 40 px layout (research D5, contract "LocationWeatherWidget states"): line 1 is the location name and line 2 is `NN°C`; the stale state shows an inline `last known` marker, **not** a separate block line, and the aria-label still ends in "(last known reading)"; the unavailable state is a single line; no `position`/`top`/`left` on the root; `RESERVED_ATTRIBUTE` is no longer on the widget itself
- [X] T018 [P] [US1] Add a test to `ClientApp/src/viewer/extensions/components/useAvoidReservedCorner.test.ts`: with one reserved element shaped like the new row (left 16, top 16, 40 px tall, 600 px wide) and `side = 'left'`, the hook returns `16 + 40 + MARGIN` (research D9)
- [X] T019 [P] [US1] Add a `ChatPage` row-order test in `ClientApp/src/features/chat/pages/ChatPage.test.tsx`: inside the top bar's start group, DOM order is Home button → "Flumeria Studio" → weather status → site-boundary status (seed `activeLocationStore`, mock weather, seed `activeSiteBoundaryStore`)

### Implementation for User Story 1

- [X] T020 [US1] Implement the `topStart` slot in `ClientApp/src/components/workspace-shell/WorkspaceOverlay.tsx` per research D1 / contract W1–W6: a single absolute top bar (`top/left/right: 0`, `m: {xs: 2, sm: 3}`, `display: flex`, `alignItems: flex-start`, `pointerEvents: none`); start group `Stack` (`flex: '0 1 auto'`, `minWidth: 0`, `flexWrap: wrap`, `alignItems: center`, `gap: 1`, `pointerEvents: none`, `RESERVED_ATTRIBUTE`); top cluster `FloatingToolbar placement="inline"` with `ml: 'auto'`, `flex: 'none'`. When `topStart` is absent, render the cluster exactly as today (W5). Update the component doc comment
- [X] T021 [US1] Convert `HomeProjectCard` in `ClientApp/src/features/chat/components/HomeProjectCard.tsx` to return a fragment: the Home `Fab` (unchanged, `pointerEvents: 'auto'` on the Fab itself) plus the title rendered through `HudCard`. Remove the absolute positioning and the wrapper `Box`, and update the doc comment
- [X] T022 [US1] Rework `LocationWeatherWidget` in `ClientApp/src/features/viewer/components/LocationWeatherWidget.tsx` to render through `HudCard` with the D5 two-line layout (condition icon 20 px; `caption` location name with ellipsis; `subtitle2`/600 temperature; inline `· last known` stale marker; single-line unavailable state). Keep the widget's user-facing strings (`Weather unavailable`, `last known`, the aria-label templates) together in one local constant, not scattered through the JSX. Delete `shellSx`, the position offsets, and the per-widget `RESERVED_ATTRIBUTE`. Keep every existing `role`/`aria-label` and the `setLocationName` effect unchanged
- [X] T023 [US1] Switch `ClientApp/src/viewer/extensions/builtin/boundaryConfidenceExtension.tsx` from `contributeOverlay(SiteBoundaryConfidenceBadge)` to `contributeHudItem(SiteBoundaryConfidenceBadge)`, and update its doc comment
- [X] T024 [US1] Remove the badge's own `position: 'absolute'` / `top` / `left` and its per-item `RESERVED_ATTRIBUTE` in `ClientApp/src/features/viewer/components/SiteBoundaryConfidenceBadge.tsx`, so it lays out as a row item. The content and surface changes belong to US2 and US3
- [X] T025 [US1] Compose the row in `ClientApp/src/features/chat/pages/ChatPage.tsx`: pass `topStart={<><HomeProjectCard /><LocationWeatherWidget /><ExtensionHudItemHost /></>}` to `WorkspaceOverlay`; remove the direct `<LocationWeatherWidget />` mount and the `<HomeProjectCard />` child; update the surrounding specs/036 comments
- [X] T026 [US1] Grep `ClientApp/src` for leftover dependencies on the old offsets (`top: { xs: 76`, `top: { xs: 168`, and comments naming "stacked below"/"under LocationWeatherWidget"), and fix any found (e.g. the top-left comment in `ClientApp/src/features/viewer/components/ViewerSurface.tsx` next to `<ExtensionToolbar />`)
- [X] T027 [US1] Run the US1 tests plus the full `npx vitest run`, and confirm no regressions against the T001 baseline

**Checkpoint**: The row works end to end. The site card is in the row but still shows its old content (fixed in US2).

---

## Phase 4: User Story 2 — Compact site-location card (Priority: P2)

**Goal**: The site card shows only the name and confidence, on the shared surface, with no stripe (FR-002a, FR-006–FR-008, FR-012)

**Independent Test**: Resolve a boundary that has `sourceDetail` and alternative candidates. The card shows exactly two lines, and neither "Source:" nor "Also considered:" is present (quickstart §2 step 2)

### Tests for User Story 2

- [X] T028 [P] [US2] Update `ClientApp/src/features/viewer/components/SiteBoundaryConfidenceBadge.test.tsx` for B1, B2, B4, and B5: `null` without name or level; exactly name + confidence label; no "Source:" or "Also considered:" text even when the store has `sourceDetail` and `alternativeCandidateNames`; a long name renders `noWrap` (ellipsis); aria-label is exactly `"{siteName} boundary: {label}"`; no violet `#9C62DE` anywhere in the rendered styles

### Implementation for User Story 2

- [X] T029 [US2] Rewrite the `SiteBoundaryConfidenceBadge` body in `ClientApp/src/features/viewer/components/SiteBoundaryConfidenceBadge.tsx` onto `HudCard` (`maxWidth: 260`, `role="status"`, new aria-label). Two lines: `subtitle2` `noWrap` name and `caption` label, both at `lineHeight: 1.25`. Remove the `sourceDetail`/`alternativeCandidateNames` selectors and their lines, the `ACCENT` constant, the stripe/border, and the `isDark` surface branch. Update the component doc comment (it now lives in the HUD row, contributed as a `hudItem`)
- [X] T030 [US2] Check that nothing else reads the badge-only behaviour that was removed. Grep for `Also considered` and `Source:` in `ClientApp/src` (tests included) and update any stale assertion

**Checkpoint**: US1 + US2 both verifiable. The card is compact, but the icon is still the old colour.

---

## Phase 5: User Story 3 — Confidence colour on the shield (Priority: P3)

**Goal**: A shield icon for each level (check / plain / `!`), in theme-aware success/warning/error tones that meet ≥3:1 contrast in both themes (FR-009–FR-011)

**Independent Test**: For each of high/medium/low, in light and dark theme, the icon is the correct shape and colour, and axe passes (quickstart §2 steps 2, 3, 5)

### Tests for User Story 3

- [X] T031 [P] [US3] Add tests to `ClientApp/src/features/viewer/components/SiteBoundaryConfidenceBadge.test.tsx` (table-driven over level × theme): the icon is `GppGoodOutlined` / `ShieldOutlined` / `GppMaybeOutlined` (assert on each icon's `data-testid`, e.g. `GppGoodOutlinedIcon`); its colour equals `palette.{success|warning|error}.main` in light and `.light` in dark; `getContrastRatio(iconColour, theme.palette.background.paper) >= 3` for every level × theme (FR-011, SC-004 — axe does not check non-text contrast); the icon is `aria-hidden`; the text label is still present (FR-010)
- [X] T032 [P] [US3] Extend `ClientApp/src/features/viewer/components/SiteBoundaryConfidenceBadge.a11y.test.tsx` to run axe for every level in both light and dark theme

### Implementation for User Story 3

- [X] T033 [US3] In `ClientApp/src/features/viewer/components/SiteBoundaryConfidenceBadge.tsx`, replace the Remix icons with the `@mui/icons-material` set, and add a colocated `CONFIDENCE_VISUAL: Record<SiteBoundaryConfidenceLevel, { Icon, tone: 'success' | 'warning' | 'error' }>` lookup (data-model.md). Resolve the colour as `theme.palette[tone][mode === 'dark' ? 'light' : 'main']` (research D7), with an icon size of about 20 px. Update the FR-006 comment to describe shape + text + colour
- [X] T034 [US3] Run the badge unit and a11y tests, then the full suite

**Checkpoint**: All three stories are complete.

---

## Phase 6: Polish & Cross-Cutting

- [X] T035 [P] Update the doc comment in `ClientApp/src/viewer/extensions/components/useAvoidReservedCorner.ts` (it names the old `LocationWeatherWidget`/`SiteBoundaryConfidenceBadge` stack; it now measures the HUD row's start group), per research D9
- [X] T036 [P] Add a "Amended by specs/073" note for `contributeHudItem`, linking [contracts/extension-context-hud-item.md](contracts/extension-context-hud-item.md), to `specs/050-viewer-extension-framework/contracts/extension-context.md`
- [X] T037 Run the full gate from `src/AskLucy.Web/ClientApp`: `npx tsc -b --noEmit`, `npx eslint src`, `npx vitest run`. All green against the T001 baseline. If `ChatPage.test.tsx` fails, re-run it in isolation before treating it as a regression
- [ ] T038 Run the live screenshot loop in [quickstart.md](quickstart.md) §2 (steps 1–7) and the §3 accessibility spot check. **This is the only check for FR-004, FR-005, SC-001, and SC-005** (jsdom can't lay out a page). Skipping it leaves them unverified, so record each step's pass/fail in the checklist notes (T039). If the two-line text is unreadable at 40 px, apply the research D4 fallback (44 px for the row, the Home Fab, and the cluster together) and repeat the loop — *2026-09-25: steps 1–2 covered by the user's live screenshots; steps 3–7 and §3 not run, accepted by the user (see checklist notes).*
- [X] T039 Update `specs/073-studio-hud-top-row/checklists/requirements.md` notes with the verification outcome, and mark the spec **Status: Implemented** in `specs/073-studio-hud-top-row/spec.md`

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (T001)** → **Foundational (T002–T014)** → **US1 (T015–T027)** → **US2 (T028–T030)** → **US3 (T031–T034)** → **Polish (T035–T039)**
- US2 and US3 both edit `SiteBoundaryConfidenceBadge.tsx` and its test file, so they run **in sequence**, not in parallel. US2 depends on US1's T024 (the badge moved into the row) and uses `HudCard` from T005.
- US3 could technically come before US2, but it's written against US2's `HudCard` body. Keep the P-order.

### Within phases

- Foundational: T002‖T004‖T008‖T009‖T010 are test files and can be written in parallel. T003 needs T002; T005 needs T004; T006 → T007 → T008 (run); T011 needs T010; T012 needs T006 + T009 + T011; T013 needs T011; T014 needs T012.
- US1: T015‖T016‖T017‖T018‖T019 in parallel. Then T020 (overlay) and T021/T022/T023/T024 (separate files) can go in parallel after T020. T025 needs T020–T024. T026 → T027.

### Parallel example (Foundational)

```text
Together: T002 FloatingToolbar.test.tsx | T004 HudCard.test.tsx | T009 ExtensionHudItemHost.test.tsx | T010 ContributionErrorBoundary.test.tsx
Then:     T003 FloatingToolbar.tsx      | T005 HudCard.tsx      | T011 ContributionErrorBoundary.tsx | T006→T007→T012 extension kind + host → T013 overlay host
```

### Parallel example (US1 implementation)

```text
After T020: T021 HomeProjectCard.tsx | T022 LocationWeatherWidget.tsx | T023 boundaryConfidenceExtension.tsx | T024 SiteBoundaryConfidenceBadge.tsx
Then:       T025 ChatPage.tsx
```

## Implementation Strategy

- **MVP = Phase 1 + 2 + US1**: the row itself, which is the main visible change the user asked for. It can be shipped and reviewed on its own.
- **Increment 2 = US2**: the card shrinks to two lines. The row then holds its intended 40 px height everywhere.
- **Increment 3 = US3**: confidence colours.
- Commit per phase (Conventional Commits, e.g. `feat(studio): lay the top-left HUD out as one row`). Push straight to `main`, following the solo-developer convention.

## Baseline notes

- **Before (2026-09-25, T001)**: `npx tsc -b --noEmit` clean; `npx vitest run` 263 files / 1797 tests passed in 165 s. No existing failures, and the known `ChatPage.test.tsx` flakes did not show up in this run.
- **After (2026-09-25, T027/T034/T037)**: `tsc -b --noEmit` clean, `eslint src` clean, `vitest run` 266 files / 1839 tests passed in 157 s. No regressions.
- Unplanned edits: three solar-extension test mocks (`CameraAttitudeWidget.test.tsx`, `SolarAnalysisOverlay.test.tsx`, `solarAnalysisExtension.test.ts`) build `ExtensionContext` object literals, so each needed the new `contributeHudItem: vi.fn()` member for `tsc -b` to pass.
- The `ChatPage` row-order test (T019) seeds the location *after* mount, because `ChatPage` clears `activeLocationStore` on mount when jsdom reports geolocation unavailable. That is the same precedent the existing agent-POI test follows.
