# Feature Specification: Location & Site-Boundary Regression Fix

**Feature Branch**: `044-location-viewer-regression`

**Created**: 2026-08-29

**Status**: Draft — diagnosis complete, evidence attached

**Input**: User description: "Investigate a regression in the location and site-boundary workflow. 'Show me Al Safa Park 2' stopped reliably resolving and/or displaying in the viewer after the site-boundary/Gemini Vision work. Do not assume the geocoding timeout change fixed it. Identify the exact regression point with evidence, make the smallest safe fix, keep Gemini Vision strictly additive and non-blocking."

---

## Diagnosis Summary *(evidence, not speculation)*

### Current failure status

Confirmed reproducible in an automated probe against the real `SendChatMessageCommandHandler`. When the site-boundary step fails, the user sees Lucy say *"I've located Al Safa Park 2."* — but **the viewer never receives the location**, and the chat turn dies before the assistant message is persisted.

Probe output (handler driven end to end, boundary step made to throw):

```
Exception escaped handler: HttpRequestException
Chunks emitted: 2
ConfirmedLocation delivered: False
  delta=Here you go.                    loc=<null>
  delta=I've located Al Safa Park 2.    loc=<null>
```

This matches the reported symptom exactly: **the location resolves, Lucy confirms it in text, and the viewer stays empty.**

### Answers to the five diagnostic questions

| # | Question | Answer (evidence) |
|---|----------|-------------------|
| 1 | Does the location fail to resolve? | **No.** `LocationResolutionService` returns `Confirmed` with the correct coordinates. Its confirmation sentence is emitted to the user. |
| 2 | Does it resolve but fail to update the viewer? | **Yes — this is the regression.** The `ConfirmedLocation` payload is never emitted, so the controller never writes the `__LOCATION__` SSE event. |
| 3 | Does boundary retrieval interfere with location handling? | **Yes.** Boundary resolution was inserted *between* location resolution and the viewer-update emission, making an optional step a hard prerequisite. |
| 4 | Did the Gemini Vision integration introduce a regression? | **It triggered the latent one.** Vision added two unguarded network calls (ESRI imagery + Gemini) into that blocking path. It is not itself the structural defect. |
| 5 | Which exact commit/change caused it? | **`88b631a`** introduced the structural defect. **`a4c879b`** and **`9b19695`** made it fire in practice. |

### Exact code path involved

```
AiController.SendMessageStream        — consumes the WHOLE handler stream before writing any trailing event
└─ SendChatMessageCommandHandler.Handle
   ├─ line 107  locationResolutionService.ResolveAsync(...)      → starts concurrently  [OK]
   ├─ line 138  aiProvider.StreamChatAsync(...)                  → text streams to user [OK]
   ├─ line 190  var confirmedLocation = locationOutcome.ConfirmedLocation   [OK] correct value in hand
   ├─ line 202  await boundaryResolutionService.ResolveAsync(...) [X] UNGUARDED — no try/catch, no timeout
   │            └─ BoundaryResolutionService.ResolveAsync
   │               ├─ candidateProvider.SearchAsync(...)          — Overpass, 30s   (guarded)
   │               └─ AnalyzeWithVisionAsync(...)                 [X] NOT wrapped in try/catch
   │                  ├─ satelliteImageProvider.FetchAsync(...)   — ESRI, 30s
   │                  └─ visionAnalyzer.AnalyzeAsync(...)         — Gemini, inherits 2-MINUTE client timeout
   └─ line 213  yield ChatStreamChunk(..., confirmedLocation, ...) ← ONLY reached if line 202 returns
```

Two independent failure modes on the same line:

* **Fail-fast**: anything thrown out of `ResolveAsync` escapes the iterator. Line 213 never runs, the controller's `await foreach` throws, and `__LOCATION__`, message persistence, and `[DONE]` are all lost. *(Confirmed by probe.)*
* **Fail-slow**: nothing throws, but the vision call inherits the shared `GoogleGemini` HttpClient's **2-minute** timeout (`DependencyInjection.cs:178-180`; the committed `GeminiBoundaryVisionAnalyzer` has no per-call budget). The viewer waits through Overpass 30s + ESRI 30s + Gemini 120s ≈ **3 minutes** of silence, well past any proxy idle timeout on the shared host.

