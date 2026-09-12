# Feature Specification: Solar Analysis

**Feature Branch**: `052-solar-analysis`

**Created**: 2026-09-12

**Status**: Draft

**Input**: User description: "Sun path, real building shadows and time scrubbing over a site — the first capability built entirely on the viewer extension framework."

## Context

An architect or engineer looking at a site wants to know where the sun goes. Which facades get morning light. How far the building across the street throws its shadow at four in the afternoon in December. Whether the courtyard sees any sun at all in winter. These are among the first questions asked at the start of a project, and answering them today means leaving the workspace for a separate tool.

This feature answers them in place. It is also the proof that the three specifications before it were sufficient: it is the first capability built entirely as an extension, the first to draw real geometry positioned in the world, the first to declare drawing requirements, the first to need a per-frame subscription, and the first to contribute both a live panel and a viewer toolbar entry. If the framework is wrong, this is where it shows.

A working reference implementation exists. It is a specification of behaviour, not code to adopt: it creates its own drawing surface, which this implementation must not do, and it manages its resources in ways the framework now prevents.

## Clarifications

### Session 2026-09-12

- Q: Times in UTC, as the reference implementation uses, or local to the site? → A: Local to the site. UTC is an artefact of the prototype and would read as wrong to anyone using this professionally — "shadows at 14:00" means local afternoon, not an offset from Greenwich. The underlying calculation is unchanged; only presentation and input differ.
- Q: Does this feature produce quantitative results — hours of sunlight per surface, irradiance, overshadowing compliance? → A: No. This release is visual and qualitative: where the sun is, where shadows fall, how they move. Quantitative analysis is a substantially larger feature with its own accuracy, validation and liability obligations, and belongs in its own specification.
- Q: Where does building data come from? → A: Fetched by the platform rather than by the browser, reusing the existing arrangement that already retrieves map data for site boundaries. This gives caching, rate limiting and a single place to handle the source being slow or unavailable, and treats third-party geometry as untrusted at the correct boundary.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See Where the Sun Is and Where It Goes (Priority: P1)

A user looking at a site picks a date and sees the sun's path across the sky above it — today's arc, the summer and winter extremes, the hours marked along the way — together with the numbers: where the sun is now, when it rises and sets, how long the day is. They can see immediately how the site is oriented against the sun.

**Why this priority**: This is the smallest slice that answers a real question, and it needs no building data at all — so it works everywhere, including places where no building information exists. It is also the foundation the shadow work builds on.

**Independent Test**: Can be fully tested by opening solar analysis on a site, choosing a date, and confirming the sun path and the numeric readings are correct against an independent reference for that location and date.

**Acceptance Scenarios**:

1. **Given** a site is shown in the viewer, **When** the user opens solar analysis, **Then** the sun path for the current date appears above the site, correctly oriented against north.
2. **Given** solar analysis is open, **When** the user reads the figures, **Then** they see the sun's current position, sunrise, sunset and day length for that site and date, in the site's local time.
3. **Given** solar analysis is open, **When** the user changes the date, **Then** the path updates for the new date.
4. **Given** a date is chosen, **When** the user looks at the path, **Then** the summer and winter extremes are distinguishable from the chosen day's path, and hours are marked along it.
5. **Given** a site where the sun does not rise or does not set on the chosen date, **When** the figures are shown, **Then** this is stated plainly rather than shown as missing or nonsensical values.
6. **Given** the user moves to a different site, **When** the viewer settles, **Then** the sun path and figures update for the new location.

---

### User Story 2 - See Real Shadows at a Moment (Priority: P1)

The user sets a date and time and sees shadows cast by the actual buildings around the site, on the actual ground. Not a diagram — the real footprints, at their real heights, casting where they really would.

**Why this priority**: This is the question users actually come with, and the reason a sun path alone is not enough. It depends on User Story 1 for the sun's position, and carries most of this feature's technical substance.

**Independent Test**: Can be fully tested by opening solar analysis on a site with known surrounding buildings, setting a known date and time, and confirming shadows fall in the correct direction and at plausible lengths against an independent reference.

