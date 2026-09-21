# Feature Specification: Solar Analysis Accuracy & Performance

**Feature Branch**: `063-solar-accuracy-performance`

**Created**: 2026-09-21

**Status**: Draft

**Input**: User description: "A correctness-and-performance pass over the existing specs/052-solar-analysis capability, with no new user-facing features: refraction correction so the altitude figure agrees with the module's own sunrise/sunset times; merged footprint geometry; a shadow camera fitted to actual content rather than a fixed radius ratio; no shadow-map re-render when the sun has barely moved; and static dome furniture separated from the date-dependent day arc."

## Context

Solar analysis (specs/052) shipped as a working capability: sun path, real building shadows, time scrubbing. This specification does not add to what it does. It corrects one place where the feature contradicts itself, and removes four places where it does more work than the result requires.

The correctness item is the reason this is a specification rather than a maintenance ticket. The feature reports a solar altitude computed as the sun's *true geometric* position, and separately reports sunrise and sunset times computed against the standard **−0.833°** reference altitude that accounts for atmospheric refraction and the sun's apparent radius. Both are correct in isolation and they disagree with each other: at the sunrise time this feature itself computes and displays, the altitude figure this feature itself displays reads approximately −0.83°, not 0°. A user checking one number against the other finds the tool inconsistent, and the inconsistency is largest at exactly the low-sun moments the feature exists to explain.

The four performance items share a cause: work that is correct but unconditional. Per-building meshes and materials where one merged geometry would serve; a shadow camera sized to a fixed multiple of the analysis radius rather than to the geometry actually in it; a shadow map recomputed every frame of playback regardless of whether the sun moved enough to change it; and the entire sun-path dome — compass dial, mount post, shell, twelve-month lattice — rebuilt whenever the date changes, though only the day arc depends on the date.

This work is bounded by the five constraints recorded in `src/AskLucy.Web/ClientApp/src/features/solar/README.md`, which were discovered the hard way by the reference prototype and are regressions rather than style preferences if lost. One of them is in direct tension with the shadow-camera item and is raised as a clarification below.

## Clarifications

### Session 2026-09-21

- Q: Is this release allowed to change what users see, or only what they wait for? → A: Only the refraction correction changes a displayed value, and it changes it by less than a degree. Everything else must be visually indistinguishable from today's output except for being sharper and arriving sooner. Any change that alters shadow position, path geometry, or figure values beyond the refraction correction is out of scope and is a defect in this release, not a feature of it.
- Q: Does this release change the stated accuracy tolerances? → A: The rise/set tolerances are inherited from NOAA and do not change. The position tolerance is measured rather than inherited, and must be re-measured against the corrected quantity rather than assumed to carry over.
- Q: Does the refraction-corrected altitude drive the rendered sun direction as well as the displayed figure, or only the figure? → A: Everywhere — the figure, the light, the shadows and the sun marker all use the corrected altitude. Shadows are cast by the light that actually arrives at the site, which is the bent light, so the corrected position is the more truthful one for rendering and not merely a display convenience. Keeping a single altitude in the system is also the point of the exercise: two values that are supposed to agree are precisely how the original defect arose. The consequence is accepted — shadow directions shift by up to roughly half a degree at the horizon and are unchanged above about fifteen degrees, so SC-009 is stated as bounded by the correction rather than as bit-identical.
- Q: How is the single-radius invariant from specs/052 constraint 4 re-established once shadow extents stop being a fixed multiple of the analysis radius? → A: By keeping one radius, and deriving it from the geometry actually present rather than from the fixed 1.3 multiplier. The shadow region and the ground plane continue to come from that single value exactly as they do today, so the invariant is preserved literally rather than replaced by a substitute rule, and the recorded grey-blob failure remains structurally impossible. Inspection of the reference prototype during drafting showed the current multiplier has no derivation behind it — the prototype hard-codes a fixed ±260 m extent that happened to suit its test site, and `SHADOW_FRUSTUM_RATIO` is that same number expressed as a formula. This decision therefore supplies a basis where none existed rather than replacing a considered design. A direction-dependent extension of the region at low sun is deferred: it breaks the single-value invariant, and is only worth that cost if measurement against SC-005 and SC-006 shows the content-derived radius alone to be insufficient.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The Numbers Agree With Each Other (Priority: P1)

