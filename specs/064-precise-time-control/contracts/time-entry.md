# Contract: Time Entry & Slider Marks

**Feature**: specs/064-precise-time-control

Covers `panels/timeEntry.ts` (added) and the wiring changes in `panels/SolarTimeControlPanel.tsx`.

Everything in `timeEntry.ts` is a pure function of its arguments — no DOM, no store, no clock, no
timezone database of its own. That is deliberate (research D6): MUI Slider pointer-dragging is close
to untestable in jsdom, so no decision may live inside the drag handler.

---

## `timeEntry.ts` (added)

### `parseLocalTimeEntry(text: string): TimeEntryResult`

Text the user typed → a minute of the day, or a named reason for refusal.

```ts
type TimeEntryResult =
  | { ok: true; minuteOfDay: number }
  | { ok: false; reason: 'malformed' | 'outOfRange' }
```

**Contract**:
- MUST accept `H:MM` and `HH:MM` in 24-hour form. Leading zeros optional on the hour.
- MUST tolerate surrounding whitespace and a missing leading zero, because both are what people
  actually type.
- MUST reject anything not parseable as a time with `'malformed'` (FR-003).
- MUST reject an hour above 23 or a minute above 59 with `'outOfRange'`, and MUST NOT clamp it
  (FR-004). `25:00` is a mistake to report, not a number to round down.
- MUST NOT throw. Every input, including empty string, returns a result.
- MUST be a discriminated union so that an unhandled refusal is a compile error rather than a value
  that silently reads as success (§4 TypeScript).

**Deliberately not supported**: 12-hour input with am/pm, seconds, and times beyond the day. The
panel displays 24-hour time and the day is a single calendar day (spec Assumptions). Recorded so
their absence is a decision, not an oversight.

### `formatLocalTime(minuteOfDay: number): string`

`HH:MM`, zero-padded. **Moved** from `SolarTimeControlPanel.tsx`, where it is defined today, with
behaviour unchanged. It is the exact inverse of `parseLocalTimeEntry` for every valid minute, and a
round-trip test MUST pin that.

### `snapToQuarterHour(minuteOfDay: number): number`

**Contract**:
- Returns the nearest multiple of `SNAP_MINUTES`.
- MUST clamp within the day: 23:53 snaps to 23:45, never to 24:00 (FR-018, and the spec's final
  edge case).
- Ties round consistently; the direction is stated in the implementation and pinned by a test.

### `buildTimeSliderMarks(trackWidthPx: number | null): SliderMark[]`

Tier selection per [data-model.md](../data-model.md), returning MUI's mark shape with an `isHour`
distinction available for styling.

**Contract**:
- `null` — meaning not yet measured — MUST return the **Full** tier, not the empty one
  (research D5). jsdom never measures, and ticks must be assertable in their own tests.
- Tier selection MUST derive from `MIN_TICK_SPACING_PX` and `MIN_LABEL_SPACING_PX` rather than from
  hard-coded width breakpoints. The panel is user-resizable; a breakpoint is a guess about a width
  the user controls (D4).
- Hour marks MUST be distinguishable from quarter-hour marks in the returned data, so the component
  styles them differently without re-deriving which is which (FR-012).
- Labels MUST appear only at the tier's chosen interval, and MUST be absent entirely below the
  label threshold (FR-014).
- MUST return marks for a full day inclusive of both ends.

---

## `SolarTimeControlPanel.tsx` (modified)

### The readout becomes an input

**Contract**:
- The time readout becomes an editable field retaining its monospace compact styling (FR-022).
- Typing MUST NOT move the view. The store is written on commit only (FR-002, US1 scenario 5).
- Commit is Enter **or** blur. Blur applies rather than discards (US1 scenario 2).
- On commit the entry is parsed, then checked for existence on the current date, then written via
  the store's existing `setLocalMinuteOfDay` — never through a new store action, never as a second
  time value (FR-020, research D1).
- Committing MUST stop playback (FR-009, research D8).
- A rejected entry MUST retain the user's text so it can be corrected without retyping (FR-005),
  MUST leave the previous time in force, and MUST state the reason inline, following the wording
  pattern `BuildingCorrectionsPanel` already uses — name the bound, say the previous value was kept
  (FR-003, FR-024, research D7).
- The draft MUST be cleared when the date changes (FR-010, research D9). This is the only path by
  which a draft could apply a time to a date the user did not choose it for.

### Nonexistent local times

**Contract** (FR-007, research D2):
- After parsing, the candidate MUST be checked by round-tripping through the existing
  `fromLocalParts` and `toLocalParts`: if the resulting instant projects back to a *different*
  local minute, the typed time does not exist on that date.
- That case MUST be stated to the user, not silently resolved to the neighbouring instant.
- No new timezone logic may be written. Both functions already exist in `solar/timeZone.ts` and
  already handle the transitions; this is detection, not computation.
- The autumn fold — a local time occurring twice — MUST be left to `fromLocalParts`'s existing
  deterministic convergence (FR-008). A test pins that it resolves *consistently*, not which of the
  two instants it picks.

### Slider behaviour

**Contract** (FR-016, FR-017, FR-019, research D3):
- `step` MUST remain `1`, leaving MUI's keyboard handling and `aria-valuetext` untouched.
- Snapping MUST occur in `onChangeCommitted` and MUST apply **only** when the interaction began with
  a pointer. MUI fires that callback on key-up as well as pointer-release, so an undiscriminated
  snap would move arrow keys by 15 minutes and violate FR-017.
- The interaction source MUST be recorded on `pointerdown` / `keydown` in a ref, not in state — it
  must not cause a render, and it is not part of the component's output.
- A drag MUST NOT jump when it begins, only settle when it ends (FR-019).
- Movement MUST stop at both ends of the day rather than wrapping (FR-018).

### Preserved

- `aria-valuetext` MUST carry the local time on every route by which the time can change, including
  typed entry (FR-021). The existing test pinning `'14:00'` rather than `'840'` MUST still pass.
- The compact two-row layout, the label widths and the amber accent are unchanged (FR-022).
- Nothing in this feature may touch a solar calculation, a scene object, a shadow or a figure
  (FR-023).

---

## `copy.ts` (modified)

New strings, per constitution §7 — none inline in the component:

| String | For |
|---|---|
| Malformed entry | FR-003 |
| Out-of-range entry | FR-004 |
| Nonexistent local time on this date | FR-007 |

Each MUST follow the established pattern of the neighbouring `invalidHeight` and
`invalidGroundOffset` entries: say what is required, and say the previous value was kept.