### Last known working state

`88b631a^` — immediately before site-boundary resolution was wired into the chat pipeline:

```csharp
var confirmedLocation = locationOutcome.ConfirmedLocation;
var hasAnyActiveLocation = confirmedLocation is not null || activeLocation is not null;
var viewerZoom = hasAnyActiveLocation ? zoomCommand : null;

if (retrievalOutcome is not null || memoryOutcome is not null || confirmedLocation is not null || viewerZoom is not null)
{
    yield return new ChatStreamChunk(null, null, retrievalOutcome, memoryOutcome, confirmedLocation, viewerZoom);
}
```

**Zero network calls between resolving the location and delivering it to the viewer.** That property is what the fix must restore.

### Relevant commits

| Commit | Role |
|--------|------|
| `88b631a` feat(viewer): resolve and render site boundary polygons in chat | **Regression point.** Inserted an unguarded `await boundaryResolutionService.ResolveAsync(...)` between `confirmedLocation` and the chunk that carries it. |
| `a4c879b` fix(site-boundary): …add Gemini vision verification | Added ESRI + Gemini calls inside that blocking path, via an `AnalyzeWithVisionAsync` with no try/catch. |
| `9b19695` feat(site-boundary): let Gemini vision correct positionally-wrong OSM geometry | Enlarged the vision payload/latency on the same unguarded path. |
| `ee8ecb2` fix(geocoding): widen Geocoding timeout 15s→30s | **Did not address this.** It targets `LocationResolutionService`'s geocoding call, which the evidence shows succeeds. Correctly treated as not-the-fix. |

### Root cause

An **optional enhancement was placed on the critical path of a mandatory outcome**, with neither a failure boundary nor a time boundary.

`LocationResolutionService` is documented as *"never throws into the stream — every failure path maps to Unavailable."* `BoundaryResolutionService` claims the same contract (*"never throws into the calling chat turn (constitution §VIII)"*) and `AnalyzeWithVisionAsync`'s own XML doc claims it *"never throws and never blocks resolution on failure"* — **but the code implements neither guarantee.** `BoundaryResolutionService.ResolveAsync` catches only `BoundaryProviderUnavailableException` from the candidate provider; the entire vision sub-path is uncaught and unbounded.

The documented contract and the implemented behaviour diverged. The fix is to make the code honour the contract already written for it.

---

## Clarifications

### Session 2026-08-29

- Q: Given the controller writes `__LOCATION__` only after the whole handler stream drains, how should the viewer receive the confirmed location promptly? → A: Emit the location before boundary resolution starts — the handler yields the confirmed-location chunk before awaiting the boundary, and the controller flushes the viewer-update event as soon as it sees that chunk rather than after the loop. Boundary resolution stays in the same turn and its own event follows later.
- Q: What time budget should bound the site-boundary step? → A: A single 45-second cap covering the entire step end to end (candidate search + imagery + vision combined), not per-dependency limits alone. Typical runs complete in 10–30s, so 45s lets the full pipeline finish normally while bounding the worst case below the shared host's proxy idle-timeout range.
- Q: When a new site's location is confirmed but its boundary fails or times out, what happens to the boundary still stored on the chat from the previous site? → A: Clear it. Whenever a confirmed location names a different site than the stored boundary, the stored boundary is cleared regardless of whether a replacement was produced — mirroring what the client already does for the on-screen overlay.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The viewer shows the location even when boundary resolution fails (Priority: P1)

A user asks Lucy to show them a place. Lucy finds it and says so. The map moves to that place — regardless of whether the site outline could be worked out.

**Why this priority**: This is the reported regression and the headline item in the Definition of Done. Seeing the place you asked for is the core promise; the outline is a bonus on top. Shipping only this story restores the product to working order.

**Independent Test**: Drive the chat handler with a confirmed location and a boundary step that throws; assert the viewer-update payload is still delivered and no exception escapes.

**Acceptance Scenarios**:

