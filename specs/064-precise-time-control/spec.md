# Feature Specification: Precise Time-of-Day Control

**Feature Branch**: `064-precise-time-control`

**Created**: 2026-09-21

**Status**: Draft

**Input**: User description: "We need to add fine tuning for the timings slider in the solar analysis Time of Day as it was difficult for me to select the minutes, and if you can show ticks with 15 minutes steps on the slider it will be wonderful."

## Context

The Time of Day panel delivered by specs/052 offers exactly one way to choose a moment: drag a slider spanning the 1440 minutes of a day. At the panel's working width that slider resolves to roughly a minute and a half per pixel, and the time beside it is text, not an input. Choosing a specific minute is therefore not merely fiddly — for a target like 06:41 it is a matter of luck.

This surfaced while preparing the verification of specs/063, whose central check reads: *set the time to the sunrise the panel reports, and read the altitude*. That instruction cannot be reliably carried out with the present control. A feature whose correctness is verified by naming an exact minute needs a way to name an exact minute.

The request as received was for 15-minute tick marks. Ticks are worth having and are included here, but they solve orientation rather than precision: at the panel's width, ticks every 15 minutes stand about nine pixels apart, which aids aiming and does not make an individual minute selectable. The precision problem is solved by letting the user type the time. This specification therefore covers both, plus a snapping behaviour that makes ordinary dragging land on tidy values instead of arbitrary ones.

Nothing about solar calculation, scene drawing, or shadows is touched. This is a change to how a user names a moment, not to what the system does with it.

## Clarifications

### Session 2026-09-21

- Q: Does the request for a two-handled range, visible in the reference image supplied with it, form part of this feature? → A: No. The reference image was supplied to illustrate the tick-and-label styling. Selecting a time *range* — a from/to window — is a distinct capability already carried on the deferred list for the next solar specification, and pulling it in here would change this feature from a control refinement into a new analysis mode.
- Q: When does a typed time take effect — on each keystroke, or on commit? → A: On commit. Applying each keystroke would fight the user mid-entry, since the intermediate states of typing "06:41" include values that are incomplete and values that are valid but unintended. Commit is confirmation or leaving the field.
- Q: Snapping the slider to 15 minutes removes the ability to reach 06:41 by dragging. Is that acceptable? → A: Yes, provided a non-dragging path to any individual minute exists — which is the point of the typed input and of keyboard stepping. Dragging is a coarse-targeting gesture and benefits from landing on tidy values; the two fine paths remain unrestricted.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Setting an Exact Minute (Priority: P1)

A user needs the view at one specific minute — the sunrise the panel reports, a meeting time, the moment a neighbouring building's shadow reaches a boundary. They enter that time directly and the view moves to it.

**Why this priority**: This is the capability that does not currently exist in any form. Both other items in this release refine a gesture that already works; this one removes a hard blocker, and it is the reason the release is being built now.

**Independent Test**: Type a specific time into the readout, confirm it, and check the scene and figures correspond to that exact minute.

**Acceptance Scenarios**:

1. **Given** the panel is open, **When** the user types a valid local time into the readout and confirms it, **Then** the view moves to exactly that minute and the slider position updates to match.
2. **Given** the user has typed a time, **When** they leave the field without explicitly confirming, **Then** the typed value is applied, so a moment is not lost by clicking away.
3. **Given** the user types something that is not a valid time, **When** they confirm or leave the field, **Then** the entry is rejected with a visible indication of what is wrong, the previous time is retained, and the rejection is not silent.
4. **Given** the user types a time outside the day, **When** they confirm, **Then** it is rejected on the same visible terms rather than being clamped without explanation.
5. **Given** the user is part-way through typing, **When** the entry is incomplete, **Then** the view does not move, so the scene is not dragged through unintended moments keystroke by keystroke.
6. **Given** a time has been typed and applied, **When** the user then drags the slider, **Then** the slider takes over cleanly with no reversion to the typed value.
7. **Given** playback is running, **When** the user types a time, **Then** the behaviour is defined and consistent rather than the two fighting for the same value.

---

### User Story 2 - Seeing Where the Hours Are (Priority: P2)

