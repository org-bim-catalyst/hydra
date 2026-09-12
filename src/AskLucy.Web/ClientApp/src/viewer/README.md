# Viewer Engine

The extensible viewer platform behind the Flumeria workspace (specs/027-immersive-viewer-platform).
See `specs/027-immersive-viewer-platform/` for the full spec, plan, research, data model, and
contracts this package implements — this file is a quick orientation, not a duplicate of those.

## Layout

- `api/` — the typed contracts: `layers.ts` (`RenderLayer`), `commands.ts` (`ViewerCommand`/
  `ViewerCommandResult`), `events.ts` (`ViewerEvent`), `engine.ts` (`IViewerEngine`). Mirrors
  `contracts/viewer-engine-api.md` exactly.
- `engine/` — `ViewerEngine` (the facade implementing `IViewerEngine`), `viewerEventBus.ts`
  (pub/sub), `viewerEngineInstance.ts` (the shared singleton), `PlaceholderRenderTarget.tsx`/
  `ViewerFallback.tsx`/`MapRenderTarget.tsx` (the three things the viewer can currently show).
- `camera/` — isometric/plan view-mode application and continuous-rotation driving, applied to
  whichever real render target is active.
- `layers/gis/` — `GoogleMapsGisLayer.ts`, bridging a Google Maps `WebGLOverlayView` to a
  Three.js scene (research.md Decision 3).
- `layers/model/` — reserved for future model/drawing content (contract-only today).
- `selection/` — `resolveSelection.ts`, the deterministic overlap-resolution rule.
- `overlays/` — the `Overlay` type alias for `RenderLayer`s with `kind: 'overlay'`.
- `store/` — `viewerEngineStore.ts`, the session-scoped Zustand store `ViewerEngine` reads/writes.

## Using the viewer from a future AI-agent integration

