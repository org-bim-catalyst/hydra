# Solar Analysis (specs/052-solar-analysis, amended by specs/063 and specs/064)

Sun path, real building shadows and time scrubbing over the active site — the first capability
built entirely on the viewer extension framework (specs/049-051). See `specs/052-solar-analysis/`
for the spec, plan, research and contracts; this file exists so the constraints the reference
prototype discovered the hard way are not rediscovered.

Two later features amended it in place rather than forking it: **specs/063** (accuracy and
performance — refraction, the derived shadow radius, the merged footprint geometry, the split sun
path) and **specs/064** (the time entry field and slider ticks). Where a constraint below names a
spec, that spec is the one that owns it now.

## Module map

```
features/solar/
├── api/siteBuildingsApi.ts        GET /api/v1/site-buildings client
├── solar/
│   ├── solarPosition.ts           ported NOAA algorithm (research D1) — no runtime dependency
│   ├── refraction.ts              NOAA's piecewise refraction correction, split from the semidiameter (063)
│   ├── daySummary.ts              sunrise/sunset/day length, polar-condition handling
│   ├── noaaFullDay.fixture.ts     NOAA's own published full-day table, the SC-002 reference (063)
│   └── timeZone.ts                tz-lookup + Intl.DateTimeFormat (research D2)
├── buildings/footprintGeometry.ts ONE merged extrusion for every footprint; casts, never drawn (063)
├── scene/
│   ├── SolarScene.ts              owns the extension's Drawing Space group and everything in it
│   ├── sunPathCurve.ts            the dome, split by lifetime: fixed furniture vs dated path (063)
│   ├── sunLight.ts                DirectionalLight + shadow camera, radius derived per-elevation (063)
│   └── shadowGround.ts            ShadowMaterial ground plane, sized with sunLight from one radius
├── store/
│   ├── solarAnalysisStore.ts      site, analysis moment (instantUtc canonical), buildings, status
│   └── correctionsStore.ts        session-scoped, site-keyed height/offset overrides (research D11)
├── panels/
│   ├── SolarTimeControlPanel.tsx  live panel: date, time slider/entry field, play/stop
│   ├── timeEntry.ts               pure decision logic for the entry field + tick marks (specs/064)
│   ├── BuildingCorrectionsPanel.tsx live panel: height correction, ground offset, reset
│   ├── panelContracts.ts          the panels' type keys and zod `data` schemas, kept out of the
│   │                              components so Fast Refresh can still hot-swap them
│   └── solarFiguresContent.ts     content-panel document (specs/049 blocks), not a component
├── components/
│   ├── SolarAnalysisOverlay.tsx   site-following, building fetch, scene/panel refresh
│   └── CameraAttitudeWidget.tsx   tilt/heading bubble, placed clear of the reserved corner
└── copy.ts                        every user-facing string (constitution §7)

viewer/extensions/builtin/solarAnalysisExtension.tsx  thin registration shim (the only viewer/ file)
viewer/extensions/declared.ts                          one added id (the only other viewer/ file)
```

## The ported NOAA algorithm — provenance and stated tolerances

`solar/solarPosition.ts` is a direct port of the reference prototype's NOAA solar-position
formulas (public domain). **No `astronomy-engine`/`suncalc` dependency was added** — research D1
explains why: the prototype's own polar-case handling (`cosH0 > 1`/`cosH0 < -1`) already answers
the one thing a library would have been needed for.

Stated tolerances (exported as named constants from `solarPosition.ts`, so the figures panel's
wording and the tests that verify it can never drift apart):

| Quantity | Tolerance | Basis |
|---|---|---|
| Sunrise/sunset, \|lat\| ≤ 72° | ±60 s | Inherited from NOAA's documented figure |
| Sunrise/sunset, \|lat\| > 72° | ±10 min | Inherited from NOAA's documented figure |
| Azimuth/altitude | ±0.1° | **Measured** against a solar-noon identity, not inherited — NOAA states no position figure |

## Five constraints carried from the reference implementation (research D13-D17)

These were discovered the hard way by the prototype (`sunpath-osm-shadows-13.html`) and are
binding on this implementation. Losing any of them is a regression, not a style choice.

1. **Buildings cast shadows but are never drawn** (research D13) — `colorWrite: false`,
   `depthWrite: false`, `castShadow: true` in `footprintGeometry.ts`. The basemap already draws
   its own 3D buildings; a second copy from OSM data reads as a shifted "ghost duplicate", not
   information. A developer toggle (`SolarScene.setShowBuildingMass`) re-enables `colorWrite` to
   verify the massing.

   **specs/063 made this ONE mesh, not one per building.** Every footprint is merged into a single
   `BufferGeometry` sharing a single material, so a site of 300 buildings is 1 draw call in the
   shadow pass instead of 300. Two consequences bind anything that touches this file: the
   site/neighbour tint can no longer be a second material, so it survives as a per-vertex `color`
   attribute with `vertexColors: true`; and the massing toggle is necessarily all-or-nothing, since
   there is one material left to flip. `buildFootprintMeshes` returns the merged mesh alongside the
   `extentMetres` and `tallestHeightMetres` constraint 4 needs, plus an `excludedCount` — a
   degenerate ring is dropped from the merge and *counted*, never silently discarded.
