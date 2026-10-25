# Feature Specification: Solar Hour Lines (Analemma Curves)

**Feature Branch**: `086-solar-hour-lines`

**Created**: 2026-10-24

**Status**: Draft

**Input**: User description: "Hour lines (analemma curves) on the solar sun-path dome, matching the reference tool. For each local clock hour 0-23, draw on the dome the curve traced by the sun's position at that hour on every day of the year — the analemma for that hour. Each curve is a thin tube at the dome radius, in the existing dim hour-line colour (distinct from the chosen-day arc, the two solstice arcs and the monthly lattice), split into contiguous runs wherever the sun is below the horizon so no chord is drawn through the ground; an hour that never sees the sun in the sampled year draws nothing. The existing hour-number marks already sit on these curves and label them, so no new labels. Constraints: fixed furniture (specs/052 FR-021/FR-022); local clock hours only, via `solar/timeZone.ts` `fromLocalParts`; sampling budget 24 hours × 365 days × one `solarPosition` call each, built once per site/year; no change to the solar calculation, shadows, figures panel, day-length chart or backend; colours from the MUI theme, never hardcoded. Tests: points are above the horizon; runs are split (no segment crosses from evening to morning); the 12:00 curve passes through the 12:00 mark's position."

## Context

The sun-path dome (specs/052) shows the chosen day's arc, the two solstice extremes, a dim monthly lattice of twelve arcs, and hour-number marks placed along the chosen day. What it does not show is the hour lines the reference tool draws on the same dome: for each local clock hour, the curve the sun traces at that hour across the whole year — that hour's analemma.

Those curves complete the instrument. The chosen day's arc answers "where does the sun go today"; an hour curve answers "where does the sun stand at 09:00 on every day of the year", which is the question a designer actually asks when deciding whether a façade is shaded at nine in the morning — in June, in December, and at every time between. The hour marks already sit on these curves; the curves are what the marks are marks *on*. specs/085 deliberately excluded "the analemma" from its scope, so this feature is that exclusion being lifted for the hour curves alone.

Three facts shape the specification.

1. **These are fixed furniture.** An hour's curve depends on where you are (latitude and longitude), which year, and the site's time zone — never on the chosen date or the current instant. specs/052 FR-021/FR-022 split the dome by lifetime precisely so that scrubbing and playback rebuild nothing; if the hour lines rebuilt on a tick, they would undo that split.
2. **The clock is the site's clock.** Commit a4a5b674 moved the hour marks from UTC to local clock hours — in Dubai the "12" mark had been sitting where the sun stands at 16:00 local. The curves must resolve their hours through the same conversion, or every mark east or west of Greenwich floats off its own curve on first draw.
3. **A curve that ignores the horizon draws through the ground.** At nearly every latitude the above-horizon samples for a fixed hour are not contiguous across the year: the winter months are missing. Sampling the year and joining every surviving sample into one curve would draw a chord straight across the missing months, below the terrain, from dusk to dawn. The curve therefore has to be assembled from contiguous above-horizon runs.

## Amendments to earlier specifications

This specification ships on top of two earlier ones and changes neither of their scope. Where a requirement below carries a pointer, read the pointer with it.

| Specification | What this adds to it |
|---|---|
| [specs/052-solar-analysis](../052-solar-analysis/spec.md) | The dome's fixed furniture gains the 24 hour curves. FR-021 (fixed furniture does not rebuild on a date/time change) and FR-022 (one consistent picture as time changes) apply unchanged; FR-005 … FR-007 (the dome's content and its orientation) are extended by a new kind of dome content. |
| [specs/085-solar-twilight-daylength](../085-solar-twilight-daylength/spec.md) | Its out-of-scope line ("the analemma") no longer applies to the hour curves. The Cartesian, stereographic, equidistant and spherical sun-path diagrams remain out of scope. |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read the Sun's Path for Any Hour of the Year (Priority: P1)

A designer opens the solar analysis for a site and looks at the dome. For each local clock hour, a thin, dim curve traces where the sun stands at that hour on every day of the year. The chosen day's hour marks sit exactly on their curves, so "this is where 09:00 is today" reads as a point on the year's 09:00 path — and the spread of the curve tells them how far that hour moves between midsummer and midwinter.

**Why this priority**: This is the feature. Everything else is a correctness property of these curves.

**Independent Test**: Draw the dome for a site and date, find the mark labelled with a given hour, and confirm it lies on that hour's curve. Visually, the dome carries one curve per hour that sees the sun at any point in the year, at the dome radius, distinguishable from the chosen day, both solstices and the monthly lattice.

