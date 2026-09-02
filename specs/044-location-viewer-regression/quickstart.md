# Quickstart: Validating the Location & Site-Boundary Regression Fix

**Feature**: `044-location-viewer-regression` | **Date**: 2026-08-29

How to prove the fix works. Scenario 1 reproduces the regression **before** any code changes — run it first, and confirm it fails.

**Prerequisites**: .NET 10 SDK; repo root at `E:\Workshop\BIM Catalyst\Web Apps\Platform\Ask Lucy`. Scenarios 1–4 need no network, no database, and no API keys — all external dependencies are substituted.

Details referenced rather than repeated here: event ordering and behavioural guarantees in [contracts/chat-stream-events.md](./contracts/chat-stream-events.md); state transitions in [data-model.md](./data-model.md); design rationale in [research.md](./research.md).

---

## Scenario 1 — Reproduce the regression (run BEFORE implementing)

**Proves**: the defect is real and the test would have caught it.

Harness: `tests/AskLucy.Application.Tests/Ai/SendChatMessageBoundaryIntegrationTests.cs` (existing substitutes for every dependency).

Setup: location resolves to `"Al Safa Park 2"`; `IBoundaryResolutionService.ResolveAsync` throws `HttpRequestException`.

```
dotnet test tests/AskLucy.Application.Tests/AskLucy.Application.Tests.csproj `
  --filter "FullyQualifiedName~SendChatMessageBoundaryIntegrationTests"
```

**Expected before the fix — FAILING**: the exception escapes the handler; no chunk carries `ConfirmedLocation`. Observed during diagnosis:

```
Exception escaped handler: HttpRequestException
Chunks emitted: 2
ConfirmedLocation delivered: False
```

**Expected after the fix — PASSING**: no exception escapes; a chunk carries `ConfirmedLocation`; the turn completes.

> If this scenario passes before you change any code, the test is not exercising the real path — fix the test before trusting anything downstream.

---

## Scenario 2 — Location survives a failing boundary (FR-001, FR-002)

**Proves**: US1 — the headline regression.

Assert, for a boundary substitute that throws:

- exactly one chunk carries `ConfirmedLocation`, with the right name and coordinates;
- no exception escapes `Handle`;
- enumeration runs to completion (the background memory-extraction enqueue at the end is reached);
- the user-facing boundary-unavailable sentence is emitted, and does **not** claim the location failed.

Repeat with several exception types — `HttpRequestException`, `JsonException`, `InvalidOperationException`, `IndexOutOfRangeException` — since FR-002 covers *any* type, not just network faults.

---

## Scenario 3 — Location arrives before the boundary step (C-1, FR-001a)

**Proves**: the ordering half of the fix, which the handler-side reorder alone does not achieve.

Setup: a boundary substitute that records the order of events — e.g. appends to a shared list when invoked, while the test appends when it observes a chunk carrying `ConfirmedLocation`.

Assert the `ConfirmedLocation` chunk is observed **before** `ResolveAsync` is entered.

**Also assert at the controller level** that `__LOCATION__` is written before the boundary step begins. This is the guarantee that actually reaches the user — a handler-only assertion would pass even with the controller still draining the whole stream first, which is precisely the trap described in research Decision 1.

---

## Scenario 4 — Hanging boundary does not stall the turn (FR-003, SC-007)

**Proves**: US2 — the fail-slow path.

Setup: `BoundaryTimeoutSeconds = 1` via `Options.Create(...)`; boundary substitute awaits `Task.Delay(Timeout.Infinite, ct)`.

Assert:

- the `ConfirmedLocation` chunk is delivered;
- the turn completes in roughly the configured budget, not indefinitely;
- a timeout is logged, distinguishable from a provider failure;
- the user is told the outline was unavailable.

Separately assert **cancellation is not misreported** (FR-007, H-3): cancel the *caller's* token mid-boundary and confirm `OperationCanceledException` propagates and is **not** logged as a boundary timeout. This is the catch-ordering trap from research Decision 2 — a test that only covers the timeout side will not catch a reversed guard.

---

## Scenario 5 — Vision failure degrades to the OSM boundary (FR-004, FR-008, B-3)

**Proves**: US3 — Gemini Vision stays strictly additive.

Harness: `tests/AskLucy.Application.Tests/SiteBoundaries/BoundaryResolutionServiceTests.cs`.

With OSM candidates resolving normally, run one case per failure mode:

| `ISatelliteImageProvider` | `IBoundaryVisionAnalyzer` | Expected |
|---|---|---|
| returns `null` | not reached | deterministic OSM boundary returned |
| throws | not reached | deterministic OSM boundary returned |
| returns an image | throws | deterministic OSM boundary returned |
| returns an image | times out | deterministic OSM boundary returned |
| returns an image | returns a plausible observed boundary | **corrected geometry used**, source `AiInterpretation` |
| returns an image | returns an implausible boundary | mapped geometry kept, rejection logged |

The last two rows are the regression guard for `9b19695` — they prove the fix did not quietly disable the capability it was meant to protect.

---

## Scenario 6 — Stale boundary is cleared on site change (FR-009a/b, S-1…S-4)

**Proves**: the correctness repair from clarification Q3.

Setup: chat has `ActiveBoundary` for `"Al Safa Park 2"`; a new turn confirms `"Zabeel Park"`; boundary resolution fails.

Assert:

- `ActiveBoundary` is cleared (not left as Al Safa Park 2);
- `ActiveLocation` is updated to Zabeel Park and **not** cleared (S-4);
- the next turn injects no boundary context into the model prompt;
- when the *same* site is referenced again, the boundary is still reused without re-resolving (FR-009 not broken by the new clear).

That last assertion matters: an over-eager clear would silently defeat the reuse optimisation and re-hit Overpass on every turn.

---

## Full validation before commit

```
dotnet build AskLucy.sln
dotnet test tests/AskLucy.Application.Tests/AskLucy.Application.Tests.csproj
dotnet format --verify-no-changes
```

Run the **whole** Application test suite, not only the touched files — page/handler-level tests in this repo carry their own assertions about streamed chunks and have been broken by narrower runs before.

Client suite is unchanged by this feature (research Decision 7), but run it once to confirm that claim:

```
cd src/AskLucy.Web/ClientApp
npm test
npx tsc -b --noEmit
```

`tsc -b`, not bare `tsc --noEmit` — the root config uses project references and a bare invocation silently checks nothing.

---

## Manual verification (deployed)

**"Show me Al Safa Park 2"** — the Definition of Done.

1. Viewer moves to Al Safa Park 2 within a few seconds of Lucy's confirmation sentence, *before* any outline appears.
2. The boundary outline follows shortly after, with confidence and source narrated.
3. Gemini Vision still corrects the known positional offset against satellite imagery.

Then force degradation — an invalid Gemini credential, or `EnableAiVisionVerification: false` — and repeat. The location must still display, the turn must still complete, and the user must be told the outline is unavailable. Anything else means the fix is incomplete.
