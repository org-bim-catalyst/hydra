# Feature Specification: Solar Twilight Times & Year Day-Length Chart

**Feature Branch**: `085-solar-twilight-daylength`

**Created**: 2026-10-22

**Status**: Draft

**Input**: User description: "Twilight times and a year-long day-length chart for the solar analysis panel. Two additions to the existing specs/052-solar-analysis Time of Day and Solar Analysis panels, using the sun's position math that already exists in ClientApp/src/features/solar/solar (solarPosition.ts, daySummary.ts, refraction.ts) and adding nothing new to the calculation.

(1) Twilight times. The Solar Analysis panel currently reports sunrise, sunset and day length for the selected date. Add the three standard twilight boundaries alongside them: civil twilight (sun's upper edge 6 degrees below the horizon), nautical twilight (12 degrees below) and astronomical twilight (18 degrees below), each as a begin and an end time, plus the length of each twilight phase. These are the same kind of figure as sunrise and sunset and must be derived from the same solar-position solver rather than from a table of constants, so they agree with the reported altitude at the same instants the way sunrise and sunset do. Where a phase does not occur on the selected date (for example astronomical twilight never ends inside the polar circles in summer, or civil twilight never begins at a high latitude in winter), the panel must say that plainly instead of showing a wrong or missing time. The polar-day and polar-night cases already reported must keep working, and the polar statement must remain the only place the panel says the sun does not rise or set.

(2) A day-length chart for the year. A new compact chart in the Solar Analysis panel showing, for every day of the year at the selected site, the length of daylight with the twilight phases as bands around it — that is, a horizontal band per day whose extent covers night, then the three twilight phases in order, then daylight, so the year reads as a gradient that widens and narrows with the seasons. The chart must mark the selected date, and show the site's latitude and hemisphere. It must use the same sunrise/sunset/twilight definitions as the panel's figures, so a day's band and that day's stated sunrise agree. The chart is read-only: it does not set the time, does not navigate, and clicking it does not change the selection.

Out of scope: the analemma and the Cartesian, stereographic, equidistant and spherical sun-path diagrams; quantitative results such as hours of sunlight per surface, irradiance or compliance checks; comparing two sites or two dates side by side; exporting or saving the analysis; anything that changes the solar calculation, the scene or the shadows. The existing single-source-of-truth rule from specs/052 FR-022 holds: every control writes only instantUtc through the store's setters, and the new chart writes nothing at all. The panel keeps its compact layout and the existing accessibility commitments (FR-032: the figures carry the information in text, and the chart is an enhancement over them)."

## Context

Solar analysis ([specs/052](../052-solar-analysis/spec.md)) reports the sun's position for a site and moment, and the figures panel reports sunrise, sunset and day length for the chosen date. Those three figures answer "when is it light", which is not the question an architect or a landscape designer actually asks at the end of a design day. The questions are "when does it get dark enough to light the site", "how long is the useful twilight", and "how does that change across the year".

The reference tool the feature was modelled on answers all three in one place: a **Twilight Times** panel beside the main view, and a **day-length chart** that draws the whole year as a gradient — a wide band of daylight in summer, a narrow one in winter, with the twilight phases layered between day and night.

Neither exists here. The solar module already computes the sun's position accurately enough to place it, and already solves for the horizon crossing (specs/063), so this feature is not new astronomy: it is the same solver asked for more crossings, and the same figures asked to cover a year.

Three facts shape the specification.

1. **Twilight is a set of thresholds, not a separate phenomenon.** Civil, nautical and astronomical twilight are the sun's centre at 6°, 12° and 18° below the horizon. Once those crossings are solved for the same way rise and set is solved for, the three phases fall out as bands between adjacent thresholds, and the day's structure is fully described by a handful of numbers.
2. **The feature's one horizon definition must survive.** specs/063 replaced the inherited −0.833° with a single derived definition — the sun's upper edge, refraction-corrected, on the horizon — so that the reported altitude at sunrise reads the same number at every site and date. Twilight must be defined the same way, or the panel will show two families of figures that cannot be checked against each other.
3. **A year of crossings is a different order of work from one day.** Eight crossings a day is arithmetic. Eight crossings for every day of the year is thousands of solves, and the figures panel is rebuilt on every frame of time playback. The chart therefore has to be computed once and reused, or the feature will cost the panel its responsiveness — which specs/052 FR-023 requires and the reference prototype discovered the hard way.

## Amendments to earlier specifications

This specification ships on top of two earlier ones and changes neither of their scope. Where a requirement below carries a pointer, read the pointer with it.

| Specification | What this adds to it |
|---|---|
| [specs/052-solar-analysis](../052-solar-analysis/spec.md) | FR-002 gains the three twilight figures; FR-031 (the figures are panel content) is extended by a new kind of panel content; FR-003 (local time) and FR-032 (accessibility) apply to the new figures and the chart. |
| [specs/049-panel-content-model](../049-panel-content-model/contracts/content-vocabulary.md) | Nothing changes. The chart is a live panel, not a content block (FR-020, FR-021, amended 2026-10-23), so the vocabulary gains no kind and its version does not move. |

## Clarifications

### Session 2026-10-22

- Q: Which altitude defines a twilight phase — the sun's centre or its upper edge? → A: The **centre**, at −6°, −12° and −18°. This is the published standard (NOAA and USNO almanac tables quote the sun's *centre* at those geometric altitudes), so a user checking a phase against an almanac finds the same time. The upper-edge convention belongs to rise and set alone, where the sun's disc is what appears; it is not extended to twilight.
- Q: Geometric or refraction-corrected? → A: **Geometric**, matching the published standard. The panel therefore has to state that twilight is defined on the unrefracted centre, because the altitude figure it shows beside them is refraction-corrected and reads a different number (−5.43° at the civil threshold, not −6.00°). Stating the difference is the same discipline specs/063 applied to the altitude quantity.
- Q: Then what does "agree with the reported altitude" mean here? → A: The same property rise and set has: at the reported threshold crossing, the reported altitude reads **one constant at every site and date** — −5.43°, −11.43°, −17.43° — and the panel says why. It is not the same number as the definition because the definition is unrefracted; it is the *same number everywhere*, which is what makes the figures checkable rather than merely plausible.
- Q: What exactly is "the length of each twilight phase"? → A: The duration of the band the phase occupies — the time for the sun to cross from one threshold to the next: civil twilight is the time from the horizon to −6°, nautical from −6° to −12°, astronomical from −12° to −18°. It is the number people mean when they ask how long twilight lasts, and it is the width of the band on the chart. It is not the span across the whole night, which is a different and much larger number.
- Q: Which crossings does each phase report as its begin and end? → A: The two crossings of that phase's own threshold — the evening one as the phase's *end* (the sun has fallen to −6°) and the morning one as its *begin* (the sun has climbed back to −6°), which is the usage in weather reporting. The band edges around it are the neighbouring thresholds, and the panel labels which threshold each time refers to, so the panel cannot be misread.
- Q: Do twilight figures change the polar statement? → A: No. A date can have a −6° crossing and no sunrise at all (the sun stays below the horizon but not below −6°), so twilight times and "the sun does not rise" are independent facts. The panel says "the sun does not rise" only in the place it already says it, and reports twilight crossings wherever they occur.
- Q: What happens at a high latitude where a threshold is never crossed on that date? → A: That phase simply has no crossing on that date, and the panel says so in words for that phase — "the sun does not reach 6° below the horizon on this date" — while the phases that do occur are still reported.
- Q: Where does the chart live? → A: In the Solar Analysis panel, beside the figures it explains, so the two are read together. It is part of the panel's content, not a separate window.
- Q: Is the chart interactive? → A: No. It marks the selected date and shows the site's latitude and hemisphere. Clicking it changes nothing — it is not a control, and it does not set the time, the site, or the selection.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The User Knows When It Gets Dark (Priority: P1)