**Acceptance Scenarios**:

1. **Given** a site with mapped surrounding buildings, **When** solar analysis opens, **Then** those buildings appear as solid forms at their recorded heights.
2. **Given** buildings are shown and the sun is above the horizon, **When** the user views the site, **Then** each building casts a shadow in the direction and at the length the sun's position implies.
3. **Given** the sun is below the horizon, **When** the user views the site, **Then** no shadows are cast and this is evident rather than ambiguous.
4. **Given** a building whose height is not recorded in the source data, **When** it is shown, **Then** a stated default is used and the user can see that the height was assumed rather than known.
5. **Given** the site has no mapped buildings nearby, **When** solar analysis opens, **Then** the user is told so, and the sun path still works.
6. **Given** building data cannot be retrieved, **When** solar analysis opens, **Then** the user is told, and the sun path still works.

---

### User Story 3 - Watch Shadows Move Through the Day (Priority: P2)

The user drags through the day and watches the shadows sweep across the site, or presses play and lets it run. The figures track as they go. In a few seconds they understand the site's whole day.

**Why this priority**: This is what makes the feature persuasive rather than merely informative — a static shadow answers one question, a moving one answers the question the user did not know to ask. It depends on the first two stories.

**Independent Test**: Can be fully tested by dragging through a full day and confirming shadows and figures update smoothly and consistently, and by playing the day through and confirming it animates without degrading the viewer.

**Acceptance Scenarios**:

1. **Given** solar analysis is open, **When** the user moves through the time of day, **Then** the sun position, shadows and figures all update together and remain consistent with one another.
2. **Given** the user is moving through time, **When** they do so quickly, **Then** the viewer stays responsive and the display keeps up rather than lagging behind or freezing.
3. **Given** the user starts playback, **When** the day runs, **Then** time advances continuously and can be stopped, leaving the display at the moment it stopped.
4. **Given** playback is running, **When** the user does something else in the viewer, **Then** the viewer remains usable.
5. **Given** playback is running, **When** the user closes solar analysis, **Then** playback stops and everything it was drawing is removed.

---

### User Story 4 - Correct the Building Data (Priority: P2)

The mapped height of the building on the site is wrong, or was never recorded. The user corrects it and the shadows update. They can also raise or lower the analysis relative to the ground where the terrain or the data needs it.

**Why this priority**: Source building data is frequently incomplete or wrong, and an analysis the user knows to be based on a bad number is worse than no analysis. This makes the feature trustworthy. It depends on User Story 2.

**Independent Test**: Can be fully tested by changing the height of a building and confirming its shadow changes correspondingly, and by adjusting the ground offset and confirming the analysis moves with it.

**Acceptance Scenarios**:

1. **Given** a building is shown, **When** the user views its details, **Then** they can see its height and whether that height was recorded in the source or assumed.
2. **Given** a building's height is wrong, **When** the user corrects it, **Then** the building and its shadow update to match.
3. **Given** the analysis sits at the wrong level relative to the ground, **When** the user adjusts the offset, **Then** the analysis moves accordingly.
4. **Given** the user enters an impossible height or offset, **When** they apply it, **Then** they are told it is not valid and the previous value is kept.
5. **Given** the user has made corrections, **When** they move to a different site, **Then** it is clear that corrections apply to the site they were made on.

---

### User Story 5 - Ask Lucy About the Sun (Priority: P3)

The user asks Lucy about sunlight on the site in plain language. She runs the analysis, opens it in the viewer, and explains what it shows — in prose, not by reciting numbers the user can see.

**Why this priority**: It makes the capability reachable by asking rather than by knowing where the control is, which is the platform's whole premise. It depends on the analysis existing, and the feature is fully usable without it.

**Independent Test**: Can be fully tested by asking Lucy about sunlight or shadows on the active site and confirming she opens the analysis and describes what it shows.

**Acceptance Scenarios**:

