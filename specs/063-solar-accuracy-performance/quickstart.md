# Quickstart: Validating Solar Analysis Accuracy & Performance

**Feature**: specs/063-solar-accuracy-performance | **Date**: 2026-09-21

Two halves. The automated half proves the arithmetic. The manual half proves the picture, and is
written so it can be executed without any solar-geometry knowledge — every step states the value to
expect before you look.

---

## Prerequisites

```bash
# Frontend tests — no database, no backend, no GPU required
cd "src/AskLucy.Web/ClientApp"
npx vitest run src/features/solar
```

The solar math modules are pure functions; nothing in the automated half needs a running app.

For the manual half, run the app as usual and open a site in the viewer. Central Dubai or another
dense urban location is needed for the performance checks — an empty plot has nothing to cast
shadows.

---

## Part 1 — Automated

### The central assertion

One test carries the feature's main claim (FR-002, SC-001):

> At the sunrise and sunset instants `daySummary()` returns, `solarPosition().altitudeDegrees`
> equals `−SOLAR_SEMIDIAMETER_DEGREES` (−0.2667°) within `SOLAR_POSITION_TOLERANCE_DEGREES`.

It must pass at **every** test location and date, and the value must not vary between them. A test
that passes at one site and fails at another means the semidiameter and refraction terms have been
conflated again — the exact defect this feature repairs.

### Reference-value assertions

| What | Compared against | Tolerance |
|---|---|---|
| Altitude across a full day | NOAA's published **corrected-for-refraction** column | `SOLAR_POSITION_TOLERANCE_DEGREES` |
| Sunrise / sunset | NOAA published values | 60 s (\|lat\| ≤ 72°), 600 s beyond |
| Azimuth | Unchanged from current release | `SOLAR_POSITION_TOLERANCE_DEGREES` |
| Altitude above 15° | Unchanged from current release | `SOLAR_POSITION_TOLERANCE_DEGREES` |

**Do not assert rise/set against the current release's output** (FR-004a). It differs by design —
12–55 s in the tropics, up to 172 s at Reykjavík — and the new value is the more accurate one.

### Geometry and disposal

- `shadowGround.test.ts`'s existing invariant (ground half-extent strictly inside the frustum
  half-extent) must pass for content-derived radii, the no-buildings fallback, and a lone tall
  building.
- Merged geometry: one geometry and one material for N buildings; the mass toggle still reveals all
  of them; a degenerate ring is excluded without failing the merge.
- Disposal: repeated date changes, site changes, and open/close cycles leave nothing accumulated
  (SC-010).

### Full-suite reminder

Run the whole solar folder, not just the file you touched — `ChatPage.test.tsx`-style page-level
tests carry their own assertions about components they render. Then the backend, unchanged but
worth confirming:

```bash
npx vitest run src/features/solar
dotnet test "Ask Lucy.sln" --filter "FullyQualifiedName~Buildings"
```

---

## Part 2 — Manual

### Before you change anything

Capture the "before" state, or half the checks below have nothing to compare against.

1. Open solar analysis on a dense urban site.
2. Note the site name and the date shown.
3. Screenshot the figures panel.
4. Set the time to **12:00** and screenshot the whole viewer.
5. Set the time to about **one hour before sunset** and screenshot the whole viewer.

Keep these. Steps 4 and 5 are the comparison baseline.

### Check 1 — the numbers agree (the main repair)

1. Read the **sunrise** time in the figures panel.
2. Set the time control to exactly that time.
3. Read the **altitude**.

**Expect: −0.27°.**

Repeat at a different site and a different date. **Expect: −0.27° again** — the same number, every
time. That constancy *is* the fix; a value that drifts between sites means it is not fixed.

Repeat for sunset. Expect −0.27°.

*Why not zero:* sunrise is when the sun's upper edge appears, so its centre — the point being
reported — is still one sun-radius below the horizon. −0.27° is that radius.

### Check 2 — nothing moved at midday

1. Set the time to **12:00** on the same site and date as your baseline screenshot.
2. Compare against the "before" shot.

**Expect: indistinguishable.** Shadows in the same places, altitude and azimuth unchanged. Any
visible shadow movement at midday is a defect — the correction is negligible when the sun is high.

### Check 3 — sunrise/sunset times moved slightly

Compare the rise and set times against your "before" figures-panel screenshot.

**Expect: a shift of up to about a minute** at mid latitudes, less in the tropics. This is intended
— the new times are the more accurate ones. A shift of more than ~3 minutes anywhere outside the
Arctic is worth reporting.

### Check 4 — shadows at low sun

1. Set the time to about an hour before sunset.
2. Compare against your "before" low-sun screenshot.

**Expect:** edges equally or more sharply defined, never blurrier. Long shadows running their full
length, not stopping at an invisible line. **No large grey patch anywhere on the ground** — that
last one is the failure the prototype hit, and is the single most important thing to look for.

### Check 5 — the new no-shadow window

1. Set the time to a minute or two after sunrise.

**Expect:** the sun is up, no shadows are drawn, and **a message on screen says why.** Shadows are
suppressed below 1° elevation because their length diverges. If shadows vanish with no explanation,
that is a defect.

### Check 6 — the dome is stable across dates

1. Note the compass dial, the mount post and the faint monthly lattice.
2. Step the date forward several times.

**Expect:** the day's arc changes; the dial, post and lattice do not flicker, move or change.

### Check 7 — scrubbing stays smooth

1. On a dense site, drag the time slider across a full day.
2. Then press play and rotate the camera while it runs.

**Expect:** continuous motion, no stutter, viewer stays responsive. A screenshot cannot show this —
capture a short screen recording if it looks wrong.

### Check 8 — corrections still respond immediately

1. Start playback.
2. Change a building's height mid-playback.

**Expect:** its shadow updates at once, not after a delay. The playback gate must never hold back a
geometry change.

---

## If it is wrong

```bash
git reset --hard pre-solar-upgrade     # restores f42e6814, before any of this work
```

The spec and plan commits are separate from the implementation, so the documents can be kept while
only the code is reverted.
