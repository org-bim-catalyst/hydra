# Viewer Platform — Spec Outlines 050–052

**Created:** 2026-09-12
**Status:** Superseded — all three have since been written as full specs. Kept as the record of how the split was decided and which options were rejected.

- 050 → `specs/050-viewer-extension-framework`
- 051 → `specs/051-viewer-scene-content-api`
- 052 → `specs/052-solar-analysis`

Where a spec and this outline disagree, the spec wins.

Companion to `specs/049-panel-content-model` (written). Reference material: `docs/APS_VIEWER.md`, and the user's working sun-path/OSM-shadow prototype.

---

## Why four specs

| Spec | Delivers | Primary risk |
|---|---|---|
| 049 | Panel content model | Low — self-contained, no framework dependency |
| 050 | Extension framework | **Regression** — migrates 5 shipped capabilities |
| 051 | Scene & content API | Design — the contract everything later depends on |
| 052 | Solar analysis | Scope — the first real feature, easy to over-build |

The ordering is deliberate. 049 settles the panel model *before* 050 repackages it, so the migration moves a finished thing. 050 lands while only five capabilities need moving — the migration gets more expensive every spec we delay it. 051 grows the engine once the extension contract exists to consume it. 052 proves all three.

Two of these carry a **visual regression event** that needs explicit sign-off rather than quiet inclusion:

- **050** — moving five shipped capabilities behind a new lifecycle.
- **051** — enabling shadow maps and tone mapping changes how the already-tuned magma-glow and boundary-highlight layers render (`728b89f`, `dbf4c73`, `5d59cc9`, `0821559`). Before/after comparison required.

---

## 050 — Viewer Extension Framework

**Purpose.** A stable contract by which a viewer capability is packaged, registered, loaded, and torn down — so the viewer core knows none of them by name.

**Why separate from 051.** 050 is about *who owns what and when it loads*. 051 is about *what the viewer can do*. The five capabilities being migrated need only today's command surface, so the migration does not have to wait for the engine to grow. The contract must be designed as an extensible surface, because 051 extends it.

### User stories

- **US1 (P1) — Capabilities load as independent extensions.** The viewer behaves identically; underneath, each capability is a separately loadable unit. This is the migration, and it carries all the regression risk.
- **US2 (P2) — A new capability ships without touching the core.** Verified by adding a trivial extension that contributes an overlay, a live panel kind and a toolbar entry.
- **US3 (P2) — Extension failures are visible and contained.** One fails; the user is told which, the viewer opens, everything else works.
- **US4 (P3) — A user-toggleable capability turns on and off.** The activate/deactivate axis, distinct from loaded/unloaded.

### Requirement groups

1. **Extension contract** — stable identity; start and stop; optional activate/deactivate with declared modes; declared metadata (display name, description, toggleable, load-on-start).
2. **Registry and loader** — register once by identity; load by identity; duplicate identity is a configuration error, not a silent replacement; unknown identity is visible; loading an already-loaded extension or stopping an unstarted one is a no-op.
3. **Extension context** — what an extension receives on start: the published viewer command and event surface, plus contribution helpers. Extensions reach the viewer only through this.
4. **Contributions and teardown** — an extension may contribute overlays, live panel kinds (from 049), and **viewer-embedded toolbar entries**. Every contribution is tracked and withdrawn in full on stop, including event subscriptions. Teardown is enforced by the framework, not left to author discipline.
5. **Readiness ordering** — an extension may start before the render target exists. The contract needs the equivalent of APS's `onToolbarCreated`: a hook fired when a contribution host becomes available, or immediately if it already is.
6. **Host** — `ViewerSurface` starts a declared set and otherwise references no individual capability.
7. **Failure isolation** — start failure is visible and names the capability, others continue; stop failure is surfaced and does not block the rest; the viewer stays usable while extensions start and never blocks indefinitely on one.
8. **Migration** — the four capabilities `ViewerSurface` mounts: floating panel host, POI markers, site boundary confidence badge, site boundary overlay. No behaviour change; existing specs/028 and specs/042 coverage passes. **The boundary overlay is sequenced last**, so it moves only once the contract is proven by three easier migrations — it is the capability with the most post-release history (specs/042 bug-fix rounds, the specs/044 regression).

   Not migrated here: the weather widget, marker style selector and rotation toggle, which are mounted by `ChatPage`, not `ViewerSurface`. Moving those is really a question about where viewer controls live, which the toolbar decision settles — deferred to 051 or a small follow-up.

