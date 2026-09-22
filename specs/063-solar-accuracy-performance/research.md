# Research: Solar Analysis Accuracy & Performance

**Feature**: specs/063-solar-accuracy-performance | **Date**: 2026-09-21

All decisions below were reached by reading the existing implementation and, where a number was
in question, by measuring rather than reasoning. Measurements were produced with a standalone
harness reproducing `solarPosition.ts` exactly; the figures quoted are reproducible from it.

---

## D1 — The defect is a bundled constant, not a missing correction

**Decision**: Separate the two physical effects the `−0.833°` threshold conflates, and apply each
where it belongs: atmospheric refraction to the reported altitude, the sun's angular radius to the
definition of rise and set.

**Rationale**: `−0.833°` is `−(34′ refraction + 16′ semidiameter)`. `daySummary.ts` uses it whole;
`solarPosition.ts` applies neither half. The first instinct — add refraction to the reported
altitude and stop — was measured and rejected:

| Site | Date | Reported today | With refraction only |
|---|---|---|---|
| Dubai | 21 Mar | −0.929° | −0.574° |
| Dubai | 21 Jun | −0.815° | −0.409° |
| London | 21 Mar | −0.923° | −0.564° |
| Singapore | 21 Dec | −0.770° | −0.341° |
| Tromsø | 21 Mar | −0.955° | −0.608° |

The residual neither vanishes nor holds still. Two causes: the semidiameter term is still
unaccounted for (the sun's *centre* is genuinely below the horizon when its *upper edge* appears,
so zero is the wrong target), and the `−0.833°` threshold is applied to a declination and equation
of time evaluated once at local noon and reused for the whole day, which is why the geometric
altitude at the computed rise instant ranges over `−0.955°…−0.770°` instead of sitting at
`−0.833°`.

**Alternatives considered**:
- *Correct the display only* — rejected: reduces the disagreement without removing it, and leaves
  a varying residual that no user can be told the meaning of.
- *Report the upper edge's altitude, so the figure reads 0.00° at sunrise* — rejected: no
  published source tabulates that quantity, which would forfeit FR-006's external verification.

---

## D2 — Refraction model: the NOAA piecewise correction

**Decision**: Adopt the NOAA Solar Calculator's own refraction correction, the same public-domain
source `solarPosition.ts` was ported from. Piecewise in the true elevation `te`, in arcseconds:

| Band | Correction |
|---|---|
| `te > 85°` | `0` |
| `te > 5°` | `58.1/tan − 0.07/tan³ + 0.000086/tan⁵` |
| `te > −0.575°` | `1735 + te(−518.2 + te(103.4 + te(−12.79 + 0.711·te)))` |
| otherwise | `−20.772/tan` |

### D2a — correction to D2, found by measurement during US1

**The fourth band is not used.** Implemented literally, `−20.772/tan(te)` makes refraction
*decrease* as the sun sinks — 0.5749° at −0.575°, 0.4569° at −0.724°, 0.3968° at −0.833°, reaching
zero at the nadir — which is not how refraction behaves. It is an extrapolation outside the range
the fit covers, and the rise/set root falls inside it.

NOAA's page states the two treatments side by side without reconciling them: *"For sunrise and
sunset calculations, we assume 0.833° of atmospheric refraction. In the solar position calculator,
atmospheric refraction is modeled as: [the piecewise model]"*. At −0.833° the piecewise model
yields 0.397°, not the 0.567° the rise/set constant assumes. Both cannot be honoured.

Measured consequence of honouring the band: the upper-edge solve lands at a geometric −0.7236°
rather than the conventional −0.833°, and every rise came out late and every set early against
NOAA's published annual tables, by 26–60 s at Dubai, London and Singapore, 82–99 s at Tromsø and up
to **200 s at Reykjavík** — outside FR-004a's 60 s at latitudes well inside the ±72° band, and
worse than the release this feature replaces.

**Decision**: hold the near-horizon fit at its boundary value for all `te ≤ −0.575°`. Horizon
refraction becomes a constant 0.5749° (34.5′), agreeing with the Astronomical Almanac's standard
34′ and with the −0.833° convention NOAA's published times rest on. The rise/set threshold becomes
a derived −0.8416°, and the reported altitude at rise and set stays exactly −0.2667°.

