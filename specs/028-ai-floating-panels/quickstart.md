# Quickstart: AI-to-UI Floating Panel Framework

Manual + scripted validation guide for this feature once implemented. See
[data-model.md](./data-model.md) and [contracts/](./contracts/) for the shapes referenced below.

## Prerequisites

- Backend running locally (`dotnet run --project src/AskLucy.Web`), migrations applied (new
  `UserPanelPreference` table).
- Frontend dev server running (`npm run dev` in `src/AskLucy.Web/ClientApp`), authenticated session, on
  the immersive viewer page (`/studio`, per spec 027/024).
- Devtools console access — this feature exposes `floatingPanelStore` and `panelTypeRegistry` on
  `window` in development builds only (mirrors spec 027's `viewerEngine` devtools exposure), so
  Scenario 1 doesn't require a real AI agent turn to be wired up yet (spec Assumption: the AI-side
  decision of *what* to show is out of this feature's scope).

## Scenario 1 — AI presents a visual response as a panel (US1, FR-001/FR-002/FR-021)

```ts
floatingPanelStore.getState().openPanel({
  requestId: 'demo-1',
  typeKey: 'chart',
  title: 'Daily Sun Exposure',
  data: { series: [{ label: 'Exposure (hrs)', values: [4, 6, 8, 7, 5] }], chartKind: 'bar' },
})
```

**Expect**: a floating, semi-transparent panel appears over the viewer at the first cascade position
(near the configured starting corner), titled "Daily Sun Exposure", rendering a bar chart, while the
viewer underneath remains visible and pannable/zoomable/selectable (FR-003, SC-008).

Repeat with a second, different `requestId`. **Expect**: the second panel appears offset from the
first (cascade — FR-021), not stacked exactly on top of it.

## Scenario 2 — Unknown type and malformed data (US1-AS3, FR-016/FR-017, SC-007)

```ts
floatingPanelStore.getState().openPanel({ requestId: 'demo-2', typeKey: 'not-a-real-type', title: 'X', data: {} })
floatingPanelStore.getState().openPanel({ requestId: 'demo-3', typeKey: 'chart', title: 'Bad Data', data: { nonsense: true } })
```

**Expect**: both calls produce a visible panel with a clear "unsupported type" / "couldn't load this
panel's data" fallback message respectively — never nothing happening, never a console-only failure,
never a crashed viewer. Confirm no unhandled promise rejection appears in the devtools console.

## Scenario 3 — Drag, resize, minimize, close, focus (US2, FR-004–FR-009)

1. With the Scenario 1 panels open, drag one by its title bar to a new position. **Expect**: it follows
   the pointer smoothly and stays where released (SC-002).
2. Drag a resizable panel's corner handle. **Expect**: it resizes and its content (the chart) reflows to
   fit.
3. Open the `parameters` type panel (fixed-size, per contracts/panel-type-registry.md) and confirm no
   resize handles are shown.
4. Click minimize on one panel. **Expect**: it collapses to a compact bar; clicking restore returns it
   to its prior size/position exactly.
5. Click close on one panel. **Expect**: it disappears from `floatingPanelStore`'s panel list (verify
   via `floatingPanelStore.getState().panels`).
6. With two overlapping panels, click the one behind. **Expect**: it visibly comes to the front
   (highest `zOrder`) and shows a focused-state style.

## Scenario 4 — Panel cap and LRU eviction (FR-022)

```ts
for (let i = 0; i < 11; i++) {
  floatingPanelStore.getState().openPanel({ requestId: `cap-${i}`, typeKey: 'table', title: `Panel ${i}`, data: { columns: ['A'], rows: [] } })
}
```

**Expect**: exactly 10 panels remain open (`MAX_CONCURRENT_PANELS`, data-model.md); `cap-0` (the
least-recently-focused, since none were manually focused) has been automatically closed to make room
for `cap-10`. No error, no blocked request — the 11th `openPanel` call always succeeds.

## Scenario 5 — Opacity preference (US3, FR-010–FR-012)

1. Navigate to Settings → **Viewer** tab (new tab, appended to `SETTINGS_TAB_INDEX`).
2. **Expect**: an opacity slider bounded `40%`–`100%` (spec Clarifications Q4), defaulting to `85%` on
   a fresh account.
3. Move the slider to `55%`. **Expect**: every currently-open floating panel's transparency updates
   immediately, no page reload (SC-005).
4. Reload the page, reopen a panel. **Expect**: it renders at `55%` opacity (persisted preference,
   FR-012).
5. Attempt (via direct API call, `curl`/devtools network tab replay) `PUT /api/v1/panels/preferences`
   with `{ "opacityPercent": 10 }`. **Expect**: `400` Problem Details — the floor is enforced
   server-side too, not just by the slider's UI bounds.

## Scenario 6 — Viewer context association (US4, FR-013/FR-014)

```ts
viewerEngine.addLayer({ id: 'demo-layer', kind: 'model', visible: true, zIndex: 0, metadata: {} })
floatingPanelStore.getState().openPanel({
  requestId: 'demo-6', typeKey: 'summary', title: 'Site Notes', data: { heading: 'Site Notes', body: 'Demo' },
  contextAssociation: { layerId: 'demo-layer', elementId: 'demo-element' },
})
```

1. Click the panel's "Locate in viewer" button (shown whenever `contextAssociation` carries both a
   `layerId` and `elementId` — generic panel chrome, not specific to the `summary` type).
   **Expect**: `viewerEngine.select('demo-layer', 'demo-element')` is invoked (fails gracefully if
   `demo-element` was never registered selectable — that's expected in this manual demo; the point is
   the panel drives the viewer command, not that the element resolves) (FR-014, US4-AS1).
2. Call `viewerEngine.removeLayer('demo-layer')`. **Expect**: the panel's `contextStatus` becomes
   `'invalid'` and the panel visibly shows the "association is no longer valid" indicator (US4-AS2;
   Edge Cases: viewer object removed).
3. Call `viewerEngine.displayContent('demo-layer', { updated: true })` on a *different* panel still
   associated with `demo-layer` (before removing it). **Expect**: that panel's `contextStatus` becomes
   `'stale'` and shows the corresponding indicator — the "relevant state changed" half of FR-014, not
   just outright removal.

## Scenario 7 — Panel preferences API, direct HTTP (backend-only check)

```bash
curl -H "Authorization: Bearer <token>" "https://localhost:<port>/api/v1/panels/preferences"
# → 200 { "opacityPercent": 85 }  (default, before any save)

curl -X PUT -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"opacityPercent":60}' "https://localhost:<port>/api/v1/panels/preferences"
# → 200 { "opacityPercent": 60 }
```

## Automated test suites

```bash
# Backend
dotnet test tests/AskLucy.Application.Tests --filter FullyQualifiedName~Panels
dotnet test tests/AskLucy.Infrastructure.Tests --filter FullyQualifiedName~Panels
dotnet test tests/AskLucy.Web.Tests --filter FullyQualifiedName~Panels

# Frontend
cd src/AskLucy.Web/ClientApp
npm run test -- panels          # unit/component/a11y tests under viewer/panels/

# E2E
npx playwright test tests/AskLucy.E2E.Tests/AiFloatingPanels.spec.ts
```

All suites MUST pass, and the frontend suite MUST include `jest-axe` a11y checks for `FloatingPanel`
chrome (drag handle, resize handles, minimize/close buttons all keyboard-operable with visible focus
states — constitution §7/§10) and the new Settings "Viewer" tab's opacity slider, matching the existing
`*.a11y.test.tsx` convention.