A user reads the figures panel, notes the sunrise time, scrubs the time control to that exact moment, and reads the sun's altitude. It says the sun is on the horizon. Today it says the sun is roughly eight tenths of a degree below the horizon, which invites the reasonable conclusion that something in the tool is wrong.

**Why this priority**: This is the only item in this release that is a correctness defect rather than an optimisation. A tool whose own figures contradict each other loses the user's trust in every other number it shows, including the ones that are right. It is also independent of all four performance items and can ship alone.

**Independent Test**: Can be fully tested by setting the time to the feature's own computed sunrise for a known location and date and confirming the reported altitude is zero within tolerance, and by confirming reported altitudes across a full day still match published reference values for that location within the stated position tolerance.

**Acceptance Scenarios**:

1. **Given** a site and date with a normal sunrise, **When** the user sets the time to the sunrise the figures panel reports, **Then** the reported solar altitude reads 0.0° within the stated tolerance.
2. **Given** a site and date with a normal sunset, **When** the user sets the time to the reported sunset, **Then** the reported solar altitude reads 0.0° within the stated tolerance.
3. **Given** the sun is high in the sky, **When** the user reads the altitude, **Then** it is unchanged from today's value within the stated tolerance, because the correction is negligible above roughly fifteen degrees.
4. **Given** any moment of any day at a site, **When** the reported altitude is compared against an independent published reference for the same quantity, **Then** it agrees within the stated position tolerance.
5. **Given** the figures panel is read, **When** the user looks for what the altitude figure means, **Then** which quantity is being shown is stated, rather than left for the user to infer.
6. **Given** a site inside the polar circles on a date where the sun does not rise or does not set, **When** the figures are shown, **Then** the existing plain statement of that condition is unchanged by this correction.

---

### User Story 2 - Shadows Stay Sharp and Complete (Priority: P1)

A user looks at shadows on a site late in the afternoon, when the sun is low and shadows are long. The shadows are crisply edged and they extend as far as the geometry casts them, rather than stopping at an invisible boundary or blurring into a soft smear.

**Why this priority**: Low sun is when shadow analysis matters most and when the current fixed-extent shadow camera is least suited to it — the shadow map's resolution is spread across ground that has nothing on it, while genuinely long shadows risk running past the camera's extent. It is user-visible, unlike the remaining items, and it carries the release's main technical risk.

**Independent Test**: Can be fully tested by comparing shadow edge definition at a low sun angle before and after, and by confirming that a long shadow from a tall building at a low sun angle is drawn to its full length rather than truncated.

**Acceptance Scenarios**:

1. **Given** a site with buildings and the sun low in the sky, **When** the user views the shadows, **Then** each shadow is drawn to the full length the sun's position implies, with no truncation at an arbitrary boundary.
2. **Given** the same site and moment, **When** shadow edges are compared against the current release, **Then** they are equally or more sharply defined, never less.
3. **Given** a site where buildings are clustered in a small part of the analysis area, **When** shadows are drawn, **Then** detail is not degraded by the empty area around them.
4. **Given** any sun position above the horizon, **When** the user views the site, **Then** no spurious dark region appears anywhere in the analysis area — the specific failure the current fixed-ratio sizing exists to prevent.
5. **Given** the sun is below the horizon, **When** the user views the site, **Then** no shadows are cast, unchanged from today.
6. **Given** a site with no buildings at all, **When** solar analysis opens, **Then** the ground renders correctly with no shadows and no visual artefact.

---

### User Story 3 - Scrubbing Stays Smooth on Dense Sites (Priority: P2)

