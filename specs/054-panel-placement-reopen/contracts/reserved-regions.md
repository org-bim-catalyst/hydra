# Contract: Reserved Regions

**Module**: `viewer/panels/layout/reservedRegions.ts`

**Nature**: The feature's only DOM-aware module, deliberately kept thin (~20 lines) so that everything interesting stays in the pure `arrangement.ts`.

---

## The declaration side — how chrome opts out of being covered

Any element that floating panels must not cover declares itself by carrying a single data attribute:

```tsx
<Paper data-panel-reserved sx={{ position: 'absolute', top: 16, right: 16 }}>
```

A shared constant is exported so no consumer hardcodes the string:

```ts
export const RESERVED_ATTRIBUTE = 'data-panel-reserved'
```

### Rules for declaring

| # | Rule | Rationale |
|---|---|---|
| R1 | The attribute goes on the element whose box *is* the obstacle — the visible surface, not a transparent full-viewport wrapper. | A wrapper at `inset: 0` would reserve the entire screen. This is the single most likely mistake; `WorkspaceOverlay` and `ExtensionOverlayHost` both have exactly such a wrapper. |
| R2 | An element that is conditionally rendered simply stops declaring when it unmounts. No deregistration call exists or is needed. | D2 — the pull-based design has no lifecycle to leak. |
| R3 | An element that is present but visually hidden (zero-size, `display:none`) is ignored automatically, because it measures as a zero rect. | Avoids a phantom obstacle pinned at the origin. |
| R4 | Floating panels themselves MUST NOT declare the attribute. Their boxes reach the algorithm through `ArrangementInput.panels`. | Declaring both would double-count them and let a panel block itself. |

### Current declarers

| Element | File | Wrapping note |
|---|---|---|
| Top-cluster toolbar (theme, account) | `components/workspace-shell/WorkspaceOverlay.tsx` | On the `pointerEvents:auto` wrapper of each `FloatingToolbar`, **not** the outer `inset:0` Box (R1) |
| Right-stack toolbar (viewer tools) | `components/workspace-shell/WorkspaceOverlay.tsx` | same |
| Bottom-end toolbar (chat trigger) | `components/workspace-shell/WorkspaceOverlay.tsx` | same |
| Extension toolbar | `viewer/extensions/components/ExtensionToolbar.tsx` | On the `Paper`. The `top: {xs:460, sm:480}` stopgap is removed in the same change (D9). |
| Camera attitude widget | `features/solar/components/CameraAttitudeWidget.tsx` | On the outer `Box`. The `top: {xs:512, sm:532}` stopgap is removed in the same change (D9). |
| Panel dock (reopen tray) | `viewer/panels/components/PanelDock.tsx` | On the rail itself (D7) |

---

## The collection side

```ts
function collectReservedRects(hostRect: DOMRect | { left: number; top: number }): Rect[]
```

### Behaviour

| # | Requirement | Rationale |
|---|---|---|
| C1 | Queries `document.querySelectorAll('[data-panel-reserved]')` at call time. Nothing is cached between calls. | D2 — rects cannot go stale if they are read when they matter. |
| C2 | Each element's `getBoundingClientRect()` is normalized to host-relative coordinates by subtracting `hostRect.left` / `hostRect.top`. | `FloatingPanel.position` is host-relative; `getBoundingClientRect` is viewport-relative. Mixing the two silently mis-places every panel when the host is not at the viewport origin. |
| C3 | Rects with `width <= 0` or `height <= 0` are discarded. | R3 — hidden elements must not become origin-pinned obstacles. |
| C4 | Returns `[]` when `document` is unavailable or nothing matches. Never throws. | Constitution §2-VIII; also keeps SSR/test environments safe. |
| C5 | Order of the returned array is not significant and MUST NOT be relied on by callers. | The consumer treats it as a set. |

### Testing note

`getBoundingClientRect()` returns all zeros under jsdom. Tests for this module stub it per element (`vi.spyOn(el, 'getBoundingClientRect')`); tests for placement *behaviour* go against `arrangement.ts` with literal `Rect` data instead, which is the reason for the split in the first place (D2).

---

## Call sites

`collectReservedRects` is called only from `FloatingPanelHost`, which already holds the host ref used by `clampToViewport`, at exactly four moments:

1. A panel opened (including reopened from the tray)
2. A panel closed
3. The window resized (alongside the existing `clampToViewport` call)
4. The explicit "arrange" action fired

Plus once per drag gesture, at drag start, to build the frozen candidate-slot list (D6).

It is never called during render, in a `useEffect` without one of the triggers above, or on every pointer move.