Everything an agent needs is `viewerEngine` (`engine/viewerEngineInstance.ts`) and the types in
`api/`. Every command returns a `ViewerCommandResult` — check `.ok` before trusting `.data`, and
never assume a command throws instead of failing gracefully (it won't). Subscribe to events with
`viewerEngine.on(type, handler)` (returns an unsubscribe function) rather than polling state.

```ts
import { viewerEngine } from './engine/viewerEngineInstance'

const result = viewerEngine.addLayer({ kind: 'gis', metadata: { center: { latitude, longitude } } })
if (!result.ok) {
  // handle result.error — never assume success
}

const unsubscribe = viewerEngine.on('selectionChanged', (event) => {
  console.log('Selection changed:', event.layerId, event.elementId)
})
```

## Commands

| Command | Signature | Notes |
|---|---|---|
| `addLayer` | `(layer: RenderLayerInput) => ViewerCommandResult<{ layerId: string }>` | Fails on a duplicate id. |
| `removeLayer` | `(layerId: string) => ViewerCommandResult` | Fails if the layer doesn't exist. |
| `setLayerVisibility` | `(layerId: string, visible: boolean) => ViewerCommandResult` | Fails if the layer doesn't exist. |
| `zoomToLocation` | `(latitude: number, longitude: number, zoom?: number) => ViewerCommandResult` | Fails on out-of-range coordinates; succeeds as a no-op if no real content is active. |
| `setViewMode` | `(mode: 'isometric' \| 'plan') => ViewerCommandResult` | Always succeeds; no visible effect on the placeholder (FR-013). |
| `setRotationEnabled` | `(enabled: boolean) => ViewerCommandResult` | Always succeeds; no visible effect on the placeholder (FR-017). |
| `select` | `(layerId: string, elementId: string) => ViewerCommandResult` | Fails unless the element was registered via `registerSelectableElement` by the layer that owns it. |
| `clearSelection` | `() => ViewerCommandResult` | Always succeeds. |
| `displayContent` | `(layerId: string, content: unknown) => ViewerCommandResult` | Fails if the layer doesn't exist, or `content` is null/undefined. |
| `createOverlay` | `(overlay: OverlayInput) => ViewerCommandResult<{ overlayId: string }>` | Fails on a duplicate id. |

## Events

`layerAdded`, `layerRemoved`, `contentLoaded`, `selectionChanged`, `viewModeChanged`,
`rotationChanged` — see `api/events.ts` for exact payload shapes.

## Panels (`panels/`)

The floating panel framework (specs/028), reshaped by specs/049 into a content model. See
`specs/049-panel-content-model/` for the full spec, plan, research, data model and contracts.

- `content/` — the panel content vocabulary. `blocks.ts` defines each block kind (`heading`,
  `text`, `keyValue`, `table`, `chart`, `metric`, `image`, `divider`) as a zod schema, plus the
  loose document envelope (`panelContentSchema`) that gates a whole document without deep-checking
  every block — that split is what lets one malformed block degrade individually instead of
  failing the entire panel. `ContentRenderer.tsx` renders an ordered block sequence; `blockRegistry.ts`
  is its internal kind→renderer map. Presenting a new kind of content Lucy has never shown before
  requires composing a different sequence of these blocks — never new code.
- `actions/` — the closed action allowlist (`allowlist.ts`) a block or block entry may invoke.
  Every command is an explicit, written-out mapping onto the viewer's published `IViewerEngine`
  surface — never dynamic dispatch by a content-supplied string, because panel content is composed
  by a language model and constitution §8 treats that as untrusted input. `ActionAffordance.tsx` is
  the shared presentation every actionable entry renders through; an action that fails validation
  is rendered inert, never merely refused on click.
- `chrome/` — `PanelChrome` (title bar / resizable / default size) and `resolveChrome`, applying a
  request's override on top of a base chrome and clamping to the minimum usable size.
- `registry.ts` — narrowed by specs/049 to hold only **live panel kinds**: panels whose content is
  code rather than data (continuous state, an owned drawing surface, or values flowing back into
  them live). No content panel needs registration. Nothing registers here today — the four
  built-in kinds this registry used to hold at import time became content blocks instead.
- `store/floatingPanelStore.ts` — owns every open panel's lifecycle: cascade placement, z-order,
  the fixed-cap LRU eviction, minimize/restore, viewport clamping, and the two viewer-context
  subscriptions. Branches on a request's `kind` (`content` vs `live`) at construction; everything
  after that is common to both.

Two capabilities give Lucy access to panels: `present_panel_content` (always available — composes
content freely from the vocabulary) and `open_live_panel` (available only while something has
registered a live kind; ships with no consumer in this feature).

## Extensions (`extensions/`)

The extension framework (specs/050) — how the viewer scales up its capabilities, modeled loosely
on Autodesk Platform Services viewer extensions (see `docs/APS_VIEWER.md`) but built framework-side
rather than author-side, since that reference implementation's own samples leak panels and event
listeners on unload. See `specs/050-viewer-extension-framework/` for the full spec, plan, research,
data model and contracts.

- `ViewerExtension.ts` — the contract: `id`, `manifest` (`displayName`, `description`,
  `toggleable?`, `startsWithViewer?`), `start(context)`/`stop()`, and an optional
  `activate(mode?)`/`deactivate()` pair for a `toggleable` extension. A factory function, never a
  base class to extend — there is no is-a relationship to model.
- `context.ts` — `createExtensionContext(id)`, an extension's **only** route to the viewer. Passes
  the existing `viewerEngine` through unchanged, plus tracked helpers — `on()`, `contributeOverlay`,
  `contributeToolbarEntry`, `registerLivePanelKind`, `openPanel` — that record what they did against
  the calling extension's id, so `stop()` can withdraw everything without the author's cooperation.
  `openPanel` is the one deliberate exception: not tracked, because a panel the user can close is
  theirs, not the extension's.
- `loader.ts` — starts/stops declared extensions by id. Contains every failure (a thrown/rejected
  `start()`, a timed-out `start()`, a thrown `stop()`, an unknown declared id) so one extension's
  problem never blocks another's, and enforces the idempotency rules a double-invoked React 19
  Strict Mode effect actually exercises: start-when-started and stop-when-not-started are no-ops,
  stop-while-starting wins and discards whatever the in-flight start contributed.
- `registry.ts` — the catalogue of extensions known to the application, mirroring `panelTypeRegistry`'s
  posture: throws on a duplicate id in development, resolves an unknown id to `undefined` rather
  than throwing.
- `declared.ts` — `DECLARED_EXTENSIONS`, the ordered list of extension ids the viewer starts when it
  opens. Changing what the viewer does means changing this list, not the viewer.
- `store/viewerExtensionStore.ts` — per-extension lifecycle/activation state and the flat,
  insertion-ordered list of live contributions every host renders from.
- `components/` — `ExtensionOverlayHost` (renders every contributed overlay), `ExtensionToolbar`
  (the **viewer-embedded** toolbar — distinct from the workspace overlay's page-level controls
  outside the viewer; renders nothing when empty), `ExtensionFailureNotice` (names every currently
  failed or erroring capability by its manifest `displayName`, reusing the panel hub's Chip pattern
  rather than a new notification system).
- `builtin/` — the four migrated capabilities (`panelsExtension`, `poiMarkerExtension`,
  `boundaryConfidenceExtension`, `siteBoundaryExtension`). Each contributes its existing,
  unchanged component; `ViewerSurface.tsx` names none of them.

Adding a new capability costs one module (a `start()` that calls a few `context.contribute*` calls)
plus one id in `declared.ts` — nothing else changes, including `ChatPage` and `WorkspaceOverlay`.

See `specs/050-viewer-extension-framework/quickstart.md` for the full manual verification walkthrough.

## Scene, content and elements (`scene/`, `content/`, `elements/`)

The viewer scene and content API (specs/051) — grows the published command surface from a
map-shaped set into one that owns georeferenced 3D content: content Lucy can load/replace/clear, a
single reference point every capability positions against, isolated drawing spaces extensions
render into, and viewer-owned redraw scheduling. See
`specs/051-viewer-scene-content-api/` for the full spec, plan, research, data model and contracts.

- `scene/SceneAnchor.ts` — the one published `ReferencePoint` (FR-008). No capability sets it
  directly; it is set once, by the engine, when the first content loads.
- `api/coordinateFrame.ts` — `worldToLocal`/`localToWorld`, the one published conversion between
  real-world coordinates and the viewer's local positioning space. **ENU convention: X = East,
  Y = North, Z = Up** — stated once here, referenced everywhere else. Maps directly onto this
  scene's own Three.js axes (a property of the map bridge's own camera setup, not a general
  Three.js fact) — a capability never needs a separate remapping step.