**Measured after the change**, against published NOAA tables:

| Reference | Result |
|---|---|
| Rise/set, 5 sites × 4 dates (18 pairs, 36 values) | worst 40.2 s, all inside the 60 s tolerance |
| Full-day corrected elevation, 150 above-horizon samples | agrees to 5e-7°, the transcription precision |
| Full-day azimuth, all 240 samples | agrees to 5e-7° |
| Full-day corrected elevation, 90 below-horizon samples | diverges by up to 0.563°, deliberately |

The last row is the accepted cost, and it is confined to altitudes at which the sun is already
down. Pinned in `refraction.test.ts` and `solarPosition.test.ts` so it cannot grow unnoticed.

**Rationale**: Same provenance as the existing port, so the feature keeps a single citable source
and remains checkable against NOAA's published "corrected for refraction" column. It is a
standard-atmosphere model; FR-006's tolerance is measured against it rather than assumed.

**Alternatives considered**: Bennett/Sæmundsson closed forms (equivalent within this tolerance,
different provenance — no reason to mix sources); temperature/pressure-parameterised models
(rejected as out of scope per the spec's Assumptions — the platform has no site weather input).

---

## D3 — Rise and set by root-finding, not by threshold

**Decision**: Solve for the instant at which `apparent(geometric altitude) + 0.2667° = 0` — the
refraction-corrected upper edge at the horizon — using Newton iteration seeded from the present
closed-form estimate, with a 30 s finite-difference slope, converging to `<1e−7°`.

**Rationale**: Measured across five sites and four dates, this yields a reported altitude at the
computed rise instant of exactly **−0.2667°**, constant to within 0.0001° at every site and date
including Tromsø and Reykjavík. That constancy is the whole point: it is the sun's angular radius
and nothing else, so it can be stated in one sentence and asserted as a single number.

Seeding from the existing closed form means the iteration begins within ~3 minutes of the answer;
convergence is reached in a handful of steps. The present code path is retained as the seed rather
than deleted, so the polar-condition detection (`cosH0` out of range) continues to work exactly as
today before any iteration is attempted.

**Measured shift from the current release**:

| Band | Shift |
|---|---|
| Tropics (Dubai, Singapore) | 12–55 s |
| Mid latitude (London) | 17–77 s |
| High latitude (Tromsø, Reykjavík) | 3–172 s |

**Rationale for accepting the shift**: it is existing error being removed. The current release
evaluates the sun's position once at noon; the solver evaluates it at the instant in question.
Tests therefore assert against published NOAA values, never against the current release's output
(FR-004a). The largest shifts occur where rise/set is genuinely ill-conditioned — near the polar
boundary the sun grazes the horizon and a hundredth of a degree moves the crossing by minutes.

**Alternatives considered**: bisection (robust but needs a bracketing pass, and the seed is already
close); keeping the closed form and accepting a varying residual (rejected — this is D1).

---

## D4 — One altitude, corrected, used everywhere

**Decision**: `solarPosition()` returns the refraction-corrected altitude as its only altitude.
The light direction, the shadows, the sun marker and the figures panel all consume that one value.
The uncorrected geometric altitude is not exposed.

**Rationale**: Spec FR-007. Shadows are cast by the light that actually arrives, which is the
refracted light, so the corrected direction is the more truthful one for rendering rather than a
display convenience. More importantly, two values that are supposed to agree are precisely how
this defect arose; the remedy is to have one.

**Consequence**: shadow direction shifts by up to ~0.5° at the horizon, is unchanged above ~15°,
and `sunPathCurve.ts`'s arcs move correspondingly. SC-009 is written to bound this rather than to
forbid it. The internal root-finder in D3 necessarily works with the uncorrected value as an
intermediate — that is a local variable inside one function, not a second exposed altitude.

---

## D5 — Merged footprint geometry

**Decision**: Build one `BufferGeometry` from all footprint extrusions via
`BufferGeometryUtils.mergeGeometries`, with a single shared material, replacing the per-building
`Mesh` + per-building `MeshStandardMaterial` in `footprintGeometry.ts`.

**Rationale**: `buildFootprintMesh` currently allocates a material per building; at the 300-building
cap that is 300 draw calls and 300 materials traversed every shadow-map render, every playback
frame. The geometry is static once built — buildings do not move relative to one another — so the
merge cost is paid once per data load.

`mergeGeometries` ships in Three.js's own `examples/jsm/utils/BufferGeometryUtils`, already a
transitive part of the viewer's Three.js dependency. No new package (constitution §3, and the
spec's no-new-dependency posture inherited from specs/052 research D1).

**The mass toggle** (`setShowMass`, README constraint 1) becomes simpler, not harder: one material's
`colorWrite` flag instead of a traversal setting 300. `castShadow: true`, `colorWrite: false`,
`depthWrite: false` semantics are unchanged.

**Per-building identity**: the only consumer that needs to distinguish one building from another is
`BuildingCorrectionsPanel`, which reads height and provenance from `solarAnalysisStore.siteBuildings`
— the data, not the geometry. Nothing picks or raycasts against individual footprint meshes today,
so identity is not lost by merging. A height correction rebuilds the merged geometry, which is the
same cost as today's rebuild of one mesh plus a redraw.

**Alternatives considered**: `InstancedMesh` (rejected — footprints are distinct polygons, not
repetitions of one shape); keeping per-building meshes and sharing only the material (a real but
partial win — leaves 300 draw calls, which is the dominant cost).

---

## D6 — A content-derived radius, still a single radius

**Decision**: Replace the constant `SHADOW_FRUSTUM_RATIO = 1.3` applied to the analysis radius with
a radius computed from the footprints actually present, extended by the longest shadow they cast at
the lowest elevation at which shadows are still drawn (D7). Both the shadow camera extent and
`shadowGround.ts`'s ground plane continue to derive from that one value, exactly as today.

**Rationale**: Spec FR-008/FR-009. README constraint 4 (specs/052 research D16) requires a single
radius feeding both, on pain of the recorded "big fake grey blob" — clamped shadow-map lookups
reading as *in shadow* across ground outside the frustum. That invariant is preserved literally
here: one value in, two sizes out, unchanged in structure. Only the value's derivation changes.

Inspection of the reference prototype during drafting found that the present multiplier has no
derivation behind it at all. `sunpath-osm-shadows-12.html` hard-codes `shadow.camera.left = −260`
and its three siblings, a figure that suited its test site; `SHADOW_FRUSTUM_RATIO = 1.3` over a
200 m default radius is that same 260 m expressed as a formula. This decision supplies a basis
where none existed rather than overturning a considered design.

**Alternatives considered**: extending the region directionally along the sun's azimuth (the
tightest fit, and the biggest sharpening win — but it cannot be expressed as a single radius, so it
breaks the invariant; explicitly deferred by FR-014a pending measurement against SC-005/SC-006);
resizing the ground plane in lockstep with a tightly fitted region (risks shadows falling off a
shrunken ground).

