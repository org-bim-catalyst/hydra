# Contract: Solar Analysis Panels

Three panel surfaces (FR-030, FR-031), split by what they actually are: two are interactive code,
one is data.

## 1. Time Control — live panel

```ts
typeKey: 'solar.time-control'
```

| Control | Requirement | Behaviour |
|---|---|---|
| Date picker | FR-020 | Site-local date. Changing it recomputes path, figures, shadows. |
| Time slider | FR-020, FR-022 | 0…1439 local minutes. Dragging updates sun, shadows and figures **together**. |
| Play / stop | FR-021 | Stopping leaves the display at the moment it stopped — never resets. |
| Speed | FR-021 | Default 120 local-minutes per real second. Chosen from 5, 15, 30, 60 or 120 min/s (shown as `5 min/s` … `2 h/s`); changeable while playing; kept when the site changes. Writes only the speed, never the instant (FR-022). Added 2026-09-25 — the row existed but no control was ever built. |

**Consistency guarantee (FR-022)**: every control writes only `instantUtc`; sun position, shadows
and figures are all derived from it. They cannot disagree, because there is one value, not three.

**Responsiveness (FR-023, SC-004)**: the slider writes directly to the store and calls
`invalidate()`. Shadow geometry is not rebuilt on time change — only the light direction moves,
which is what makes scrubbing cheap enough to read as continuous motion. Building geometry is
rebuilt only when buildings or heights change (FR-019).

## 2. Building Corrections — live panel

```ts
typeKey: 'solar.corrections'
```

| Control | Requirement | Behaviour |
|---|---|---|
| Site building height | FR-025 | Shows current metres **and** whether `known` or `assumed` (FR-011). Editing rebuilds that building and its shadow. |
| Ground offset | FR-026 | Moves the analysis relative to ground. |
| Site label | FR-028 | Names the site these corrections apply to. |
| Reset | — | Restores source values for this site. |
| Show building massing | FR-016 | A switch, off by default. On draws the boxes that cast the shadows so they can be compared against the basemap's own buildings; off leaves them casting but invisible (research D13). Flips the material in place — no rebuild. A view choice, not a correction: it applies to every site and Reset leaves it alone. Added 2026-09-25 — the scene toggle existed but was developer-only. |

**Validation (FR-027)**: height `> 0` and `≤ 1000` m; offset within `±500` m. An invalid entry is
rejected with a stated reason and the **previous value is kept** — never silently clamped, never
discarded without a message.

## 3. Solar Figures — content panel

Composed client-side from specs/049's block vocabulary (research D12) and opened with
`context.openPanel({ kind: 'content', … })`. **Not** a purpose-built component — FR-031 requires
this explicitly.

```json
{
  "version": 1,
  "blocks": [
    { "kind": "heading", "text": "Sun — 13 September, 14:00", "level": 1 },
    { "kind": "metric", "label": "Azimuth", "value": 247.3, "unit": "°" },
    { "kind": "metric", "label": "Altitude", "value": 41.8, "unit": "°" },
    { "kind": "keyValue", "items": [
      { "label": "Sunrise", "value": "06:12" },
      { "label": "Sunset", "value": "18:34" },
      { "label": "Day length", "value": "12 h 22 min" },
      { "label": "Times shown in", "value": "Asia/Dubai" }
    ]},
    { "kind": "divider" },
    { "kind": "text", "text": "Design-stage study, not a certified analysis. Sun position accurate to within 1 arcminute. Building heights marked “assumed” were not recorded in the source data." }
  ]
}
```

### Required content rules

- **"Times shown in" is always present** (FR-003) — the zone id, or the explicit statement that it
  could not be determined and UTC is in use.
- **The closing `text` block is always present** (FR-043, FR-044, SC-010): design-stage study, the
  calculation's accuracy, and the fact that heights may be assumed. It is content, not a tooltip,
  so a user encountering the results cannot miss it.
- **The accuracy figure is not hand-typed.** It comes from the tolerance constants
  `solarPosition.ts` exports and `solarPosition.test.ts` asserts against reference values, so the
  accuracy claimed to the user is by construction the accuracy actually verified. Research D1 marks
  the azimuth/altitude tolerance as *measured* rather than inherited; a literal in the panel could
  drift away from what the tests prove and turn a measured figure into an unfounded one.
- **Polar cases replace the sunrise/sunset rows** with a plain statement — "The sun does not set on
  this date" / "does not rise" (FR-002). Never a blank, never `null` rendered as a dash.
- **Below-horizon** adds "The sun is below the horizon — no shadows are cast" (FR-017).

### Why this is the first client-composed content document

Every prior content panel was composed by the model and validated at the server gate. This one is
composed in the browser. `panelContentSchema` is indifferent to the author, so no framework change
is implied — but it is the first exercise of that path, and the quickstart checks it deliberately.

## Presentation (amended 2026-09-14)

All three panels declare `density: 'compact'` chrome (specs/049 FR-017) to match the solar-analysis
reference page (`sunpath-osm-shadows-13.html`) and leave the scene they describe visible:

| Panel | Default size | Layout |
|---|---|---|
| Solar Figures | 260 × 320 | Metrics render as highlighted rows (amber monospace value); key/value items as rows on hairline dividers; the closing statement as an 11px note. Still the same content document — FR-031 is unchanged. |
| Time Control | 420 × 160 | Two rows: date; play/stop, slider, amber monospace local time. |
| Building Corrections | 280 × 300 | Small section titles, dim provenance notes, narrow monospace inputs beside small bordered Apply/Set buttons. |

The camera-attitude readout (true north and tilt) is not one of these panels: it is an overlay
whose `cameraChanged` subscription is taken once in the extension's `start()` (specs/050 FR-028).

## Accessibility (FR-032, constitution §7)

- All three panels are keyboard-operable end to end; the slider is a native `range` input with
  `aria-valuetext` carrying the local time, so assistive technology reads "14:00", not "840".
- Play/stop is a button with an accessible name that reflects state.
- The figures panel inherits `ContentRenderer`'s existing accessibility — one of the reasons FR-031
  puts them there rather than in a bespoke component.
- jest-axe covers all three. The 3D display itself is an enhancement; per the spec's own Assumption,
  the figures carry the information in text.