1. **Given** a site is shown, **When** the user asks Lucy about sun or shadows there, **Then** she opens solar analysis for that site and says what it shows.
2. **Given** Lucy has opened the analysis, **When** the user reads her reply, **Then** it refers to what is on screen rather than restating figures already visible.
3. **Given** no site is shown, **When** the user asks about sunlight, **Then** Lucy says a site is needed first rather than opening an empty analysis.
4. **Given** the analysis could not be produced, **When** Lucy replies, **Then** she says so and why, rather than describing results that do not exist.

---

### Edge Cases

- What happens at extreme latitudes where the sun does not rise or set for the chosen date? Figures must state this plainly rather than showing blanks or nonsensical values.
- What happens on the days either side of a daylight-saving transition? Local times must remain correct and unambiguous through the change.
- What happens when the site sits inside a building footprint, or between several? The building the analysis treats as the site's own must be chosen by a stated rule.
- What happens when building footprints overlap or are self-intersecting in the source data? They must be handled or excluded without failing the whole analysis.
- What happens when a very large number of buildings surround the site? The analysis area must be bounded, and the user told if data was limited.
- What happens when shadows would fall outside the analysis area? The extent must be defined so that shadows are either correctly shown or clearly bounded, never rendered as a spurious dark region.
- What happens when the user changes date or time while building data is still loading? The display must stay consistent rather than mixing old and new.
- What happens when solar analysis is open and the viewer's content is replaced beneath it? The analysis must follow the new site or close, not continue against content that has gone.
- What happens when the device cannot support the drawing this requires? The user must be told, consistent with the viewer's existing behaviour when 3D is unavailable.
- What happens when the site's time zone cannot be determined? The time basis in use must be stated rather than silently assumed.

## Requirements *(mandatory)*

### Solar Position

- **FR-001**: The system MUST compute the sun's position — its compass direction and its height above the horizon — for a given location, date and time, to a stated accuracy.
- **FR-002**: The system MUST compute sunrise, sunset and day length for a given location and date, and MUST state plainly when the sun does not rise or does not set.
- **FR-003**: All times MUST be presented and entered in the site's local time, with the time basis in use stated, including when it cannot be determined.
- **FR-004**: Local times MUST remain correct and unambiguous across daylight-saving transitions.

### Sun Path

- **FR-005**: The system MUST show the sun's path across the sky above the site for the chosen date, correctly oriented against north.
- **FR-006**: The sun path MUST distinguish the chosen day's path from the seasonal extremes, and MUST mark hours along the chosen day's path.
- **FR-007**: The sun's current position for the chosen time MUST be shown on the path.
- **FR-008**: The sun path MUST update when the date, the time or the site changes.

### Surrounding Buildings

- **FR-009**: The system MUST retrieve building footprints within a bounded distance of the site, through the platform rather than directly from the browser.
- **FR-010**: Building footprints MUST be shown as solid forms at their recorded height, using a stated default where no height is recorded.
- **FR-011**: The user MUST be able to see, for the building being analysed, whether its height was recorded in the source or assumed.
- **FR-012**: The system MUST identify which building the site sits in or nearest to, by a stated rule.
- **FR-013**: Footprints that cannot be used MUST be excluded without failing the whole analysis.
- **FR-014**: When no buildings are found, or building data cannot be retrieved, the user MUST be told, and the sun path MUST still work.
- **FR-015**: The number of buildings retrieved MUST be bounded, and the user MUST be told when data was limited.

### Shadows

- **FR-016**: Buildings MUST cast shadows consistent with the sun's computed position, onto the ground and onto each other.
- **FR-017**: No shadows MUST be cast when the sun is below the horizon, and this MUST be evident to the user.
- **FR-018**: The area within which shadows are computed MUST be defined and sized to the analysis area, so that no spurious shadowed region appears outside it.
- **FR-019**: Shadows MUST update whenever the sun's position, the buildings or their heights change.

### Time Control

- **FR-020**: The user MUST be able to choose the date and move through the time of day.
- **FR-021**: The user MUST be able to play the day through continuously and stop it, leaving the display at the moment it stopped.
- **FR-022**: The sun position, shadows and figures MUST update together and remain consistent with one another as time changes.
- **FR-023**: The viewer MUST remain responsive while the user moves through time or plays the day through.
- **FR-024**: Closing solar analysis MUST stop playback and remove everything it was drawing.