A user drags the time control across a full day on a dense urban site with hundreds of surrounding buildings. The shadows sweep continuously and the viewer stays responsive, rather than stuttering as each frame redraws work that did not need redrawing.

**Why this priority**: Time scrubbing is the feature's most persuasive moment and the one most exposed to the current per-building drawing cost. It depends on nothing in the first two stories and is measurable independently, but it is an improvement to something that already works rather than a repair of something broken.

**Independent Test**: Can be fully tested by scrubbing and playing a full day on a site at the maximum supported building count and measuring frame pacing and responsiveness against the current release.

**Acceptance Scenarios**:

1. **Given** a site at the maximum supported building count, **When** the user drags across a full day, **Then** shadows and figures keep pace with the control without visible stutter.
2. **Given** the same site, **When** the user starts playback, **Then** the day animates continuously and the viewer remains usable for other interaction throughout.
3. **Given** playback is running, **When** the sun moves by an amount too small to change the shadows perceptibly, **Then** no visible stepping, flicker, or lag is introduced by whatever work is skipped.
4. **Given** a building's height is corrected mid-playback, **When** the correction is applied, **Then** its shadow updates immediately rather than waiting for a later frame.
5. **Given** the developer mass-visibility toggle is enabled, **When** buildings are revealed, **Then** the toggle still works and still shows every building.
6. **Given** any point during scrubbing, **When** shadows are compared against the current release at the same instant, **Then** they are in the same place.

---

### User Story 4 - Changing the Date Does Not Rebuild the Sky (Priority: P3)

A user steps through dates to compare seasons. Each change updates the day's arc without the surrounding dome — the compass dial, the mount, the shell, the monthly lattice — visibly flickering or hitching as it is discarded and reconstructed.

**Why this priority**: It is the smallest and most contained item, and the one with the least user-visible consequence today. It is worth doing because date stepping is how users compare seasons, and because the fixed furniture genuinely does not depend on the date.

**Independent Test**: Can be fully tested by stepping through a sequence of dates and confirming the fixed dome elements neither flicker nor change, and that the day arc and hour marks update correctly for each date.

**Acceptance Scenarios**:

1. **Given** solar analysis is open, **When** the user changes the date, **Then** the day's arc, its hour marks and the current-position marker update for the new date.
2. **Given** the user changes the date, **When** the change is drawn, **Then** the compass dial, mount, shell and monthly lattice do not flicker, move, or change.
3. **Given** the user steps rapidly through many dates, **When** they stop, **Then** the display shows the last date chosen with no accumulated artefacts.
4. **Given** the user moves to a different site, **When** the viewer settles, **Then** the full dome including the fixed furniture rebuilds for the new location.
5. **Given** a date change crosses into a different year, **When** the path updates, **Then** the monthly lattice and seasonal extremes are correct for the new year.
6. **Given** solar analysis is closed, **When** it is reopened, **Then** nothing retained across date changes leaks into the new session.

---

### Edge Cases

- What happens at the moment the sun crosses the horizon, where the refraction correction is largest and the sun is transitioning between casting and not casting shadows? The altitude figure, the shadow state and the rise/set times must all agree on which side of the horizon the sun is on.
- What happens at high latitudes on days near the polar boundary, where a fraction of a degree of correction determines whether the sun rises at all? The rise/set determination and the altitude figure must not disagree about whether the sun rose.
- What happens when buildings are clustered at the far edge of the analysis area, so that content-fitted shadow extents are strongly off-centre from the site? Shadows must remain correct and the ground must remain free of spurious dark regions.
- What happens when a single building is far taller than the rest, so the shadow extent needed at low sun is dominated by one object? The extent must accommodate it without collapsing detail for everything else.
- What happens when the sun is within a fraction of a degree of the horizon and shadow lengths tend toward the unbounded? There must be a defined bound beyond which shadows are not drawn, reached predictably rather than by running out of extent.
- What happens when building data arrives, or a height correction is applied, while playback is running? The shadow result must reflect the new geometry immediately, not after the next threshold is crossed.
- What happens when the analysis area contains no buildings, so there is no content to fit shadow extents to? A defined fallback extent must apply.
- What happens when the device's drawing capability limits shadow resolution below what fitting assumes? Behaviour must degrade to the current release's quality, not below it.

