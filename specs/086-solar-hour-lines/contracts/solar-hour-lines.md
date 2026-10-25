# Contract: Solar Hour Lines

**Feature**: specs/086-solar-hour-lines

Covers `scene/sunPathCurve.ts`, `scene/SolarScene.ts`, `components/SolarAnalysisOverlay.tsx` and
`solar/timeZone.ts`. It **amends** specs/063's
[`contracts/solar-scene.md`](../../063-solar-accuracy-performance/contracts/solar-scene.md) for
`sunPathCurve.ts` and `SolarScene.ts`; everything in that contract not restated here still holds,
including the bans on touching renderer-global and scene-global state or `sceneAnchor.set(...)`.

---

## `sunPathCurve.ts` — the hour lines

### `sampleHourLineRuns` — **new**

```ts
export function sampleHourLineRuns(
  hour: number,
  latitude: number,
  longitude: number,
  year: number,
  timeZoneId: string,
  radiusMetres: number,
): THREE.Vector3[][]
```

**Contract**:

- Produces one sample per calendar day of `year` (365 or 366), **never** a fixed 365.
- Each sample is `solarPosition(fromLocalParts(localDate, hour * 60, timeZoneId), latitude, longitude)`.
  The minute-of-day is `hour * 60` exactly — the value the hour marks use — so mark and curve share
  one conversion (FR-009).
- A sample is kept only when `altitudeDegrees >= 0`, the filter `sampleDayArcPoints` applies
  (FR-005).
- Kept samples are returned as contiguous runs in day order; a below-horizon sample closes the
  current run. There is no threshold, tolerance or merging of runs (FR-004).
- Returns `[]` when the hour never sees the sun in the year (FR-006). Never throws for a valid
  latitude, longitude, year and zone.

### `splitAboveHorizonRuns` — **new, exported for test**

```ts
export function splitAboveHorizonRuns<T>(samples: ReadonlyArray<T | null>): T[][]
```

**Contract**: the run rule as a pure function, so the split is unit-testable without a site. A
`null` marks a below-horizon sample and closes the current run: `[a, b, null, c]` →
`[[a, b], [c]]`. It never joins across a `null`. Empty input and all-`null` input both return
`[]`.

### `buildHourLines` — **new**

```ts
export function buildHourLines(
  latitude: number,
  longitude: number,
  year: number,
  timeZoneId: string,
  radiusMetres: number,
  color: number,
): THREE.Mesh[]
```

**Contract**:

- For each hour 0–23, each run of `sampleHourLineRuns` with two or more points becomes one mesh
  through the existing `buildTubeFromPoints` at `HOUR_LINE_TUBE_RADIUS` (0.15) and
  `HOUR_LINE_OPACITY` (0.5).
- Every returned mesh carries `userData.localHour = hour`.
- Every mesh's material colour is `color`. The function holds no colour of its own — no default
  literal exists (FR-003).
- Returns `[]` when no hour has an above-horizon sample; returns the subset of hours that do
  (FR-006).
- Geometry is `TubeGeometry`, never `THREE.Line` (specs/052 research D17, README constraint 5).

### `buildFixedFurniture` — **signature changed**

```ts
export function buildFixedFurniture(
  latitude: number,
  longitude: number,
  year: number,
  timeZoneId: string,
  hourLineColor: number,
  radiusMetres?: number,
): FixedFurnitureObjects
```

**Contract**:

- `timeZoneId` and `hourLineColor` are **required**. Neither has a default: a default zone would
  silently draw the curves on the wrong clock, a default colour would be the hardcoded literal
  FR-003 forbids.
- `FixedFurnitureObjects` gains `hourLines: THREE.Mesh[]`, built by `buildHourLines` and returned
  in hour order. The existing fields (`dial`, `mountPost`, `shell`, `monthlyArcs`) are unchanged.
- The function remains purely a function of its arguments: same arguments → equivalent furniture,
  no hidden state.
- The shell still carries `renderOrder = 1` and must keep drawing last.

---

## `SolarScene.ts` — the fixed key, the colour, the group

### `updateSunPath` — **signature changed**

```ts
updateSunPath(
  localDate: string,
  latitude: number,
  longitude: number,
  currentInstantUtc: Date,
  azimuthDegrees: number,
  altitudeDegrees: number,
  timeZoneId: string,
  hourLineColorCss: string,
): boolean
```

**Contract**:

- The fixed rebuild key becomes `${site}|${year}|${radius}|${timeZoneId}`. A time-zone change at
  the same coordinates and year **must** rebuild the fixed furniture exactly once (FR-008).
- `hourLineColorCss` is converted to a three.js hex inside this method
  (`new THREE.Color(css).getHex()`); the scene is the only place the conversion happens.
- When the fixed key is unchanged but `hourLineColorCss` resolves to a different hex, the existing
  hour-line meshes' material colours are set in place and the method returns `true`; **no geometry
  is rebuilt** (FR-003, research D7).
- The fixed group receives the hour lines: `dial`, `mountPost`, `...monthlyArcs`, `...hourLines`,
  `shell`. They belong to the fixed disposal scope (FR-012).
- The DOM-free behaviour, the `lastSunPathArguments` replay and the dated-path behaviour are
  unchanged.

---

## `SolarAnalysisOverlay.tsx` — the theme is the only new input

**Contract**:

- The overlay reads `useTheme()` and passes `theme.palette.text.secondary` as `hourLineColorCss`.
- `theme.palette.text.secondary` is in the dome effect's dependency list, so a theme change
  re-runs the effect; the in-place re-tint plus `invalidate()` is what makes the toggle visible.
- No user-facing string is added (FR-011); nothing else in the effect changes.

---

## `timeZone.ts` — the formatter memo

**Contract**:

- `formatParts` reuses one `Intl.DateTimeFormat` per zone id instead of constructing one per call.
- The options, the zone handling, the fixed-point loop and every public result are unchanged;
  `timeZone.test.ts` must stay green without edits.
- The memo is keyed by the resolved zone id string and is module-scoped; no eviction is needed
  (the set of zones a session touches is bounded by the sites it visits).

---

## Amendments recorded

| Where | What changes |
|---|---|
| specs/063 `contracts/solar-scene.md` | The `sunPathCurve.ts` table's "Fixed furniture" row gains the hour lines and its dependency list gains the time zone and the line colour; the `SolarScene.ts` section's fixed-group rebuild condition gains the time zone. |
| `features/solar/README.md` | The module map's `sunPathCurve.ts` and `timeZone.ts` lines note the hour lines and the formatter memo. |
