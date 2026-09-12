# Quickstart: Panel Content Model

**Feature**: `049-panel-content-model`

How to verify this feature end to end. Scenarios map to the spec's user stories and success criteria.

## Prerequisites

- Backend running with a valid database connection (`PERSISTENCE_TESTS_CONNECTION_STRING` for test runs; the value lives in `appsettings.Development.json`).
- Frontend dev server running, with an authenticated session.
- A development build — the devtools handles below ship only in development.

```bash
# frontend
cd src/AskLucy.Web/ClientApp
npm install
npm run dev

# unit + a11y tests
npm test

# type check — note the -b flag; a bare `tsc --noEmit` checks nothing in this repo
npx tsc -b --noEmit

# lint
npm run lint
```

```bash
# backend tests
dotnet test
```

Panels are driven through `window.__askLucyFloatingPanelStore` in development, mirroring the specs/028 approach, so these scenarios do not require a live AI turn.

---

## Scenario 1 — Composed content renders (US1, SC-001)

Open the workspace, then in the browser console:

```js
window.__askLucyFloatingPanelStore.getState().openPanel({
  requestId: crypto.randomUUID(),
  kind: 'content',
  title: 'Al Safa Park 2',
  content: { version: 1, blocks: [
    { kind: 'heading', text: 'Al Safa Park 2' },
    { kind: 'keyValue', items: [
      { label: 'Address', value: 'Al Wasl Road, Dubai' },
      { label: 'Coordinates', value: '25.1412, 55.2210' },
      { label: 'Plot number', value: null }
    ]},
    { kind: 'divider' },
    { kind: 'text', text: 'Resolved from the site boundary service.' }
  ]}
})
```

**Expect**: a panel over the viewer showing the heading, the three labelled values with the missing plot number marked explicitly as unavailable, a separator, and the prose. No code was written for "location info" — this is the point of the feature.

Repeat with `table`, `chart`, `metric` and `image` blocks to confirm each renders. Toggle the theme and confirm every block stays legible in both (SC — spec FR-007).

---

## Scenario 2 — Every previous presentation still works (SC-003)

For each of the four retired types, compose the equivalent block document and confirm the result is at least as good as before:

| Retired type | Equivalent |
|---|---|
| `summary` | `heading` + `text` blocks |
| `parameters` | one `keyValue` block |
| `table` | one `table` block |
| `chart` | one `chart` block |

**Expect**: no loss of information or legibility in any of the four.

---

## Scenario 3 — Actions drive the viewer (US2)

```js
const s = window.__askLucyFloatingPanelStore.getState()
window.__askLucyViewerEngine.addLayer({ id: 'demo-layer', kind: 'overlay' })

s.openPanel({
  requestId: crypto.randomUUID(),
  kind: 'content',
  title: 'Layers',
  content: { version: 1, blocks: [
    { kind: 'keyValue', items: [
      { label: 'Hide demo layer', value: 'demo-layer',
        action: { command: 'setLayerVisibility', args: { layerId: 'demo-layer', visible: false } } },
      { label: 'Not activatable', value: 'no action here' }
    ]}
  ]}
})
```

**Expect**: the first entry is visibly activatable and keyboard-reachable; activating it hides the layer. The second is not activatable and not focusable. Tab to the first entry and activate with Enter — it must work by keyboard alone (SC-008).

Then remove the layer and activate again: **expect** a visible message that the target is unavailable, not silence (spec FR-015).

---

## Scenario 4 — Rejected actions are inert (US4, SC-005)

```js
window.__askLucyFloatingPanelStore.getState().openPanel({
  requestId: crypto.randomUUID(),
  kind: 'content',
  title: 'Action safety',
  content: { version: 1, blocks: [
    { kind: 'keyValue', items: [
      { label: 'Not allowlisted', value: 'x',
        action: { command: 'removeLayer', args: { layerId: 'gis-current-location' } } },
      { label: 'Bad arguments', value: 'y',
        action: { command: 'zoomToLocation', args: { latitude: 999, longitude: 'north' } } }
    ]}
  ]}
})
```