- `scene/DrawingSpaceRegistry.ts` — issues each drawing capability its own isolated `THREE.Group`
  ("Drawing Space") via `context.acquireDrawingSpace()`, appended to the one shared scene in
  acquisition order (draw order, FR-015). Contains any exception a capability's `onFrame` callback
  throws (`drawingCallbackFailed`, constitution §2.VIII) so one capability's failure never stops
  another's callback or the render loop. Withdrawn automatically on extension stop — an author
  never calls `release()` themselves.
- `scene/RedrawScheduler.ts` — `invalidate()` is the *only* sanctioned way any capability (or the
  viewer itself) requests a redraw. Coalesces every call arriving before the next frame into
  exactly one underlying redraw, and does nothing when nothing has changed — fixes a real bug
  where the map bridge previously redrew unconditionally on every single frame.
- `scene/rendererState.ts` — resolves the union of every capability's declared drawing
  requirements (`'shadows'`, `'toneMapping'`) onto the one shared renderer; a capability never
  touches `renderer.shadowMap.enabled`/`toneMapping` itself. Conflicts are reported
  (`drawingRequirementConflict`), never silently decided.
- `scene/activeScene.ts` — the one live `THREE.Scene` the map bridge owns, published so
  viewer-owned content (not capability-owned drawing spaces) has somewhere to be added.
- `content/ViewerContent.ts` — `ContentSource` (`'gis'` | `'model'`), `WorldPlacement`
  (location/height/orientation/scale), and the `ViewerContent` load-state machine
  (`loading` → `loaded` | `failed`, with a closed `ContentFailureReason` set).
- `content/contentStore.ts` — tracks every currently-loaded `ViewerContent`, session-scoped like
  every other viewer store.
- `content/loaders/gltfContentLoader.ts` — the one supported 3D format (glTF), read through the
  platform's existing signed-URL file access mechanism; reads each node's `extras` into the shared
  element index (`elements/elementIndex.ts`).
- `elements/elementIndex.ts` — `elementId → properties`, built when content loads. Reports
  `hasProperties: false` explicitly for an element with none, never an empty object.
- `elements/resolveElementOverlap.ts` — deterministic overlap resolution for elements at the same
  screen point: highest drawing-space (acquisition) order wins, then nearest to the camera within
  one space. Mirrors `selection/resolveSelection.ts`'s own convention exactly.
- `engine/ViewerEngine.ts` gains `loadContent`/`replaceContent`/`unloadContent`/`listContent`,
  `getReferencePoint`/`getCameraState`, `getElementInfo`/`selectAndFrame`, and `invalidate` —
  additive only; every command that existed before this feature keeps its exact meaning
  (`ViewerEngine.contract.test.ts` is the evidence).

Two gaps are recorded, not silently skipped: the one-time renderer color/lighting treatment's
visual before/after review, and the multi-capability frame-rate comparison, both need a human with
a live, GPU-capable browser — see `specs/051-viewer-scene-content-api/quickstart.md` Scenarios 5
and 9.

## Manual verification (no AI agent required)

In a development build, the running engine is exposed as `window.__askLucyViewerEngine` — open
devtools on `/studio` and try:

```js
window.__askLucyViewerEngine.setViewMode('plan')
window.__askLucyViewerEngine.select('gis-current-location', 'current-location')
```

See `specs/027-immersive-viewer-platform/quickstart.md` Scenario 5 for the full walkthrough.