## Requirements *(mandatory)*

### Functional Requirements — Solar Position Accuracy

- **FR-001**: The system MUST apply an atmospheric refraction correction to the solar altitude it reports, so that the reported altitude and the reported sunrise and sunset times are derived from a consistent definition of the horizon.
- **FR-002**: At the sunrise and sunset instants the system itself computes, the altitude the system reports MUST read zero within the stated position tolerance.
- **FR-003**: The system MUST state, in the figures it presents, which altitude quantity is being shown, so that a user comparing it against an external reference knows which of that reference's two published figures to compare against.
- **FR-004**: The system MUST continue to determine sunrise, sunset, day length and polar conditions against the same standard reference altitude it uses today, unchanged by this correction.
- **FR-005**: The reported azimuth MUST be unchanged by this correction.
- **FR-006**: The position tolerance the system states MUST be re-measured against the corrected quantity and against published reference values for that same quantity, rather than inherited from the previous measurement.
- **FR-007**: The corrected altitude MUST be the single altitude the system uses — for the figure it displays, for the direction it casts light and shadows from, and for the sun's position on the path. The system MUST NOT maintain a second, uncorrected altitude for any purpose.

### Functional Requirements — Shadow Quality

- **FR-008**: The system MUST derive the radius that sizes the shadow region from the geometry actually present, rather than from a fixed multiple of the analysis radius.
- **FR-009**: That derived radius MUST remain the single value from which both the shadow region and the shadow-receiving ground surface are sized, preserving the invariant recorded as specs/052 constraint 4 (research D16) rather than substituting a different rule for it.
- **FR-010**: The system MUST NOT produce any spurious dark region in the analysis area under any sun position — the failure mode the current fixed-ratio sizing was introduced to prevent.
- **FR-011**: Shadow edge definition MUST be equal to or better than the current release at every sun elevation.
- **FR-012**: The system MUST define a lower sun elevation bound beyond which shadows are not drawn, and reach it predictably rather than by exhausting the computed region.
- **FR-013**: The system MUST apply a defined fallback extent when the analysis area contains no buildings.
- **FR-014**: The derived radius MUST account for the length of the shadows the present geometry casts at the lowest sun elevation at which shadows are still drawn, so that fitting more tightly to the buildings does not cause their shadows to be truncated.
- **FR-014a**: The system MUST NOT extend the shadow region directionally as a function of the sun's azimuth in this release. That approach is deferred because it cannot preserve the single-value invariant, and is warranted only if measurement shows FR-008's derived radius alone cannot satisfy SC-005 and SC-006.

### Functional Requirements — Drawing Efficiency

- **FR-015**: The system MUST combine surrounding building geometry so that the cost of drawing it does not scale with the number of buildings the way it does today.
- **FR-016**: The developer mass-visibility toggle MUST continue to reveal and hide all building massing.
- **FR-017**: Buildings MUST continue to cast shadows without being drawn over the basemap's own buildings, unchanged from today.
- **FR-018**: The system MUST avoid recomputing shadows during continuous playback when the sun has not moved enough for the result to differ perceptibly.
- **FR-019**: Any change to the geometry — a height correction, a ground offset, newly arrived building data — MUST update shadows immediately, regardless of how little the sun has moved.
- **FR-020**: Skipped recomputation MUST NOT be perceptible as stepping, flicker, or lag during playback.
- **FR-021**: The system MUST rebuild only the date-dependent parts of the sun path when the date changes, leaving the fixed dome elements in place.
- **FR-022**: The system MUST rebuild the complete sun path, including fixed elements, when the site or the year changes.
- **FR-023**: Drawing resources released or retained by this change MUST NOT leak across date changes, site changes, or open-and-close cycles of the capability.

