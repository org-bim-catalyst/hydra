# Implementation Plan: Studio HUD Top Row

**Branch**: `073-studio-hud-top-row` | **Date**: 2026-09-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/073-studio-hud-top-row/spec.md`

## Summary

Replace the studio's hand-offset, three-layer stack of top-left chrome with one left-anchored flex row: **Home ● → [Flumeria Studio] → [weather] → [site card]**. All items are 40 px tall and share a centreline with the top-right control cluster. `WorkspaceOverlay` gains a `topStart` slot and renders one top bar holding both the new start group and the existing cluster, so the two cannot overlap. The site card, which is contributed by a viewer extension and rendered in a lower stacking context, reaches the row through a new `hudItem` extension contribution kind. The three cards share a new `HudCard` surface that follows the theme. The site card is cut down to name + confidence, with a shield icon per level (check / plain / `!`) coloured from the theme's success/warning/error tokens (`.light` variants in dark mode, for contrast).

## Technical Context

**Language/Version**: TypeScript (strict), React 19

**Primary Dependencies**: MUI (theme, `Stack`, `Fab`), `@mui/icons-material` (already installed; the shield icons come from it), Zustand (`viewerExtensionStore`, `activeSiteBoundaryStore`), TanStack Query (`useCurrentWeather`)

**Storage**: N/A. Presentation only. No backend, API, or schema change.

**Testing**: Vitest + Testing Library + axe (a11y). Full-suite run required (page-level `ChatPage.test.tsx`).

**Target Platform**: Browser SPA (`src/AskLucy.Web/ClientApp`)

**Project Type**: Web application, frontend only

**Performance Goals**: No layout measuring or re-render loops introduced. The row is pure CSS flex. Map GPU work is untouched.

**Constraints**: WCAG 2.1 AA (≥3:1 non-text contrast for the icons, in both themes); theme-token colours only; non-interactive cards stay `pointerEvents: none`; 360–2560 px widths without overlap.

**Scale/Scope**: About 10 source files touched, 2 new components (`HudCard`, `ExtensionHudItemHost`), 1 framework API addition.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Clean Architecture / dependency rule | ✅ | Frontend only. `ChatPage` (feature) → `workspace-shell` (shared) and `viewer/extensions` (framework). No new inward-pointing violations. |
| II. SOLID | ✅ | `WorkspaceOverlay` keeps sole ownership of top-edge layout. The badge stays owned by its extension. `hudItem` is an open extension point (O/C). |
| III. Simplicity (DRY/KISS/YAGNI) | ✅ | It removes three copies of the card surface and every hand-tuned offset. The layout uses CSS flex rather than measuring (research D1). `hudItem` exists because a real consumer needs it now, not speculatively (D3). |
| IV. Composition over inheritance | ✅ | The row is composed from slot children, and `HudCard` wraps content. |
| V. DI & testability | ✅ | Every piece can be rendered in isolation. The extension host renders from the store, as it does today. |
| VI. Separation of concerns | ✅ | Layout (overlay), surface (`HudCard`), and content (each widget) are separated. |
| VIII. No silent failures | ✅ | No new failure paths. The weather error state stays visible ("Weather unavailable"). Extension failure containment is unchanged. |
| §7 Design system | ✅ | The shared component has 3 consumers. It reuses `FloatingToolbar` (`placement="inline"`) and the `CIRCULAR_ACTION_CHROME` tokens. |
| §7 Accessibility | ✅ | Icon shape + text + colour, with contrast measured (research D7). axe tests for every level in both themes. Accessible names preserved. |
| §7 Responsive | ✅ | Flex wrap inside the space the cluster leaves free. No fixed-pixel row width. |
| §7 Theming | ✅ | The hardcoded `#9C62DE` accent and the per-card `isDark` branches are **removed**. Colours come only from palette tokens. |
| §10 Testing | ✅ | Unit, a11y, and page tests land with the change (quickstart §1). |

**Post-design re-check**: ✅ No violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/073-studio-hud-top-row/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── studio-hud-row.md
│   └── extension-context-hud-item.md
├── checklists/requirements.md
└── tasks.md              # /speckit-tasks
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/
├── components/workspace-shell/
│   ├── WorkspaceOverlay.tsx            # + topStart slot, single top bar (D1)
│   ├── FloatingToolbar.tsx             # + placement: 'anchored' | 'inline' (D2)
│   ├── HudCard.tsx                     # NEW shared 40 px surface (D6)
│   └── *.test.tsx                      # WorkspaceOverlay / FloatingToolbar / HudCard
├── features/chat/
│   ├── pages/ChatPage.tsx              # passes topStart; stops mounting LocationWeatherWidget directly
│   └── components/HomeProjectCard.tsx  # fragment, no self-positioning; title → HudCard
├── features/viewer/components/
│   ├── LocationWeatherWidget.tsx       # HudCard, 2-line 40 px layout, inline stale marker (D5)
│   ├── SiteBoundaryConfidenceBadge.tsx # HudCard, 2 lines, shield icons + tone (D7, D8)
│   └── ViewerSurface.tsx               # comment update only (top-left is no longer the badge's home)
└── viewer/extensions/
    ├── ViewerExtension.ts              # + 'hudItem' Contribution member
    ├── context.ts                      # + contributeHudItem
    ├── components/ExtensionHudItemHost.tsx   # NEW (D3)
    ├── components/useAvoidReservedCorner.ts  # doc comment only (D9)
    └── builtin/boundaryConfidenceExtension.tsx  # contributeOverlay → contributeHudItem
```

**Structure Decision**: Existing SPA layout. New shared primitives go in `components/workspace-shell/`, next to the chrome they generalize. The new host goes in `viewer/extensions/components/`, beside `ExtensionOverlayHost`.

## Implementation order (for /speckit-tasks)

1. **Foundation**: `FloatingToolbar` `placement`, `HudCard`, `hudItem` kind + `contributeHudItem` + `ExtensionHudItemHost` (each with tests).
2. **US1 (P1), row**: `WorkspaceOverlay` `topStart` top bar. `HomeProjectCard` becomes a fragment. `ChatPage` composes the row. `LocationWeatherWidget` gets the 40 px layout. The boundary extension migrates to `hudItem`.
3. **US2 (P2), compact card**: drop the Source and Also-considered lines, add ellipsis, new aria-label, `HudCard` surface, remove the stripe.
4. **US3 (P3), confidence tone**: shield icon set + theme-aware tone mapping + axe per level/theme.
5. **Polish**: doc-comment updates (D9, `ViewerSurface`), the specs/050 contract cross-reference, the full test suite, the quickstart §2 screenshot loop.

## Risks

| Risk | Mitigation |
|---|---|
| The two-line text looks cramped at 40 px | Line-height 1.25 is measured to fit (D4). The screenshot loop checks it. If it's unreadable, fall back to 44 px for the whole row + Home + cluster in one change (the alternative in research D4). |
| `ChatPage.test.tsx` flakes under the full suite (known) | Run it in isolation to confirm. See the memory note on ChatPage flakiness. |
| Something else still depends on the old absolute positions | Grep for `168`/`180`/`76`/`84` offsets and `HomeProjectCard` positioning during implementation. `useAvoidReservedCorner` adapts automatically (D9). |

## Complexity Tracking

Not needed. There are no constitution violations.
