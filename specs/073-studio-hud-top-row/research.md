# Research: Studio HUD Top Row

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-09-25

## Current state (as found)

The four row items are mounted by three unrelated layers today, each absolutely positioned by hand-tuned pixel offsets:

| Item | Mounted by | Stacking layer | Position today |
|---|---|---|---|
| Home button + "Flumeria Studio" chip | `HomeProjectCard` — a `WorkspaceOverlay` child (`ChatPage.tsx`) | `WorkspaceOverlay`, `zIndex: 2` | `top {16,20}, left {16,20}` |
| Weather label | `LocationWeatherWidget` — a direct `ChatPage` child | page root (no z-index) | `top {76,84}, left {16,24}` |
| Site card | `SiteBoundaryConfidenceBadge` — contributed by the `viewer.boundary-confidence` extension via `contributeOverlay`, rendered by `ExtensionOverlayHost` inside `ViewerSurface` | `ViewerSurface` (`zIndex: 0` stacking context) → overlay host `zIndex: 1` | `top {168,180}, left {16,24}` |

Every vertical offset is a guess at the height of the item above it. A single row can't be built by editing offsets: the weather label's width varies (location name length), so the site card's `left` cannot be known statically. The site card also lives in a lower stacking context than the other two, so it can't be flex-laid-out beside them without moving where it renders.

## D1 — One flex row, owned by `WorkspaceOverlay`

- **Decision**: `WorkspaceOverlay` gains a `topStart?: ReactNode` slot. When `topStart` or the top cluster is present, it renders a single absolutely-positioned **top bar**: a full-width flex container, with the start group (`topStart`, `flex: 0 1 auto`, `minWidth: 0`, `flexWrap: wrap`) on the left and the existing top-right cluster (`flex: none`, `ml: 'auto'`) on the right. Both groups are **in-flow** children of the bar. The start group is sized to its content and only shrinks (and wraps) when space runs out. It must not grow to fill the gap: a start group stretched to the cluster would put a reserved rect in the top-right corner, and `ExtensionToolbar`'s `useAvoidReservedCorner('right')` would then measure it.
- **Rationale**: Overlap with the top-right cluster (FR-005, SC-005) becomes impossible by construction. The start group can only use the space the cluster leaves free, and wraps inside it. There is no measuring, no `ResizeObserver`, and no first-frame jump. `WorkspaceOverlay` already owns the top-right cluster (`topClusterLeading`), so owning its left-hand counterpart is the same responsibility, not a new one. Because both groups are in-flow, each carries `RESERVED_ATTRIBUTE` on a real, non-zero rect. This avoids the 0×0 collapse documented in `WorkspaceOverlay.tsx` ("FOUND LIVE 2026-09-13").
- **Alternatives considered**:
  - *Absolute row with `maxWidth: calc(100% - Npx)`*: N depends on how many top-cluster buttons are showing (`MarkerStyleSelector` is conditional), so any constant is wrong in some state. Rejected.
  - *Absolute row that measures the cluster with `ResizeObserver`*: works, but adds a render → measure → re-render cycle, a first-paint overlap window, and a second copy of the measuring pattern `useAvoidReservedCorner` already carries. Rejected as more machinery for the same result.
  - *Build the row in `ChatPage`*: `ChatPage` doesn't know the top cluster's width, which brings the overlap problem back. Rejected.

## D2 — `FloatingToolbar` gets an in-flow placement