**Expect**: neither entry renders as activatable; neither is keyboard-reachable; no handler fires; the map layer is still present; both refusals appear in the console for diagnosis. `removeLayer` must not execute under any circumstances — it is excluded from the allowlist precisely because content must not be able to destroy viewer content.

---

## Scenario 5 — Partial rendering (US4, SC-007)

```js
window.__askLucyFloatingPanelStore.getState().openPanel({
  requestId: crypto.randomUUID(),
  kind: 'content',
  title: 'Degradation',
  content: { version: 1, blocks: [
    { kind: 'heading', text: 'This renders' },
    { kind: 'notARealKind', whatever: true },
    { kind: 'table', columns: [] },
    { kind: 'text', text: 'So does this' }
  ]}
})
```

**Expect**: the heading and the closing text render normally; the unknown kind shows a visible placeholder; the malformed table shows a visible error. Four blocks in, four outcomes, none silent.

---

## Scenario 6 — Chrome variants (US3)

Open three panels with `chrome` set to `{titleBar: true, resizable: true}`, `{titleBar: true, resizable: false}` and `{titleBar: false, resizable: false}`.

**Expect**:
- The first drags by its title bar, resizes, minimises, restores to its exact prior size and position, closes.
- The second offers no resize affordance and keeps its size.
- The third shows no title bar but exposes a grip: focusable, labelled, draggable, with arrow-key nudging (Shift for a larger step) and close/minimise beside it.

All three must be fully operable by keyboard (SC-008).

---

## Scenario 7 — Nothing to show opens nothing (spec FR-030)

Ask Lucy to present content in a turn that produces no renderable result.

**Expect**: no empty panel appears, and the user is told why. Verify at the backend too: a `present_panel_content` call with `blocks: []` must be refused by `CapabilityExecutor`'s schema gate before any push occurs.

---

## Scenario 8 — Server-side validation holds (SC-005)

Backend test, not a browser step. Invoke `present_panel_content` with content that violates the document envelope — no `blocks` array, a `blocks` entry with no `kind`, more than 50 blocks, the wrong `version`.

**Expect**: `CapabilityExecutor` refuses each before execution, with the refusal logged and reported through the capability's normal failure path. No `PanelRequested` push occurs.

Separately, confirm an image block cannot be made to fetch an arbitrary address: open a content panel with an `image` block whose `fileId` is a URL-shaped string (e.g. `https://example.com/x.png`). **Expect**: the panel shows the image as unavailable, and the network tab shows no request to that address — only a request to the platform's own file-download endpoint, which then fails to find a matching file. `fileId` is never interpolated into an `<img src>` (research D10).

---

## Scenario 9 — Schema parity (research D2)

```bash
cd src/AskLucy.Web/ClientApp
npm test -- panel-content-schema
```

**Expect**: the test regenerates the JSON Schema from the zod source and compares it to the committed `contracts/panel-content.schema.json`. It must fail if they differ — that failure is the entire mechanism preventing the client and server vocabularies from drifting apart.

---

## Scenario 10 — Nothing from specs/028 regressed (SC-004)

```bash
cd src/AskLucy.Web/ClientApp
npm test                      # full suite, not just touched files
```

Run the **whole** suite. `ChatPage.test.tsx` carries its own panel assertions independent of the panel components' own test files, so a green run of the touched files proves nothing.

Then exercise manually: drag, resize, minimise and restore to the exact prior position, close, focus and stacking with several panels overlapping, the opacity preference applied to both open and newly-opened panels, cascade placement, and eviction of the least-recently-focused panel past the cap.

**Expect**: no user-visible difference from the current release in any of these.

E2E (requires a deployed environment and `E2E_BASE_URL`):

```bash
cd tests/AskLucy.E2E.Tests
npm test
```

`AiFloatingPanels.spec.ts` fixtures change to the new request shape; its assertions should not need to.
