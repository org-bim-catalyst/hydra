# Research: Precise Time-of-Day Control

**Feature**: specs/064-precise-time-control | **Date**: 2026-09-21

Phase 0. Every decision below was checked against the code actually in the repository rather than
against how the components are generally used, because two of the four hard parts turned out to be
already solved here and one turned out to be harder than it looks.

---

## D1 — The single-source rule costs nothing to preserve

**Question**: specs/052 FR-022 requires every control to write one value. Does adding a typed entry
threaten that?

**Finding**: No. `store/solarAnalysisStore.ts` already exposes `setLocalMinuteOfDay`, which
recomputes `instantUtc` from the local date and minute and writes the whole moment
(`solarAnalysisStore.ts:124-128`). A typed entry has exactly the same job as the slider: produce a
minute-of-day and hand it to that setter.

**Decision**: The typed entry calls `setLocalMinuteOfDay`. No new store action, no new state shape,
no second time value.

---

## D2 — Daylight saving is already handled, and can be *detected* for free

**Question**: FR-007 requires stating when a typed local time does not exist on the chosen date.
How much machinery does that need?

**Finding**: None, as it turns out. `solar/timeZone.ts`'s `fromLocalParts` resolves a local time to
a UTC instant by fixed-point convergence against the zone's actual offset, explicitly so that the
spring-forward gap and the autumn-back fold each land on exactly one instant. It already does the
hard part.

What it cannot do is *tell you* which case you are in — it silently returns the nearest resolvable
instant. But the detection is a round trip:

```
requested = 02:30 on a spring-forward date
instant   = fromLocalParts(date, requested, zone)
observed  = toLocalParts(instant, zone).localMinuteOfDay   →  03:30
observed !== requested  ⇒  the requested local time does not exist on that date
```

**Decision**: Detect the gap by round-tripping through the two existing functions and comparing. No
new timezone logic, no new dependency, no reimplementation of tzdata rules.

**Consequence for FR-008 (the fold)**: `fromLocalParts`'s convergence already picks one of the two
instants deterministically, and the round trip confirms the local time *does* exist. That satisfies
"a defined and consistently applied rule" without additional code. It is recorded here so the
absence of fold-handling code is a decision rather than an oversight.

**Why this matters**: Egypt observes DST, and Badr is the site under test. This is a live case, not
a theoretical one.

---

## D3 — MUI Slider cannot give drag-snapping and one-minute keys from `step` alone

**Question**: FR-016 wants dragging to settle on 15 minutes; FR-017 wants arrow keys to move one
minute. Can a single `step` do both?

**Finding**: No. MUI's Slider derives keyboard movement from `step`. `step={15}` snaps the drag and
breaks the keyboard; `step={1}` keeps the keyboard and abandons snapping. The two requirements are
in direct conflict through that one property.

**Options considered**:

| Option | Verdict |
|---|---|
| `step={15}`, accept 15-minute arrow keys | Rejected — violates FR-017, and FR-017 exists to stop snapping becoming a restriction |
| `step={1}`, snap inside `onChange` | Rejected — snaps keyboard movement too, same failure |
| `step={1}`, snap in `onChangeCommitted` | Rejected alone — MUI fires it on key-up as well as pointer-release, so arrows would snap |
| `step={1}`, snap in `onChangeCommitted` **only when the interaction began with a pointer** | **Chosen** |

**Decision**: Keep `step={1}` so the underlying control, its keyboard behaviour and its
`aria-valuetext` are untouched. Record the interaction source on `pointerdown` / `keydown`, and snap
to the nearest quarter-hour in `onChangeCommitted` only when the source was a pointer.

**Consequence**: During a drag the value tracks the pointer at one-minute resolution and settles on
release — which is what FR-019 asks for (no jump when a drag *begins*), and is a better feel than
snapping mid-drag.

---

## D4 — Tick spacing, and what "legible" means numerically

**Question**: FR-011 asks for 15-minute ticks. FR-013 and FR-014 require them to stay legible and
to degrade in a *defined* way. Defined by what number?

**Measurement**: A day holds 96 quarter-hour intervals and 24 hour intervals. Spacing is therefore
track width ÷ interval count:

| Track width | 15-min spacing | Hour spacing |
|---|---|---|
| 1200 px | 12.5 px | 50 px |
| 900 px | 9.4 px | 37.5 px |
| 600 px | 6.3 px | 25 px |
| 400 px | 4.2 px | 16.7 px |
| 240 px | 2.5 px | 10 px |

A 1 px mark needs roughly 5–6 px of clear space either side to read as discrete rather than as a
hatched band. Below about 6 px spacing the 96 marks merge into exactly the grey smear FR-013
prohibits.

Labels are the tighter constraint. An "18" label is ~14 px at the panel's 11 px label size and needs
~32 px of pitch to avoid collision; 24 hourly labels therefore need ~770 px of track, which the
panel does not always have.

