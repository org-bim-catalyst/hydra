# Contract: Solar Scene, Geometry & Shadows

**Feature**: specs/063-solar-accuracy-performance

Covers `buildings/footprintGeometry.ts`, `scene/sunLight.ts`, `scene/shadowGround.ts`,
`scene/sunPathCurve.ts`, `scene/SolarScene.ts` and the extension's playback callback.

**Binding throughout**: renderer-global and scene-global state remain framework-owned (FR-024,
specs/052 FR-038, README constraint 2). Nothing in this contract may set
`renderer.shadowMap.enabled`, `.type`, `.autoUpdate`, `scene.environment`, or call
`sceneAnchor.set(...)`.

---

## `footprintGeometry.ts` — one geometry, one material

**Replaces** `buildFootprintMesh(building, showMass) → Mesh | null` (per building) with a builder
that takes all buildings and returns a single mesh (research D5, FR-015).

**Contract**:
- All footprint extrusions MUST be merged into one `BufferGeometry` with one shared material.
- `castShadow: true`, `colorWrite: false`, `depthWrite: false` semantics are unchanged: buildings
  cast shadows and are not drawn over the basemap's own buildings (FR-017, README constraint 1).
- The developer mass toggle MUST continue to reveal and hide **all** massing (FR-016), now by
  flipping `colorWrite` on the one material.
- A building whose ring is degenerate, self-intersecting, or otherwise unusable MUST be excluded
  from the merge without failing the merge — the existing per-building `null` return becomes a
  skip, and the exclusion MUST remain visible to the user through the existing excluded-count
  path, not silently dropped (constitution §2 VIII).
- An empty building list MUST produce no mesh rather than an empty-geometry mesh.
- The merged geometry MUST expose the extent and tallest-height figures `sunLight.ts` needs to
  derive the shadow radius, so that radius derivation does not re-traverse the source data.

**Disposal**: one geometry and one material to dispose instead of N of each. The existing deep
`disposeGroupContents` in `SolarScene.ts` continues to cover it.

---

## `sunLight.ts` — content-derived radius and a shadow floor

### `SHADOW_FRUSTUM_RATIO` — **removed**

Research D6: it has no derivation. It is the prototype's hard-coded ±260 m restated as a ratio.

### `MIN_SHADOW_ELEVATION_DEGREES: 1.0` — added

**Contract** (research D7, FR-012):
- Above 1° apparent elevation, shadows are cast as today.
- At or below 1°, the directional light is disabled exactly as it already is below the horizon —
  no shadows, no partial shadows, no divergent frustum.
- This window MUST be **stated to the user**, not left looking like a failure. The sun is visibly
  up and no shadows are drawn; that needs a reason on screen (constitution §2 VIII in spirit).

### Shadow radius derivation

```
buildingExtent = max distance from the anchor to any merged footprint vertex
longestShadow  = tallestBuildingMetres / tan(MIN_SHADOW_ELEVATION_DEGREES)
shadowRadius   = buildingExtent + longestShadow
```

**Contract** (FR-008, FR-009, FR-013, FR-014):
- The derived radius MUST accommodate the longest shadow the present geometry can cast at the
  lowest elevation at which shadows are still drawn, so that fitting more tightly to the buildings
  cannot truncate their shadows.
- It MUST remain a **single scalar** feeding both the shadow camera extent and the ground plane.
  The invariant of README constraint 4 is preserved literally; only the input value changes.
- With no buildings present, a stated fallback radius applies (FR-013).
- The region MUST NOT be extended directionally as a function of sun azimuth in this release
  (FR-014a) — that cannot be expressed as one scalar and is deferred pending measurement.
- `near`/`far` continue to bracket the tallest building, as today.

---

## `shadowGround.ts` — unchanged in structure

`GROUND_PLANE_RATIO` continues to derive the ground plane from the same single radius. The existing
`shadowGround.test.ts` assertion — ground half-extent **strictly less than** frustum half-extent for
every tested radius — is the mechanism enforcing README constraint 4 and MUST now pass for
content-derived radii too, including the no-buildings fallback and the tall-lone-building case
(FR-010, SC-007).

---

## `sunPathCurve.ts` — split by lifetime

**Replaces** `buildSunPath(...)` returning one bag with two builders (research D9, FR-021, FR-022).

| Builder | Produces | Depends on |
|---|---|---|
| Fixed furniture | Compass dial, mount post, monthly lattice | Radius; latitude, longitude, year |
| Dated path | Day arc, hour marks, seasonal extremes, current marker | The above, plus date and instant |

**Contract**:
- A date change MUST rebuild only the dated path. The fixed furniture MUST NOT flicker, move or
  change (FR-021, SC-008).
- A site change or a year change MUST rebuild both (FR-022).
- `buildDomeShell` is fixed furniture and **is** assembled into the scene — corrected during
  implementation: this contract originally said the shell was "not assembled", which is not what
  specs/052 shipped. What README constraint 5 actually dropped is the prototype's *glass* dome,
  which read as glass only through `MeshPhysicalMaterial.transmission` against a
  `scene.environment` the extension may not assign (FR-038). Research D17 sanctioned the plain
  transparent replacement that ships today. Classifying it as fixed furniture MUST NOT reintroduce
  `transmission` or an `envMap`, and MUST preserve its `renderOrder` — it encloses everything else
  and writes no depth, so it has to draw last even now that the arcs live in a sibling group.
- Arcs remain `TubeGeometry`/`CylinderGeometry`, never `THREE.Line` (README constraint 5).
- Arc sampling and the marker use the corrected altitude (see `solar-position.md`).

---

## `SolarScene.ts` — two disposal scopes

**Contract** (FR-023, SC-010):
- The fixed group and the dated group are siblings under the extension's Drawing Space group, each
  with its own disposal scope.
- The dated group is disposed and rebuilt on date or instant change; the fixed group only on site
  change, year change, or extension stop.
- `disposeAll` MUST cover both. Opening, exercising and closing the capability repeatedly MUST
  leave nothing accumulated.
- `setGroundOffset` continues to move the extension's own group and MUST NOT call
  `sceneAnchor.set(...)` (README constraint 3).
- Redraws continue to be requested through the framework's single `invalidate()` per change; no
  repeated per-frame `invalidate()` and no return to `requestRedraw()` (FR-025, README
  constraint 2).

---

## Playback gate

**Contract** (research D8, FR-018, FR-019, FR-020):
- During continuous playback only, skip the shadow recomputation when the sun direction has moved
  less than `SHADOW_GATE_DEGREES` (0.25°) since the last update.
- The gate MUST be implemented by not recomputing the inputs the shadow pass consumes. It MUST NOT
  be implemented by touching `renderer.shadowMap.autoUpdate` or any other renderer-global flag
  (FR-024) — that is the conventional Three.js approach and it is forbidden here.
- Any geometry change MUST clear the gate unconditionally and update immediately: a height
  correction, a ground offset change, newly arrived building data, a site change (FR-019).
- Scrubbing and single-step time changes are **not** gated — only continuous playback.
- The gate MUST NOT be perceptible as stepping, flicker or lag (FR-020).
- If the gate proves unachievable within FR-024, the sanctioned response is a new
  `DrawingRequirement` raised against specs/051 and recorded as an SC-009 finding there — following
  the precedent specs/052 set for its own framework risks — **not** a reach into renderer state
  from this feature. The release remains shippable without the gate.