---

## D7 — A stated low-elevation bound

**Decision**: Draw shadows only above a sun elevation of **1°**; below it the light is disabled
exactly as it already is below the horizon.

**Rationale**: Spec FR-012. Shadow length goes as `height / tan(elevation)`, which diverges as
elevation → 0: a 50 m building casts 2.9 km of shadow at 1°, 5.7 km at 0.5°. Without a bound the
content-derived radius of D6 diverges with it, and the shadow map's fixed 2048² resolution spread
over kilometres produces exactly the mush this release exists to remove. 1° is below the point at
which shadow detail is meaningful and above the point at which the radius misbehaves.

This is a new user-visible behaviour — a narrow window just after sunrise and before sunset in
which the sun is up but no shadows are drawn. It is covered by FR-012 and must be stated to the
user rather than left to look like a failure (constitution §2 VIII in spirit: an absent result with
a reason is not a silent one).

**Alternatives considered**: clamping the radius to a maximum and accepting truncated shadows
(rejected — truncation is the failure FR-009 exists to prevent); no bound (rejected — measured
divergence above).

---

## D8 — Gating shadow recomputation during playback

**Decision**: During continuous playback only, skip the shadow-map update when the sun direction
has moved less than **0.25°** since the last update. Any geometry change — height correction,
ground offset, new building data, site change — clears the gate unconditionally.