### Key entities

Viewer Extension · Extension Manifest · Extension Registry · Declared Extension Set · Lifecycle State · Contribution Ledger · Viewer Toolbar

### Decisions taken

- **Viewer toolbar — declared in 050, built when first needed.** The contract states that an extension may contribute a toolbar entry, and 050 implements a minimal host for it inside the viewer container (unrelated to the specs/041 ribbon). None of the four migrated capabilities has a button, so the surface is not designed in detail here; 052's solar extension is the first real consumer and drives that design. This avoids both building an elaborate surface with no consumer and changing the contract one spec after writing it.
- **Migration scope — `ViewerSurface`'s four only**, boundary overlay last. See requirement group 8.

### Open decisions

- **Is the declared set static?** Assume yes — fixed in the application, not per-user, no extension-manager UI.
- **Does the contribution ledger cover GPU resources?** Not yet — nothing in 050 draws. The ledger must be designed so 051 can extend it there.

---

## 051 — Viewer Scene & Content API

**Purpose.** Grow the viewer's published surface from a map-shaped command set into one that can host georeferenced 3D content — so extensions can draw, and Lucy can load and control what is shown.

**Why it is the pivotal spec.** Everything in 052 depends on the constraints settled here, and getting them wrong is expensive to undo. The four hard constraints below are not obvious from the code and were found by analysing the prototype against `GoogleMapsGisLayer`.

### The four constraints this spec must encode

1. **One anchor, framework-owned.** The camera matrix derives from a single `fromLatLngAltitude`. Extensions never set an anchor; they place geometry in local ENU metres via a published helper. `GoogleMapsGisLayer` has already been bitten by stale-anchor placement.
2. **ENU, not Three.js Y-up.** X=East, Y=North, Z=Up. Encoded once, in the coordinate helpers.
3. **Framework-owned render scheduling.** Extensions get `invalidate()`; nothing else touches redraw. Today both the prototype and the GIS layer run a permanent redraw loop that the prototype's own comment warns desyncs the map camera.
4. **Renderer state is global.** Extensions *declare* what they need (shadows, tone mapping, colour space); the framework applies it. Never mutate the renderer directly.

### User stories

- **US1 (P1) — Lucy loads, replaces and clears viewer content.** The API-driven ask. The map still auto-loads at startup; Lucy can replace it.
- **US2 (P1) — An extension draws georeferenced 3D content.** Scoped scene access with correct placement.
- **US3 (P2) — The user selects an element and sees its properties.** This is what makes 049's clickable element table real.
- **US4 (P2) — The viewer stays responsive with several extensions drawing.** Render scheduling and resource lifecycle.
- **US5 (P3) — Content that fails to load says so.** Replaces the empty surface with an explanation.

### Requirement groups

1. **Content commands** — load, replace, unload and list georeferenced content; stable content identity; startup auto-load preserved.
2. **Georeferencing** — framework-owned anchor; published latLng↔local conversion; altitude; per-model anchor, rotation and scale.
3. **Scene access** — each extension receives an isolated scene container, never the shared root.
4. **Renderer capabilities** — declared, not mutated; enabling shadows and tone mapping is an explicit, reviewed visual change across the existing scene.
5. **Render scheduling** — `invalidate()` with coalescing; frame callbacks; no extension-owned loop.
6. **Camera state and events** — read position, tilt, heading; emit camera-change events. None exist today, and the level widget needs them.
7. **Selection and properties** — pick an element, get a stable identifier, read its metadata. Extends 049's action allowlist.
8. **Resource lifecycle** — geometries, materials, textures and render targets released on unload and on replace.
9. **Failure surfaces** — load failure, unsupported format, missing or invalid georeferencing, all visible.

### Key entities

Viewer Content · Georeference (anchor, altitude, rotation, scale) · Scene Container · Renderer Capability Declaration · Frame Subscription · Element (identity + properties) · Camera State

### Open decisions

