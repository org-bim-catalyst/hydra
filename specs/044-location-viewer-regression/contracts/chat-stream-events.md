# Contract: Chat Stream Events & Boundary Step

**Feature**: `044-location-viewer-regression` | **Date**: 2026-08-29

The externally-observable interface here is the **SSE event stream** from `POST /api/v1/ai/chat`, consumed by `ClientApp/src/features/chat/api/aiApi.ts`. No payload schema changes. What this feature changes — and what therefore needs pinning down — is **event ordering and delivery guarantees**.

---

## 1. SSE event ordering

### Before (defective)

```
data: <content delta>            ...model text, streamed
data: <location confirmation sentence>
data: <boundary confirmation sentence>     ← only after the boundary step returns
                                           ← ENTIRE STREAM BLOCKED HERE up to ~90s
data: __RAG__{...}
data: __MEMORY__{...}
data: __LOCATION__{...}                    ← viewer finally updates
data: __SITE_BOUNDARY__{...}
data: __ZOOM__in
data: [DONE]
```

If the boundary step threw, everything from `__RAG__` onward was lost — including `__LOCATION__`, assistant-message persistence, and `[DONE]`.

### After (required)

```
data: <content delta>            ...model text, streamed
data: <location confirmation sentence>
data: __LOCATION__{...}                    ← FLUSHED IMMEDIATELY, before the boundary step
data: <boundary confirmation sentence>     ← boundary step runs here, bounded at 45s
data: __RAG__{...}
data: __MEMORY__{...}
data: __SITE_BOUNDARY__{...}               ← omitted entirely if no boundary was produced
data: __ZOOM__in
data: [DONE]
```

### Guarantees

| ID | Guarantee |
|---|---|
| **C-1** | `__LOCATION__` is written and flushed **before** the boundary step begins. No network call may occur between resolving a location and writing this event. |
| **C-2** | `__LOCATION__` is emitted whenever a location was confirmed, regardless of the boundary step's outcome. |
| **C-3** | `__SITE_BOUNDARY__` is optional. Its absence is a normal outcome, never an error signal. |
| **C-4** | `[DONE]` is always written, and the assistant message always persisted, regardless of boundary outcome. |
| **C-5** | The turn completes within `BoundaryTimeoutSeconds` (45 s) of the model's text ending, even if every boundary dependency hangs. |
| **C-6** | Clients MUST NOT depend on the relative order of trailing events. Each is dispatched by its own prefix. |

**C-6 is already satisfied by the current client** — `aiApi.ts` matches prefixes per line with no ordering state — which is why `__LOCATION__` moving ahead of `__RAG__`/`__MEMORY__` needs no client change. It is stated as a contract so a future client cannot quietly introduce the dependency.

---

## 2. Boundary step contract (`IBoundaryResolutionService.ResolveAsync`)

The interface signature is unchanged. Its **behavioural contract** — already documented in the XML comments but not implemented — becomes binding:

| ID | Contract |
|---|---|
| **B-1** | MUST NOT throw for any provider, network, parsing, or data failure. Every such failure maps to a `BoundaryResolutionOutcome` with an explanatory `ConfirmationText`. |
| **B-2** | MAY throw `OperationCanceledException` **only** when its supplied token is cancelled. This is the sole permitted exception. |
| **B-3** | Vision failure (imagery fetch or analysis) MUST degrade to the deterministically-scored boundary. Vision may improve a result; it may never remove one. |
| **B-4** | When vision is disabled or uncredentialed, behaviour MUST be identical to the pre-vision implementation. |

### Caller obligation (`SendChatMessageCommandHandler`)

Because B-1 is a contract the *service* asserts, and turn integrity cannot depend on another type keeping its promise, the caller adds an independent guarantee:

| ID | Contract |
|---|---|
| **H-1** | The handler MUST catch every exception from the boundary step and continue the turn. This is defence at the turn boundary, not distrust of B-1 — it also covers faults outside the vision path. |
| **H-2** | The handler MUST pass a token linked to the request token with `CancelAfter(BoundaryTimeoutSeconds)`. |
| **H-3** | The handler MUST distinguish the two cancellation causes **against the original request token**: if `cancellationToken.IsCancellationRequested` the user cancelled → re-throw; otherwise the budget expired → log a timeout and continue without a boundary. |

**H-3 is the subtle one.** Once a linked token is passed down, `GeminiBoundaryVisionAnalyzer`'s own `when (cancellationToken.IsCancellationRequested)` guard sees the *linked* token and re-throws on budget expiry rather than degrading. That is correct only because the handler re-adjudicates against the original token. Reversing the two catch clauses would report every user cancellation as a boundary timeout.

---

## 3. Stored-state contract (`UserChat`)

| ID | Contract |
|---|---|
| **S-1** | When a confirmed location names a site different from `ActiveBoundary.SiteName` (ordinal, case-insensitive), `ActiveBoundary` MUST be cleared — whether or not a replacement was produced. |
| **S-2** | The clear MUST be atomic with the location write (same unit of work), so stored location and stored boundary are never observably inconsistent. |
| **S-3** | Boundary prompt context MUST NOT be injected for a site that is not the active one. Satisfied transitively by S-1, since injection reads `ActiveBoundary` at turn start. |
| **S-4** | A boundary failure MUST NOT clear or modify `ActiveLocation`. |

---

## 4. Configuration contract

| Key | Type | Default | Constraint |
|---|---|---|---|
| `BoundaryScoring:BoundaryTimeoutSeconds` | int | 45 | `[Range(1, 300)]`; MUST be greater than `VisionTimeoutSeconds`, else vision can never complete within the aggregate budget |
| `BoundaryScoring:VisionTimeoutSeconds` | int | 30 | Existing (`8e83b8f`), unchanged |

Both validated at startup via `IValidatableObject`, alongside the existing weight-sum and confidence-threshold checks.

---

## 5. What is explicitly NOT changing

Pinned so the implementation cannot drift into unrelated work:

- Payload schemas of every SSE event, including `__LOCATION__` and `__SITE_BOUNDARY__`.
- `IBoundaryResolutionService`, `ISatelliteImageProvider`, `IBoundaryVisionAnalyzer` signatures.
- Gemini Vision's geometry-correction capability and its plausibility check (area ratio 0.3×–3.0×, centroid within search radius).
- Boundary reuse for a repeated same-site reference.
- Confidence/source narration, alternative-candidate disclosure, and correction guidance.
- The geocoding client's 30 s timeout from `ee8ecb2`.
- Any client-side file.
- Database schema.
