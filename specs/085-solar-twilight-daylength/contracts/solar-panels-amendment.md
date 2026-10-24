# Amendment to specs/052's panel contract

**Feature**: specs/085-solar-twilight-daylength | **Date**: 2026-10-23

This is the change [specs/052-solar-analysis/contracts/solar-panels.md](../052-solar-analysis/contracts/solar-panels.md)
takes. The contract itself stays in force; this records what is added to it.

---

## Change 1 — the Solar Figures panel gains the twilight rows

**Where**: the "Solar Figures" section's rows.

**Added**, after the existing Sunrise / Sunset / Day length rows:

| Row | Requirement | Behaviour |
|---|---|---|
| Civil twilight | FR-001, FR-002 | The two times the sun's centre crosses 6° below the horizon, and the length of the civil band. |
| Nautical twilight | FR-001, FR-002 | The same for 12°. |
| Astronomical twilight | FR-001, FR-002 | The same for 18°. |

**Added**, among the panel's closing statements:

| Statement | Requirement | Behaviour |
|---|---|---|
| Threshold definition | FR-004 | That the twilight thresholds are the sun's centre before refraction, and that the altitude shown is refraction-corrected — with the three constants the altitude figure therefore reads at each threshold. |
| Phase not occurring | FR-006 | For a phase the sun's centre does not reach on the selected date, that it does not reach it, naming the threshold. |
| Times not determined | FR-008 | For a threshold whose crossing could not be solved, that the times could not be determined. |

**Unchanged**: the sunrise, sunset, day-length, polar, time-basis, accuracy, design-stage and
assumed-heights rows and statements keep their present values and wording. The sun-does-not-rise
and sun-does-not-set statements keep their place, and remain the only places the panel says the
sun does not rise or set.

---

## Change 2 — a day-length chart panel is added beside the figures panel

**Where**: a new section, after "Building Corrections".

| Control | Requirement | Behaviour |
|---|---|---|
| Day length chart | FR-011 … FR-019 | A live panel showing every day of the selected date's year, each day's phases in order across the local day, with the selected date marked, the site's latitude and hemisphere stated, a legend naming the bands, and the year's longest and shortest day in text. Read-only: it sets no time, changes no site and navigates nowhere. |

The panel is registered through the extension's `registerLivePanelKind` path, the same as the Time
of Day and Building Corrections panels, and opens when the solar analysis activates.

**Unchanged**: the "Extending the vocabulary" rule in specs/049's contract. This feature adds no
block kind, so the vocabulary's version does not move, and the committed JSON Schema artifact and
the server's model-facing schema are untouched.

---

## Why the chart is a separate panel and not a row in the figures panel

Recorded here because it is the one place this feature's shape departs from the obvious reading
of its request.

The Solar Analysis panel is a **content panel**: its document is composed from the shared block
vocabulary and is rebuilt, re-opened, re-parsed and re-rendered **on every tick of playback**,
because the figures track the time of day. A year of per-day boundaries inside that document
would therefore be re-validated and re-rendered every frame — the cost FR-016 and SC-009 forbid.

Adding the chart to the shared vocabulary instead was rejected for a second reason: the
vocabulary is an interchange format with a server-side contract, and a new kind means a version
increment that moves the client's envelope, the committed JSON Schema artifact and the server's
model-facing schema together — a change every panel in the product depends on, made for a chart
only the solar feature uses and that a model could never compose.

A live panel avoids both, and is the shape the solar feature already uses for its two other
panels and the shape the reference tool uses for its own day-length chart. The user sees the
chart and the figures at the same time, which is what was asked for; they are two panels rather
than one.