A user dragging toward mid-afternoon can see where the hours fall along the slider instead of estimating from the two endpoints, and lands near the intended hour on the first attempt.

**Why this priority**: It is what the user explicitly asked for, and it makes the existing dragging gesture materially better. It is a refinement rather than a missing capability, so it ranks below the typed entry.

**Independent Test**: Look at the slider and confirm the tick marks are present, that hour positions are distinguishable from quarter-hour positions, and that the marks are legible rather than a grey smear at the panel's normal width.

**Acceptance Scenarios**:

1. **Given** the panel is open, **When** the user looks at the slider, **Then** tick marks appear at 15-minute intervals across the day.
2. **Given** the tick marks are shown, **When** the user looks for the hours, **Then** hour positions are visually distinguished from the quarter-hour positions between them.
3. **Given** the panel is at its normal width, **When** the ticks are drawn, **Then** they read as discrete marks rather than merging into a continuous band.
4. **Given** the panel is resized narrower, **When** the ticks would become illegible, **Then** the display degrades in a defined way rather than becoming visual noise.
5. **Given** the ticks are present, **When** the panel is viewed in either light or dark theme, **Then** they remain legible in both.
6. **Given** the ticks are present, **When** the slider is used, **Then** they do not obstruct the handle or the time readout.

---

### User Story 3 - Coarse and Fine Control Together (Priority: P2)

A user drags the slider and it settles on quarter-hour values rather than an arbitrary minute. When they need something between, the keyboard moves a minute at a time.

**Why this priority**: It completes the pair. Snapping makes dragging predictable; minute-level keyboard stepping guarantees that snapping never becomes a restriction. Shipping snapping without the fine path would be a regression, so the two belong in one story.

**Independent Test**: Drag the slider and confirm it settles on quarter-hour values; focus it and confirm a single arrow-key press moves exactly one minute.

**Acceptance Scenarios**:

1. **Given** the user drags the slider, **When** they release it, **Then** the time settles on a 15-minute boundary.
2. **Given** the slider has keyboard focus, **When** the user presses an arrow key once, **Then** the time moves by exactly one minute.
3. **Given** the slider has keyboard focus, **When** the user holds an arrow key, **Then** the time advances at a usable rate without skipping minutes unpredictably.
4. **Given** the time is at a value that is not on a 15-minute boundary, **When** the user drags the slider, **Then** it moves to boundaries from there without an initial jump the user did not ask for.
5. **Given** assistive technology is in use, **When** the slider value changes by any route, **Then** the announced value is the local time, unchanged from today's behaviour.
6. **Given** the user reaches the start or end of the day, **When** they continue stepping, **Then** the value stops at the boundary rather than wrapping to the other end of the day.

---

### Edge Cases

- What happens when a typed local time does not exist on the chosen date, because the local clock jumps forward over it for daylight saving? The site's timezone is used for all of this panel's times, and such an hour is genuinely absent from that date. The outcome must be stated to the user rather than silently resolved to a different moment.
- What happens when a typed local time occurs twice on the chosen date, because the local clock falls back over it? A defined and consistent choice must be made rather than an arbitrary one.
- What happens when the user types a time while playback is running? The interaction between an explicit instruction and an automatic one must be resolved deliberately.
- What happens when the user types the time that is already set? The view must not flicker or reset.
- What happens when the date changes while a typed but uncommitted entry sits in the field? The stale entry must not be applied to the new date without the user's knowledge.
- What happens at the day's boundaries, where a 15-minute snap could round past the end of the day?

## Requirements *(mandatory)*

### Functional Requirements — Direct Time Entry