**Decision**: Derive the tier from measured width against two named constants rather than from
hard-coded breakpoints:

| Tier | Condition | Shows |
|---|---|---|
| Full | 15-min spacing ≥ `MIN_TICK_SPACING_PX` | 15-min ticks, hours emphasised |
| Reduced | hour spacing ≥ `MIN_TICK_SPACING_PX` | Hour ticks only |
| None | below that | No ticks |

Labels are tiered independently at `MIN_LABEL_SPACING_PX`: every hour, else every 3 hours, else
every 6 hours, else none. This is self-adjusting and states the rule as arithmetic rather than as a
guess about panel widths.

**Rejected**: hard-coded pixel breakpoints. The panel is user-resizable, so any breakpoint chosen
today is a guess about a width the user controls.

---

## D5 — Measuring the width in a way that survives jsdom

**Question**: Tier selection needs the track width. `ResizeObserver` is absent in jsdom and every
rect there is 0×0, which would put every test in the "None" tier and hide the ticks from their own
tests.

**Finding**: The repository already has this exact problem solved. `hooks/useWholeRowScroll.ts:30`
guards with `typeof ResizeObserver === 'undefined'` and degrades to the *uncapped* layout — the
normal case, never a broken one — with the reasoning recorded in its own comment at lines 17-18.

**Decision**: Follow that precedent. Guard the observer, and treat *unmeasured* as the Full tier
rather than the None tier. Degradation applies only to a width that was actually measured and found
wanting. Under test, ticks are present and assertable.

---

## D6 — Where the logic lives so it can be tested

**Question**: MUI Slider pointer-dragging is close to untestable in jsdom — it needs
`getBoundingClientRect` stubbing and synthesised pointer sequences, and the existing
`SolarTimeControlPanel.test.tsx` sensibly does not attempt it (it drives the slider with
`fireEvent.change`).

**Decision**: Put every decision in a pure module, `panels/timeEntry.ts`, and leave the component
holding only wiring:

| Function | Responsibility |
|---|---|
| `parseLocalTimeEntry(text)` | Text → minute-of-day, or a stated reason for refusal |
| `formatLocalTime(minuteOfDay)` | The inverse (moved from the component, where it is today) |
| `snapToQuarterHour(minuteOfDay)` | Nearest 15, clamped inside the day |
| `buildTimeSliderMarks(trackWidthPx)` | Tier selection and the mark list, per D4 |

Each is a pure function of its arguments, unit-testable with no DOM (constitution §2 V). The
component's remaining testable surface — that a typed entry reaches the store, that a rejected one
does not — is reachable with `fireEvent` exactly as the existing tests are.

---

## D7 — What "reject visibly" means for a control this small

**Question**: FR-003 and FR-024 require a rejected entry to be visible, not silent. The panel is a
compact two-row control with no room for an alert.

**Finding**: The feature already has a precedent for exactly this shape of problem.
`BuildingCorrectionsPanel` validates height and ground-offset entry and states the rejection through
`copy.invalidHeight` / `copy.invalidGroundOffset`, each of which names the bound *and* says the
previous value was kept.

**Decision**: Mirror that. The entry field takes an error state and the message is shown inline
beneath the row, with wording that follows the existing pattern — what is required, and that the
previous time was kept. New strings live in `copy.ts` per constitution §7, alongside the ones they
imitate.

**Consequence for FR-005** (correct without re-entering the whole value): the draft text stays in
the field when rejected. The user fixes the typo; nothing is cleared out from under them.

---

## D8 — Typing while playback runs

**Question**: FR-009 requires the relationship to be defined. Playback advances the time
continuously; a typed entry names one moment. Both write the same value.

**Options**: apply and keep playing (the typed moment is overwritten within a frame — the user's
instruction visibly evaporates); apply and pause (the explicit instruction wins); or refuse the
entry while playing (hostile).

**Decision**: Committing a typed time stops playback. A user who names a moment wants to look at it.
This also removes the contention outright rather than defining a race.

**Not chosen**: refusing entry during playback. It makes the user perform two actions to express one
intention.

---

## D9 — The stale-draft hazard

**Question**: FR-010 — what happens to text typed but not committed when the date changes underneath
it?

**Finding**: This is the one place a draft could become a wrong answer rather than merely a
discarded one: "06:41" typed on the 21st, then the date moved to the 22nd, then committed, applies a
time the user chose for a different day. On a DST boundary the two are not even the same duration
apart.

**Decision**: Clear the draft when the date changes. The field reverts to displaying the current
time. Recorded as its own decision because it is the least obvious requirement in the specification
and the easiest to omit.

---

## D10 — Scope: the two-handled range

The reference image supplied with the request shows a two-handle slider with a highlighted band.
Confirmed as styling reference only (spec Clarifications). Range selection is a distinct analysis
capability already carried on the deferred list for the next solar specification, and is out of
scope here. Noted so a later reader does not mistake its absence for an oversight.
