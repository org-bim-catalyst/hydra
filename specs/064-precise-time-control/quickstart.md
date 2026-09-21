# Quickstart: Validating Precise Time-of-Day Control

**Feature**: specs/064-precise-time-control | **Date**: 2026-09-21

Two halves. The automated half proves the parsing, snapping and tier arithmetic. The manual half
proves the control feels right, and is written so each step states the expected outcome before you
look.

---

## Prerequisites

```bash
cd "src/AskLucy.Web/ClientApp"
npx vitest run src/features/solar/panels
```

No backend, no database, no GPU. The logic under test is pure functions plus one component.

For the manual half, open solar analysis on any site. Badr, Egypt is the useful one — it is the
baseline site for specs/063 and it observes daylight saving, which is the only genuinely tricky
case here.

---

## Part 1 — Automated

### Pure functions (`timeEntry.test.ts`)

| Assertion | Why it matters |
|---|---|
| `parseLocalTimeEntry` accepts `6:41`, `06:41`, `" 06:41 "` | What people actually type |
| Rejects `abc`, `""`, `6:` as `malformed` | FR-003 |
| Rejects `25:00`, `06:75` as `outOfRange`, **without clamping** | FR-004 — `25:00` is a mistake to report, not a number to round |
| `formatLocalTime` round-trips every valid minute | The two are inverses, pinned |
| `snapToQuarterHour(1433)` → `1425`, never `1440` | FR-018 — the day has no 24:00 |
| `buildTimeSliderMarks(null)` returns the Full tier | Research D5 — unmeasured is normal, not broken |
| Tier selection derives from the spacing constants | FR-014, and no hard-coded breakpoints |

### Component (`SolarTimeControlPanel.test.tsx`)

| Assertion | Requirement |
|---|---|
| A committed entry writes through `setLocalMinuteOfDay` | FR-020 — one value, one setter |
| Typing alone does **not** move the view | FR-002 |
| Blur commits; it does not discard | US1 scenario 2 |
| A rejected entry keeps the text, keeps the old time, shows a reason | FR-003, FR-005 |
| Committing stops playback | FR-009 |
| Changing the date clears an uncommitted draft | FR-010 |
| A spring-forward gap time is stated, not silently moved | FR-007 |
| An arrow key moves exactly one minute | FR-017, SC-006 |
| `aria-valuetext` still reads `14:00`, not `840` | FR-021 — the existing test must still pass |

### Full suite

```bash
npx vitest run                 # not just the touched files
npx tsc -b --noEmit            # bare `tsc --noEmit` is a no-op under this project's references
```

---

## Part 2 — Manual

### Check 1 — type an exact minute (the reason this feature exists)

1. Open solar analysis and note the **sunrise** time in the figures panel.
2. Click the time readout beside the slider.
3. Type that exact time — for Badr on 21/09/2026, `06:41`.
4. Press Enter.

**Expect:** the view moves to that minute, the readout shows `06:41`, and the slider handle moves to
match. **One attempt, no fiddling.** This is the step specs/063's verification depends on.

### Check 2 — the ticks

Look at the slider.

**Expect:** tick marks every 15 minutes, with the hour positions more prominent than the quarter-hour
ones between them, and labels at the hours. They should read as distinct marks — if they look like a
solid grey band, that is a defect.

### Check 3 — ticks survive a resize

Drag the panel narrower, in stages, down to quite small.

**Expect:** the quarter-hour marks drop away to leave hour marks; labels thin out from hourly to
every few hours; eventually the marks disappear. At no width should you see an unreadable smear.

### Check 4 — dark theme

Switch themes.

**Expect:** ticks and labels legible in both. Not invisible, not glaring.

### Check 5 — drag snaps, keys are fine

1. Drag the handle and release it somewhere mid-morning. Read the time.
2. Now click the handle and press the **right arrow** once. Read it again.

**Expect:** the drag lands on `:00`, `:15`, `:30` or `:45`. The arrow key then moves it by exactly
**one** minute. If the arrow key moves 15 minutes, that is the defect research D3 predicted.

### Check 6 — a bad entry is refused out loud

Type `25:00` and press Enter.

**Expect:** the entry is refused with a visible message, the previous time is unchanged, and **your
text stays in the field** so you can fix it. It must not silently become 23:59, and it must not
silently do nothing.

### Check 7 — the daylight-saving case

Egypt moves its clocks forward in late April. Set the date to the transition date, then type a time
inside the skipped hour (try `00:30`, then `01:30`).

**Expect:** for the hour that does not exist on that date, a message saying so. **It must not
quietly jump to a different time.** This is the one place this feature could give a confidently
wrong answer.

### Check 8 — typing while playing

1. Press play.
2. Type a time and press Enter.

**Expect:** playback stops and the view sits at the time you typed. It must not keep running and
sweep your chosen moment away.

### Check 9 — the stale draft

1. Type a time but **do not** press Enter.
2. Change the date.

**Expect:** your uncommitted text is cleared and the field shows the current time for the new date.
It must not apply the old text to the new date.

---

## If it is wrong

```bash
git log --oneline          # find the commit before this feature
git reset --hard <sha>
```

The spec and plan commits are separate from the implementation, so the documents survive a revert of
the code.
