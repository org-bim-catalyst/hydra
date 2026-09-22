# Solar Analysis (specs/052-solar-analysis)

Sun path, real building shadows and time scrubbing over the active site — the first capability
built entirely on the viewer extension framework (specs/049-051). See `specs/052-solar-analysis/`
for the spec, plan, research and contracts; this file exists so the constraints the reference
prototype discovered the hard way are not rediscovered.

## Module map

```
features/solar/
├── api/siteBuildingsApi.ts        GET /api/v1/site-buildings client
├── solar/
│   ├── solarPosition.ts           ported NOAA algorithm (research D1) — no runtime dependency
│   ├── daySummary.ts              sunrise/sunset/day length, polar-condition handling
│   └── timeZone.ts                tz-lookup + Intl.DateTimeFormat (research D2)
├── buildings/footprintGeometry.ts extrusion; buildings cast shadows but are not drawn (research D13)
├── scene/
│   ├── SolarScene.ts              owns the extension's Drawing Space group and everything in it
│   ├── sunPathCurve.ts            the dome: chosen day, seasonal extremes, hour marks, marker
│   ├── sunLight.ts                DirectionalLight + shadow camera (research D16)
│   └── shadowGround.ts            ShadowMaterial ground plane, sized with sunLight from one radius
├── store/
│   ├── solarAnalysisStore.ts      site, analysis moment (instantUtc canonical), buildings, status
│   └── correctionsStore.ts        session-scoped, site-keyed height/offset overrides (research D11)
├── panels/
│   ├── SolarTimeControlPanel.tsx  live panel: date, time slider/entry field, play/stop
│   ├── timeEntry.ts               pure decision logic for the entry field + tick marks (specs/064)
│   ├── BuildingCorrectionsPanel.tsx live panel: height correction, ground offset, reset
│   └── solarFiguresContent.ts     content-panel document (specs/049 blocks), not a component
├── components/SolarAnalysisOverlay.tsx  site-following, building fetch, scene/panel refresh
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
2. **The redraw-timing trap** (research D14) — a single `invalidate()` per change is what's
   implemented; if a change is ever observed not to appear until the user interacts, the sanctioned
   fix is repeated `invalidate()` calls across successive frames from the extension, never a
   return to `requestRedraw()`.
3. **The ground offset moves the extension's own group, never the scene anchor** (research D15) —
   `SolarScene.setGroundOffset()` sets `drawingSpace.group.position.z`. `sceneAnchor.set(...)` is
   never called by this feature.
4. **The shadow frustum and the ground plane are sized from ONE radius** (research D16) —
   `sunLight.ts`'s `SHADOW_FRUSTUM_RATIO` (1.3) and `shadowGround.ts`'s `GROUND_PLANE_RATIO` (2.4)
   are two views of the same number. `shadowGround.test.ts` asserts the ground plane's half-extent
   is strictly less than the frustum's half-extent for every tested radius — this is the concrete
   mechanism behind FR-018, and the exact failure ("a big fake gray blob") the prototype hit.
5. **Arcs are tubes, never `THREE.Line`, and there is no `scene.environment`** (research D17) —
   `sunPathCurve.ts` builds every arc from `TubeGeometry`/`CylinderGeometry`. The prototype's glass
   dome shell is dropped entirely rather than reproduced, because it is the only element that
   needed global scene state this feature is not permitted to touch (`scene.environment` is
   shared and framework-owned, the same class of thing `renderer.shadowMap.enabled` is).

## Two live SC-009 risks (research D9, D17)

- **`DrawingSpaceHandle.onFrame` has no unsubscribe.** The guarded callback in
  `solarAnalysisExtension.tsx`'s `start()` returns immediately (and requests no redraw) when not
  playing, which satisfies FR-039's purpose even though the callback itself stays registered for
  the extension's lifetime. If this guard is ever found insufficient, the fix is an additive
  `onFrame` → unsubscribe in specs/051, recorded as an SC-009 finding against that spec — not
  absorbed here.
- **There is no `environment` drawing requirement.** Avoided by dropping the prototype's glass
  dome shell (constraint 5 above) rather than reaching for `scene.environment`. If a future
  capability genuinely needs an environment map, the right answer is a new `DrawingRequirement` in
  specs/051.

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
