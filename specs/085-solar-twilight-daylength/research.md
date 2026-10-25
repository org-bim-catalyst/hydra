# Research: Solar Twilight Times & Year Day-Length Chart

**Feature**: specs/085-solar-twilight-daylength | **Date**: 2026-10-23

Every decision below is recorded with what was rejected and why, in the house style of
specs/052 and specs/063. Where a decision rests on an external definition rather than on this
codebase's own conventions, the source is named.

---

## D1 — Twilight is defined on the sun's **centre**, geometrically, at −6°, −12°, −18°

**Decision**: a twilight threshold is the instant at which the geometric altitude of the sun's
**centre** reaches −6° (civil), −12° (nautical) or −18° (astronomical).

**Rationale**: this is the definition every published almanac uses, so a user checking a phase
against one finds the same time. NOAA's glossary states civil twilight as "the time of morning or
evening when the sun is 6° below the horizon (solar zenith angle is 96°, solar elevation angle is
−6°)". The National Weather Service states it as "the geometric center of the sun is 6 degrees
below the horizon", and the US Naval Observatory's computational form is a geometric zenith
distance of 96°, 102° and 108° for the three phases. All three name the *centre*, and all three
are geometric.

**Alternatives considered**:

- *The sun's upper edge* (the request's wording). Rejected: the upper-edge convention belongs to
  rise and set alone, where the sun's disc is what appears. No almanac defines twilight that way,
  and it would put every twilight time a fraction of a degree away from the published ones — in
  time, roughly a minute at mid latitudes and more near the horizon, which is the difference
  between agreeing with an almanac and not.
- *Apparent (refraction-corrected) centre at −6°.* Rejected: refraction is not applied to the
  published thresholds, and the module's own refraction model holds the correction at a constant
  0.5749° everywhere below the horizon anyway (see D2), so the two differ by exactly that
  constant and only one of them matches the tables.

**Consequence**: the panel reports an altitude that is *not* the threshold. See D3.

---

## D2 — Solving a threshold crossing uses the module's constant horizon refraction, and the
geometric altitude is recovered by subtracting it

**Decision**: a threshold crossing at geometric centre altitude `h` is solved as the instant at
which the module's **apparent** centre altitude equals `h + HORIZON_REFRACTION_DEGREES`
(0.5749°), because `refraction.ts` holds the correction at that constant for every geometric
altitude at or below −0.575°.

**Rationale**: `solarPosition.ts` returns the *apparent* altitude; the geometric one is not
exposed. `refraction.ts` returns the constant 0.5749° for any geometric altitude ≤ −0.575°, and
every twilight threshold (−6°, −12°, −18°) is far below that, so `apparent = geometric + 0.5749`
holds exactly over the whole range this feature solves. Solving on the apparent target is
therefore exact, not an approximation — and it avoids calling `geometricAltitudeForApparentDegrees`
(a 60-step bisection) inside the Newton iteration, which would multiply the cost of a year of
crossings by sixty.

**Alternatives considered**:

- *Invert the refraction per evaluation.* Rejected: `geometricAltitudeForApparentDegrees` is a
  60-step bisection with no closed form; inside a Newton iteration of ~5 steps × 3 evaluations per
  crossing × 8 crossings per day × 365 days it is roughly two orders of magnitude more work than
  the whole rest of the computation, for no gain in accuracy.
- *Expose a geometric altitude from `solarPosition.ts`.* Rejected as unnecessary: the constant-hold
  makes the subtraction exact, and changing a shared, already-verified function to serve one
  feature is the kind of change the constitution's §2.III simplicity principle exists to prevent.

**Consequence**: the exactness of this rests on the constant-hold remaining in place, which is a
deliberate, documented decision in `refraction.ts`. One test pins it: the geometric centre altitude
at a reported crossing must equal the threshold to within 0.001°, verified through the module's own
inverse (`geometricAltitudeForApparentDegrees`), not through the constant.

---

## D3 — The reported altitude at a threshold reads one constant everywhere, and the panel says why

**Decision**: the panel states that the twilight thresholds are defined on the sun's centre
*before* atmospheric refraction is applied, and the altitude figure shown beside them is the
refraction-corrected one. At a civil crossing the panel therefore reads **−5.43°**, at a nautical
crossing **−11.43°**, at an astronomical crossing **−17.43°** — the same value at every site and
every date, because the correction below the horizon is a constant (D2).

**Rationale**: this is the property specs/063 established for rise and set, applied to the new
figures. There, the reported altitude at sunrise reads one number (−0.27°, the sun's angular
radius) at every site and date, because the definition is the upper edge on the horizon. Here the
definition is unrefracted, so the reported altitude is the threshold plus the constant refraction.
The number is not the threshold, and the panel says so rather than leaving the user to wonder why
"6° below" reads as 5.43°.

