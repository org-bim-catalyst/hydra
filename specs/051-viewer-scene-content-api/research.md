# Phase 0 Research: Viewer Scene and Content API

**Feature**: `051-viewer-scene-content-api` | **Date**: 2026-09-12

All Technical Context unknowns are resolved below.

---

## D1 — What "Viewer Content" is, relative to the existing `RenderLayer`

**Decision**: `ViewerContent` is a new, richer wrapper around the existing `RenderLayer`
mechanism, not a replacement for it. Loading content still results in a `RenderLayer` existing
(so `MapRenderTarget` and a future model render target need no new protocol), but the content
module adds what `RenderLayer` never had: a load state machine (`loading` → `loaded` | `failed`),
format validation, world placement, and the load/replace/unload/list commands FR-001 requires.
`ViewerSurface`'s startup effect calls `engine.loadContent({ kind: 'gis', ... })` once instead of
`engine.addLayer(...)` directly — the map becomes the first, default piece of content, loaded by
the viewer itself rather than by Lucy, exactly as FR-003 requires. Subsequent location updates
keep using the existing `zoomToLocation`/`fitBounds` camera commands unchanged — moving the camera
is not replacing content.

`loadContent`/`replaceContent` apply the full `WorldPlacement` — position (via `worldToLocal`, D2)
**and** `orientationDegrees`/`scale` — to the loaded content's root transform (FR-011). Position
alone is not sufficient: a model loaded with no rotation/scale applied would satisfy "positioned"
but not "positionable by... orientation and scale," which FR-011 states as one requirement, not
two independent ones.

**Rationale**: `RenderLayer` already gives every render target (`MapRenderTarget`, a future
`ModelRenderTarget`) a stable id/visibility/z-index contract; rebuilding that from scratch would
duplicate working code for no reason (Principle III). What `RenderLayer` cannot express — "this is
still loading," "this format isn't supported," "this failed and here's why" — is exactly the gap
FR-001/FR-005/FR-007/FR-035 describe, and belongs one layer up, not folded into the render-target
handshake every layer kind must implement.

**Alternatives rejected**:
- *Extend `RenderLayer` itself with load-state fields* — every consumer of `RenderLayer` (the
  store, `MapRenderTarget`) would need to understand load states that only content loading
  produces; `createOverlay`'s overlay layers have no load state at all. Keeping content as a
  wrapper keeps `RenderLayer` exactly as simple as it already is.
- *Replace `RenderLayer` outright* — would touch every existing consumer (`MapRenderTarget`,
  `viewerEngineStore.layers`, `createOverlay`) for no functional gain; FR-038/FR-039 explicitly
  forbid redefining what already exists.

---

## D2 — The single reference point and the published coordinate conversion (FR-008–FR-013)

**Decision**: One `SceneAnchor` module owns the viewer's single real-world reference point
(latitude/longitude, no altitude component needed — altitude is a `worldToLocal` call argument,
not part of the reference point itself). It publishes `worldToLocal(latLng, altitudeMetres) →
{ x, y, z }` and `localToWorld(...)`, built on the exact same equirectangular-projection formula
`GoogleMapsGisLayer.ts`'s own private `toLocalMeters` helper already used — one conversion, one
implementation, generalized from a single-file helper into the published surface every capability
draws through.
**Correction during implementation**: this decision originally proposed building the conversion on
`transformer.fromLatLngAltitude` — the matrix `GoogleMapsGisLayer.onDraw` computes for the
*camera's* projection every frame. That function is only reachable inside Google's own `onDraw`
callback (it is a parameter *of* that callback, not a standalone API), so a general-purpose
`worldToLocal` usable from anywhere cannot call it directly. The existing `toLocalMeters` equirectangular
math — already real, working code in this file, used for the site-boundary ring's positioning — is
the actual shared implementation; the outcome (one published conversion, ENU convention, no
duplicate implementations) is unchanged.

