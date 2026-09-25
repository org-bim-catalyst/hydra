# Phase 0 Research: Solar Analysis

Decisions taken before design, each with the alternatives actually weighed. Where a decision
corrects an assumption carried in from the spec or from the reference prototype, that is stated
rather than quietly fixed.

**The reference implementation** (`sunpath-osm-shadows-13.html`, supplied 2026-09-13) was read in
full after D1–D12 were first drafted. It changed two of them and added five constraints that were
not visible from the spec alone. Those revisions are marked below rather than silently folded in —
the prototype is a specification of behaviour, and what it *discovered the hard way* is the most
valuable thing in it.

---

## D1 — Solar position and rise/set: port the reference implementation's NOAA code

**Decision** *(revised 2026-09-13 after reading the reference implementation; confirmed with the
user)*: port the prototype's NOAA solar-position implementation into a tested TypeScript module at
`features/solar/solar/solarPosition.ts`. **No runtime dependency is added.**

**Rationale**:

- The prototype already contains a working, exercised implementation of the published NOAA
  algorithm (~40 lines): mean longitude, equation of centre, apparent longitude, obliquity
  correction, declination, equation of time, hour angle → altitude/azimuth.
- It already handles the polar cases FR-002 needs, and handles them *distinguishably*: `cosH0 > 1`
  → sun never rises (polar night), `cosH0 < -1` → sun never sets (midnight sun). This was the
  single strongest argument for a library, and the prototype answers it.
- NOAA documents rise/set accuracy: **within ±1 minute for latitudes within ±72°, within ±10
  minutes beyond**, with the approximations "very good for years between 1800 and 2100". Every
  SC-001 test location including Tromsø (69.6°N) falls inside the ±72° band.
- Constitution and CLAUDE.md both say to avoid unnecessary dependencies; ~230 KB and a supply-chain
  addition is a real cost for a calculation the project already has working code for.
- Pure functions of `(instant, latitude, longitude)` — trivially unit-testable with no rendering,
  which is what actually validates SC-001.

**Alternatives considered**:

- **`astronomy-engine`** (MIT, v2.1.19) — this was the original D1. It documents ±1 arcminute
  against NOVAS C 3.1/DE405 for *position as well as* rise/set, which NOAA does not formally
  quantify for position, so SC-001's position tolerance would have been inherited rather than
  measured. Rejected once the prototype arrived: its two decisive advantages (documented accuracy,
  null rise/set semantics) shrank to one, and that one is answerable by testing against reference
  values — which SC-001 requires regardless of which implementation ships.
- **`suncalc`** (~3 KB) — no formally documented error bound, and returns `Invalid Date` rather
  than a distinguishable never-rises signal. Rejected.
- **Computing server-side** — rejected under D3 below.

**Stated tolerances for SC-001/SC-002**:

| Quantity | Tolerance | Basis |
|---|---|---|
| Sunrise / sunset | ±60 s within ±72° latitude | **Inherited** from NOAA's documented figure |
| Sunrise / sunset beyond ±72° | ±10 min | **Inherited** from NOAA's documented figure |
| Azimuth / altitude | ±0.1° | **Measured**, not inherited — NOAA states no position figure. Asserted against published NOAA Solar Calculator values at the SC-001 test locations. |

The azimuth/altitude row is the one tolerance in this feature that is measured rather than
inherited, and it is labelled as such so nobody later mistakes it for a published guarantee. It is
recorded in the figures panel wording (FR-044) as the calculation's stated accuracy.

---

## D2 — Site-local time: `tz-lookup` + `Intl.DateTimeFormat`

**Decision**: Resolve the site's IANA time zone offline from its coordinates with `tz-lookup`
(CC0, ~150 KB, no network), then do every conversion and every piece of formatting through the
platform's own `Intl.DateTimeFormat` with that `timeZone`.

**Rationale**:

- FR-003 requires site-local time; FR-004 requires correctness "across daylight-saving
  transitions". `Intl.DateTimeFormat` with an IANA zone id gets DST right from the browser's own
  tzdata — there is no arithmetic for us to get wrong, and no offset to cache and have go stale on
  the transition day. The only missing piece is coordinates → zone id, which is what `tz-lookup`
  supplies.