### Corrections

- **FR-025**: The user MUST be able to correct the height of the building being analysed, and the building and its shadow MUST update accordingly.
- **FR-026**: The user MUST be able to adjust the analysis's offset relative to the ground.
- **FR-027**: Invalid heights and offsets MUST be rejected with an explanation, keeping the previous value.
- **FR-028**: It MUST be clear which site a correction applies to.

### Presentation

- **FR-029**: Solar analysis MUST be offered through an entry in the viewer's own toolbar.
- **FR-030**: The time controls and the building corrections MUST be presented as panels the user can move, minimise and close.
- **FR-031**: The solar figures MUST be presented as panel content rather than as a purpose-built component.
- **FR-032**: All panels and controls MUST meet the platform's accessibility standard, including keyboard operability.

### Lucy

- **FR-033**: Lucy MUST be able to open solar analysis for the active site and describe what it shows.
- **FR-034**: Lucy's reply MUST refer to what is displayed rather than restating figures the user can already see.
- **FR-035**: Lucy MUST NOT open solar analysis when no site is shown; she MUST say a site is needed.
- **FR-036**: When the analysis cannot be produced, Lucy MUST say so and why, rather than describing results that do not exist.

### Framework Conformance

- **FR-037**: Solar analysis MUST be built as an extension, and MUST reach the viewer only through the extension context.
- **FR-038**: Solar analysis MUST declare the drawing features it requires rather than changing the viewer's drawing settings itself.
- **FR-039**: Solar analysis MUST request redraws rather than driving the viewer's drawing, and MUST use a per-frame subscription only while it genuinely needs one.
- **FR-040**: Stopping solar analysis MUST remove everything it drew, release every resource it held, and withdraw every panel and toolbar entry it contributed.
- **FR-041**: Solar analysis MUST position everything it draws through the viewer's published coordinate conversion, and MUST NOT set its own reference point.
- **FR-042**: When the viewer's content is replaced while solar analysis is open, it MUST follow the new site or close, and MUST NOT continue against content that has gone.

### Stated Limits

- **FR-043**: The product MUST state that this is a design-stage study and not a certified analysis, where a user encounters the results.
- **FR-044**: The accuracy of the solar position calculation, and the fact that building heights may be assumed, MUST be discoverable by the user.

### Failure

- **FR-045**: Every failure — building data unavailable, no buildings found, time zone undetermined, drawing unsupported — MUST produce a user-understandable explanation and MUST be recorded for diagnosis.
- **FR-046**: No failure introduced by this feature may be observable only in logs.

### Key Entities

- **Site**: The location being analysed — its position on the earth, and the local time basis that applies there.
- **Solar Position**: Where the sun is for a site at a moment — its compass direction and height above the horizon.
- **Day Summary**: Sunrise, sunset and day length for a site and date, including the cases where the sun does not rise or set.
- **Sun Path**: The sun's track across the sky for a date, with the seasonal extremes for comparison and hours marked along it.
- **Building**: A footprint near the site with a height, a record of whether that height was known or assumed, and whether it is the building the site sits in.
- **Analysis Moment**: The date and time currently being shown.
- **Correction**: A user-supplied height or ground offset that overrides the source data for a site.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Sun position, sunrise, sunset and day length agree with an independent reference within a stated tolerance, across a set of test locations spanning equatorial to high latitudes and dates spanning the year.
- **SC-002**: Shadow direction and length are correct for the computed sun position, verified against an independent reference at known locations, dates and times.
- **SC-003**: A user can go from a site in the viewer to understanding its sunlight across a full day in under a minute.
- **SC-004**: Moving through the time of day updates the display smoothly enough to read as continuous motion, and the viewer stays responsive throughout.
- **SC-005**: Local times are correct across daylight-saving transitions at 100% of tested transition dates.
- **SC-006**: Opening and closing solar analysis 50 times returns the viewer's resource use to its starting level, with no accumulation.
- **SC-007**: Closing solar analysis leaves nothing it drew or contributed behind — zero residual geometry, panels, toolbar entries or subscriptions.
- **SC-008**: Every failure produces a user-visible explanation — zero failures are observable only in logs.
- **SC-009**: Solar analysis is built without requiring any change to the viewer core, the extension framework, or the panel framework — the measure of whether specifications 049 to 051 were sufficient.
- **SC-010**: A user encountering the results can discover that this is a design-stage study, the accuracy of the calculation, and whether the building heights used were known or assumed.