- **Formats in v1.** Recommend glTF only — single modern format, well-specified, carries metadata in `extras`. OBJ later, and only if something needs it.
- **Where does content come from?** Presumably the existing file management engine and its signed URLs, rather than arbitrary addresses.
- **Where does element metadata come from?** Embedded in the model, or a sidecar the platform stores? Affects how Lucy gets the element list for a table.
- **Does the shared scene need a draw-order contract?** Probably, once more than one extension draws. May be deferrable to 052.

---

## 052 — Solar Analysis Extension

**Purpose.** The first real extension, and the proof that 050 and 051 are sufficient. Sun path, real building shadows, time scrubbing over a site.

**Source material.** The user's working prototype is a complete functional reference. It is a *specification of requirements*, not code to port — it creates its own `WebGLOverlayView`, which the real implementation must not do.

### User stories

- **US1 (P1) — See the sun's position and path** for a site and date: azimuth, altitude, sunrise, sunset, day length, and the path dome.
- **US2 (P1) — See real building shadows** at a chosen moment, cast by actual footprints around the site.
- **US3 (P2) — Scrub time and watch shadows move**, with the readout tracking live.
- **US4 (P2) — Correct building height and ground offset** where the source data is wrong or missing.
- **US5 (P3) — Ask Lucy to run and explain the analysis** for the active site.

### Requirement groups

1. **Solar position** — azimuth, altitude, sunrise, sunset, day length for a site and moment, to a stated accuracy.
2. **Sun path visualisation** — day arc, monthly arcs, solstice and equinox references, hour marks, compass base.
3. **Building geometry** — footprints within a radius of the site; extrusion by height from source tags with a documented fallback; identification of the primary building.
4. **Shadows** — directional light along the true sun vector; casting and receiving geometry; shadow extent sized to the analysis area. (The prototype documents a real failure here: geometry outside the shadow camera frustum reads as a fake grey blob.)
5. **Time control** — date selection, time scrub, playback, live readout.
6. **Overrides** — building height and ground offset, with the source of each height visible.
7. **Panels** — a live time-control panel and a live building panel; the solar readout is content per 049.
8. **Lucy capability** — run analysis for the active site, present results, explain them.
9. **Failure surfaces** — building data unavailable, no buildings found, site outside coverage.
10. **Stated limits** — this is a design-stage study tool, not a certified analysis. The accuracy boundary must be stated in the product, not just the spec.

### Open decisions

- **Timezone.** The prototype is UTC-only, which will feel wrong to users. Recommend site-local time, with the solar math unchanged. Needs deciding before the UI is designed.
- **Building data path.** Fetch server-side, reusing the specs/042 Overpass infrastructure — not from the browser. Gives caching, rate limiting, and treats third-party geometry as untrusted at the right boundary. Note the documented short-timeout failure mode on site4now.
- **Quantitative results?** Hours of sun per surface, irradiance, overshadowing compliance — all much larger features. Recommend v1 is visual and qualitative only, with quantitative analysis as a later spec.
- **Where does the solar math run?** Client, for scrub responsiveness. Lucy's explanation may need the same values server-side — decide whether to duplicate or to have the client report them.

---

## If a spec proves too big

Likely split points, in the order I would use them:

1. **051 → content API and scene API.** Model loading and selection/properties could ship separately from scene access, render scheduling and renderer capabilities. Extensions need the scene half; Lucy needs the content half.
2. **052 → sun path and shadows.** Sun path with a readout is useful alone and needs no building geometry; shadows need OSM ingestion, extrusion and the shadow pipeline.
3. **050 → framework and migration.** Build the framework, migrate only the panel capability, and move the remaining three in a follow-up. Only worth doing if the migration proves riskier than expected — a half-migrated viewer leaves two patterns for the same thing, which is worse than either endpoint.

---

## Where viewer capabilities live today

Recorded because it is not obvious and it shaped the 050 scope decision.

| Mounted by | Capability |
|---|---|
| `ViewerSurface` | Floating panel host · POI marker overlay · Site boundary overlay · Boundary confidence badge |
| `ChatPage` | Location weather widget · Marker style selector · Rotation toggle |

Things that look like viewer controls are page-level chrome positioned over the viewer. There is no viewer-owned control surface at all — which is why the toolbar in 050 is an invention rather than an extension of something existing.
