# Phase 0 Research: Location & Site-Boundary Regression Fix

**Feature**: `044-location-viewer-regression` | **Date**: 2026-08-29

All Technical Context unknowns are resolved below. No `NEEDS CLARIFICATION` remains.

---

## Decision 1 — Emit the location chunk before awaiting the boundary, and flush it mid-stream

**Decision**: Split the single trailing `ChatStreamChunk` into two. The handler yields the chunk carrying `ConfirmedLocation` (plus `RetrievalOutcome`, `MemoryOutcome`, `ViewerZoom`) *before* the boundary await, then yields a second chunk carrying only `ConfirmedBoundary` afterwards. In `AiController`, write and flush `__LOCATION__` inside the `await foreach` the moment a chunk carrying `ConfirmedLocation` arrives, rather than after the loop.

**Rationale**: The handler-side reorder alone is not sufficient. `AiController.cs:76` drains the entire stream before writing any trailing event, so a chunk yielded earlier still reaches the client no sooner. Both halves are required to restore the pre-`88b631a` property that no network call sits between resolving a location and delivering it.

**Alternatives considered**:

- *Handler reorder only* — rejected: the controller's drain-then-write structure nullifies it entirely. This is the trap that makes the fix look smaller than it is.
- *Background job + SignalR push* — rejected in clarification: a re-architecture with a new delivery channel and its own failure surface, disproportionate to a regression repair.
- *Keep ordering, rely on a tight cap alone* — rejected in clarification: leaves the viewer waiting the full budget on every slow boundary, and forces SC-002's 5 s target to be abandoned.

**Consequence on wire ordering**: `__LOCATION__` will now precede `__RAG__` and `__MEMORY__`, which are still written after the loop. This is safe — see Decision 7.

---

## Decision 2 — Bound the step with a linked `CancellationTokenSource`, not `Task.WaitAsync`

**Decision**: Wrap the boundary call in a `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` with `CancelAfter(BoundaryTimeoutSeconds)`, passing the linked token into `ResolveAsync`.

**Rationale**: `WaitAsync(timeout)` abandons the *await* but leaves the underlying work running — the Overpass, ESRI, and Gemini HTTP calls would continue consuming connections on a shared host for up to 90 s after the turn moved on. A linked token actually cancels the in-flight work. It is also the idiom already established in this codebase by `GeminiBoundaryVisionAnalyzer` (spec-043 FR-034), so the two nest consistently.

**Alternatives considered**:

- *`Task.WaitAsync(TimeSpan)`* — rejected: leaks in-flight work; the existing `locationTask` uses it only because that task is already running concurrently and cannot be cancelled meaningfully.
- *Lowering the individual HttpClient timeouts* — rejected: they are shared with other call sites (the `GoogleGemini` client also serves chat), and per-call limits still sum to ~90 s.

### Trap: caller-cancellation vs budget-cancellation must be read from the *original* token

`GeminiBoundaryVisionAnalyzer` distinguishes the two cases with `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) throw;`, where `cancellationToken` is whatever the caller passed it. Once the handler passes a *linked* token down, the analyzer's "caller cancelled" test becomes true when the **aggregate budget** fires — so it re-throws instead of degrading gracefully.

That is acceptable and in fact correct, provided the handler makes the final determination against the **original** request token, not the linked one:

```
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    → genuine user cancellation; re-throw (FR-007)
catch (OperationCanceledException)
    → budget expired; log timeout, proceed without a boundary (FR-003)
```

Getting this backwards would report every user cancellation as a boundary timeout, or hang the turn on a real cancellation. It is called out here because the inner analyzer's identically-shaped guard makes the mistake easy.

---

## Decision 3 — Two layers of protection, each with a distinct job

**Decision**: Add failure isolation in **both** places, deliberately:

| Layer | Scope | Protects |
|---|---|---|
| `BoundaryResolutionService.AnalyzeWithVisionAsync` | `try/catch` around the imagery + vision sub-path, degrading to `BoundaryVisionAnalysis.NotConfigured` | **Vision is additive** (FR-004). Makes the method's existing XML contract true. Also benefits `SiteBoundaryResolverTool`, the other caller. |
| `SendChatMessageCommandHandler` around `ResolveAsync` | catch-all + aggregate cap | **The chat turn survives** (FR-002), independent of the service's internals — covers `SearchAsync` throwing an unexpected type, `scorer.ScoreAll` faulting, `ranked[0]` indexing an empty list, and any future addition to the service. |

**Rationale**: These are not redundant — they enforce different invariants at different scopes. Constitution §III (Simplicity) would flag duplicated defence, so the distinction is recorded explicitly: the inner wrap is about *feature semantics* (vision optional), the outer is about *turn integrity* (nothing kills the stream). Removing either leaves a real gap: without the inner wrap a vision failure would abort the whole boundary including a perfectly good OSM result; without the outer wrap a non-vision fault still kills the turn.

**Alternatives considered**:

- *Handler-only catch-all* — rejected: a vision failure would discard a usable deterministic boundary, violating FR-004 and leaving the service's documented contract false.
- *Service-only isolation* — rejected: the crash path this feature exists to close (`ranked[0]`, unexpected exception types) lives outside the vision sub-path.

**§VIII compliance**: every catch logs the cause and yields a user-visible outcome. Neither is a catch-and-discard.

---

## Decision 4 — Clear the stale boundary inside `RecordActiveLocationCommandHandler`