**Alternatives considered**:

- *Define the thresholds on the apparent centre so the figure reads exactly −6°.* Rejected: it
  would put the times a fraction of a degree from every published table, which is the whole reason
  D1 chose the published definition. The mismatch is stated instead of engineered away.
- *Report the geometric altitude as a second figure.* Rejected: two altitude figures in one panel
  invites the reader to compare two numbers that differ by a constant, which is the confusion the
  single stated quantity avoids.

---

## D4 — The crossing solver is generalised once, and both the horizon and the thresholds use it

**Decision**: the Newton solver in `daySummary.ts` is extracted into a shared module parameterised
by the quantity being driven to zero, and the twilight crossings use it. The closed-form seed
(cos H₀ / hour angle) is computed for the threshold's own altitude.

**Rationale**: the existing solver is already the right algorithm — Newton on the instant, seeded
from the closed form, converging to 1e-5° of apparent altitude, refusing to return a seed it has
not converged to. Reimplementing it for thresholds would duplicate its convergence guarantees, its
iteration cap, its "a grazing sun has no determinable crossing" branch, and its refusal to
approximate, and any divergence between the two would be a silent difference in the figures.

**Alternatives considered**:

- *A separate twilight solver.* Rejected: two solvers means two sets of convergence guarantees
  and two places where a future fix has to be applied twice.
- *Analytic (no iteration) twilight times.* Rejected: the closed form is only a seed; specs/063
  measured the residual the closed form leaves and it is far outside the stated tolerance.

**Consequence**: `daySummary.ts` loses its private solver and imports it. That is a refactor of a
shipped, verified module, so its own tests stay green and are the guard.

---

## D5 — A phase's length is the time from the preceding threshold to its own, measured on the
evening side

**Decision**: the length of a twilight phase is the duration of the band it occupies — civil
twilight is the time from the horizon to −6°, nautical from −6° to −12°, astronomical from −12° to
−18° — measured from the evening crossings.

**Rationale**: this is the number people mean when they ask how long twilight lasts (civil
twilight runs about half an hour at mid latitudes), and it is exactly the width of the band on the
chart. The alternative — the span from the evening crossing of a threshold to the morning crossing
of the same threshold — is the length of the *night*, not of the phase, and is an order of
magnitude larger.

**Alternatives considered**:

- *The night span.* Rejected for the reason above; the night's own length is derivable from the
  same crossings and is not printed.
- *Both sides.* Rejected: the morning and evening bands differ by a minute or two, and printing
  both invites a question the panel cannot answer usefully ("which is the twilight?").

**Consequence**: at a high latitude where a threshold is not reached on that date, the phase
below it has no length, and the panel says the phase does not occur rather than printing a
number for it.

---

## D6 — A threshold that is not reached is reported per phase, and is not a rise/set statement

**Decision**: whether a threshold is reached on a given date is decided by that threshold's own
hour-angle bound (|cos H₀| > 1 means the sun never crosses it that day), and the panel says so for
that phase alone. The sun-does-not-rise and sun-does-not-set statements stay exactly where they
are, and are still about the horizon.

**Rationale**: the two facts are independent. At 69° N in late December the sun does not rise, but
it does descend below −6°, −12° and −18° during the day, so three twilight crossings exist on a
date the panel also calls a polar night. Conversely, in midsummer the sun may never descend below
−6°, so no civil twilight occurs on a date with an ordinary sunrise. Reporting "the sun does not
rise" because a twilight phase is missing would be a false statement, and reporting the phase as
missing because the sun did not rise would hide a real crossing.

**Alternatives considered**:

- *One combined polar verdict for the whole panel.* Rejected: it would either suppress real
  crossings or imply the sun did not rise when it did.
- *Silently omitting the phase.* Rejected: specs/052 FR-045 and the constitution's §2.VIII both
  require a failure to be stated rather than left as an absence.

---

## D7 — The chart is a **live panel** registered by the solar extension, not a new content kind

**Decision**: the day-length chart is a **live panel** — a React component with its own state,
registered through the extension's existing `context.registerLivePanelKind(...)` path and opened
alongside the figures panel when the analysis activates. The shared panel content vocabulary is
**not** extended, and its version does not move.

**Rationale**: two findings, each of which independently rules the content-kind route out.