2. **The redraw-timing trap** (research D14) — a single `invalidate()` per change is what's
   implemented; if a change is ever observed not to appear until the user interacts, the sanctioned
   fix is repeated `invalidate()` calls across successive frames from the extension, never a
   return to `requestRedraw()`.
3. **The ground offset moves the extension's own group, never the scene anchor** (research D15) —
   `SolarScene.setGroundOffset()` sets `drawingSpace.group.position.z`. `sceneAnchor.set(...)` is
   never called by this feature.
4. **The shadow frustum and the ground plane are sized from ONE radius** (research D16) — one
   radius goes in and two sizes come out: the ground plane is the radius itself, and the frustum is
   `SHADOW_FRUSTUM_MARGIN` (1.08) larger, so the plane sits **strictly inside** the frustum rather
   than exactly at its edge. `shadowGround.test.ts` asserts that for every tested radius, and
   `solarAnalysisExtension.test.ts` re-asserts it across the whole elevation sweep — this is the
   concrete mechanism behind FR-018, and the exact failure ("a big fake grey blob" from clamped
   edge lookups) the prototype hit. specs/063 replaced the earlier pair of ratios (1.3 and 2.4)
   with this single margin; only the radius's derivation and the margin's expression changed, never
   the one-radius rule.

   **specs/063 derives that radius instead of fixing it.** `shadowRadiusMetres(extent, tallest,
   elevation)` = the ground the merged footprints actually occupy, plus the longest shadow they can
   cast at the *current* sun elevation — not at the radius they were queried with, and not at a
   worst-case elevation held all day. The elevation is floored at
   `MIN_SHADOW_ELEVATION_DEGREES = 1.0`, because `tan(0)` is zero and the radius would run away to
   infinity at sunrise. Evaluating per-elevation rather than once at the floor is the deliberate
   departure from the task's literal formula, and the whole point: measured on a real site (~200 m
   of low-rise, tallest 9 m) the static version is 716 m against 200 m, a frustum 3× wider over the
   same 2048² shadow map — i.e. blurrier at every elevation, which would have *failed* the
   "equal or better at every sun elevation" requirement it was meant to satisfy. It remains a single
   scalar with no azimuth dependence.
5. **Arcs are tubes, never `THREE.Line`, and there is no `scene.environment`** (research D17) —
   `sunPathCurve.ts` builds every arc from `TubeGeometry`/`CylinderGeometry`. What was dropped is
   the prototype's **glass** dome shell, because glass is the only element that needed global scene
   state this feature is not permitted to touch (`scene.environment` is shared and framework-owned,
   the same class of thing `renderer.shadowMap.enabled` is).

   A plain transparent shell *is* built and *is* assembled — `buildDomeShell` returns a
   `MeshStandardMaterial` with `transparent: true`, `depthWrite: false` and no `envMap`. The
   binding rule is therefore "no `transmission`, no `envMap`", not "no shell". Because it encloses
   everything and writes no depth it must draw last, which used to happen implicitly by being
   appended last to a single group; now that specs/063 split the dome into sibling groups,
   `renderOrder = 1` says so explicitly.

## Two live SC-009 risks (research D9, D17)

- **`DrawingSpaceHandle.onFrame` has no unsubscribe.** The guarded callback in
  `solarAnalysisExtension.tsx`'s `start()` returns immediately (and requests no redraw) when not
  playing, which satisfies FR-039's purpose even though the callback itself stays registered for
  the extension's lifetime. If this guard is ever found insufficient, the fix is an additive
  `onFrame` → unsubscribe in specs/051, recorded as an SC-009 finding against that spec — not
  absorbed here.
- **There is no `environment` drawing requirement.** Avoided by dropping the prototype's glass
  dome shell in favour of a plain transparent one (constraint 5 above) rather than reaching for
  `scene.environment`. If a future capability genuinely needs an environment map, the right answer
  is a new `DrawingRequirement` in specs/051.

## Refraction and the semidiameter are separate things (specs/063)

`solar/refraction.ts` exists because the inherited `−0.833°` rise/set threshold bundles two
unrelated physical effects: atmospheric refraction (~0.57° at the horizon) and the sun's angular
radius (`SOLAR_SEMIDIAMETER_DEGREES = 0.2667`). They belong in different places — refraction is a
property of the *reported altitude*, the semidiameter is the *definition of rise and set*. Keeping
them apart is what makes the altitude reported at sunrise the same number at every site and every
date, and it is why `RISE_SET_GEOMETRIC_ALTITUDE_DEGREES` is derived from the two rather than
written as a literal.

