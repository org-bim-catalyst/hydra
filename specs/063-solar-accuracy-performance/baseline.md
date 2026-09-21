# Pre-Implementation Baseline

**Captured**: 2026-09-21, before any spec 063 code change
**Build**: `pre-solar-upgrade` (f42e6814) via `localhost:7170/studio`

These are the "before" values every check in [quickstart.md](./quickstart.md) Part 2 compares against.

## Site

| | |
|---|---|
| Location | بدر، مصر (Badr, Egypt) |
| Timezone | Africa/Cairo |
| Date | Sunday 21 September 2026 |
| Assumed building height | 9 m (source data records none) |
| Ground offset | 0 |

## Day figures

| Figure | Value |
|---|---|
| Sunrise | 06:41 |
| Sunset | 18:51 |
| Day length | 12 h 10 min |

Two days after the September equinox at latitude ≈ 30° N, so a day length marginally over 12 h is
the expected figure. **These three values are expected to shift by up to ~1 minute** after the fix
(FR-004a, SC-003) — that shift is the correction, not a regression.

## Instant figures

| Time | Azimuth | Altitude | Shadows |
|---|---|---|---|
| 12:00 | 157.4° | 58.5° | Cast; very short (sun near its highest) |
| 17:51 | 263.4° | 12.2° | Cast; long, running ENE |
| 19:50 | 278.7° | −13.6° | None — below-horizon message shown |

**Expected after the fix**:

- **12:00** — altitude within 0.1° of 58.5°, azimuth unchanged at 157.4°. Any visible change at
  midday is a defect; refraction is ~0.01° at this elevation.
- **17:51** — altitude rises by roughly 0.07°, invisible in a figure rounded to one decimal.
  Azimuth unchanged. Shadows equally or more sharply defined.
- **19:50** — still no shadows, still a stated reason.
- **06:41** (the reported sunrise) — currently reads about **−0.83°**; must read **−0.27°** after.

## Observation carried into implementation

In the 17:51 capture the cast shadows are confined to a band across the site while buildings
further out cast none. That is the fixed `SHADOW_FRUSTUM_RATIO` region, and it is the behaviour
FR-008/FR-014 replace with a content-derived radius. Re-check this same view after the change: the
band should cover the buildings that have shadows to cast, with no truncated shadow ends.