**Rationale**: Spec FR-018/FR-019/FR-020. The solar arithmetic is negligible; the shadow-map render
is the cost. 0.25° is below the angular resolution of a shadow edge at any plausible viewing
distance, so the skipped frames are indistinguishable. The sun moves ~0.25° per minute of real
solar time, so at typical playback rates this gates a useful fraction of frames without ever
letting the shadow lag perceptibly behind the sun marker.

**The framework constraint is the risk here.** README constraint 2 and specs/052 FR-038 put
`renderer.shadowMap` state off-limits to a capability — it is renderer-global, the same class of
thing as `scene.environment`. The gate must therefore be implemented as *not recomputing the
inputs the shadow pass consumes*, not as toggling `renderer.shadowMap.autoUpdate`. If measurement
shows that insufficient, the correct response is a `DrawingRequirement` raised against specs/051,
recorded as an SC-009 finding against that spec — the precedent specs/052 set for exactly this
situation — not a reach into renderer state from here.

**Alternatives considered**: `renderer.shadowMap.autoUpdate = false` with manual `needsUpdate`
(the conventional Three.js approach, and forbidden here by FR-024); time-based throttling
(rejected — decouples the gate from what the user actually perceives, which is sun movement).

---

## D9 — Splitting the dome

**Decision**: Split `buildSunPath` into fixed furniture (compass dial, mount post, shell, monthly
lattice) built once per site-and-year, and a date-dependent group (day arc, hour marks, current
marker) rebuilt on date change. `SolarScene.updateSunPath` disposes and rebuilds only the latter.

**Rationale**: Spec FR-021/FR-022. `updateSunPath` currently calls `disposeGroupContents` on the
whole `sunPathGroup` and rebuilds everything `buildSunPath` returns, including
`buildMonthlyGridArcs` — twelve sampled arcs — for every date change. Only the day arc, its hour
marks and the marker depend on the date. The monthly lattice depends on latitude, longitude and
year; the dial, post and shell depend on radius alone.

**Disposal is the hazard, not the split** (FR-023). Two sibling groups under the extension's
Drawing Space, each with its own disposal scope, replaces one group with one scope. The existing
`disposeGroupContents` is deep and correct; it must be applied to the date-dependent group only,
and the fixed group disposed on site or year change and on extension stop. The existing
`disposeAll` test hook must cover both.

**Note**: `buildDomeShell` is exported by `sunPathCurve.ts` but is *not* assembled into the scene —
README constraint 5 records that the prototype's glass dome was dropped deliberately, since it was
the one element needing `scene.environment`. Classifying it as fixed furniture does not
reintroduce it.

---

## D10 — Tolerances must be re-measured, not carried over

**Decision**: `RISE_SET_TOLERANCE_SECONDS` (60) and `RISE_SET_TOLERANCE_SECONDS_HIGH_LATITUDE`
(600) are inherited from NOAA and stand unchanged. `SOLAR_POSITION_TOLERANCE_DEGREES` (0.1) was
*measured* against the uncorrected quantity and must be re-measured against the corrected one
before it is re-stated.

**Rationale**: Spec FR-006. The constants are exported so the figures panel's wording and the
tests assert against one source (specs/052 T006); that property is only meaningful if the number
is true of what is now computed. The re-measurement is a task, not an assumption — if the
corrected quantity lands outside 0.1° against NOAA's published corrected column, the constant
changes and the panel's wording changes with it, automatically.

---

## Summary of what changes

| Area | Today | After |
|---|---|---|
| Reported altitude | True geometric | Refraction-corrected, used everywhere |
| Rise/set | Closed form at `−0.833°`, noon declination | Solved for corrected upper edge at horizon |
| Altitude at reported sunrise | −0.77°…−0.96°, varying | −0.2667°, constant |
| Footprint drawing | 300 meshes, 300 materials | 1 merged geometry, 1 material |
| Shadow radius | `analysisRadius × 1.3` (undelivered 260 m) | Derived from footprints + longest drawn shadow |
| Shadows below 1° elevation | Drawn, increasingly meaningless | Not drawn, stated |
| Playback shadow updates | Every frame | Gated at 0.25° of sun movement |
| Date change | Whole dome rebuilt | Day arc only |