*The vocabulary is an interchange format with a server-side contract.* specs/049's "Extending the
vocabulary" says adding a kind means a zod schema, a renderer, a registry entry, a regenerated
JSON Schema artifact and a version increment. The artifact is not decoration: it is compared
against the client's zod source by one test and against the server's model-facing schema literal by
another, so a version increment moves three coupled places and every panel document in the product
depends on the result. Doing that for a chart only the solar feature uses — and which the model
could never compose meaningfully, since it cannot compute a sunrise — is a large blast radius for
a small feature.

*The figures panel's document is rebuilt on every tick.* The overlay's effect depends on the current
moment, so during playback the document is rebuilt, re-opened, re-parsed and re-rendered every
frame. A year's worth of per-day boundaries inside that document would be re-validated and
re-rendered every frame (D9, D10) — directly against spec FR-016 and SC-009.

A live panel is the mechanism the solar feature already uses for its two other panels — the time
control and the building corrections — and research D12 in specs/052 sets out exactly why: a
panel that owns computed state and its own rendering is interactive code, which is what a live
panel kind is for; the figures are data, which is what the content vocabulary is for. The chart is
the former. The reference tool puts its own day-length chart in a separate pane too.

**Alternatives considered**:

- *A new content block kind.* Rejected for the two reasons above.
- *A bespoke component inside the solar module, outside the panel framework.* Rejected: a panel's
  contents come from the framework, not from the feature.
- *Extending the existing `chart` kind with a new `chartKind`.* Rejected: the kind's schema and
  its server-side artifact still have to move, and the grouped-bar/line renderer cannot express
  a continuous band through each day.

**Consequence**: the shared vocabulary stays at version 1, the committed artifact is unchanged,
the server's schema is unchanged, and the feature touches no file outside `features/solar/` and
the one extension shim that already registers the solar feature's panels.

---

## D8 — No version increment, because no kind is added

**Decision**: the vocabulary version stays at 1. The version exists so that a change to the
vocabulary is a deliberate, identifiable event (specs/049 FR-008); this feature does not change
the vocabulary, so there is nothing to identify.

**Rationale**: D7 removed the kind addition that the version increment was required for. The
mechanics that made an increment hazardous in the first place — a client envelope that validates
every content panel, a server-side model-facing schema, and a committed JSON Schema artifact
guarded by two tests — are all left alone, which is the whole point.

**Alternatives considered**:

- *Bump the version anyway.* Rejected: the version would then describe a vocabulary change that
  did not happen, and every document in the product would carry a version number that means
  nothing different.

**Consequence**: nothing to regenerate, nothing to keep in step, and no test changes outside the
solar feature's own.

---

## D9 — The chart's per-day data is computed once per site and year, and cached

**Decision**: the year profile is computed once for a (site, year) pair, cached, and reused. The
figures panel is rebuilt on every time-of-day tick, so the profile is held outside the document
and referenced by it.