A designer finishing a lighting concept for Al Safa Park 2 wants to know when the site falls below usable daylight, and for how long each level of twilight lasts, so they can place lighting that switches on at the right moment. They open the Solar Analysis panel and read, for the date they are working on, not just sunrise and sunset but the three twilight phases with their times and their durations.

**Why this priority**: This is the feature's substance and the smaller of the two. Twilight times are figures a professional already knows how to use and already checks against an almanac; without them the panel answers a question nobody asked. It is also independently testable: one date, one site, a set of numbers.

**Independent Test**: Open the Solar Analysis panel for a known site and date and confirm that three twilight phases are reported with times and durations, that the times agree with an independent published table for the same site and date, and that the reported altitude at each reported threshold reads the same constant everywhere.

**Acceptance Scenarios**:

1. **Given** a site and a normal date, **When** the user reads the figures, **Then** civil, nautical and astronomical twilight are each reported with their times and the length of the phase, alongside the existing sunrise, sunset and day length.
2. **Given** the same site and date at a second site, **When** the user reads the figures, **Then** the altitude reported at each twilight threshold reads the same number as at the first site, because both come from one definition.
3. **Given** a site and date, **When** the user sets the time to a reported twilight threshold, **Then** the altitude figure reads the stated constant for that threshold.
4. **Given** a high-latitude site in midsummer, **When** the sun never reaches 18° below the horizon on that date, **Then** the panel says plainly that astronomical twilight does not occur on that date, and still reports the phases that do occur.
5. **Given** a high-latitude site in midwinter, **When** the sun never rises, **Then** the panel still says, in the place it always says it, that the sun does not rise on this date, and reports whichever twilight crossings occur.
6. **Given** a site and a date, **When** the user compares the panel's twilight times with a published almanac for the same site and date, **Then** they agree within the stated tolerance.
7. **Given** a date the panel could not solve for, **When** the user reads the figures, **Then** the panel says the times could not be determined for that date, in the same place the times would have appeared, and never shows a plausible-looking wrong time.
8. **Given** a site with a daylight-saving transition on the selected date, **When** the user reads the times, **Then** they are correct local clock times for that date, and the panel continues to state which time basis is in use.

