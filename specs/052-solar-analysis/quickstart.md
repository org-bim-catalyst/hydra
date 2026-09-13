# Quickstart: Solar Analysis Validation

Runnable checks proving the feature works end to end, one per success criterion. Scenarios that
genuinely cannot be performed in this environment say so explicitly and state the method for a
human to run later — the same honesty applied to specs/050's SC-007 and specs/051's SC-004/SC-009,
rather than claiming a pass that was never observed.

## Prerequisites

```bash
# Frontend
cd "src/AskLucy.Web/ClientApp"
npm install                      # picks up tz-lookup (solar position is ported source, not a dep)
npx tsc -b --noEmit              # NOT bare `tsc --noEmit` — a no-op in this repo
npm test

# Backend  (note the space in the solution name)
export PERSISTENCE_TESTS_CONNECTION_STRING="<ConnectionStrings:DefaultConnection from appsettings.Development.json>"
dotnet build "Ask Lucy.sln"
dotnet test "Ask Lucy.sln" --no-build
```

---

## Scenario 1 — Sun position and day summary are correct (SC-001, US1)

**Run**: `npm test -- solarPosition daySummary`

Unit tests compare the ported NOAA implementation against published NOAA Solar Calculator
reference values at a fixed set of locations and dates spanning the year:

| Location | Why |
|---|---|
| Quito (0.2°S) | Equatorial |
| Dubai (25.2°N) | The platform's primary site |
| London (51.5°N) | Mid-latitude with DST |
| Tromsø (69.6°N) | Midnight sun and polar night |
| Ushuaia (54.8°S) | Southern hemisphere — seasons inverted |

**Tolerances** (research D1) — note which are inherited and which are measured:

| Quantity | Tolerance | Basis |
|---|---|---|
| Sunrise / sunset, \|lat\| ≤ 72° | ±60 s | Inherited from NOAA's documented figure |
| Sunrise / sunset, \|lat\| > 72° | ±10 min | Inherited from NOAA's documented figure |
| Azimuth / altitude | ±0.1° | **Measured** — NOAA states no position figure |

**Expect**: all pass. Tromsø in June returns `polarCondition: 'midnight-sun'` with null rise/set —
not an error, not a blank (FR-002). The ported implementation distinguishes the two polar cases
through the `cosH0 > 1` / `cosH0 < -1` bounds, so "never rises" and "never sets" cannot be
confused.

---

## Scenario 2 — Shadows fall correctly (SC-002, US2)

**Run**: `npm test -- sunLight shadowGround`

Asserts the sun's ENU unit vector for known position/date/time, and that the light's direction is
its negation — so shadow direction is verifiable without a GPU. Checks a known case: Dubai,
21 June, 15:00 local → sun west-northwest, shadows cast east-southeast, length ≈ height / tan(alt).

**Also asserts** (FR-018, research D16): the `ShadowMaterial` ground plane's half-extent is
**strictly less than** the shadow camera's frustum half-extent. A ground plane reaching outside the
frustum produces clamped shadow-map lookups that read as "in shadow" — the large fake grey blob the
reference implementation hit and left a warning comment about. This is the concrete failure FR-018
was written against, so it is asserted rather than assumed.

**Also asserts** (research D13): building meshes have `colorWrite === false` and
`castShadow === true` — they cast without drawing. The developer toggle flips `colorWrite` back on.

**Not measured in this environment**: the visual correctness of rendered shadow pixels. Requires a
GPU-capable browser. **Method for a human**: open a site with known surrounding buildings, set
21 June 15:00, and compare shadow directions against any sun-position reference for that
location and time.

---

## Scenario 3 — Buildings retrieved through the platform (US2, FR-009)

**Run**: `dotnet test "Ask Lucy.sln" --no-build --filter "FullyQualifiedName~Buildings"`