1. **Given** Lucy has resolved "Al Safa Park 2", **When** the boundary step throws any exception, **Then** the viewer has already received the confirmed location, the assistant message is still persisted, and the turn still completes normally.
2. **Given** Lucy has resolved a location, **When** the boundary step returns normally, **Then** the location reaches the viewer first and the boundary follows in the same turn, both rendering as today.
3. **Given** Lucy has resolved a location, **When** the boundary step fails, **Then** the user is told the outline is unavailable — no silent omission, and no error text implying the *location* failed.
4. **Given** the user cancels the request mid-turn, **When** the boundary step is in flight, **Then** cancellation propagates as cancellation and is never reported as a boundary or vision failure.
5. **Given** the chat already shows a boundary for a previous site, **When** a new site's location is confirmed but its boundary fails, **Then** the previous boundary is cleared from both the viewer and stored chat state, and the next turn's prompt carries no boundary context.

---

### User Story 2 - The viewer updates promptly, not minutes later (Priority: P1)

A user asks to be shown a place. The map moves within seconds of Lucy finishing her sentence, even when the boundary or vision services are slow or hanging.

**Why this priority**: Equal-first with US1 because it is the second, independent way the same line fails, and it produces an identical user-visible symptom. Fixing only the exception path would leave the fail-slow path live.

**Independent Test**: Make the boundary step hang; assert the viewer-update payload is delivered within the stated budget and the turn completes.

**Acceptance Scenarios**:

1. **Given** Lucy has resolved a location, **When** the boundary step exceeds its 45-second budget, **Then** the viewer has already received the location — emitted before the boundary step began — and the turn completes without waiting further.
2. **Given** the boundary step is abandoned on timeout, **When** the turn completes, **Then** the timeout is recorded for diagnosis and the user is told the outline was not available in time.
3. **Given** every boundary and vision dependency hangs, **When** the turn runs, **Then** it still completes within 45 seconds of the model's text ending, and is never left open indefinitely.

---

### User Story 3 - Gemini Vision remains strictly additive (Priority: P2)

Gemini Vision continues to correct positionally-wrong OSM geometry, but can never degrade anything that worked without it.

**Why this priority**: The vision capability is valuable and must be preserved — but it is an enhancement on top of a working boundary result, so it ranks below restoring the base flow.

**Independent Test**: With boundary candidates resolving normally, make each vision sub-step (imagery fetch, vision analysis) fail or time out independently; assert the deterministic OSM boundary is still returned each time.

**Acceptance Scenarios**:

1. **Given** OSM candidates resolved successfully, **When** satellite imagery cannot be fetched, **Then** the deterministically-scored boundary is returned unchanged.
2. **Given** OSM candidates resolved successfully, **When** the vision analysis throws or times out, **Then** the deterministically-scored boundary is returned unchanged and the failure is recorded, not surfaced as an error.
3. **Given** vision returns an observed boundary that passes the plausibility check, **When** the boundary is delivered, **Then** the corrected geometry is used and the correction is explained to the user — preserving `9b19695`'s capability.
4. **Given** vision is disabled or has no credential, **When** a boundary is resolved, **Then** behaviour is identical to the pre-vision implementation.

---

### Edge Cases

