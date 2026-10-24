# Contract: Solar Twilight Times & Year Day-Length Chart

**Feature**: specs/085-solar-twilight-daylength | **Date**: 2026-10-23

This contract describes what the feature presents and how it behaves. It extends
[specs/052's panel contract](../052-solar-analysis/contracts/solar-panels.md) — the amendment to
that contract is in [solar-panels-amendment.md](solar-panels-amendment.md), and where the two
speak about the same thing, this one is read with it.

---

## 1. The twilight figures — added to the Solar Analysis panel

The Solar Analysis panel is the figures panel (specs/052 research D12): a **content panel** whose
document is composed from specs/049's block vocabulary. The twilight figures join the existing
sunrise, sunset and day-length rows.

### Rows

| Row | Value |
|---|---|
| Civil twilight | The two times the sun's centre crosses 6° below the horizon, and the length of the civil band. |
| Nautical twilight | The same for 12°. |
| Astronomical twilight | The same for 18°. |

The three phase rows appear in that order — civil, nautical, astronomical — which is the order
of increasing darkness.

### The times a phase reports

A phase reports **two** times: the crossing on the way down and the crossing on the way up.

- The **descending** crossing is when the sun falls through the threshold: the end of that
  evening's daylight, and the start of the night.
- The **ascending** crossing is when the sun climbs back through it: the end of the night, and
  the start of the next day's twilight.

They are labelled so the reader is never in doubt which is which.

### The length a phase reports

The length is the duration of the band the phase occupies, measured on the evening side:

| Phase | The band |
|---|---|
| Civil twilight | The horizon to −6°. |
| Nautical twilight | −6° to −12°. |
| Astronomical twilight | −12° to −18°. |

The length is the band's width — the number a person means when they ask how long twilight lasts
— and it is the same width the chart draws for that band.

### When a phase does not occur

A phase occurs on a date only if the sun's centre reaches its lower threshold on that date. Where
it does not, the panel says so **in words for that phase**, naming the threshold:

> *The sun does not reach 18° below the horizon on this date, so there is no astronomical
> twilight.*

The row is **not** omitted, and no time is shown for it. The panel's existing statement that the
sun does not rise, or does not set, stays exactly where it is and keeps its own wording: a
missing twilight phase is a different fact from a missing sunrise, and at high latitude both
occur on the same date (research D6).

### The threshold definition is stated

The panel states, once, that the twilight thresholds are the sun's centre **before** atmospheric
refraction is applied, and that the altitude figure shown beside them is refraction-corrected.
The two therefore differ by a constant, and the panel says what it is:

- at a civil crossing, the altitude figure reads **−5.43°**;
- at a nautical crossing, **−11.43°**;
- at an astronomical crossing, **−17.43°**.

These are the same values at every site and on every date, and the panel's wording makes that
visible rather than leaving the reader to wonder why "6° below" reads as 5.43°.

### A threshold that could not be solved

Where the solver does not converge for a threshold, the panel says the times could not be
determined, in the same place the times would have appeared, exactly as the existing
"could not be determined" wording does for a sunrise that would not solve. An unconverged answer
is never shown as though it were solved.

---

## 2. The day-length chart — a live panel

The chart is a **live panel**, registered by the solar extension and opened when the solar
analysis activates, beside the Solar Analysis panel.

It is registered like the Time of Day and Building Corrections panels — through
`context.registerLivePanelKind(...)`, with its own data schema — and it never touches the
shared content vocabulary (research D7, D8, D14).

### What it shows

| Element | Value |
|---|---|
| The chart | One column per day of the selected date's year, in calendar order left to right. Each column is that day's phases, in order, down the local day from midnight at the top to midnight at the bottom. |
| The axes | Day of the year across; hour of the local day down, with gridlines and labels every two hours. |
| The bands | Night, then astronomical twilight, nautical twilight, civil twilight, daylight, then the same twilight phases in reverse, then night. |
| The selected date | Marked on its column, with the two times its daylight begins and ends labelled beside the crossings. |
| The caption | The site's latitude, and which hemisphere it lies in. |
| The description | A text description of the chart, for assistive technology. |
| The legend | The five bands, named, so the chart is never read by colour alone. |
| The extremes | The year's longest and shortest day, with their dates and lengths, as text — the chart shows the shape, these two numbers name it (research D13). |

The daylight band's width follows the seasons: it is widest at the site's summer and narrowest at
its winter, and the whole year reads as a gradient that narrows and widens.

### What it does not do

The chart is **read-only**. It does not set the time, does not change the site, does not change
the selection, and does not navigate anywhere — whether by pointer or by keyboard. It is not a
control, and no part of the platform's canonical state is written through it.

### Its data

The chart is drawn from the year's **profile**: one phase sequence per day, computed once per
site and year and reused. The profile is not recomputed when the time of day changes, and not
when the selected date changes within the same year (research D9). The chart's bands and the
panel's figures come from the same day-level computation, so they cannot disagree (research D12).

### Its colours

Each band is a named series, and its colour is resolved from the MUI theme by the solar module
and handed to the chart. The chart hardcodes no colour, and the bands follow the light and dark
themes automatically (research D11).

---

## 3. What the feature does not change

- The **sun's position**, the sun path, the shadows and the scene are untouched. The thresholds
  are read from the same position function the rest of the feature uses.
- The **rise and set figures** keep their values, their rounding and their wording.
- The **shared content vocabulary** keeps its version, its schemas and its server-side contract.
- The **time control and building corrections panels** are unchanged.
- The **canonical instant** is written only by the controls that already write it; this feature
  writes nothing.