- Offline resolution keeps scrubbing immediate: no round trip when the date changes, matching the
  Assumption that the calculation runs where the display is.
- `tz-lookup` returns `null` for coordinates it cannot place (mid-ocean, some boundary cases).
  FR-003 requires the time basis be stated "including when it cannot be determined" — so this null
  is surfaced as an explicit "times shown in UTC — time zone could not be determined for this
  location" statement in the figures, not silently defaulted.

**Alternatives considered**:

- **Google Time Zone API** — authoritative and already adjacent to the Maps key in use, but it is a
  network round trip, a metered cost, and a failure mode on the critical path of a control the user
  drags. Site-local time is not worth a request per site.
- **Deriving an offset from longitude** — wrong across every political boundary and incapable of
  DST. Not viable against FR-004.
- **Resolving on the backend** — possible, but adds a round trip for a value the browser can
  compute instantly and which nothing server-side needs (see D3).

---

## D3 — Lucy does not recompute the solar figures

**Decision**: `OpenSolarAnalysisCapability` opens the analysis for the active site and returns only
the site, the date/time and a status. It performs **no** solar computation. The figures are
computed once, in the browser, and Lucy's prose describes what the user is looking at.

**Rationale**:

- FR-034 is explicit: "Lucy's reply MUST refer to what is displayed rather than restating figures
  the user can already see." She therefore does not need the numbers, which dissolves the apparent
  tension in the spec's Assumption that figures should be "obtained consistently rather than
  computed twice by different means" — they are computed once, not twice, because the only consumer
  that renders them is the browser.
- This keeps `astronomy-engine` out of the backend entirely: no second implementation to keep in
  agreement, no drift between what Lucy says and what the panel shows, and no server-side
  dependency added for a display concern.
- FR-035 (no site → say a site is needed) and FR-036 (analysis could not be produced → say so and
  why) are both decidable from state the capability already has, without any astronomy.

**Mechanism**: mirror the trailing-SSE tag pattern proven twice in this codebase —
`AdjustViewerFocusCapability`/`__ZOOM__` (specs/038) and `LoadViewerContentCapability`/
`__VIEWER_CONTENT__` (specs/051). The capability returns `AgentToolResult.Success(json)`;
`StructuredPayloadExtractor` picks it up by capability key onto a `ChatStreamChunk` field;
`AiController` writes a trailing `data: __SOLAR_ANALYSIS__{json}` SSE event; `aiApi.ts` parses it
into a `ChatStreamEvent`; `useChatStream.ts` opens the analysis. Nothing new is invented.