---

### User Story 2 - The User Sees the Whole Year At Once (Priority: P2)

A designer reviewing a masterplan wants to understand the site's seasonal character: how long the days are in June versus December, when the site is in its most and least useful light, and how the twilight phases stretch and squeeze with the season. They open the Solar Analysis panel and see a chart of the entire year, with the selected date marked, so they can place themselves in it.

**Why this priority**: This is the visual that makes the seasonal pattern obvious, and it is the one the reference tool leads with. It is second because it is an enhancement over figures that already exist: the numbers for the selected date are available without it. It is also the more expensive of the two — a year of crossings, computed once.

**Independent Test**: Open the panel for a site and confirm the chart draws every day of the year, that the daylight band is wide in the site's summer and narrow in its winter, that the selected date is marked, and that the chart's boundaries for a chosen day match that day's stated sunrise and sunset and twilight times.

**Acceptance Scenarios**:

1. **Given** a site in the northern hemisphere, **When** the user opens the panel, **Then** the chart covers every day of the year and the daylight band is visibly wider in summer than in winter.
2. **Given** a site in the southern hemisphere, **When** the user opens the panel, **Then** the widest part of the daylight band falls in the site's own summer, not the calendar's.
3. **Given** a site near a polar circle, **When** the user opens the panel, **Then** the chart shows the days on which the sun does not set, and the days on which it does not rise, rather than a band that implies a continuous day or night.
4. **Given** a selected date, **When** the user reads the chart, **Then** the selected date is marked, and the site's latitude and the hemisphere it lies in are shown.
5. **Given** any day of the year, **When** the user compares the chart's band boundaries with the panel's stated sunrise, sunset and twilight times for that same day, **Then** they agree.
6. **Given** a site, **When** the user changes the time of day, **Then** the chart does not change and is not recomputed, and the panel stays as responsive as it is without the chart.
7. **Given** a site, **When** the user clicks anywhere on the chart, **Then** nothing changes — the time, the site, the selection and the chart are all unaffected.
8. **Given** a site in a time zone with a daylight-saving transition, **When** the chart draws the transition days, **Then** the day is drawn on the local clock axis, with the repeated hour shown twice and the missing hour empty, and the surrounding days are unaffected.