## Scope

### In Scope

- Solar position, sunrise, sunset and day length for a site, date and time, in site-local time.
- The sun path for a date, with seasonal extremes, hour marks and the current position.
- Retrieval and display of surrounding building footprints at their recorded or assumed heights.
- Shadows cast by those buildings, onto the ground and onto each other.
- Date selection, movement through the day, and playback.
- User correction of building height and of the analysis's ground offset.
- A viewer toolbar entry, the live control panels, and the figures as panel content.
- Lucy's ability to open and explain the analysis.
- The stated limits of the analysis, visible where results are encountered.

### Out of Scope

- Quantitative results: hours of sunlight per surface, irradiance, energy yield, daylight factor, overshadowing or right-to-light compliance. Each carries accuracy, validation and liability obligations well beyond a visual study, and belongs in its own specification.
- Shadows cast by, or onto, terrain and vegetation. The ground is treated as level at the analysis offset.
- Reflections, inter-reflection, sky luminance and any treatment of atmosphere or weather.
- Editing building footprints, or adding buildings that are not in the source data.
- Exporting, reporting or saving an analysis. Corrections are not persisted beyond the session.
- Comparing two dates, two sites or two design options side by side.
- Shadows cast by models the user loads, as distinct from mapped buildings. This may follow once model loading is established.
- Any change to the viewer, extension or panel frameworks. Needing one is a signal that an earlier specification was incomplete.

## Assumptions

- Building footprint data comes from the same source the platform already uses for site boundaries, retrieved through the platform with its existing caching and failure handling. That source's coverage and quality vary by place, which is why stated defaults and user correction are requirements rather than refinements.
- The solar calculation runs where the display is, so that moving through the day is immediate. Where Lucy needs the same figures to explain them, they are obtained consistently rather than computed twice by different means.
- The ground is treated as level at the analysis offset. Terrain is out of scope, and the ground offset exists precisely so a user can compensate where that assumption is poor.
- A bounded radius around the site is sufficient for the buildings that meaningfully shadow it. The bound is stated, and the user is told when data was limited.
- Corrections apply to the session and the site they were made on, and are not persisted. Persisting them implies a model of site records that does not exist yet.
- The site's local time basis is derived from its position. Where it cannot be determined, the basis in use is stated rather than assumed silently.
- The published solar algorithm used is a well-established one whose accuracy is documented, so the stated tolerance is inherited rather than invented.
- Accessibility for a continuously-updating spatial display means the controls and the figures are fully operable and readable by keyboard and assistive technology; the spatial display itself is understood to be a visual medium with a textual equivalent in the figures.

## Dependencies

- **specs/051-viewer-scene-content-api** — supplies scene access, the coordinate conversion and reference point, declared drawing requirements, redraw scheduling, per-frame subscriptions and resource tracking. Everything this feature draws depends on it.
- **specs/050-viewer-extension-framework** — supplies the extension contract, the context, the viewer toolbar and contribution teardown.
- **specs/049-panel-content-model** — supplies live panels for the controls, and content panels for the figures.
- **specs/042-site-boundary-resolution** — supplies the existing arrangement for retrieving map data, which building retrieval reuses. Note the recorded sensitivity of that arrangement to slow responses.
- **specs/045-conversational-agent-runtime** — supplies the mechanism by which Lucy offers and invokes the analysis.

## Downstream

Quantitative solar analysis — sunlight hours, irradiance, compliance assessment — is the natural successor and is deliberately excluded here. Shadows from user-loaded models become possible once model loading from specs/051 is established.
