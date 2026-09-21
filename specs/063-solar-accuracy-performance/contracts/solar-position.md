# Contract: Solar Position & Day Summary

**Feature**: specs/063-solar-accuracy-performance

Covers `solar/refraction.ts` (added), `solar/solarPosition.ts` and `solar/daySummary.ts`. These are
pure functions of their arguments with no rendering, DOM or network involvement, and are unit-tested
directly (constitution §2 V).

---

## `refraction.ts` (added)

### `refractionCorrectionDegrees(geometricAltitudeDegrees: number): number`

The NOAA Solar Calculator's piecewise atmospheric refraction correction (research D2), returning
degrees to **add** to a geometric altitude.

**Contract**:
- Returns `0` for altitudes above 85°.
- Is continuous across its band boundaries to within the position tolerance.
- Is defined for every finite input including altitudes below the horizon — the rise/set solver
  evaluates it there.
- Is a pure function: no clamping to the caller's expectations, no side effects.

### `SOLAR_SEMIDIAMETER_DEGREES: 0.2667`

The sun's mean angular radius. Exported because it is simultaneously the definition of rise/set and
the expected figure in SC-001; the two must not be able to disagree.

**Not modelled**: the ±1.7% annual variation in apparent semidiameter with the Earth–Sun distance.
It is an order of magnitude below the position tolerance. Stated here so the omission is a recorded
decision rather than an oversight.

---

## `solarPosition.ts` (modified)

### `solarPosition(instantUtc, latitude, longitude): SolarPositionResult`

**Changed**: `altitudeDegrees` is now the **refraction-corrected apparent** altitude of the sun's
centre. It is the only altitude the function returns.

**Contract**:
- `altitudeDegrees` MUST be `geometric + refractionCorrectionDegrees(geometric)`.
- The uncorrected geometric altitude MUST NOT be returned, exposed on the result type, or reachable
  by any caller (research D4, FR-007). It exists only as a local intermediate.
- `azimuthDegrees` MUST be unchanged by this feature (FR-005). Refraction raises the apparent
  position vertically; it does not rotate it.
- `declination` and `eqTime` remain as they are — intermediates `daySummary.ts` reuses.
- Above 15° altitude, the returned value MUST be within the position tolerance of the current
  release's value; the correction is negligible there and any larger difference is a defect.

### `solarPositionToEnuUnitVector(azimuth, altitude)`

**Unchanged in signature and behaviour.** It now receives a corrected altitude from every caller,
which is how the corrected value reaches the light and the sun marker. No caller may pass it an
uncorrected altitude.

### `SOLAR_POSITION_TOLERANCE_DEGREES`

**Re-measured, not assumed** (research D10, FR-006). The value stated MUST be the measured maximum
deviation of `solarPosition().altitudeDegrees` from NOAA's published **corrected-for-refraction**
column across the test locations. If measurement exceeds the present `0.1`, the constant changes and
the figures panel's wording changes with it automatically, because both read this export.

---

## `daySummary.ts` (modified)

### `daySummary(dateUtc, latitude, longitude): DaySummary`

**Changed**: sunrise and sunset are solved for rather than approximated (research D3, FR-002a).

**Contract**:
- Sunrise and sunset MUST be the instants at which
  `solarPosition().altitudeDegrees + SOLAR_SEMIDIAMETER_DEGREES = 0` — the refraction-corrected
  upper edge at the horizon.
- At each returned instant, `solarPosition().altitudeDegrees` MUST equal
  `−SOLAR_SEMIDIAMETER_DEGREES` within the position tolerance. **This is the feature's central
  assertion** and must hold at every test location and date, with no site- or season-dependent
  variation (FR-002, SC-001).
- The existing closed form remains, as the solver's seed and as the polar-condition test. The
  `cosH0 > 1` / `cosH0 < −1` branches MUST continue to report "sun never rises" / "sun never sets"
  exactly as today, and MUST be evaluated **before** any iteration is attempted (FR-004).
- Polar conditions, rise, and set MUST be decided against the one upper-edge definition, so that the
  three can never disagree about whether the sun rose (FR-004).
- `dayLengthMinutes` follows from the solved instants.

**Convergence and failure (constitution §2 VIII, FR-028)**:
- The solver MUST have a stated iteration cap and a stated convergence threshold.
- On failure to converge it MUST NOT silently return the seed, the last iterate, or a plausible
  wrong time. It MUST surface the failure to the caller, and the caller MUST surface it to the user
  as an explicit "could not be determined" rather than as a missing or nonsensical value — the same
  treatment the polar cases already receive.
- Non-convergence is expected to be unreachable in practice given the seed's quality; it is
  specified because "unreachable" and "unhandled" are different things.

**Tolerances**: `RISE_SET_TOLERANCE_SECONDS` (60) and `RISE_SET_TOLERANCE_SECONDS_HIGH_LATITUDE`
(600) are unchanged and remain inherited from NOAA.

**Validation basis** (FR-004a): rise/set assertions MUST compare against published NOAA values.
They MUST NOT compare against the current release's output, which research D3 measured as differing
by 12–55 s in the tropics, up to 77 s at mid latitude and up to 172 s at Reykjavík — differences that
are existing error being removed.

---

## Consumers that must be updated in step

| Consumer | Change |
|---|---|
| `scene/sunLight.ts` | Receives the corrected altitude; also gains the 1° shadow floor (see `solar-scene.md`) |
| `scene/sunPathCurve.ts` | Arc sampling and the current-position marker use the corrected altitude |
| `panels/solarFiguresContent.ts` | Displays the corrected altitude and **states which quantity it is** (FR-003) |
| `copy.ts` | Supplies that label, and the wording for the below-1° no-shadow window |

No consumer may retain, recompute or derive an uncorrected altitude for any purpose.