**Acceptance Scenarios**:

1. **Given** a mid-latitude site and a date, **When** the dome is drawn, **Then** there is one hour curve for each local clock hour that sees the sun at any point in the year, each a thin tube lying at the dome's radius.
2. **Given** a site and date, **When** the user looks at an hour mark on the chosen day, **Then** the mark's position lies on that hour's curve.
3. **Given** the drawn dome, **When** the user compares the hour curves with the chosen day's arc, the two solstice arcs and the monthly lattice, **Then** the hour curves are dimmer and thinner and remain distinguishable from all three, in both the light and the dark theme.
4. **Given** a site and date, **When** the user reads the curves, **Then** the hour curves carry no new labels or legends; the existing hour-number marks remain the only labels.

---

### User Story 2 - Nothing Rebuilds While Scrubbing or Playing (Priority: P2)

A designer scrubs the time of day and plays the day through. The hour curves stay exactly where they are, drawn once, while the chosen day's arc, the hour marks and the sun marker update around them — the same stillness the dial and the monthly lattice already have.

**Why this priority**: The curves are a year-wide picture. Rebuilding thousands of samples on every tick would make scrubbing visibly stall, which is the failure specs/052 FR-021/FR-022 and its tests exist to prevent.

**Independent Test**: Draw the dome, capture the identity of every fixed-furniture object, scrub across a year of dates and a full day of times, and confirm the fixed-furniture identities are unchanged and no hour-line sampling re-runs.

**Acceptance Scenarios**:

1. **Given** a drawn dome, **When** the selected date or time of day changes, **Then** the hour curves are the same objects as before and are not rebuilt.
2. **Given** a drawn dome, **When** the site or the year changes, **Then** the hour curves are rebuilt once, together with the rest of the fixed furniture.
3. **Given** a dome first drawn before the site's time zone was known, **When** the time zone resolves, **Then** the hour curves are rebuilt exactly once on the site's clock, and the hour marks use the same clock.

---

### User Story 3 - Correct at the Horizon and at the Poles (Priority: P3)

A designer working at a high latitude sees hour curves that stop at the horizon rather than diving through the ground, and that are absent for hours that never see the sun. At a site in deep polar night the dome shows no hour curves at all.

**Why this priority**: It is a correctness property rather than new capability, but a curve drawn through the ground is visually wrong at exactly the sites where the sun-path picture matters most.

**Independent Test**: Build the curves for a set of latitudes spanning the equator to the polar circles and inspect that every point is at or above the horizon, that each above-horizon run is a separate object, and that the polar-night and never-rising-hour cases draw nothing.

**Acceptance Scenarios**:

1. **Given** an hour whose sun drops below the horizon for part of the year, **When** the curve is drawn, **Then** each contiguous above-horizon stretch is its own run, and no drawn segment connects two points separated by a below-horizon stretch.
2. **Given** a site and year in which an hour never has the sun at or above the horizon, **When** the dome is drawn, **Then** nothing is drawn for that hour.
3. **Given** a site and year in which the sun never reaches the horizon for any hour (deep polar night), **When** the dome is drawn, **Then** no hour curves are drawn at all.
4. **Given** an hour whose sun stays above the horizon for the whole year (polar day), **When** the curve is drawn, **Then** it is a single unbroken run.
5. **Given** a date with a daylight-saving transition at an hour that does not exist locally, **When** the curve is drawn, **Then** that hour resolves to exactly one instant through the same conversion the hour marks use, and is neither skipped nor counted twice.

### Edge Cases