- **FR-001**: The system MUST allow the user to set the time of day by entering a local time directly, without dragging.
- **FR-002**: An entered time MUST take effect on confirmation or on leaving the field, not on each keystroke.
- **FR-003**: The system MUST reject an entry that is not a valid time of day, retain the previously set time, and make the rejection visible to the user rather than silent (constitution §2 VIII).
- **FR-004**: The system MUST reject an entry outside the bounds of a day on the same visible terms, rather than silently clamping it.
- **FR-005**: When an entry is rejected, the system MUST make it possible to correct it without re-entering the whole value.
- **FR-006**: An entered time MUST be interpreted in the site's timezone, consistent with every other time this panel shows.
- **FR-007**: When an entered local time does not exist on the chosen date because of a daylight-saving transition, the system MUST state that rather than resolving it to a different moment without explanation.
- **FR-008**: When an entered local time occurs twice on the chosen date because of a daylight-saving transition, the system MUST resolve it by a defined and consistently applied rule.
- **FR-009**: The relationship between direct entry and running playback MUST be defined, and the two MUST NOT contend for the same value.
- **FR-010**: An uncommitted entry MUST NOT be applied to a date other than the one in effect when it was typed.

### Functional Requirements — Tick Marks

- **FR-011**: The system MUST display tick marks at 15-minute intervals along the time slider.
- **FR-012**: Hour positions MUST be visually distinguished from the quarter-hour positions between them.
- **FR-013**: Tick marks MUST remain legible at the panel's normal width and MUST NOT obscure the slider handle or the time readout.
- **FR-014**: Where the available width cannot accommodate legible marks, the system MUST degrade the display in a defined way rather than rendering unreadable marks.
- **FR-015**: Tick marks MUST be legible in both the light and dark themes.

### Functional Requirements — Slider Behaviour

- **FR-016**: Dragging the slider MUST settle on 15-minute boundaries.
- **FR-017**: Keyboard stepping MUST move the time by one minute per key press, so that snapping never prevents reaching an individual minute.
- **FR-018**: Slider movement MUST stop at the start and end of the day rather than wrapping.
- **FR-019**: When the current time is not on a 15-minute boundary, beginning a drag MUST NOT produce a jump the user did not initiate.

### Functional Requirements — Preservation

- **FR-020**: Every control in this panel MUST continue to write a single moment through the existing store setters, with no second representation of the time held anywhere, preserving specs/052 FR-022. A draft entry that the user has not yet committed is not a second representation of the current time and is permitted, provided it cannot be read as the current time by anything else.
- **FR-021**: The local-time announcement made to assistive technology MUST be preserved for every route by which the time can change.
- **FR-022**: The panel MUST retain its existing compact two-row layout and visual language.
- **FR-023**: No solar calculation, scene, shadow, or figure behaviour may change as a result of this feature.
- **FR-024**: Every failure path introduced by this work MUST surface to the user rather than being swallowed (constitution §2 VIII).

### Key Entities

- **Chosen moment**: The single instant the panel controls, unchanged in nature by this feature. What changes is the number of ways a user can name it.
- **Draft entry**: Text the user has typed but not yet committed. It exists only within the entry field, is discarded on rejection or on a date change, and is never a source of truth for anything else.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can set the time to any specific minute of the day in a single deliberate action, without repeated attempts.
- **SC-002**: Setting the time to a named minute — the verification step specs/063 depends on — succeeds on the first attempt, where today it depends on luck.
- **SC-003**: Every invalid or impossible entry produces a visible explanation; none is silently ignored, silently corrected, or silently applied as a different time.
- **SC-004**: A user can identify the hour positions on the slider by sight, without reference to the numeric readout.
- **SC-005**: Dragging the slider settles on a 15-minute boundary every time.
- **SC-006**: A single arrow-key press changes the time by exactly one minute, at every point in the day.
- **SC-007**: Every route by which the time changes announces the new local time to assistive technology.
- **SC-008**: Solar figures, sun position, and shadows for a given moment are identical to the current release.

## Assumptions

- The site's timezone continues to govern every time this panel displays or accepts, as established by specs/052.
- The 15-minute interval is taken from the request as stated and is not made configurable; no evidence suggests a second interval is wanted.
- The day remains a single calendar day in the site's timezone, with the date chosen separately. This feature does not introduce crossing midnight by time entry.
- The existing playback behaviour, its speed, and its controls are unchanged except where FR-009 requires the interaction with direct entry to be defined.
- No backend change, no API change, and no persisted state change is required.
- The panel's existing compact styling vocabulary is sufficient; no new visual language is introduced.
