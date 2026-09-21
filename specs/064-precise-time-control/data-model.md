# Data Model: Precise Time-of-Day Control

**Feature**: specs/064-precise-time-control | **Date**: 2026-09-21

No persisted entity changes, no API payload, no store schema change. What follows models the one
piece of state this feature introduces — the draft entry — and the rule that stops it becoming a
second answer to "what time is it?".

---

## The chosen moment (unchanged)

`solarAnalysisStore`'s `moment` remains the single source of truth, exactly as specs/052 defined it:
`instantUtc` is canonical, `localDate` and `localMinuteOfDay` are projections of it through the
site's timezone, and every control writes through a setter that recomputes the whole moment.

This feature adds a third route to that setter. It does not add a value.

| Route | Writes via | Resolution |
|---|---|---|
| Slider drag | `setLocalMinuteOfDay` | Settles on 15 minutes |
| Keyboard arrows | `setLocalMinuteOfDay` | 1 minute |
| **Typed entry (new)** | `setLocalMinuteOfDay` | 1 minute |

---

## The draft entry (new)

Text the user has typed and not yet committed. It lives in the entry field's local component state
and nowhere else.

### States

| State | Field shows | Store | Exit |
|---|---|---|---|
| **Idle** | The current time, formatted | Authoritative | User focuses and types → Editing |
| **Editing** | The user's text, unvalidated | Unchanged — the view does not move (FR-005 of the spec's US1 scenario 5) | Commit, or date change |
| **Rejected** | The user's text, retained, with a stated reason | Unchanged; previous time kept | User edits again → Editing |
| **Committed** | The current time, reformatted | Written once | → Idle |

### The rule that keeps it honest

A draft is **not** a representation of the current time. It is a proposal. Three properties enforce
that, and each is separately testable:

1. Nothing outside the entry field reads it — not the slider, not the figures, not the scene.
2. It is never written to the store until committed.
3. It is discarded, not applied, when the date changes beneath it (FR-010, research D9).

This is the narrow exemption FR-020 grants to specs/052's single-source rule. Stated explicitly so
that a later reader can check the exemption is still narrow.

### Commit triggers

Confirmation (Enter) or leaving the field. Both apply; blur applies rather than discards, so a
moment is not lost by clicking away (US1 scenario 2).

### Committing stops playback

Research D8. A user who names a moment wants to look at it, and playback would otherwise overwrite
the typed value within a frame — the instruction would visibly evaporate. Stopping removes the
contention rather than defining a race (FR-009).

---

## Entry outcomes

`parseLocalTimeEntry` returns a discriminated union, so every refusal has a named reason and an
unhandled one is a compile error rather than a silent pass:

| Outcome | Cause | User sees |
|---|---|---|
| Accepted | A valid time within the day | The view moves |
| Malformed | Not parseable as a time | Stated reason; previous time kept (FR-003) |
| Out of range | Parseable but outside 00:00–23:59 | Stated reason; not silently clamped (FR-004) |
| **Nonexistent local time** | Falls in a spring-forward gap on this date | Stated reason; not resolved to a different moment (FR-007) |

The fourth is determined after parsing, because it depends on the date and zone, not on the text.

---

## Named constants

Exported, so assertions and behaviour share one source and cannot drift — the practice specs/052
established with its tolerance constants.

| Constant | Value | Origin | Used by |
|---|---|---|---|
| `MINUTES_PER_DAY` | `1440` | Definitional | Range validation, clamping |
| `SNAP_MINUTES` | `15` | The request as stated | Drag snapping; tick interval |
| `MIN_TICK_SPACING_PX` | `6` | Research D4 — below this, 96 marks merge into a band | Tick tier selection |
| `MIN_LABEL_SPACING_PX` | `32` | Research D4 — an "18" label is ~14 px at the panel's 11 px label size | Label tier selection |

---

## Tick tiers

A derived value, not state. Recomputed from the measured track width on resize.

| Tier | Condition | Ticks | Labels |
|---|---|---|---|
| Full | `width / 96 ≥ MIN_TICK_SPACING_PX` | Every 15 min, hours emphasised | Per the label tier |
| Reduced | `width / 24 ≥ MIN_TICK_SPACING_PX` | Hours only | Per the label tier |
| None | Below that | None | None |

Label tier, selected independently: every hour, else every 3 hours, else every 6 hours, else none —
whichever is the densest whose pitch clears `MIN_LABEL_SPACING_PX`.

**Unmeasured is Full, not None** (research D5). Degradation applies only to a width actually
measured and found wanting. `ResizeObserver` is absent in jsdom and every rect there is 0×0; the
repository's existing precedent (`hooks/useWholeRowScroll.ts:30`) degrades to the normal case rather
than the broken one, and this follows it. Under test, ticks are present and assertable.

---

## State that does not change

For the avoidance of doubt: `solarAnalysisStore`'s shape, `correctionsStore`, the panel content-block
model from specs/049, the site's timezone resolution, and every solar calculation, scene object and
shadow are untouched by this feature.