**Alternative considered**: a SignalR push, as the panel framework uses. Rejected — the viewer-
affecting capabilities all use the trailing-SSE tag, and this is a viewer-affecting capability. (An
earlier reading of specs/051 assumed MediatR/SignalR here and had to be corrected against the code;
the correction is recorded in that spec's research D8 and is not repeated as a mistake.)

---

## D4 — Building footprints through the platform, reusing specs/042's Overpass infrastructure

**Decision**: Add `IBuildingFootprintProvider` in `Application/Buildings`, implemented by
`OverpassBuildingFootprintProvider` in `Infrastructure/Buildings`, reusing the **existing
`"Overpass"` named `HttpClient`** and its mirror list. Expose it to the browser through a new
authenticated read endpoint, `GET /api/v1/site-buildings`.

**Rationale**:

- The spec's third Clarification settles that building data is fetched by the platform, not the
  browser — for caching, rate limiting, one place to handle the source being slow, and because
  third-party geometry is untrusted data that should cross a trust boundary we control (§8 prompt
  injection/untrusted-content posture applies to geometry too).
- `IBoundaryCandidateProvider` + `OverpassBoundaryCandidateProvider` already solve, in this
  repository, every hard part of talking to Overpass: three attempts, rotation across the cluster's
  own nodes rather than retrying a saturated load balancer, a **30 s** client timeout (not 15 s —
  the recorded false-unavailability trap on this host), a typed unavailable-exception, and a
  `[timeout:25]` server-side budget. Mirroring that shape is DRY at the level that matters
  (constitution §2.III) and inherits hard-won operational knowledge.
- The provider is an interface in `Application` so a future authoritative/cadastral source is an
  additive `Infrastructure` implementation (§2.II OCP, §3 Infrastructure isolation).

**Why a REST endpoint and not only an agent capability**: FR-029 offers solar analysis through the
viewer's own toolbar, so the user reaches it without Lucy. A toolbar click must be able to fetch
buildings directly. The endpoint is the primary path; the capability (D3) only opens the analysis,
which then uses the same endpoint.

**Caching and rate limiting are part of this decision, not extras** *(added after `/speckit-analyze`
found both unimplemented)*. The spec's Clarification justifies the platform round trip on
"caching, rate limiting and a single place to handle the source being slow or unavailable". All
three must therefore actually exist: an `IMemoryCache` entry keyed by rounded coordinates plus
radius, and a `buildings-endpoints` rate-limit policy mirroring `WeatherController`'s. A
justification that nothing implements is worse than no justification, because it reads as settled.
Note that `OverpassBoundaryCandidateProvider` documents "no caching by design" — that is correct
for *its* one-shot boundary resolution during a chat turn, and is not a precedent for a lookup the
user can re-trigger by toggling a panel.

**Query shape**: `way["building"](around:R,lat,lng)` plus `relation["building"]` skipped for v1 —
the same simplification `OverpassBoundaryCandidateProvider` makes and for the same reason (simple
closed ways cover the large majority; relation support is additive later).

**Bounds (FR-015)**: radius default 300 m (configurable via options), hard cap on returned count
default 300. When the cap truncates the result the response carries `limited: true` and the count
actually available, so the user can be told rather than silently shown a partial city.

---

## D5 — Building heights: known vs assumed

**Decision** *(default revised to 9 m after reading the reference implementation)*: resolve each
footprint's height in this order, recording which rule fired:

| Order | Source | Result |
|---|---|---|
| 1 | `height` tag (metres, parsed leniently for a trailing `m`) | **known** |
| 2 | `building:levels` × 3.0 m per level | **assumed** |
| 3 | Default **9.0 m** | **assumed** |

The 9 m default is the prototype's, and it is better than the 10 m originally drafted here for a
reason worth keeping: 9 m *is* three levels at the same 3 m per level rule used one row above, so
the default and the levels rule agree with each other instead of being two unrelated constants.

**Rationale**: FR-010 requires a stated default; FR-011 requires the user be able to see whether a
height "was recorded in the source or assumed". Carrying a two-value provenance flag (`known` /
`assumed`) alongside the metres is the smallest thing that satisfies both, and it is computed on
the **backend** — where the raw tags are — so the browser never re-parses OSM tag soup. The
3 m-per-level and 10 m constants are stated in the contract and surfaced in the UI (FR-044), not
buried.

**Alternative considered**: carrying the raw tag dictionary to the browser and deciding there.
Rejected — it pushes untrusted third-party strings further into the client for no benefit, and
duplicates a parsing rule that has exactly one correct home.

---

## D6 — Shadows: DirectionalLight + orthographic shadow camera + `ShadowMaterial` ground

**Decision**:

- Declare `shadows` and `toneMapping` via `drawingSpace.declareDrawingRequirement(...)` — never
  touch `renderer.shadowMap` or `renderer.toneMapping` directly (FR-038, specs/051 FR-016).
- Put a `THREE.DirectionalLight` **inside the extension's own Drawing Space group**, positioned
  along the sun's ENU unit vector at a fixed distance, with `castShadow = true`.
- Size the light's **orthographic** shadow camera exactly to the analysis radius, and set
  `shadow.camera.near/far` to bracket the tallest building. This is what makes FR-018 true: shadows
  are computed only within the analysis area, so no spurious dark region can appear outside it.
- Receive shadows on a finite `THREE.PlaneGeometry` at the ground offset using
  `THREE.ShadowMaterial` — which renders *only* the received shadow and is transparent elsewhere,
  so the map imagery beneath shows through unchanged.
- When the sun is below the horizon: disable the light and hide the ground plane, and state it in
  the figures (FR-017 — "evident rather than ambiguous").

**Rationale**: `ShadowMaterial` is the only way to cast onto a map basemap without painting an
opaque ground over it. An orthographic shadow camera (not perspective) is correct for a
directional light and is the lever FR-018 actually needs. Sizing it to the analysis radius also
bounds the GPU cost, which matters here specifically: this codebase has already measured the Google
Maps WebGL bridge contending for the GPU with other 3D work
(`sphere_perf_was_actually_map_gpu_contention`), so an unbounded shadow frustum would be a
performance regression, not just a correctness one.

**Enabling shadows is its own deliberate step.** `shadowMap.enabled` is global. The magma-glow and
boundary-highlight layers were tuned without it (commits 728b89f, dbf4c73, 5d59cc9, 0821559). The
plan treats switching it on as a separate task with an explicit before/after visual comparison,
rather than a side effect of adding a light — this is the fourth hard constraint recorded when the
049–052 sequence was agreed.

---

## D7 — Footprint extrusion, and excluding what cannot be used

**Decision**: convert each footprint ring lat/lng → local metres with specs/051's `worldToLocal`,
build a `THREE.Shape`, and extrude with `ExtrudeGeometry({ depth: heightMetres, bevelEnabled: false })`.
A footprint is **excluded** (FR-013) when it has fewer than 4 points, is not a closed ring, has
zero or near-zero area, or is self-intersecting.

**Rationale**: FR-013 requires unusable footprints be excluded "without failing the whole
analysis" — so exclusion is a filter with a count, not an exception. The count of excluded
footprints is carried to the UI so the user is told rather than silently shown fewer buildings than
exist. Self-intersection is detected cheaply by segment-pair intersection on the ring; at the
bounded ring sizes OSM produces this is not worth a library.

**Positioning**: every vertex goes through `worldToLocal` against the viewer's single reference
point — the extension never sets a reference point of its own (FR-041, and the first hard constraint
of the 049–052 sequence: stale self-anchoring has already bitten `GoogleMapsGisLayer` once).

---

## D8 — Which building is the site's own (FR-012)

**Decision**: the stated rule is — **the footprint whose polygon contains the site point; if none
contains it, the footprint whose edge is nearest to the site point, provided it is within 25 m;
otherwise none.**

**Rationale**: FR-012 requires the rule be *stated*, not that it be clever. Point-in-polygon first
is the unambiguous case the spec's edge-case note ("the site sits inside a building footprint")
asks about. The nearest-edge fallback with a stated radius handles a geocode that lands in the
street outside its building without silently adopting a building across the road. Returning *none*
is a legitimate outcome and is shown as such rather than forced to a guess.

**Edge case covered**: "between several footprints" — containment is tested in the order Overpass
returned, and the first containing footprint wins; overlapping containment in OSM data is a data
error, and picking deterministically beats picking cleverly.

---

## D9 — Per-frame subscription during playback — **and the one real SC-009 risk**

**Decision for the plan**: subscribe once via `drawingSpace.onFrame(...)` when the extension
starts, and **guard inside the callback**: when playback is not running the callback returns
immediately and, critically, does **not** call `invalidate()`. Only while playing does it advance
the clock and request the next redraw.

**Why this is the honest reading**: FR-039 requires solar analysis to "request redraws rather than
driving the viewer's drawing, and use a per-frame subscription only while it genuinely needs one."
The purpose behind that requirement — recorded as the third hard constraint of this sequence — is
that neither the prototype nor `GoogleMapsGisLayer` may run a permanent redraw loop. The guard
satisfies that purpose exactly: when idle, zero redraws are requested, so the viewer goes quiet.

**The risk, stated plainly**: specs/051's `DrawingSpaceHandle.onFrame(callback)` returns `void` —
there is **no unsubscribe**. So the literal wording of FR-039 ("only while it genuinely needs one")
cannot be fully satisfied: an inert callback stays registered while the extension is running, costing
one no-op call per actual drawn frame. Notably, specs/051 *did* add a `frameSubscription`
contribution kind carrying an `unsubscribe`, which nothing currently produces — the shape was
anticipated and the producing API was not.

**How this is to be handled**: SC-009 says needing a framework change is evidence an earlier
specification was incomplete, and that the change belongs *there* rather than being absorbed
quietly here. So:

1. Build against the guard, as above. It is sufficient for every acceptance scenario in User
   Story 3 and for SC-004.
2. If implementation shows the guard is *not* sufficient, the fix is the additive change to
   specs/051 — `onFrame` returning an unsubscribe function, wired to the already-existing
   `frameSubscription` contribution kind — and it is to be **recorded as an SC-009 finding against
   specs/051**, not written off as part of this feature.

This is the one place where this feature's role as a test of the framework has a live chance of
returning a negative result, and it is better stated up front than discovered and smoothed over.

---

## D10 — Following the site, and content replaced beneath the analysis (FR-042)

**Decision**: the extension subscribes to `useActiveLocationStore` for the site, and to the viewer's
`contentLoading`/`contentFailed` events through `context.on(...)`. On a site change it **recomputes
for the new site**: new time zone, new sun path, new building fetch, corrections re-keyed (D11).
It closes only if the viewer has no site at all.

**Rationale**: FR-042 permits "follow the new site or close"; following is strictly better for the
user and costs nothing extra, since every input the analysis has is already derived from the site.
Closing is reserved for the case where following is meaningless. Acceptance scenario US1-6 ("the
user moves to a different site → the sun path and figures update") requires following anyway, so
the two requirements agree.

**Revised 2026-09-25 — close, not follow.** Live testing showed following was not "strictly
better": with the analysis open on Al Safa Park 2, confirming Dubai Mall carried the sun path and
figures onto a site the user had never asked to analyse, which read as if they had. FR-042's other
branch is now the behaviour: leaving a site deactivates the analysis and withdraws its three panels
(`ExtensionContext.withdrawPanel`, which bypasses the reopen tray, since a reopened panel would be
empty or describe the site just left). US1-6 is amended to match. Two details matter:

- The close is a synchronous `useActiveLocationStore.subscribe` taken in `start()`, not an overlay
  effect. When Lucy confirms a new site and opens the analysis for it in the same turn, both stream
  events can land before React commits; an effect would then close the analysis Lucy had just
  opened. Subscribed, the close runs inside the location update, before that `activate()`.
- Only a site being *left* closes it. An analysis opened before any site existed still follows the
  first site that arrives, and the stale-data guard below is unchanged.

**Stale-data guard (edge case: "changes date or time while building data is still loading")**: each
building fetch carries the site key it was issued for; a response whose key no longer matches the
current site is discarded. The display therefore never mixes the old site's buildings with the new
site's sun.

---

## D11 — Corrections: session-scoped, keyed by site

**Decision**: a Zustand store mapping a **site key** (latitude/longitude rounded to ~1 m,
6 decimal places) to that site's corrections — per-building height overrides and the ground offset.
Not persisted; cleared on reload.

**Rationale**: the spec's Out of Scope is explicit that corrections "are not persisted beyond the
session", and its Assumption explains why: persisting implies a model of site records that does not
exist yet (YAGNI, §2.III). FR-028 requires it be clear which site a correction applies to — keying
by site makes that structural rather than a label, and lets the panel name the site the corrections
belong to. Rounding to 6 decimals means a returning user at the same site keeps their corrections
within the session, while a genuinely different site gets a clean slate.

**Validation (FR-027)**: height must be > 0 and ≤ 1000 m; ground offset must be within ±500 m.
Rejection keeps the previous value and states why — never a silent clamp.

---

## D12 — Figures as panel content, controls as live panels

**Decision**: the time controls and the building corrections are **live panels**
(`context.registerLivePanelKind(...)`, React components). The solar figures are a **content panel**
composed from specs/049's block vocabulary — `metric` and `keyValue` blocks — opened via
`context.openPanel({ kind: 'content', ... })`.

**Rationale**: this is what FR-030 and FR-031 separately require, and the split is principled
rather than incidental. The controls are interactive code with their own state, which is exactly
what a live panel kind is for. The figures are data — sunrise, sunset, day length, azimuth,
altitude — and FR-031 says they must be "panel content rather than a purpose-built component",
which is the block vocabulary's whole purpose. It also means the figures inherit `ContentRenderer`'s
existing accessibility and validation for free, satisfying §7/FR-032 without new a11y surface.

**Consequence worth noting**: this makes solar analysis the first consumer to compose a content
document **client-side** rather than receiving one from the model. The vocabulary and
`panelContentSchema` are indifferent to the author, so no framework change is implied — but it is
the first time that path is exercised, and the quickstart checks it explicitly.

---

## D13 — Buildings cast but are not drawn (the "ghost duplicate")

*Added 2026-09-13 from the reference implementation. Confirmed with the user; FR-010 and US2
scenario 1 were amended to match.*

**Decision**: building meshes are built with `colorWrite: false` and `depthWrite: false` but keep
`castShadow: true`. They are absent from the colour and depth passes, and present in the shadow-map
depth pass — so they cast real shadows while drawing nothing. A developer toggle flips `colorWrite`
back on to reveal the massing for verification.

**Why this is not a shortcut**: Google's vector basemap already draws its own 3D buildings when
tilted. Our OSM-sourced extrusions are a *second copy from a different data source*, and two
building datasets essentially never align pixel-perfect. Drawing both produces a visibly shifted
duplicate — which reads to a user as a rendering bug, not as information. The prototype hit this
and solved it exactly this way.

What the user actually needs from the surrounding buildings is their **shadows**; their massing is
already on screen, drawn by the basemap, better aligned than ours would be. Height and its
provenance stay visible for the building being analysed (FR-011) as figures in the corrections
panel, which is where a number belongs anyway.

**Alternative considered and rejected**: draw our massing and switch the basemap to a flat or
buildings-only style (specs/048 already provides `setMapStyle('buildings-only')`) for the duration
of the analysis. Rejected because it changes the viewer's appearance underneath the user to work
around a problem that has a cheaper answer, and it would leave the analysis unusable in whatever
style the user actually wanted to look at.

---

## D14 — A single `invalidate()` may not be enough: the redraw-timing trap

*Added 2026-09-13 from the reference implementation. This one directly qualifies D9.*

**What the prototype found**, in its own words: a permanent loop calling `requestRedraw()` every
frame "was found to desync the map's own camera transform during pans/zooms, causing the whole
scene to render shifted from its true anchor" — but a single redraw after a change produced a
different bug, where the change "doesn't appear until I interact". Its resolution was to burst
~12 redraw requests over consecutive frames after each real change.

**Decision**: request a redraw through `redrawScheduler.invalidate()` after every state change, as
FR-039 requires — and **verify during implementation whether one coalesced redraw is actually
sufficient**. specs/051's scheduler coalesces multiple `invalidate()` calls within a frame into one
draw, which is correct for the desync bug but is precisely the shape that exhibited the
"doesn't appear until I interact" symptom in the prototype.

If a single invalidate proves insufficient, the sanctioned fix is **repeated `invalidate()` calls
across successive animation frames from the extension** — which stays within the framework's
contract, since the extension is still only ever *requesting* redraws and never driving the draw
loop. It is not a licence to reintroduce `requestRedraw()`.

**Why this is recorded rather than pre-solved**: the symptom depends on the map bridge's own draw
timing, which specs/051 changed (it removed the unconditional per-frame `requestRedraw()` that the
prototype still has). The bug may simply not reproduce. Guessing either way in the plan would be
worse than naming the trap and checking.

---

## D15 — The ground offset moves the extension's group, not the anchor

*Added 2026-09-13 from the reference implementation.*

**The prototype applies the ground offset by passing it as the altitude to
`transformer.fromLatLngAltitude({ lat, lng, altitude: groundAltitude })`** — that is, by moving the
whole scene's anchor.

**This is not available to us, and must not be.** FR-041 and the first hard constraint of the
049–052 sequence forbid a capability setting the viewer's reference point; specs/051 made the
anchor single and framework-owned precisely because stale self-anchoring had already caused a bug
in `GoogleMapsGisLayer`.

**Decision**: implement FR-026's ground offset by translating the extension's **own Drawing Space
group** in Z (`group.position.z = groundOffsetMetres`). Everything the extension draws — dome,
buildings, ground plane, light target — is inside that group, so one assignment moves the entire
analysis coherently, and nothing outside the extension is affected. This is strictly better
isolated than the prototype's approach as well as being the only conformant one.

---

## D16 — Shadow frustum and ground plane must be sized together

*Added 2026-09-13 from the reference implementation, which carries the bug as a warning comment.*

**What the prototype found**: "Ground plane must stay within the shadow camera's frustum (±260
above) — anything outside it gets clamped shadow-map lookups that often read as 'in shadow',
producing a big fake gray blob unrelated to real geometry."

**This is FR-018's actual mechanism.** FR-018 requires that no spurious shadowed region appear
outside the analysis area; the requirement was written abstractly, and this is the concrete failure
it is guarding against.

**Decision**: the orthographic shadow camera's extent and the `ShadowMaterial` ground plane's size
are derived from **one** value — the analysis radius — with the ground plane strictly inside the
frustum. Concretely, following the prototype's proven ratio: frustum `±(radius × 1.3)`, ground
plane `(radius × 2.4)` square, `near`/`far` bracketing the tallest building, `shadow.bias
-0.0006`, `PCFSoftShadowMap`, `mapSize 2048²`. A test asserts the ground plane's half-extent is
less than the frustum half-extent, so this cannot silently regress.

---

## D17 — Two rendering details that are not cosmetic

*Added 2026-09-13 from the reference implementation.*

**Lines must be tubes.** The prototype: "WebGL renders plain lines at ~1px regardless of requested
width, which is why the dome was reading as 'pale'." Sun-path arcs therefore use
`TubeGeometry`/`CylinderGeometry`, never `THREE.Line`. This is a platform limitation, not a style
preference, and re-discovering it would cost a round of "why does the path look so faint".

**`scene.environment` is global — and we may not set it.** The prototype assigns a procedural sky
to `scene.environment` so its glass dome shell and metallic mounts have something to reflect. The
scene is shared and framework-owned; an extension assigning `scene.environment` is the same class
of violation as assigning `renderer.shadowMap.enabled`, which FR-038 exists to prevent.

**Decision**: build the sun-path presentation from materials that need no environment map
(`MeshBasicMaterial` for arcs, `MeshStandardMaterial` for the dial/post lit by the existing ambient
and directional lights). The prototype's glass dome shell (`MeshPhysicalMaterial` with
`transmission`) is **dropped** — it is decoration, no requirement asks for it, and it is the only
element that needs global scene state we are not permitted to touch.