### Edge Cases

- The sun's centre never reaches −6° on the selected date (high latitude, summer): civil twilight does not occur that day, and the panel says so for that phase only.
- The sun's centre never reaches −18° on the selected date: astronomical twilight does not occur, while civil and nautical may.
- The sun's centre crosses a threshold before local midnight and again after it (a phase that straddles midnight): the panel reports both crossings in the site's local time and does not merge them into a single time on the wrong date.
- The sun's centre crosses a threshold only once on the selected date (a grazing crossing at a limit latitude): the panel says the phase does not occur on that date rather than reporting one time as though it were a pair.
- The solver does not converge for one or more crossings on the selected date: the panel says the times could not be determined, as it already does for rise and set.
- A daylight-saving transition makes the local day 23 or 25 hours long: the day's figures and the chart both stay on the local clock, with the repeated hour twice and the missing hour empty.
- The site's latitude is exactly zero: the chart's band is symmetric about the year's midpoint, and nothing else changes.
- The selected date falls in a leap year: the chart covers 366 days and the selected date is marked in the right place.
- The site's time zone cannot be determined: the panel continues to state the basis it is using, as it already does, and the new figures use the same basis.

## Requirements *(mandatory)*

### Functional Requirements

**Twilight figures**

- **FR-001**: The system MUST report, for the selected site and date, the times at which the sun's centre crosses each of the three twilight thresholds — 6°, 12° and 18° below the horizon — on the way down and on the way up, expressed in the site's local time on the same basis the panel already uses for sunrise and sunset.
- **FR-002**: The system MUST report, for each of the three twilight phases, the length of that phase — the time the sun takes to cross from the preceding threshold to its own — for the selected site and date.
- **FR-003**: The twilight thresholds MUST be solved for by the same solver, with the same convergence guarantees, as the rise and set crossings the panel already reports. The system MUST NOT derive a twilight time from a table of constants, a published almanac, or a fitted approximation.
- **FR-004**: The twilight definition MUST be the sun's **centre** at the stated threshold, unrefracted, and the panel MUST state that the thresholds are defined before atmospheric refraction is applied, because the altitude figure shown beside them is refraction-corrected and therefore reads a different number.
- **FR-005**: At each reported twilight threshold crossing, the panel's reported altitude MUST read the same value at every site and date, and that value MUST be stated or derivable from what the panel states. The values MUST NOT vary between sites, dates or hemispheres.
- **FR-006**: Where a phase does not occur on the selected date because the sun's centre never reaches that threshold, the panel MUST say so in plain words for that phase, naming the threshold, and MUST NOT display a time for it.
- **FR-007**: The sunrise, sunset, day length, time-basis and accuracy statements the panel already reports MUST continue to be reported unchanged, and the panel MUST continue to state the sun does not rise (or does not set) only in the place it states it now.
- **FR-008**: A twilight crossing that could not be solved for MUST be reported the way an unsolved rise or set is reported now: the panel says the times could not be determined, in the same place the times would have appeared, and never shows an approximation as though it were a solved time.
- **FR-009**: The twilight figures MUST be correct across a daylight-saving transition in the site's time zone, and MUST be presented on the site's local clock with the time basis stated, as the other figures are.
- **FR-010**: The twilight figures MUST agree, within the tolerance the solar module already states for rise and set, with an independently published almanac for the same site and date.

**The day-length chart**