### Functional Requirements — Preservation

- **FR-024**: The system MUST NOT alter renderer-global or scene-global state, which remains owned by the viewer framework (specs/049–051, FR-038 of specs/052).
- **FR-025**: The system MUST continue to request redraws through the framework's existing mechanism, without introducing repeated redraw requests across successive frames.
- **FR-026**: Every behaviour specified by specs/052 and not explicitly changed here MUST remain as specified, including the five recorded implementation constraints.
- **FR-027**: Shadow positions at any given instant MUST be unchanged from the current release other than by the refraction correction required by FR-007. No performance item in this release may move a shadow.
- **FR-028**: Every failure path introduced or touched by this work MUST surface to the user rather than being swallowed, consistent with constitution §2 VIII.

### Key Entities

- **Reported solar position**: The azimuth and altitude presented to the user and used to place the sun, where the altitude gains a defined relationship to the horizon that it did not previously have.
- **Shadow region**: The bounded volume within which shadows are computed, currently a fixed multiple of the analysis radius and becoming a function of the geometry present and the sun's direction.
- **Fixed dome furniture**: The parts of the sun-path drawing that depend on the site and the year but not on the chosen date — compass dial, mount, shell, monthly lattice.
- **Day-dependent path**: The parts that depend on the chosen date — the day's arc, its hour marks, the current-position marker.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At the sunrise and sunset times the feature reports, the altitude the feature reports reads 0.0° within the stated position tolerance, for every test location including at least one inside the polar circles.
- **SC-002**: Reported altitude agrees with published reference values for the same quantity within the stated position tolerance, across a full day, at every test location.
- **SC-003**: Sunrise and sunset times are unchanged from the current release, to the second, at every test location and date.
- **SC-004**: At the maximum supported site density of 300 buildings, dragging across a full day maintains continuous motion with no stutter a user can perceive.
- **SC-005**: Shadow edge definition at a sun elevation of 10° is measurably sharper than the current release, and no worse at any elevation.
- **SC-006**: A long shadow cast at a sun elevation of 5° is drawn to its full geometric length, with no truncation.
- **SC-007**: No sun position at any test location produces a spurious dark region in the analysis area.
- **SC-008**: Changing the date produces no visible change to the compass dial, mount, shell or monthly lattice.
- **SC-009**: Shadow positions at a fixed instant differ from the current release by no more than the refraction correction itself — unchanged above a sun elevation of 15°, and bounded by roughly half a degree of sun direction at the horizon. No other cause of shadow movement is introduced.
- **SC-010**: Opening, exercising and closing the capability a hundred times leaves no accumulated drawing resources.

## Assumptions

- The refraction correction adopted is the standard model used by the same public-domain reference the feature's existing calculations come from, rather than a more elaborate model accounting for local temperature and pressure. Site-specific atmospheric conditions are out of scope.
- The threshold below which the sun is considered not to have moved enough to warrant recomputation is a matter for design, and is assumed to be small enough that no user can perceive the difference — on the order of a quarter of a degree — rather than tuned for maximum saving.
- The maximum supported building count remains the existing cap of 300, and the default analysis radius remains 200 m. This release does not raise either.
- The existing accuracy tolerance constants remain exported and remain the single source for both the calculations and the wording the figures panel shows, so that the two cannot drift apart.
- No backend change is required. Building retrieval, its cap, its caching and its endpoint are unchanged.
- No new user-facing control, panel, or figure is introduced. The figures panel gains only a statement of which altitude quantity it shows.
- The viewer extension framework (specs/049–051) is unchanged by this work; if any item here cannot be achieved without changing it, that is recorded as a finding against the framework rather than absorbed here, following the precedent set by specs/052.
- Quantitative solar analysis — sun hours, irradiance, overshadowing compliance — remains out of scope, as established by specs/052.