- **Leap year**: the year's 366 days are sampled, not a hardcoded 365.
- **Two disjoint above-horizon stretches**: an hour near the horizon can be above it in two separated parts of the year; both runs are drawn and neither is joined to the other.
- **Run too short to draw**: a run of fewer than two samples draws nothing rather than a degenerate tube.
- **Time zone undetermined**: the fallback time basis is used for both the curves and the hour marks, so they still agree, and the panel's existing statement of the time basis covers both.
- **Site or year change**: the curves are rebuilt once as part of the fixed furniture; a repeated change back to a previous site/year produces the same picture with no accumulated geometry.
- **Dome resized for the building under study**: the curves are redrawn at the new dome radius with the rest of the fixed furniture; they never stay at the old radius.
- **Open/close cycles**: the curves are disposed with the rest of the fixed furniture; repeated extension activation leaves no leftover geometry.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: For each local clock hour 0–23, the dome MUST show the curve traced by the sun's position at that hour on every calendar day of the selected year — that hour's analemma.
- **FR-002**: Each curve MUST be drawn as a thin tube lying at the dome's radius, so every drawn point is on the dome's shell.
- **FR-003**: The hour curves MUST be visually dim and thin, in a colour drawn from the application's shared theme colours rather than a new hardcoded literal, and MUST remain distinguishable from the chosen day's arc, both solstice arcs and the monthly lattice in both the light and the dark theme.
- **FR-004**: A curve MUST be split into contiguous above-horizon runs; no drawn segment may connect two points separated by a below-horizon stretch. A run with fewer than two samples MUST draw nothing.
- **FR-005**: Every drawn point of an hour curve MUST be at or above the horizon.
- **FR-006**: An hour that never has the sun at or above the horizon anywhere in the selected year MUST draw nothing for that hour; at a site and year where no hour does, the dome MUST draw no hour curves.
- **FR-007**: The hour curves are FIXED FURNITURE: they MUST depend only on latitude, longitude, the year and the site's time zone, and the selected date and time of day MUST NOT rebuild them.
- **FR-008**: The fixed-furniture build MUST accept the site's time zone as an input, and the condition that decides whether the fixed furniture must be rebuilt MUST include it — so that a time zone resolved after the dome was first drawn rebuilds the hour curves exactly once, on the site's clock. (`buildFixedFurniture` does not take a time zone today.)
- **FR-009**: Hours MUST be resolved through the same local-clock conversion the hour-number marks use (`fromLocalParts`, at the same minute of the hour), so that the mark for an hour lies on that hour's curve and the curves and marks cannot disagree.
- **FR-010**: The curves MUST be sampled once per hour per calendar day of the selected year — 24 × the year's day count position evaluations — and MUST NOT be recomputed on a time-of-day tick, on a frame, or on a date change within the same year and site.
- **FR-011**: The hour curves MUST introduce no new labels, legend, list, panel content or user-facing text; the existing hour-number marks remain the curves' only labels.
- **FR-012**: The hour curves MUST be part of the fixed furniture's disposal scope, so a site change, a year change, a radius change, a date change and an open/close cycle leave no accumulated geometry.
- **FR-013**: This feature MUST NOT change the solar-position calculation, the shadows, the figures panel, the day-length chart, any existing arc or mark, or any backend behaviour.
- **FR-014**: Daylight-saving transitions MUST be handled by the same conversion the marks use: a local clock hour that does not exist on a transition date resolves to exactly one instant, and one that occurs twice resolves to exactly one instant, so the year's sample count is unchanged.

### Key Entities

- **Hour line**: one local clock hour's year-wide curve. Its identity is the local hour; its shape is a set of contiguous above-horizon runs sampled from the chosen year at the site.
- **Run**: one contiguous stretch of an hour line whose samples are all at or above the horizon. The unit of drawing; separated runs are never joined.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At a mid-latitude site, a reviewer can identify the year-round path of any given hour by eye, and every hour mark visible on the chosen day sits on its own hour curve.
- **SC-002**: Zero hour-curve rebuilds across a full year of date changes and a full day of time-of-day changes: the fixed-furniture objects keep their identities.
- **SC-003**: No drawn segment lies below the horizon at any tested latitude spanning the equator to the polar circles; the deep polar-night case draws no hour curves, and every hour that never sees the sun draws none.
- **SC-004**: The hour curves are built once per site and year and cost nothing per frame; scrubbing and playback remain as responsive as before the feature.
- **SC-005**: In both the light and the dark theme, the hour curves are visible over the basemap and distinguishable from the chosen day's arc, both solstice arcs and the monthly lattice.
- **SC-006**: At three test sites spanning hemispheres and time zones, the 12:00 curve passes through the 12:00 mark's position.

## Assumptions

- **Horizon threshold is 0°**, matching the existing day arcs; the hour marks' own 2° visibility threshold is unchanged, so a winter date may show fewer marks than curves.
- **The sampled year is the selected date's year**, consistent with the existing fixed-furniture rebuild key.
- **Tube thickness and the exact dim colour are design choices** made during planning; the requirement is only that they are thin, dim, theme-derived and distinct from the four existing dome line families.
- **The one-off cost is acceptable**: 24 × a year's days (8,760 in a common year, 8,784 in a leap year) position evaluations per site/year, already the stated sampling budget. It is paid when the fixed furniture is built, never per frame.
- **The time zone is already available to the dome update** (the extension passes it today); only the fixed-furniture build and its rebuild condition need to learn about it.
- **No new controls, settings, panels or persistence**: the curves appear whenever the dome does, for every site and year, with no opt-in.
