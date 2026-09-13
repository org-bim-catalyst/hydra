# Contract: Solar Analysis Extension

What `solarAnalysisExtension` declares, contributes and withdraws. This contract exists mainly to
be checked against SC-009: every line below must be satisfiable with the frameworks as specs/049–051
already shipped them.

## Identity

```ts
id: 'viewer.solar-analysis'
manifest: {
  displayName: 'Solar Analysis',
  description: 'Sun path, shadows and time of day over the site.',
  toggleable: true,
  startsWithViewer: false,
}
```

`startsWithViewer: false` — the analysis is opened by the user (toolbar, FR-029) or by Lucy
(FR-033), not automatically. `toggleable: true` — activate/deactivate is how it is opened and
closed without unloading the extension.

## What it reaches the viewer through

**Only `ExtensionContext`** (FR-037). It imports no viewer internals, holds no reference to the
`THREE.Scene`, `Camera` or `WebGLRenderer`, and never calls `requestRedraw`.

| Need | Context member |
|---|---|
| Draw geometry | `acquireDrawingSpace()` → `handle.group` |
| Request a redraw | `handle.invalidate()` |
| Per-frame tick during playback | `handle.onFrame(cb)` (guarded — see below) |
| Shadows / tone mapping | `handle.declareDrawingRequirement('shadows' \| 'toneMapping')` |
| World → scene position | `worldToLocal(...)` from specs/051's published conversion |
| Site / content changes | `context.on('contentLoading' \| 'contentFailed', …)` |
| Toolbar entry | `context.contributeToolbarEntry(…)` |
| Control panels | `context.registerLivePanelKind(…)` |
| Figures panel | `context.openPanel({ kind: 'content', … })` |

## Contributions and teardown

| Contributed on activate | Withdrawn by |
|---|---|
| Drawing space (geometry, light, ground plane) | Framework — `drawingSpace` contribution's `release()` |
| `shadows`, `toneMapping` requirements | Framework — `rendererState.withdrawRequirements` on release |
| Toolbar entry | Framework — `toolbarEntry` contribution |
| Live panel kinds (time control, corrections) | Framework — `livePanelKind` contribution |
| Event subscriptions | Framework — `eventSubscription` contribution |
| Frame callback | Released with the drawing space |

**`stop()` does no teardown of its own beyond stopping playback.** Everything above is
framework-owned, which is the point of FR-040 and exactly what specs/050's contribution tracking
was built for. A `stop()` that needed to clean up geometry by hand would be evidence the framework
was insufficient.

### Guarantees

- **FR-040 / SC-007**: after deactivate, zero residual geometry, zero panels, zero toolbar entries,
  zero subscriptions, and `renderer.shadowMap.enabled` back to its pre-activation value.
- **SC-006**: 50 activate/deactivate cycles return scene child count and renderer state to
  baseline — the same fifty-cycle assertion specs/051 already applies to drawing spaces.
- **FR-041**: every position goes through `worldToLocal`. The extension never calls
  `sceneAnchor.set(...)`.

## Redraw and frame discipline (FR-039)

```ts
// Registered once. Inert unless playing — and critically, requests no redraw when inert.
drawingSpace.onFrame((deltaSeconds) => {
  if (!store.getState().isPlaying) return          // ← no invalidate() ⇒ viewer goes quiet
  store.getState().advanceBy(deltaSeconds)
  drawingSpace.invalidate()
})
```

**Redraw-timing trap (research D14)**: the prototype found that one redraw after a change could
leave the change invisible "until I interact", and worked around it by bursting ~12 requests over
successive frames. specs/051's scheduler coalesces per frame, which is the same shape. Implementation
must **verify** whether a single `invalidate()` suffices; if not, the conformant fix is repeated
`invalidate()` calls across successive animation frames — never a return to `requestRedraw()`.

**Known divergence from FR-039's literal wording, carried from research D9**:
`DrawingSpaceHandle.onFrame` returns `void` — there is no unsubscribe — so an inert callback stays
registered while the extension is active. The *purpose* of FR-039 (no permanent redraw loop) is
fully satisfied, because an inert callback requests no redraws. If implementation shows the guard
is insufficient, the fix is an additive `onFrame` → unsubscribe in **specs/051** (wired to the
`frameSubscription` contribution kind that already exists and nothing currently produces), recorded
as an **SC-009 finding against specs/051** rather than absorbed here.

## Rendering constraints carried from the reference implementation

Each of these was discovered the hard way by the prototype and is binding on the implementation.

| Constraint | Rule | Source |
|---|---|---|
| Buildings cast but are not drawn | `colorWrite: false`, `depthWrite: false`, `castShadow: true`; developer toggle re-enables `colorWrite` | research D13 |
| Ground offset | `group.position.z = groundOffsetMetres` on the extension's own group — **never** re-anchor the scene | research D15 |
| Shadow frustum vs ground plane | Both derived from the analysis radius; ground plane strictly inside the frustum, or a fake grey blob appears | research D16 |
| Arcs are tubes | `TubeGeometry`/`CylinderGeometry`, never `THREE.Line` — WebGL draws lines at ~1 px regardless of width | research D17 |
| No `scene.environment` | The scene is shared and framework-owned; the prototype's glass dome shell is dropped rather than reaching for global state | research D17 |

Concrete shadow values, following the prototype: frustum `±(radius × 1.3)`, ground plane
`(radius × 2.4)` square, `shadow.mapSize 2048²`, `shadow.bias -0.0006`, `PCFSoftShadowMap`,
directional light `0xfff2d0` at intensity `2.6`, ambient `0.7`. A test asserts the ground plane's
half-extent is strictly less than the frustum half-extent (FR-018).

## Drawing requirements (FR-038)

Declared, never set:

| Requirement | Why |
|---|---|
| `shadows` | The feature's substance — buildings casting onto ground and each other (FR-016). |
| `toneMapping` | Keeps the ACES filmic treatment active while lit 3D geometry is on screen. |

The extension never assigns `renderer.shadowMap.enabled` or `renderer.toneMapping`. Enabling
shadows is globally visible and is treated as its own reviewed step (research D6).

## Following the site (FR-042)

On site change: recompute time zone, sun path and buildings for the new site; re-key corrections;
discard any in-flight building response whose site key no longer matches. The analysis **follows**;
it closes only when the viewer has no site at all. It never continues against content that has gone.