**Rationale**: the Solar Analysis panel's content document is rebuilt and re-opened on every
playback frame (the overlay's effect depends on the current moment). A year of crossings is
8 crossings × 365 days = 2 920 Newton solves, each a handful of solar-position evaluations —
a few tens of milliseconds, once. Rebuilding that inside every frame would be the single largest
cost in the feature and would undo the responsiveness specs/052 FR-023 works to protect.

**Alternatives considered**:

- *Computing the profile per frame.* Rejected for the reason above.
- *A Web Worker.* Rejected: the computation is tens of milliseconds, once per site and year. A
  worker adds a build step, a message protocol and a failure path for no measurable gain.
- *Storing the profile on the server.* Rejected: the whole solar feature computes in the browser
  (specs/052 research D1), the profile depends only on latitude, longitude and the year, and
  sending it to the server would put a per-user, per-year computation on the backend for a
  read-only figure.

**Consequence**: the panel renders the chart from a cached profile, and the profile is keyed by
site and year, so changing the site or the year rebuilds it and nothing else does.

---

## D10 — The chart is drawn on a canvas, at the panel's compact width

**Decision**: the chart renders into a canvas, one column per day of the year — the year along the
horizontal axis, the local day down the vertical axis from midnight at the top — with hour
gridlines and labels, and the selected date's two daylight crossings labelled with their times.

**Amendment (2026-10-24)**: this decision first read "one row per day, the horizontal axis being
the local day", and that was built. It was the wrong way round: with hours across a 300 px width,
a row is a third of a pixel tall and the day band can only be read as a shape, never as a time —
the readings the user asked for (when does it get light, when does it get dark) had to be counted
off the figures instead. Transposing it costs nothing: `DayPhaseSequence` already carries
`startMinute`/`endMinute` in local minutes, so the same segments map onto either axis. The vertical
axis is now the day's own span, normalised per column exactly as the row form normalised per row,
so a 23- or 25-hour DST day stays contiguous (FR-017). The selected date's readings come from the
same daylight segment the band is drawn from, so the label cannot disagree with the curve.

**Rationale**: the Solar Analysis panel's default size is 260 × 320, and a year of days is 365
rows. At that size each row is a third of a pixel: a row-per-day chart cannot be drawn as DOM
elements (3 000+ nodes) and cannot be read as individual rows at all — it reads as a gradient,
which is what the feature is for ("the year reads as a gradient that widens and narrows with the
seasons"). A canvas draws it in one paint, and re-paints only when the profile or the selected
date changes.

**Alternatives considered**:

- *SVG rectangles per segment.* Rejected: about 3 300 nodes for the year, re-created whenever the
  panel re-renders, and unmeasurable by assistive technology either way — the chart is an
  enhancement over text figures (spec FR-019), not a data source.
- *Aggregating to weeks or months.* Rejected: the specification says every day of the year, and
  the seasonal shape is already legible at day resolution because the rows merge into a gradient.
- *A separate, larger chart panel.* Rejected by FR-020: the chart belongs with the figures.

**Consequence**: the chart carries a text description for assistive technology, and the panel's
text figures carry the same quantities, so nothing is available only through the picture.

---

## D11 — The chart draws named series, and the colours come from the theme, not from the chart

**Decision**: the chart draws a series of bands per day, each band labelled with a name the
solar module chooses (night, astronomical twilight, nautical twilight, civil twilight, daylight),
and each band's colour is resolved by the solar module from the MUI theme and handed to the
chart. The chart itself knows nothing about the sun.

**Rationale**: the constitution's §7 theming rule says components must not hardcode colours that
bypass the theme, and a chart with five fixed colours would need its own light and dark variants.
Resolving the colours where the theme is available means the chart follows the theme automatically,
and it keeps the chart reusable for any set of named bands.

**Alternatives considered**:

- *The chart hardcoding five colours.* Rejected: it would need a second set for the dark theme, and
  the theme's palette is the one place the application's colours are decided.
- *One colour per phase, drawn by the chart.* Rejected for the same reason.

**Consequence**: the solar module's copy and theme files carry the phase names and their colours.

---

## D12 — A phase that does not occur, and a day that has no sunrise, are separate facts in the
chart as well as in the text

**Decision**: a day's row in the chart is built from that day's crossings. A day with no sunrise
(polar night) is a single night band; a day with no sunset (midnight sun) is a single day band; a
day on which a threshold is not reached has that phase simply absent from the row.

**Rationale**: the chart and the figures are read together, and a chart that disagreed with the
figures beside it would be worse than no chart. Building both from one set of crossings makes that
impossible rather than merely likely.

**Consequence**: one crossing computation feeds the figures, the chart, and the day-length summary.
There is no second implementation of "when does the sun cross −6°" anywhere in the feature.

---

## D13 — The year's extremes are stated in text, because the chart alone is not enough

**Decision**: the longest and shortest day of the year, with their dates and lengths, are printed
in the panel as text beside the chart.

**Rationale**: the chart shows the shape; two numbers name it. A user who wants "when is the
shortest day" should not have to read a picture, and a screen-reader user cannot read the picture
at all. specs/052 FR-031 and FR-032 require the figures to carry the information in text and the
chart to be an enhancement over them.

**Consequence**: the panel states the extremes for the site's own year, computed from the same
profile the chart draws.

---

## D14 — The chart opens beside the figures panel, and the specification is amended to say so

**Decision**: the chart is its own live panel, opened when the solar analysis activates and
positioned beside the Solar Analysis panel. The specification's FR-020 (the chart is presented
*as part of* the Solar Analysis panel's content, and must not open a separate window) is amended
on 2026-10-23 to describe the live panel, and FR-021 (the vocabulary is extended for the chart)
is amended to record that it is not.

**Rationale**: D7 settles the mechanism, and the mechanism settles the placement. The reference
tool this feature was modelled on shows its own day-length chart in a pane separate from its solar
information box, and the platform's own solar analysis already presents three separate panels
beside the scene — the Time of Day control, the Building Corrections panel, and the figures
panel. A fourth is a familiar shape, and it is the only one that keeps the figures panel's
per-tick document cheap (D9, D10).

**Alternatives considered**:

- *Keeping the chart inside the figures panel.* Rejected: it puts a year's per-day data into a
  document that is rebuilt, re-parsed and re-rendered on every frame of playback, which is the
  cost the specification's own FR-016 and SC-009 forbid.

**Consequence**: the user sees the chart and the figures at the same time, which is what the
specification asked for; they are two panels rather than one. The amendment is recorded in the
specification with this reason attached, and the user is told.