**Decision**: Add `UserChat.ClearActiveBoundary(actor)` to the domain entity. In `RecordActiveLocationCommandHandler`, after `SetActiveLocation`, clear the boundary when `chat.ActiveBoundary?.SiteName` differs from the incoming location name (ordinal, case-insensitive — matching the handler's existing reuse guard).

**Rationale**: That handler already loads the chat and owns the unit of work, so the clear is atomic with the location write in a single round trip — no window in which stored location and stored boundary disagree. It also fires on every confirmed location, including the failure paths where no boundary command is ever sent, which is exactly the case FR-009a is about. Placing the invariant on the entity keeps the domain in charge of its own consistency (§I, §VI).

**Alternatives considered**:

- *Clear from `AiController`* — rejected: the controller does not load the chat, so it cannot compare site names without an extra read, and the clear would be a second, non-atomic round trip.
- *Clear from `SendChatMessageCommandHandler`* — rejected: the handler deliberately performs no persistence; all writes are the controller's commands. Adding one would break that separation.
- *A separate `ClearActiveSiteBoundaryCommand`* — rejected as YAGNI: no caller needs to clear a boundary without also setting a location.

**Bonus effect**: satisfies FR-009b for free. The prompt-context injection reads `chat.ActiveBoundary` at turn start, so once the stale row is cleared the model can no longer be told a boundary is shown for a site the user left.

---

## Decision 5 — New `BoundaryTimeoutSeconds` option, separate from `VisionTimeoutSeconds`

**Decision**: Add `BoundaryTimeoutSeconds` (default **45**, `[Range(1, 300)]`) to the existing `BoundaryScoringOptions`, alongside the committed `VisionTimeoutSeconds` (30). Surface it in `appsettings.json` under `BoundaryScoring`.

**Rationale**: They bound different things and must be independently tunable — one caps a single external call, the other caps the whole pipeline. Reusing one value for both would mean any vision-budget change silently retimes the entire step. Binding through `IOptions<T>` with a `[Range]` attribute satisfies §4 (no inline literals; validated at startup) and matches how every other budget in this area is expressed.

**Budget arithmetic** (why 45 and not 30 or 60):

| Step | Typical | Client timeout |
|---|---|---|
| Overpass candidate search | 2–10 s | 30 s |
| ESRI imagery fetch | 1–3 s | 30 s |
| Gemini vision | 5–20 s | 30 s (committed, spec-043) |
| **Total** | **~10–30 s** | **~90 s unbounded** |

45 s clears the typical case with headroom, while cutting the pathological case to half of what per-call limits alone permit. 30 s was rejected because a slow-but-healthy Overpass run alone can consume it, manufacturing the false "no boundary" outcome this project has already hit twice on this host.

---

## Decision 6 — Test the hang with a configured short budget, not a real wait

**Decision**: Regression tests for the fail-slow path set `BoundaryTimeoutSeconds` to a small value (1 s) through `Options.Create(...)` and substitute a boundary service that awaits `Task.Delay(Timeout.Infinite, ct)`. Assert the location chunk is delivered and the turn completes.

**Rationale**: Keeps the suite fast and deterministic while exercising the real cancellation path rather than a mocked timeout. The budget being configurable is what makes this testable at all — a hard-coded 45 s constant would force either a 45-second test or an untested timeout path.

**Note**: `TaskCanceledException` derives from `OperationCanceledException`; tests must assert on the observable outcome (location delivered, turn completed, timeout logged) rather than on exception identity, since the catch ordering in Decision 2 is what distinguishes the two cases.

---

## Decision 7 — No client changes

**Decision**: `src/AskLucy.Web/ClientApp` is out of scope.

**Rationale**: Verified against the code, not assumed.

- **Ordering**: `aiApi.ts` dispatches each SSE line by prefix match (`__LOCATION__`, `__RAG__`, `__MEMORY__`, `__SITE_BOUNDARY__`, `__ZOOM__`) inside a per-line loop. There is no state machine and no expectation of arrival order, so `__LOCATION__` moving ahead of `__RAG__`/`__MEMORY__` changes nothing observable.
- **Stale overlay**: `useChatStream.ts:183` already clears the boundary store when a `location` event names a site different from the stored boundary — with a comment explicitly anticipating the "boundary came back Unavailable this turn" case. Under the new ordering this gets *more* reliable, since the location event now always arrives before any boundary event.
- **`__ZOOM__`**: still written after the loop from the accumulated `viewerZoom`; unaffected.

**Risk if wrong**: a client that did depend on ordering would show a boundary for the previous site. Mitigated by the existing client tests plus the new ordering assertion in the backend integration tests.

---

## Decision 8 — `ee8ecb2`'s geocoding timeout is retained

**Decision**: Leave the 15 s → 30 s geocoding widening in place.

**Rationale**: It targets `LocationResolutionService`'s geocoding call, which the diagnosis showed succeeds. It is unrelated to this regression but harmless, and this host has a documented history of false failures from short timeouts. Reverting it would be unrelated churn (working rule 9).

---

## Resolved: the spec-043 reconciliation deferred at clarification

`8e83b8f` landed on `main` between the clarification session and this plan, committing `VisionTimeoutSeconds = 30` and the analyzer's per-call budget. The clarify session's only Deferred item is therefore closed: this feature **consumes** that option rather than introducing a competing one, and the fail-slow worst case is already reduced from ~180 s to ~90 s. The aggregate cap in Decision 5 remains necessary because per-call limits still sum.

The fail-fast crash path is untouched by `8e83b8f` and was re-verified present at current HEAD.