- **FR-011**: The Solar Analysis panel MUST present a chart covering every day of the calendar year of the selected date, for the selected site.
- **FR-012**: For each day, the chart MUST show the day's phases in order along the time-of-day axis — night, then astronomical twilight, nautical twilight, civil twilight, daylight, then the same twilight phases in reverse, then night — so that the daylight band is visibly wider in the site's summer and narrower in its winter. **Amended 2026-10-24** — the year runs along the horizontal axis and the local day runs down the vertical axis, from midnight at the top to midnight at the bottom, so the daylight region reads as the year's sunrise and sunset curves. The vertical axis MUST carry hour gridlines with labels, so a curve can be read as a time. The original wording left the orientation open; research D10 chose rows and produced a shape whose bands could not be read as times, which is what the reference tool's own chart — the reason this feature exists — shows. The data is unchanged; only the axes are transposed. See research D10.
- **FR-013**: The chart MUST mark the selected date, and MUST state the site's latitude and which hemisphere it lies in. **Amended 2026-10-24** — for the marked date the chart MUST also label the two times its daylight begins and ends, so the marked column can be read rather than only located.
- **FR-014**: The chart MUST use the same sunrise, sunset and twilight boundaries as the panel's figures for the same site and date, so that a day's band and that day's stated times agree.
- **FR-015**: The chart MUST be read-only. It MUST NOT set the time, MUST NOT change the site, MUST NOT change the selection, and MUST NOT navigate anywhere, whether by pointer or by keyboard.
- **FR-016**: The chart's per-day data MUST be computed once for a given site and year and reused, and MUST NOT be recomputed when only the time of day changes. The panel's existing responsiveness (specs/052 FR-023) MUST NOT be reduced by the chart's presence.
- **FR-017**: On a date affected by a daylight-saving transition, the chart MUST draw that day on the site's local clock, and the day's phases MUST cover it contiguously — a repeated local hour appearing as a proportionally longer night, a missing one as a shorter one — and MUST NOT distort the surrounding days. **Amended 2026-10-23** — the original wording ("the repeated hour drawn twice and the missing hour empty") described a rendering this feature does not produce: the chart's bands are drawn from the day's actual phase sequence, so a 25-hour local day has a longer night rather than a duplicated hour drawn over itself. The requirement now states the property that matters — the day's phases are contiguous and correct — instead of one particular drawing of it.
- **FR-018**: The chart MUST remain legible at the panel's compact width, and MUST remain identifiable in light and dark themes. Its colours MUST come from the theme.
- **FR-019**: The chart MUST be an enhancement over the figures, not a replacement: every quantity the chart conveys MUST also be available as text in the panel, and the chart MUST carry a text description of what it shows for assistive technology.

**Integration**

- **FR-020**: The day-length chart MUST be presented beside the Solar Analysis panel, so that it is read together with the figures it explains, and it MUST NOT open a window of its own separate from the analysis. **Amended 2026-10-23** — the chart was first specified as a new kind *inside* the Solar Analysis panel's content. Planning established that the figures panel is a content panel whose document is rebuilt, re-parsed and re-rendered on every tick of playback, so a year of per-day boundaries inside it would be re-validated every frame (against FR-016 and SC-009), and that adding a kind to the shared vocabulary is a change to an interchange format every panel in the product depends on, for a chart only this feature uses. The chart is therefore a live panel, opened with the analysis — the same mechanism the Time of Day and Building Corrections panels already use, and the shape the reference tool uses for its own day-length chart. The user sees the chart and the figures at the same time, which is what this requirement asked for. See research.md D7, D8 and D14.
- **FR-021**: The feature MUST NOT extend the shared panel content vocabulary, and the vocabulary's version MUST NOT change. **Amended 2026-10-23** — the chart is a live panel rather than a content block (FR-020, research D7), so there is no kind to add, and the vocabulary's version, its committed JSON Schema artifact and the server's model-facing schema are all left as they are.
- **FR-022**: The chart MUST write nothing. The panel's controls continue to write only the canonical instant through the store's setters (specs/052 FR-022); the chart adds no value of its own and no control of any kind.
- **FR-023**: Nothing in this feature may change the solar calculation, the sun path, the shadows, the scene, or any other specification's behaviour.