- **Decision**: Add `placement?: 'anchored' | 'inline'` to `FloatingToolbar` (default `'anchored'`, today's behaviour). With `'inline'`, it drops `position: absolute`, the anchor offsets, the outer margin, and the `maxWidth` calc. The top bar (D1) owns the margin. Its wrap and alignment behaviour stays.
- **Rationale**: This reuses the existing primitive (constitution §7 Design system) instead of re-implementing a wrapping flex cluster for the top bar. The right-stack and bottom-end clusters don't change.
- **Alternatives considered**: A new `TopBar` primitive that duplicates `FloatingToolbar`'s wrap logic. Rejected (DRY).

## D3 — Site card reaches the row through a new extension contribution kind

- **Decision**: Add a `hudItem` contribution kind to the viewer extension framework: `ExtensionContext.contributeHudItem(component)` → `{ kind: 'hudItem', extensionId, component }`. A new `ExtensionHudItemHost` renders all `hudItem` contributions in contribution order, and the studio places that host last in the row. `viewer.boundary-confidence` switches from `contributeOverlay(SiteBoundaryConfidenceBadge)` to `contributeHudItem(SiteBoundaryConfidenceBadge)`.
- **Rationale**: The badge stays owned by its extension. Stopping or failing `viewer.boundary-confidence` still withdraws it (specs/050 FR-014/FR-015), and the extensibility test's `DECLARED_EXTENSIONS` list doesn't change. Rendering from the store has the same "a contribution before the host mounts works" guarantee as `ExtensionOverlayHost` (specs/050 FR-018/FR-020). The kind is general-purpose: future glanceable status chips (e.g. solar-analysis state) can join the row without further framework changes. The existing test "hosts ignore a kind they don't recognise" already covers forward-compatibility.
- **Alternatives considered**:
  - *`createPortal` from the overlay into a DOM slot published by the row*: the badge would stay an `overlay` while actually living somewhere else, which hides the real layout relationship. It also needs a DOM-node registry and still has to handle mount ordering. Rejected.
  - *Render `SiteBoundaryConfidenceBadge` directly in the row and retire the extension*: this breaks the viewer-extension model (049–052) for one built-in and changes `DECLARED_EXTENSIONS`. Rejected.

## D3a — Render failures in contributed items are contained per item

- **Decision**: Add a new `ContributionErrorBoundary` (class component, `viewer/extensions/components/`) that wraps **each** contributed component rendered by `ExtensionHudItemHost` and `ExtensionOverlayHost`. On `componentDidCatch`, it records the failure through the store's existing lifecycle-failure path: `setLifecycle(extensionId, 'failed', 'Render failed: {message}')`. `ExtensionFailureNotice` then shows it (specs/050 FR-029), and the boundary renders `null` for that item only.
- **Rationale**: Neither host has a boundary today. While the badge lived inside `ViewerSurface`, a render throw took down the viewer. Now that `ExtensionHudItemHost` renders inside `WorkspaceOverlay`, the same throw would take down the whole overlay: chat, account menu, and every control. Constitution §2.VIII rules out both the silent drop and the collapse. A per-item boundary keeps the blast radius at one row item, and failing through the lifecycle path reuses the notice users already see for start/stop failures, so no new UI is needed. Wrapping `ExtensionOverlayHost` too is one line with the same component, and it closes the gap that already exists there.
- **Alternatives considered**:
  - *One boundary around the whole host*: one bad item would blank every other extension's item. Rejected.
  - *`recordEventFailure`*: it's worded for "an event handler threw while the extension kept running". A component that can't render isn't still running correctly. Rejected.
  - *Precedent*: `SceneErrorBoundary` in `features/chat/scene/SceneBackground.tsx` is the existing class-boundary pattern in this codebase, so this one follows its shape.

## D4 — Row height is 40 px, the height of every existing circular control

- **Decision**: Shared row height = **40 px**. The Home button (already a 40 px `Fab`) doesn't change. The three cards are 40 px rounded rectangles with their content vertically centred. The top-right cluster's buttons are also 40 px, so the whole top edge of the screen shares one centreline.
- **Rationale**: Two text lines fit inside 40 px if both use a line-height of 1.25: `subtitle2` (14 px → 17.5 px) + `caption` (12 px → 15 px) = 32.5 px, leaving about 3.75 px of vertical padding per side. Staying at 40 keeps the Home button and every workspace control unchanged, and the `right-stack` cluster's `mt` offset stays correct.
- **Alternatives considered**: 44 px for everything. Rejected: the Home button would no longer match the top-right cluster's 40 px buttons, which gives a 2 px centreline mismatch across the top edge. 40 px `Fab`s are the established control size in this workspace.

## D5 — Weather label re-layout to fit 40 px

- **Decision**: Two lines. Line 1 is the location name (`caption`, secondary emphasis, `noWrap` + ellipsis). Line 2 is the temperature (`subtitle2`, weight 600), with the condition icon (20 px) to their left. The stale state becomes a compact inline marker on line 2 (`· last known`, `caption`, reduced opacity). The accessible name keeps the "(last known reading)" wording. The "Weather unavailable" state is a single centred line in the same 40 px card.
- **Rationale**: Today's `h6` temperature (17 px × 1.4) plus the location line is about 45 px, and the stale caption adds a third line. Neither fits a 40 px row (spec edge case "Stale weather reading").

## D6 — One shared card surface

- **Decision**: Extract a `HudCard` shared component in `components/workspace-shell/` (a 40 px flex box, content vertically centred, `px: 1.5`, `borderRadius: 2`, `CIRCULAR_ACTION_CHROME.expandedBg` background, `.border`, `backdropFilter: blur(12px)`, the existing `0 2px 10px rgba(0,0,0,0.28)` shadow, and `.icon` text colour). The project title chip, weather label, and site card all render through it. `pointerEvents` is a prop so the non-interactive cards stay pass-through (FR-013).
- **Rationale**: The title chip and weather label each carry a copy of this style today. The site card has a third, divergent style (its own `isDark` branch, a violet `#9C62DE` border and stripe, and a different shadow). Three users meets the constitution §7 bar for a shared component. `CIRCULAR_ACTION_CHROME` is theme-driven, so the whole row follows light/dark together (FR-002a).
- **Removes**: the `ACCENT` constant, the stripe, the per-card `isDark` surface branch, and all hand-tuned `top`/`left` offsets on the three items.

## D7 — Confidence colour and shield icons

- **Decision**:

  | Level | Icon (`@mui/icons-material`) | Colour, light theme | Colour, dark theme |
  |---|---|---|---|
  | high | `GppGoodOutlined` (shield + check) | `palette.success.main` `#3F7D4E` | `palette.success.light` |
  | medium | `ShieldOutlined` (plain shield) | `palette.warning.main` `#B8791F` | `palette.warning.light` |
  | low | `GppMaybeOutlined` (shield + `!`) | `palette.error.main` `#B23B2E` | `palette.error.light` |

- **Rationale**:
  - **Icons**: Remix Icon (the library the badge uses today) has no "shield + warning" glyph. It only has check/cross/flash/keyhole/star/user variants. The MUI Material "gpp" set has all three shapes from one family, drawn in the same style, which is what Q1 asked for. `@mui/icons-material` is already a dependency, so nothing new is added.
  - **Colours**: they come from the theme's semantic tokens, so nothing is hardcoded (constitution §7 Theming). Measured non-text contrast against the card surface (≈ `background.paper`, at 0.97 alpha):

    | Token | vs light `#FFFFFF` | vs dark `#1D1B17` |
    |---|---|---|
    | success.main | 4.94 ✅ | 3.48 ✅ (marginal) |
    | warning.main | 3.63 ✅ | 4.74 ✅ |
    | error.main | 5.90 ✅ | **2.91 ❌** |
    | success.light (MUI +0.2 tonal) | — | 5.09 ✅ |
    | warning.light | — | 6.34 ✅ |
    | error.light | — | 4.22 ✅ |

    `error.main` fails the 3:1 bar in dark mode (FR-011), so dark mode uses the `.light` variant for all three levels. That keeps them one consistent family per theme, and every value passes.
- **Alternatives considered**: `RiShieldCrossLine` for Low. Rejected: a cross reads as "invalid/rejected", but Low means "approximate".

## D8 — Card content trims

- **Decision**: Remove the `Source: …` and `Also considered: …` lines from the badge. `sourceDetail` and `alternativeCandidateNames` stay in `activeSiteBoundaryStore`, because other consumers and Lucy's reply still use them. The accessible name becomes `"{siteName} boundary: {confidence label}"`. The site name is `noWrap` with an ellipsis, and the card has `maxWidth: 260`.
- **Rationale**: FR-006, FR-007, FR-008, FR-012. The spec keeps the data, and only the card stops showing it.

## D9 — `useAvoidReservedCorner` callers stay correct

- **Decision**: No change to the hook. The row's start group carries `RESERVED_ATTRIBUTE` (D1), so `CameraAttitudeWidget` (left corner) now sits just below one 40 px row instead of a 3-card stack. The per-item `RESERVED_ATTRIBUTE`s on the weather label and badge are removed, because the group rect covers them.
- **Rationale**: The hook measures whatever is currently reserved in the corner, so a shorter footprint automatically gives back map space (SC-001). Update the doc comments that name the old three-card stack.
- **Verification**: A unit test proves the hook positions a left-corner widget just below a reserved start-group element. The live check confirms `CameraAttitudeWidget` sits directly under the row (quickstart §2).