- Boundary step throws a non-HTTP exception (parse error, null reference, index-out-of-range from an empty scored list) → treated the same as any other boundary failure; location still delivered.
- Boundary step throws *after* partially succeeding → no partial boundary is delivered; location is unaffected.
- Both location and boundary fail → the user gets the existing location-unavailable message; no boundary message contradicts it.
- Caller cancellation during the boundary step → propagates as cancellation, never recorded as a provider failure (preserves the existing FR-035 distinction).
- Vision returns an implausible observed boundary → already handled; rejected and logged, mapped geometry kept. Must remain so.
- The same site is referenced again in a later turn → boundary is reused from chat state without re-resolving, as today.
- User navigates from a site with a boundary to a new site whose boundary fails → the stored boundary is cleared, the on-screen overlay is cleared, and the next turn's prompt carries no boundary context. No combination leaves the viewer, stored state, and prompt disagreeing about which site is displayed.
- Boundary step exceeds its 45-second budget → it is abandoned, the timeout is recorded, the turn completes, and any boundary stored for a different site has already been cleared.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The confirmed location MUST be delivered to the viewer independently of the outcome of site-boundary resolution.
- **FR-001a**: The confirmed location MUST be emitted before site-boundary resolution begins, and MUST reach the client as soon as it is emitted rather than being held until the turn's other work completes. No network call may sit between resolving a location and delivering it to the viewer.
- **FR-001b**: The site boundary, when produced, MUST reach the client as its own later delivery within the same turn. Clients MUST NOT depend on the location and boundary arriving together or in a fixed order.
- **FR-002**: No failure of the site-boundary step — of any exception type, at any depth — may terminate the chat turn, prevent assistant-message persistence, or prevent normal turn completion.
- **FR-003**: The site-boundary step MUST be bounded by a single explicit time budget of **45 seconds**, covering the whole step end to end — candidate search, satellite imagery, and vision analysis combined. Per-dependency timeouts alone do not satisfy this, since they sum. On expiry the step is abandoned and the turn completes without a boundary.
- **FR-004**: The satellite-imagery and vision-analysis sub-steps MUST each degrade to the deterministically-scored boundary on failure or timeout, never propagating an error outward.
- **FR-005**: Every boundary or vision failure MUST be recorded with enough detail to diagnose it, per the project's no-silent-failures rule.
- **FR-006**: When a boundary cannot be produced, the user MUST be told the outline is unavailable, in wording that does not imply the location itself failed.
- **FR-007**: Caller-initiated cancellation MUST continue to propagate as cancellation and MUST NOT be recorded or reported as a provider failure.
- **FR-008**: Gemini Vision's ability to correct positionally-wrong OSM geometry MUST be preserved, including its existing plausibility check.
- **FR-009**: Existing behaviour MUST be preserved where it works today: boundary reuse for a repeated site reference, boundary-context injection into the model prompt, confidence/source narration, and alternative-candidate disclosure.
- **FR-009a**: When a confirmed location names a different site than the boundary currently stored on the chat, the stored boundary MUST be cleared — whether or not a replacement boundary was produced this turn. Stored boundary state MUST NOT outlive the site it describes.
- **FR-009b**: Boundary context MUST NOT be injected into the model prompt for a site the user has navigated away from. The model must never be told a boundary is displayed for a site that is not the active one.
- **FR-010**: Regression tests MUST cover the fail-fast path (boundary throws), the fail-slow path (boundary hangs past its budget), and the stale-state path (new site confirmed, boundary fails, previously stored boundary cleared) — asserting in each case that the viewer update is still delivered and the turn still completes.

### Key Entities

- **Confirmed Location**: The resolved place — coordinates, name, confidence, and optional viewport. Drives the viewer. Mandatory output of a successful location request.
- **Site Boundary Result**: The resolved outline — polygon, area, confidence level, source, and alternatives. **Optional** output; its absence is a normal, expected state.
- **Vision Analysis**: An optional visual cross-check of the boundary. May confirm, override, or correct the mapped geometry. Its absence must be indistinguishable from the pre-vision behaviour.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: "Show me Al Safa Park 2" moves the viewer to the correct location on 100% of attempts where the place is found, regardless of boundary or vision availability.
- **SC-002**: The viewer updates within 5 seconds of Lucy finishing her confirmation sentence, in 95% of location requests — including when boundary services are degraded.
- **SC-003**: Zero chat turns are terminated by a boundary or vision failure; every such turn still persists its assistant message and completes normally.
- **SC-004**: With every boundary and vision dependency made to fail, 100% of location requests still complete successfully from the user's point of view.
- **SC-005**: Gemini Vision still corrects positionally-offset boundaries in the cases it handled before this change — no loss of the `9b19695` capability.
- **SC-006**: Every boundary/vision failure is diagnosable from recorded detail alone, with no silent failures.
- **SC-007**: A location request completes within 45 seconds of the model's text ending, even when every boundary and vision dependency hangs — no turn is left open indefinitely.

---

## Assumptions

- The regression is server-side. Evidence shows the viewer-update payload is never emitted; no client-side defect was found, and no client change is assumed necessary.
- The in-flight, uncommitted spec-043 work (provider error classification) is unrelated and will not be modified. Its `VisionTimeoutSeconds` addition overlaps this area and will be reconciled rather than duplicated or reverted.
- `ee8ecb2`'s geocoding timeout widening is retained. It is unrelated to this regression but is not harmful.
- Boundary resolution stays inside the chat turn rather than moving to a background job — the smallest safe fix is to make it non-blocking and failure-isolated, not to re-architect delivery.
- The user-facing wording for an unavailable boundary reuses the existing boundary-unavailable template; no new copy is assumed.