`solar/noaaFullDay.fixture.ts` is the evidence: NOAA's own published full-day table, transcribed
unchanged, so "accurate to within 0.1°" is a claim checked against somebody else's numbers rather
than against our own output.

Anything that aims the light or samples an arc uses the **corrected** altitude. The one sanctioned
reason a shadow may sit anywhere other than where specs/052 put it is this correction — bounded by
`HORIZON_REFRACTION_DEGREES` (< 0.6°) near the horizon and by the ±0.1° position tolerance above
15°, and purely vertical: refraction never changes the compass bearing a shadow falls along.
`shadows.integration.test.ts` asserts exactly that, which is how any *other* cause of shadow
movement gets caught.

## The dome is split by lifetime, and shadows are gated during playback (specs/063)

Two independent costs, two separate mechanisms, both in `scene/`:

**`sunPathCurve.ts` splits the dome by what each piece depends on.** `buildFixedFurniture` (compass
dial, mount post, shell, monthly lattice) is keyed on site + year; `buildDatedPath` (day arc,
seasonal extremes, hour marks, current marker) is keyed additionally on the date. `SolarScene` holds
them as **sibling groups, each its own disposal scope**, with independent rebuild keys. Stepping the
date rebuilds only the second; a time-of-day tick rebuilds neither and just moves the marker. The
thing this protects is the dial's baked 2048² canvas texture, which depends on nothing but the site
and used to be thrown away on every date step. `sunPathCurve.test.ts` asserts it on object
*identity*, not child counts — a rebuild producing the same number of children is the failure.

**`aimSun` gates the shadow-map pass during continuous playback.** A sun movement smaller than
`SHADOW_GATE_DEGREES` (0.25°) is skipped and `aimSun` returns `false`, and the caller withholds
`invalidate()`. That is the whole mechanism, and it matters *why*: rendering here is on-demand, so
not invalidating skips the frame and with it the shadow-map pass — without this feature ever
touching `renderer.shadowMap.*`, which constraint-5's sibling rule (FR-038) forbids. The angle is
measured against the direction last **actually applied**, never against the skipped ticks, so
skipped movement cannot accumulate into visible drift. The gate applies only while playing; a
scrub, a date change or a typed time is never gated. `clearShadowGate()` drops the reference on any
change that invalidates the shadow geometry itself — a height correction, a ground offset, new
building data.

One trap worth naming: a date change near a solstice can move the sun by less than 0.25° while
replacing every arc in the dome. `updateSunPath` therefore *returns whether it rebuilt*, and
`SolarAnalysisOverlay` invalidates on `domeRebuilt || sunMoved`. Gating on sun movement alone would
leave the new arcs undrawn.

## Why the buildings list lives in `solarAnalysisStore`, not local component state

`SolarAnalysisOverlay.tsx` fetches buildings and stores them in the shared
`solarAnalysisStore.siteBuildings` field (not a local `useState`) because
`BuildingCorrectionsPanel.tsx` — a separately-mounted live panel — needs to read the site
building's height and provenance (FR-011) without a prop-drilling path between two independent
panel-framework-mounted React trees. One store, read by both, is what keeps them from disagreeing.

## Naming a moment: three controls, one stored value (specs/064)

The Time of Day panel offers a slider, a typed entry field and playback. All three write
`instantUtc` through the existing `solarAnalysisStore` setters — FR-022's single-source-of-truth
rule means there is never a second time value to reconcile, and the entry field's draft text is not
one: it exists only while the field has focus, and the field otherwise displays
`moment.localMinuteOfDay`, so slider, keyboard and playback changes all reach it.

`panels/timeEntry.ts` holds the decision logic as pure functions, which is why it can be tested
without rendering: `parseLocalTimeEntry` returns a discriminated union and **never throws and never
clamps** (`25:00` is rejected, not quietly turned into `23:59`), and `buildTimeSliderMarks` decides
which ticks and labels survive at a given pixel width. A nonexistent local time (a DST gap) is
detected by round-tripping the candidate through `timeZone.ts`'s existing `fromLocalParts` /
`toLocalParts` and comparing the minute that comes back — there is no second timezone
implementation here, and there must not be.

Rejections keep the typed text in place, keep the previous time, and say why, following the
`invalidHeight` / `invalidGroundOffset` pattern in `copy.ts`. Dragging snaps to 15 minutes; arrow
keys still step one minute, so the slider handles coarse aiming and the keyboard and entry field
handle precision.

## Testing

Run the whole feature: `npx vitest run src/features/solar` (frontend) and
`dotnet test "Ask Lucy.sln" --filter "FullyQualifiedName~Buildings|FullyQualifiedName~OpenSolarAnalysisCapability"`
(backend, note the space in the solution name).