**Expect**: the provider parses Overpass responses; `height` → `known`; `building:levels` → metres
and `assumed`; neither → 10 m and `assumed`; unusable rings excluded and counted; the count cap
sets `limited: true`; an unavailable Overpass raises `BuildingProviderUnavailableException` →
`503` Problem Details, never a swallowed failure.

**Manual**: `GET /api/v1/site-buildings?latitude=25.197&longitude=55.274&radiusMetres=300` with a
bearer token returns a closed-ring payload. Unauthenticated returns `401`.

---

## Scenario 4 — Time scrubbing stays consistent (SC-004 partial, US3)

**Run**: `npm test -- SolarTimeControlPanel`

**Expect**: moving the slider updates sun position, shadow light direction and figures from the one
`instantUtc` — they cannot disagree (FR-022). Stopping playback leaves the moment where it stopped
(FR-021). Closing stops playback and withdraws everything (FR-024).

**Also asserts**: when not playing, the frame callback requests **zero** redraws — the guard from
research D9, which is what keeps the viewer quiet.

**Not measured in this environment**: actual frame rate during playback (SC-004's "reads as
continuous motion"). Requires a GPU-capable browser. **Method for a human**: open solar analysis
on a site with buildings, press play, and record frame rate in devtools' performance panel with
and without the analysis active. The relevant comparison is against the viewer's baseline, because
this codebase has already measured the Maps WebGL bridge contending for the GPU.

---

## Scenario 5 — Daylight-saving transitions (SC-005, FR-004)

**Run**: `npm test -- timeZone`

**Expect**: at London's spring-forward and autumn-back dates, and at a southern-hemisphere
transition, local times remain correct and unambiguous. The test drives `instantUtc` and asserts
the local projection — the direction that has one answer — never the reverse.

**Expect also**: `tz-lookup` returning null (mid-ocean coordinates) produces the explicit
"time zone could not be determined" basis label, not a silent UTC default (FR-003).

---

## Scenario 6 — Teardown leaves nothing behind (SC-006, SC-007, FR-040)

**Run**: `npm test -- solarAnalysisExtension`

**Expect**, after 50 activate/deactivate cycles:

- scene child count back to baseline;
- `renderer.shadowMap.enabled` back to its pre-activation value;
- zero panels, zero toolbar entries, zero event subscriptions remaining;
- no geometry or material left undisposed.

Mirrors the fifty-cycle assertion specs/051 already applies to drawing spaces.

---

## Scenario 7 — Every failure is visible (SC-008, FR-045, FR-046)

**Run**: `npm test -- SolarAnalysisOverlay` and the capability tests.

Each failure is forced and its user-visible surface asserted:

| Forced failure | Expected surface |
|---|---|
| Overpass returns 503 | `partial` state + "Building data is unavailable" notice; sun path still works |
| Zero buildings found | `partial` + "No buildings found near this site"; sun path still works |
| Time zone undetermined | Figures state the basis in use |
| WebGL unsupported | The viewer's existing unsupported-3D notice |
| Invalid height entered | Rejection message; previous value kept |
| Frame callback throws | `ExtensionFailureNotice` (framework containment) |
| Lucy asked with no site | Lucy says a site is needed; no analysis opens |

**Expect**: zero failures observable only in logs.

---

## Scenario 8 — Lucy opens and describes (US5, FR-033…FR-036)

**Run**: `dotnet test --filter "FullyQualifiedName~OpenSolarAnalysisCapability"`

**Expect**: with an active site, `{ "opened": true, … }` and the `__SOLAR_ANALYSIS__` SSE event
reaches `useChatStream`. With no active site, `{ "opened": false, "reason": "no-active-site" }` and
no activation.

**Manual**: ask Lucy "what's the sunlight like on this site in the afternoon?" with a site shown.
She opens the analysis and describes what it shows — referring to the display, not reciting the
figures the panel already shows (FR-034).

---

## Scenario 9 — Stated limits are discoverable (SC-010, FR-043, FR-044)

**Run**: `npm test -- solarFiguresContent`

**Expect**: the figures document always contains the design-stage-study statement, the stated
accuracy, and — when any displayed building height is assumed — a statement that heights may be
assumed. Asserted on the content document, so it cannot be lost to styling.

---

## Scenario 10 — Site to understood day in under a minute (SC-003)

**Not measured in this environment** — SC-003 is a usability-timing criterion and needs a person at
a browser. It is recorded here rather than left uncovered, because a success criterion with no
stated method is one nobody will ever run.

**Method for a human**: with a site already shown in the viewer, start a timer and ask someone who
has not used the feature to answer "does the courtyard get sun in the afternoon?". Stop the timer
when they state an answer they are confident in. Target: **under 60 seconds**, including opening
the analysis from the toolbar, setting a time, and reading the shadows.

**What a failure would tell you**: if it takes materially longer, the likely causes in order are
the toolbar entry not being discoverable (FR-029), the time control not being obvious enough to
reach for (FR-020), or building data taking too long to arrive (FR-009). Each has its own
remedy — the criterion is worth keeping precisely because it distinguishes them.

---

## Scenario 11 — SC-009: no framework change was needed

**This is the feature's real test.** After implementation, run:

```bash
git diff --name-only main -- src/AskLucy.Web/ClientApp/src/viewer/
```

**Expect**: only `viewer/extensions/builtin/solarAnalysisExtension.tsx` (new) and
`viewer/extensions/declared.ts` (one added id). Any other file under `src/viewer/` appearing in
that diff is an SC-009 finding and belongs to specs/049, 050 or 051 — not to be absorbed quietly
here (spec Out of Scope; checklist note).

**Known live risks**:

1. **research D9** — `DrawingSpaceHandle.onFrame` has no unsubscribe. The plan works around it with
   a guarded callback. If that proves insufficient, the additive fix belongs to specs/051 and must
   be **recorded as an SC-009 finding**, not folded into this feature.
2. **research D17** — there is no `environment` drawing requirement, so an extension cannot supply
   an environment map without assigning global `scene.environment`. Avoided here by dropping the
   prototype's glass dome shell. If a future capability genuinely needs it, the fix is a new
   `DrawingRequirement` in specs/051 — again an SC-009 finding, not a local workaround.
3. **research D14** — if one `invalidate()` per change proves insufficient, repeated `invalidate()`
   across successive frames is conformant and is **not** an SC-009 finding; returning to
   `requestRedraw()` would be a violation.

---

## Actual outcomes (T075, recorded after implementation)

Ran on the `052-solar-analysis` worktree. Frontend: `npx tsc -b --noEmit`, `npm test` (full suite),
`npx eslint .`. Backend: `dotnet build "Ask Lucy.sln"`,
`dotnet test "Ask Lucy.sln" --no-build` with `PERSISTENCE_TESTS_CONNECTION_STRING` set.

| Scenario | Outcome |
|---|---|
| 1 — Sun position/day summary (SC-001) | **Pass.** `solarPosition.test.ts` + `daySummary.test.ts`, 32/32, verified against a solar-noon identity (measured, not scraped) and the reference implementation's own polar-case bounds. |
| 2 — Shadows fall correctly (SC-002) | **Partially measured.** The ENU-vector/light-direction/frustum-vs-ground-plane assertions all pass (`sunLight.test.ts`, `shadowGround.test.ts`, `shadows.integration.test.ts`, 13/13). **Rendered shadow pixel correctness is NOT measured in this environment** — no GPU. Method for a human: as stated in the scenario. |
| 3 — Buildings via the platform (FR-009) | **Pass.** `OverpassBuildingFootprintProviderTests` (13/13) + `GetSiteBuildingsQueryHandlerTests`/`GetSiteBuildingsQueryValidatorTests` (14/14). Manual `GET /api/v1/site-buildings` call not performed against a live server in this environment; the provider/handler/validator chain is unit-tested end to end instead. |
| 4 — Time scrubbing (SC-004 partial) | **Partially measured.** `SolarTimeControlPanel.test.tsx` (6/6) and the guarded-`onFrame` tests in `solarAnalysisExtension.test.ts` pass. **Actual frame rate during playback is NOT measured in this environment** — no GPU. Method for a human: as stated in the scenario. |
| 5 — DST transitions (SC-005) | **Pass.** `timeZone.test.ts`, 8/8, including London spring-forward/autumn-back and a Sydney (southern-hemisphere) transition. |
| 6 — Teardown (SC-006, SC-007) | **Pass.** `solarAnalysisExtension.test.ts`'s fifty-cycle test — real `viewerExtensionLoader`/`drawingSpaceRegistry`/`panelTypeRegistry`, scene child count and contributions return to baseline after 50 activate/deactivate/stop cycles. |
| 7 — Every failure is visible (SC-008) | **Pass.** `failureSurfaces.test.ts` (5/5) plus the dedicated tests it cross-references (`SolarAnalysisOverlay.test.tsx`, `correctionsStore.test.ts`, `BuildingCorrectionsPanel.test.tsx`, `OpenSolarAnalysisCapabilityTests.cs`). Two rows are framework-owned and correctly not re-tested here: WebGL-unsupported (specs/049-051's own suites) and frame-callback-throws (`DrawingSpaceRegistry`'s existing containment). |
| 8 — Lucy opens and describes (US5) | **Pass** for everything backend-verifiable: `OpenSolarAnalysisCapabilityTests.cs`, 10/10, including the wording assertion that Lucy's instructions forbid reciting azimuth/altitude/sunrise/sunset. **Manual chat conversation with a live model was not run** in this environment — the SSE wiring itself (event parsing in `aiApi.test.ts`) is verified instead. |
| 9 — Stated limits are discoverable (SC-010) | **Pass.** `solarFiguresContent.test.ts`, 6/6. |
| 10 — Site to understood day in under a minute (SC-003) | **Not measured in this environment** — a usability-timing criterion needing a person at a browser, exactly as quickstart states. Recorded, not claimed. |
| 11 — SC-009: no framework change needed | **One finding — see below.** |

### SC-009 finding

```
git diff --name-only main -- src/AskLucy.Web/ClientApp/src/viewer/
  src/AskLucy.Web/ClientApp/src/viewer/extensions/declared.ts            (expected — one added id)
  src/AskLucy.Web/ClientApp/src/viewer/extensions/extensibility.test.tsx (NOT expected — finding 1)
  src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.ts      (NOT expected — finding 2)
  src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.test.ts (NOT expected — finding 2)

git ls-files --others --exclude-standard -- src/AskLucy.Web/ClientApp/src/viewer/
  src/AskLucy.Web/ClientApp/src/viewer/extensions/builtin/solarAnalysisExtension.tsx  (expected — new)
```

**SC-009 does not pass cleanly.** Three findings follow; the second is a real framework change.

`extensibility.test.tsx` needed a one-line update: its "FR-005" test hardcodes the full
`DECLARED_EXTENSIONS` array as a literal (four entries) and asserts equality against it. Adding
`'viewer.solar-analysis'` as the fifth declared extension made that literal stale, so the test's
own expected array needed the fifth entry appended — no production/framework logic changed.

This is a genuine, if minor, SC-009 finding: it means specs/050's own test suite encoded the
*exact contents* of the declared-extension list as a hardcoded expectation, rather than treating
"a new extension can be declared without touching specs/049-051" as the thing worth asserting.
The fix belongs to specs/050 (relax that one assertion to check length/order/prefix rather than a
literal array, or accept new entries by design) — not absorbed silently here. No other file
under `src/viewer/` was touched.

### Second SC-009 finding — re-opening a panel reset the user's layout (specs/049)

*Found and fixed during post-implementation review. This one required a framework change.*

`floatingPanelStore.openPanel` treated a request whose `requestId` matched an already-open panel as
a full re-creation: it filtered the old panel out and pushed a new one with a fresh cascade
`position`, `size` reset to `chrome.defaultSize`, `minimized: false`, `restoreState: null` and a
new front `zOrder`.

No previous caller exercised this — Lucy's panel pushes carry a unique `requestId` per request — so
the behaviour was latent. Solar analysis is the first consumer to refresh a panel's content under a
*stable* id, which FR-031 (figures as panel content) plus FR-022 (figures update with time) make
unavoidable: during playback the figures document changes every tick. The result was the figures
panel being destroyed and rebuilt at frame rate, resetting position and size, un-minimizing itself
and stealing z-order continuously — unusable, and contrary to US3 scenario 4 and FR-023/SC-004.

**Fixed in `viewer/panels/store/floatingPanelStore.ts`**: re-opening an already-open id is now a
refresh — content, title, data and validation update; position, size, minimized, `restoreState`,
`zOrder`, `lastFocusedAtUtc` and `opacityOverride` are preserved, and no cascade slot is consumed.
Four regression tests were added to `floatingPanelStore.test.ts`.

This is the correct home for the fix — "re-opening an open panel should not yank it back to a
cascade position" is right regardless of solar analysis — so it is recorded here as a specs/049
finding rather than worked around inside this feature. It is also the one place where SC-009's
strict claim ("no change to the viewer, extension or panel frameworks") does not hold: the panel
framework genuinely lacked an in-place content-refresh path, and FR-031 forbids the obvious
workaround of making the figures a bespoke live panel.

### Third SC-009 finding — `startsWithViewer` is declared but never honoured (specs/050)

*Added during post-implementation review.*

`ExtensionManifest.startsWithViewer` is defined in `viewer/extensions/ViewerExtension.ts` and
documented as defaulting to `true`, but **no code anywhere reads it**. `ViewerSurface.tsx` starts
every id in `DECLARED_EXTENSIONS` unconditionally, and `loader.start()` never consults the
manifest. Solar analysis is the first extension to set it `false`, which is why this surfaced now.

Consequences for this feature, both real:

1. `start()` runs at viewer load for every user, so `declareDrawingRequirement('shadows')` sets
   `renderer.shadowMap.enabled = true` globally from the moment the viewer opens — whether or not
   solar analysis is ever activated — and `rendererState` only withdraws it when the extension
   *stops* (viewer close), not on deactivate. The practical visual impact is believed nil while
   deactivated, because no shadow-casting light is in the visible set and Three.js compiles no
   shadow defines without one; but the guarantee research D6 asked for (shadows enabled as a
   deliberate, reviewed step) is not actually held by the code.
2. The manifest now states something untrue about this extension, which will mislead the next
   reader.

**The fix belongs to specs/050**, not here: either honour `startsWithViewer` in the host start
loop, or remove the field so no manifest can claim a behaviour the framework does not implement.
Until then this feature leaves the declaration in place (it describes the intent correctly) and
this finding records the gap.

Separately (not an SC-009 finding, but worth recording): adding the toolbar entry — the first
built-in extension to actually contribute one (FR-029) — changed two pre-existing tests' *expected
data*, not framework behaviour: `ChatPage.test.tsx`'s keyboard tab-order list needed "Solar
Analysis" inserted (it is now the first focusable element on the page, since `ExtensionToolbar`
sits earlier in the DOM than `WorkspaceOverlay`'s own controls), and
`ViewerSurface.a11y.test.tsx`'s "placeholder never traps focus" assertion needed to scope its
query to the placeholder element itself rather than the whole surface (the toolbar is a
legitimate sibling control, not part of the inert placeholder graphic the test was actually about).
Both were updated rather than the toolbar entry being suppressed, since a discoverable toolbar
entry regardless of site state is what FR-029 actually asks for.
