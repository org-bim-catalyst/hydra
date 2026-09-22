# Post-Implementation Verification (T051)

**For**: the person driving the app and taking screenshots.
**Against**: [baseline.md](./baseline.md) — the "before" capture from `pre-solar-upgrade`
(f42e6814), taken 2026-09-21 at **بدر، مصر (Badr, Egypt)**, **Sunday 21 September 2026**, assumed
building height **9 m**, ground offset **0**.

Every step below is mechanical: set a stated control to a stated value, capture, and move on. Each
one names the number that should appear. **Do not judge whether a shadow "looks right"** — capture
the frame and let the reviewer compare it against the expected value here.

Nine screenshots total. Steps 1–2 are setup.

---

## Setup

**Step 1.** Open `localhost:7170/studio`, sign in, and open solar analysis on **Badr, Egypt** — the
same site as the baseline. If that site is unavailable, use any dense urban site and say which one;
Checks A, F, G and H still hold, Checks B–E need the baseline's own site.

**Step 2.** Set the date to **21 September 2026**. Confirm the Building Corrections panel shows
**assumed height 9 m** and **ground offset 0**. If either differs, reset them before continuing.

---

## Check A — the panel no longer contradicts itself (the main repair)

**Step 3.** Read the **Sunrise** time in the figures panel. Record it.

> **Expected: `06:41`**, or within one minute of it (`06:40`–`06:42`). The measured rise/set shift
> at this latitude is ~3 s, so the displayed minute should be unchanged or off by one from rounding.
> A shift of more than 3 minutes is a defect.

**Step 4.** In the Time of Day panel, **type that exact time into the time field and press Enter**.
Do not drag the slider — dragging snaps to 15 minutes and cannot land on a named minute.

**Step 5.** Screenshot the **whole viewer, figures panel included**.

> **Expected, all three:**
> 1. **Altitude reads between −0.3° and 0.0°** (baseline read **−0.8°**).
> 2. **No "The sun is below the horizon — no shadows are cast." line.** Its presence at the
>    feature's own reported sunrise is the exact defect being repaired.
> 3. The closing paragraph states the altitude is the sun's **centre, corrected for atmospheric
>    refraction**.
>
> The exact figure is −0.27° at every site and date, but the field accepts only HH:MM, so the
> instant you can type is up to 59 s late. The tight value is asserted in `daySummary.test.ts`; what
> this screenshot proves is that the panel stops contradicting itself.

---

## Check B — nothing moved at midday

**Step 6.** Type **`12:00`** into the time field. Screenshot the whole viewer.

> **Expected: azimuth `157.4°`, altitude `58.5°`** — identical to baseline. Refraction is ~0.01° at
> this elevation, below the panel's one-decimal rounding. **Shadows in the same places as the
> baseline's 12:00 shot.** Any visible shadow movement at midday is a defect (SC-009).

---

## Check C — low sun, and the truncated-shadow band

**Step 7.** Type **`17:51`**. Screenshot the whole viewer.

> **Expected: azimuth `263.4°` unchanged; altitude `12.2°` or `12.3°`** (refraction adds ~0.07°,
> which may cross the rounding boundary).
>
> **The thing to capture is the ground.** The baseline records that at this instant shadows were
> confined to a band across the site while buildings further out cast none — that band was the
> fixed frustum. **Expected now: every building with a shadow to cast has one, no shadow ends at an
> invisible straight line, and there is no large grey patch anywhere on the ground.** The grey patch
> is the single most important failure to look for.

---

## Check D — the new no-shadow window

**Step 8.** Type a time **two minutes after the sunrise you recorded in Step 3** (e.g. `06:43`).
Screenshot the whole viewer.

> **Expected:** daylight, buildings present, **no shadows drawn**, and this sentence on screen:
> *"The sun is less than 1° above the horizon — shadows are not drawn at this elevation, where they
> would stretch for kilometres."*
>
> Shadows vanishing with **no** stated reason is a defect.

---

## Check E — still nothing after dark

**Step 9.** Type **`19:50`**. Screenshot the figures panel.

> **Expected: altitude `−13.6°`** unchanged from baseline (refraction is zero this far down), no
> shadows, and **"The sun is below the horizon — no shadows are cast."** present.

---

## Check F — the dome is stable across dates

**Step 10.** Type **`15:00`**. Screenshot the viewer, framing the sun-path dome so the **compass
dial**, the **mount post** and the **faint monthly lattice** are all visible.

**Step 11.** Step the date forward **four times** (22, 23, 24, 25 September). Screenshot the same
framing after the fourth step.

> **Expected:** the day's arc and the hour marks change between the two shots; **the dial, the post,
> the lattice and the dome shell are pixel-identical** — no flicker, no movement, no change in
> brightness or label position (SC-008).

---

## Check G — scrubbing stays smooth

**Step 12.** Return to 21 September. Drag the time slider across the full day in one sweep, then
press **play** and rotate the camera while it runs.

> **Expected:** continuous motion, no stutter, the camera stays responsive throughout.
> **A screenshot cannot show this** — capture a short screen recording, or state in words whether it
> stuttered and where.

---

## Check H — corrections still respond during playback

**Step 13.** With playback **running**, change the site building's height in the Building
Corrections panel (9 m → 40 m). Screenshot within a second or two of the change.

> **Expected:** the shadow lengthens **immediately**, not after a pause. The playback gate skips
> only sun movement; it must never hold back a geometry change (FR-019 as amended).

**Step 14.** Reset the height. Stop playback.

---

## Reporting

Send the nine screenshots in step order, plus the recording or a sentence for Check G, and the two
numbers you recorded by hand (Step 3's sunrise, Step 5's altitude). The reviewer compares them
against the expected values above and against [baseline.md](./baseline.md); no judgement about
solar correctness is needed from the person capturing.

**If something is wrong**, `git reset --hard pre-solar-upgrade` restores f42e6814. The spec and plan
commits are separate from the implementation, so the documents survive a code-only revert.