**The ENU convention** (FR-010), stated once in `api/coordinateFrame.ts`'s doc comment and nowhere
else: **X = East, Y = North, Z = Up**. **Correction during implementation**: this decision
originally assumed Three.js's own default scene convention would need adapting to ENU. Checking
the existing `SiteBoundaryRenderer`/`AnimatedBorderHighlight` code (the only precedent already
drawing into this scene) found it places a `{x,y}` local point straight into
`new THREE.Vector3(x, y, 0)` with no remapping — a property of how `WebGLOverlayView`'s
`transformer.fromLatLngAltitude` camera matrix sets up the local tangent plane (Google's own
documented recipe), not a general Three.js fact. ENU maps directly onto this scene's own axes; no
adaptation step exists or is needed. The outcome (one published convention, stated once, every
capability draws in it) is unchanged — only the internal "how" was corrected.

**Fixes a real existing bug**: `GoogleMapsGisLayer.ts` currently re-anchors its own
`sceneAnchor` to a site boundary's centroid on every `setSiteBoundary()` call — a second,
capability-specific reference point living inside one layer file, exactly the "no capability may
set its own" violation FR-008 forbids. `SceneAnchor` becomes the *only* place a reference point is
set; `setSiteBoundary` no longer touches it. This is deliberately a design correction, not just a
new API layered over the old behavior — the old behavior is what specs/044 already had to fix a
regression around once (per this repository's own history), and FR-012 ("content MUST remain
correctly positioned... when the viewer's reference point changes") is the guarantee that makes
future changes like it safe.

**Alternatives rejected**:
- *Let each capability compute its own local-meters offset, as `GoogleMapsGisLayer.ts` and the
  backend's `GeometryMath.ToLocalMeters` already independently do* — this is the exact duplication
  FR-009 exists to end. Two independent equirectangular-projection implementations are already a
  drift risk documented in that file's own comments.
- *Adopt Three.js's raw scene-space convention as the published one* — would leak an
  implementation detail of the current map-bridge approach into every capability's mental model,
  and would need re-deriving if the render-target implementation ever changes.

---

## D3 — Drawing spaces and isolation (FR-014–FR-018)

**Decision**: Each drawing capability receives its own `THREE.Group` — a **Drawing Space** — added
once to the one `THREE.Scene` the map bridge already owns. A capability only ever adds/removes
children of its own group; it never receives the `Scene`, `Camera`, or `WebGLRenderer` directly.
`DrawingSpaceRegistry.acquire(extensionId)` returns the group and records it (mirroring specs/050's
contribution-tracking pattern — an untracked handle here would repeat the exact leak class D052
found and fixed in `viewerExtensionStore`'s own withdrawal path); `release(extensionId)` removes
the group from the scene and disposes every geometry/material/texture attached to its subtree.

**Draw order** (FR-015): groups are appended to the scene in the order their drawing space was
acquired — the same "insertion order is the order" convention specs/050 already uses for toolbar
entries and contributions, so there is exactly one ordering rule in this codebase, not two.

**Drawing requirements, not drawing settings** (FR-016/FR-017): a capability calls
`context.declareDrawingRequirement('shadows')` (or `'toneMapping'`, if a future capability needs a
different tone curve than the one-time treatment below settles) rather than touching
`renderer.shadowMap.enabled` itself. `rendererState.ts` resolves the *union* of every currently
declared requirement onto the one shared renderer — a requirement, once declared by any active
capability, stays applied until every capability that declared it has stopped — and every
resolution (including a conflict, e.g. two incompatible tone-mapping requests) is reported through
the same `ExtensionFailureNotice` surface specs/050 built (D9), never silently decided.

**Rationale**: This is precisely the isolation guarantee specs/050's context already established
for overlays/toolbar entries/panels — "an extension never gets a raw handle it could use to break
another extension's contribution" — extended to the one resource type specs/050 explicitly left
out of scope for being "a feature ahead of schedule" (specs/050 contracts/extension-context.md,
"What is deliberately absent"). This feature is exactly the schedule that was waiting for.

**Alternatives rejected**:
- *A single shared group everyone draws into* — makes FR-014's "MUST NOT be given the ability to
  alter another capability's content" false by construction; any capability could remove another's
  object by reference.
- *Give each capability its own `THREE.Scene`, composited via multiple render passes* — real
  isolation, but multiplies render passes for no isolation benefit `THREE.Group` doesn't already
  provide at a fraction of the cost, and conflicts with there being exactly one `WebGLRenderer`
  bound to the map bridge's single WebGL context (FR-014's actual constraint is "don't let one
  capability corrupt another's state," not "give each its own renderer").

---

## D3a — Containing a capability's drawing failure (FR-019, constitution §2.VIII)

**Decision**: `DrawingSpaceRegistry` invokes every `onFrame` subscriber callback (and any other
drawing-space code it calls on a capability's behalf) inside a try/catch. A thrown callback is
recorded and emitted as a `drawingCallbackFailed` event, routed through the same
`ExtensionFailureNotice` surface D9/specs/050 already established — never left to propagate and
stop the render loop or another capability's own callback in the same frame.

**Rationale**: Identified during `/speckit-analyze`'s review of this feature's own plan: FR-019 and
this spec's own Edge Cases ("a capability's drawing throws an error mid-frame... must not stop the
rest of the viewer drawing") are the drawing-side twin of specs/050's FR-017, which already
required the same posture for tracked `on()` event handlers. Building the isolation guarantee
(D3) without this containment would leave a capability's own draw code as the one remaining place
in the extension surface where a thrown exception has no defined outcome — a live constitution
§2.VIII (No Silent Failures, NON-NEGOTIABLE) gap, not merely an unbuilt nicety.

**Alternatives rejected**: *Rely on `GoogleMapsGisLayer.onDraw`'s existing outer try/catch* — that
catch exists around the map bridge's own render call, not around each individual capability's
callback; one capability throwing inside it would still prevent every other capability's `onFrame`
callback scheduled in the same pass from running, which is exactly what FR-019 forbids.

---

## D4 — Redraw scheduling (FR-020–FR-024)

**Decision**: `engine.invalidate()` is the *only* way any capability requests a redraw.
`RedrawScheduler` coalesces every `invalidate()` call arriving before the next frame into a single
`overlay.requestRedraw()` call to the underlying `WebGLOverlayView` — a `pending` flag set on the
first call in a window and cleared once that single `requestRedraw()` fires, so ten calls in one
frame produce one redraw (FR-021), and zero calls produce zero (FR-022).

**Fixes a real existing bug**: `GoogleMapsGisLayer.onDraw` currently calls
`overlay.requestRedraw()` unconditionally on every single draw — a continuous redraw loop that
directly violates FR-022 ("MUST NOT redraw continuously when nothing has changed") and is
plausibly part of why this codebase has already documented one GPU-contention incident between two
competing drawing surfaces on this exact page (the sphere/map contention this repository has
already investigated once). This feature routes that call through `RedrawScheduler` instead.

**Per-frame subscriptions** (FR-023): `context.onFrame(callback)` registers a callback invoked
once per actual draw — for content that genuinely animates continuously (the existing
`SiteBoundaryRenderer` comet animation is the first real consumer, migrating from its current
direct `onDraw`-driven update to this subscription). Recorded as a new `frameSubscription`
contribution kind in specs/050's `Contribution` union (extending, not modifying, that closed set —
FR-005/FR-039 compatibility), so it is automatically torn down when the owning extension stops —
which is also what makes FR-024 ("a redraw request from a stopped capability MUST be ignored
safely") true by construction: a stopped extension holds no live subscription to call `invalidate()`
from in the first place, and `RedrawScheduler.invalidate()` itself is a no-op if called with no
registered caller context (defensive, but the subscription teardown is the real guarantee).

**Alternatives rejected**:
- *Leave each capability free to call the map bridge's `requestRedraw()` directly, as today* — this
  is the exact bug being fixed; every capability doing this independently is how the current
  continuous-redraw problem exists at all.
- *A fixed-rate render loop (always redraw at 60fps)* — trivially satisfies "nothing missed" at the
  direct cost of FR-022 and the GPU-contention risk this feature exists partly to close.

---

## D5 — The one-time renderer color/lighting treatment (FR-018, SC-009)

**Decision**: `rendererState.ts` sets `renderer.outputColorSpace = THREE.SRGBColorSpace` and
`renderer.toneMapping = THREE.ACESFilmicToneMapping` once, in `GoogleMapsGisLayer.onContextRestored`
— the one place `WebGLRenderer` is constructed — alongside the existing `renderer.setPixelRatio(1)`
call. This is a genuine visual-regression review, not a formality: the scene today declares no
color-space or tone-mapping handling at all (the Assumptions section's own framing —
"a correctness gap in its own right"), so this is the first time colors drawn into this scene are
being interpreted through a defined pipeline rather than Three.js's legacy default.

**What the review must cover before this is accepted** (recorded in quickstart.md, same honesty
pattern as specs/050's SC-007/T057 gaps): every existing Three.js-drawn visual —
`SiteBoundaryRenderer`'s ring/comet colors (`#9C62DE` high/medium, `#757575` low, per
`GoogleMapsGisLayer.ts`'s `BOUNDARY_STYLE`) — read against both the old (undeclared) and new
(sRGB + ACES Filmic) pipeline, before-and-after, on a real device. **This cannot be performed in
this environment** (no WebGL-capable browser here) — the code change is made and the specific
colors that need visual comparison are named here, but the comparison itself needs a human with a
running build, exactly like specs/050's startup-time measurement did.

**Rationale**: The alternative — leaving color/lighting undeclared — is not neutral; it means every
future capability that draws (specs/052's solar analysis, most concretely) inherits an undefined
visual baseline and has no reviewed treatment to build shadows/lighting on top of. Doing it here,
once, with one small existing capability's colors as the regression surface to check, is cheaper
than discovering the gap mid-way through specs/052.

**Alternatives rejected**:
- *Defer to specs/052* — specs/052 would then be doing a scene-wide visual-regression change
  disguised as a feature-specific one, and every capability that draws between now and then
  inherits the same undefined baseline this feature exists to close.
- *`THREE.NoToneMapping` (i.e., declare nothing)* — technically satisfies "declared," but is
  indistinguishable in practice from the current undeclared state and defers the actual visual
  decision (how bright/saturated the scene should read) to whoever notices it looks wrong later.

---

## D6 — Camera state (FR-025, FR-026)

**Decision**: `engine.getCameraState()` returns `{ latitude, longitude, heading, tilt, zoom }`,
read from the live `google.maps.Map` instance (`map.getCenter()`/`getHeading()`/`getTilt()`/
`getZoom()`) through the existing `ViewerRenderTargetHandle` plumbing — no new per-frame poll.
`cameraChanged` is emitted from the map's own `'idle'` event (fires once movement settles, not per
frame — Google's documented low-churn signal for "the camera stopped moving"), which is already
how a well-behaved Maps integration observes camera settling without flooding the event bus.

**Rationale**: The map bridge already has every one of these values; publishing them is exposing
existing state, not computing anything new. Using `'idle'` rather than `'bounds_changed'`/`'center_changed'`
(which fire continuously during a pan/zoom gesture) keeps `cameraChanged` a meaningful "the camera
moved somewhere and stopped" signal rather than a per-frame firehose a capability would need to
debounce itself.

**Alternatives rejected**: *Emit on every Maps camera event* — reintroduces exactly the
per-frame-churn problem D4 exists to prevent, just on the event bus instead of the redraw path.

---

## D7 — Elements, selection, and framing (FR-027–FR-031)

**Decision**: An `ElementIndex` is built when content loads, mapping `elementId → properties`
(a plain `Record<string, unknown>` read from the content as supplied — glTF node `extras`/`userData`
for a model, or nothing for the GIS layer's existing current-location marker, which carries no
element information and is reported as such per FR-031). `engine.getElementInfo(layerId, elementId)`
returns the properties or a `{ hasProperties: false }` marker — never an empty object standing in
for "nothing to show," which is exactly the failure mode FR-031 forbids.

`engine.selectAndFrame(layerId, elementId)` composes the existing `select()` command with a camera
command: if the element's content carries a `WorldPlacement`, it frames via the existing
`zoomToLocation`/`fitBounds` machinery at that placement; if the element no longer exists
(FR-029's "no longer available" case, US3 AC4), it returns a failed `ViewerCommandResult` instead
of silently doing nothing — this is the one new command FR-032 names as safe to add to specs/049's
action allowlist, since it only reads and re-frames existing content, exactly like `select` and
`zoomToLocation` already do.

**Overlap resolution** (FR-030): deterministic by drawing-space z-order (D3's insertion-order rule)
then, within one drawing space, by the element nearest the camera — the same "defined, stable"
requirement D3 already established for draw order, applied to hit-testing instead of paint order,
so there is one ordering concept in this feature, not two independently-invented ones.

**Alternatives rejected**: *A platform-managed element-properties store, separate from content
itself* — explicitly out of scope (spec Out of Scope) as a distinct future concern; this feature
reads only what content supplies.

---

## D8 — How Lucy loads content (FR-004)

**Decision**: A new backend capability, `LoadViewerContentCapability`, exposed through the exact
same capability-dispatch mechanism specs/049's `PresentPanelContentCapability` and
`OpenLivePanelCapability` already use — `IsAvailable`, an `InputSchemaJson`, and an
`IConversationCapability.ExecuteAsync` implementation. No new dispatch mechanism.

**Correction during implementation**: this decision, and tasks.md's T033, originally described
the handler as "MediatR." Checking the actual mechanism `PresentPanelContentCapability` and
`AdjustViewerFocusCapability` use, neither goes through MediatR at all — a capability is an
`IConversationCapability`/`IAgentTool` invoked directly by `CapabilityExecutor`, and (following
`AdjustViewerFocusCapability`'s exact pattern, since this capability also "emits a command rather
than performing work") its JSON result is picked up by `StructuredPayloadExtractor`, carried on
`ChatStreamChunk`, and written as a trailing `__VIEWER_CONTENT__` SSE event by `AiController` —
which the frontend's `useChatStream.ts` reads and turns into the actual `engine.loadContent(...)`
call. This is the literal "existing SSE/tool-result channel" FR-004 already named; "MediatR" was
an incorrect guess at its mechanics, not requirement text — the actual mechanism is more specific
and already fully proven by `adjust_viewer_focus`/`__ZOOM__`.

**Rationale**: FR-004 says "through the platform's existing capability mechanism" — the mechanism
already exists and already has two working examples in this exact codebase from the immediately
preceding feature. Inventing a second way for Lucy to reach the viewer would violate Principle VII
(Convention over Configuration) for no benefit.

**Alternatives rejected**: *Route content loading through the panel action allowlist instead* —
deliberately rejected by the allowlist's own existing doc comment (`addLayer`/`removeLayer`/
`displayContent`/`createOverlay` are excluded for exactly this reason: content composed by a model
must not destructively mutate viewer content as a side effect of a panel click). Loading content is
a capability-level act Lucy performs deliberately in response to a user request, not a click inside
already-rendered panel content — the same distinction specs/049 already drew.

---

## D9 — How a content/drawing failure reaches the user (FR-035–FR-037)

**Decision**: Reuses specs/050's `ExtensionFailureNotice` Chip pattern exactly — a content load
failure, an unsupported format, an unplaceable content item, and a drawing-requirement conflict all
route through the same store-backed failure surface, extended with a `content`/`drawing` failure
source alongside the existing per-extension lifecycle/event failures it already renders.

**Rationale**: specs/050 already established (research D5) that this codebase has no global toast/
snackbar infrastructure and that inventing one for a failure mode that should be rare is the larger
sin. This feature's failures are a natural extension of the same "something is wrong, name it,
don't interrupt" posture, not a new failure *class* that needs its own presentation.

**Alternatives rejected**: *A new, content-specific failure component* — would duplicate
`ExtensionFailureNotice`'s exact shape for no behavioral difference.

---

## D10 — Compatibility (FR-038, FR-039)

**Decision**: Every method this feature adds to `IViewerEngine` is new; no existing method's
signature, return shape, or emitted-event set changes. `ViewerEngine.contract.test.ts` (specs/027)
is the existing evidence for "no viewer command changed meaning" and must keep passing unchanged —
this feature adds no test of its own for that property, per the same reasoning specs/050 already
recorded for the same file. The four capabilities specs/050 migrated (panels, POI marker, boundary
confidence, site boundary) are re-verified against their existing suites after `GoogleMapsGisLayer.ts`'s
internal changes (D2's re-anchoring removal, D4's redraw routing, D5's renderer state) land, since
those are the one file all four ultimately depend on through the shared map bridge.