**This is a second latent SC-009 finding, and a milder one than D9.** If a future capability
genuinely needs an environment map, the right answer is a new `DrawingRequirement` in specs/051
(`'environment'`), not an extension reaching for `scene.environment`. Recorded here so the shape of
that future change is already known; nothing in this feature needs it.

---

## Resolved unknowns

| Unknown from Technical Context | Resolution |
|---|---|
| Which solar algorithm, and what tolerance | D1 — ported NOAA; rise/set inherited, position measured |
| How site-local time is determined | D2 — `tz-lookup` + `Intl.DateTimeFormat` |
| Whether the backend computes solar figures | D3 — no; Lucy describes, does not recompute |
| Where building data comes from | D4 — platform-side Overpass, reusing specs/042 infrastructure |
| How unknown heights are handled | D5 — three-step resolution with known/assumed provenance |
| How shadows are bounded to the analysis area | D6 — orthographic shadow camera sized to radius |
| How unusable footprints are handled | D7 — filtered with a user-visible count |
| Which building is "the site's" | D8 — containment, then nearest edge within 25 m |
| How playback avoids a permanent redraw loop | D9 — guarded callback; SC-009 risk stated |
| What happens when the site changes | D10 — follow, with a stale-response guard |
| Whether corrections persist | D11 — no; session-scoped, keyed by site |
| Panel shapes | D12 — live for controls, content blocks for figures |
| Whether our building massing is drawn | D13 — no; casts shadows only, debug toggle reveals it |
| Whether one `invalidate()` is enough | D14 — trap named; verified during implementation |
| How the ground offset is applied | D15 — move the extension's own group in Z, never the anchor |
| How FR-018's spurious-shadow region is prevented | D16 — frustum and ground plane sized from one radius |
| Line width, and the glass dome | D17 — tubes not lines; dome shell dropped (`scene.environment` is global) |