### Key Entities

- **Twilight Threshold**: one of the three depths at which a twilight phase begins or ends — 6°, 12° or 18° below the horizon — defined on the sun's centre, unrefracted.
- **Twilight Crossing**: the instant at a given site and date at which the sun's centre crosses a given threshold, on the way down or on the way up. A crossing that does not exist on that date is absent rather than estimated.
- **Twilight Phase**: the band between two adjacent thresholds, with its own length for a given day. Its boundaries are shared with the chart.
- **Day Phase Sequence**: the ordered phases of a single day, from local midnight to local midnight, with each phase's start and end. It is the unit the chart draws.
- **Year Profile**: the Day Phase Sequence for every day of a year at a site, with the selected day identified within it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The twilight times the panel reports for a given site and date agree with an independently published almanac for the same site and date within the tolerance the solar module already states for rise and set — one minute within 72° of the equator, ten minutes beyond it.
- **SC-002**: At every reported twilight threshold crossing, the panel's reported altitude reads the same value at every site and date tested, spanning equatorial, mid-latitude and polar sites and both hemispheres.
- **SC-003**: The length of each twilight phase, added to the length of daylight and of the surrounding night, accounts for the whole day at every site and date tested, to within a minute.
- **SC-004**: On a high-latitude site, every date on which a phase does not occur is reported as such in words, and no time is shown for that phase, across a full year of dates tested.
- **SC-005**: The panel's existing figures — sunrise, sunset, day length, the polar statement, the time basis and the accuracy statement — are unchanged in value and in wording after this feature ships, at every site and date tested.
- **SC-006**: The chart's band boundaries for a sampled day of the year match that day's stated sunrise, sunset and twilight times exactly, for at least ten days spread across the year.
- **SC-007**: The chart's daylight band is widest in the site's summer and narrowest in its winter, at sites in both hemispheres.
- **SC-008**: The chart's data is available within one second of the site being established, and the panel's own figures appear without waiting for it.
- **SC-009**: Scrubbing or playing through a whole day with the chart open leaves the panel's response indistinguishable from the panel without the chart.
- **SC-010**: Clicking or keying anything in the chart changes nothing a user can observe — the time, the site, the selection, the panel's contents and the chart itself are all unchanged.
- **SC-011**: Every quantity the chart conveys is also readable as text in the panel, and the chart carries a description of what it shows for assistive technology.
- **SC-012**: The panel's accessibility checks pass with the chart present, in both light and dark themes, and the chart is legible at the panel's compact width.

## Assumptions

- The existing solar module's position algorithm, its refraction treatment and its rise-and-set solver are reused unchanged; this feature adds thresholds, not astronomy.
- The published standard for twilight — the sun's centre at 6°, 12° and 18° below the horizon, unrefracted — is the definition users will check against.
- The panel keeps its compact layout and its existing scroll behaviour; the chart is a compact addition within it.
- The chart is composed for the site's own year, in the site's own local time, exactly as the rest of the panel's figures are.
- The site's time zone and daylight-saving rules are those the panel already uses.
- Verifying the twilight times against a published almanac is done during implementation, against the same kind of published source specs/063 already uses for its own figures.

## Out of Scope

- The analemma, and the Cartesian, stereographic, equidistant and spherical sun-path diagrams.
- Quantitative results: hours of sunlight per surface, irradiance, energy yield, daylight factor, overshadowing or right-to-light compliance. specs/052 placed these out of scope, and they remain out of scope here.
- Comparing two sites, or two dates, side by side.
- Exporting, reporting, printing or saving the analysis or the chart.
- Making the chart interactive, or using it to change the time, the site or the selection.
- Any change to the solar calculation, the sun path, the shadows or the scene.
- Editing footprints, terrain, vegetation, or anything about the site that the panel does not already edit.
