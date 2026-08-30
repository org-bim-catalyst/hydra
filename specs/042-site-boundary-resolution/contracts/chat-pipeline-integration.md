# Contract: Chat pipeline integration (PRIMARY mechanism)

This is what actually makes User Stories 1-3 work in a normal Lucy conversation. It is not the agent-tool contract (`site-boundary-resolver-tool.md`, which is a secondary surface) — see research.md #11 for why this correction was necessary.

## Backend: `SendChatMessageCommandHandler`

Extends the existing block around `SendChatMessageCommandHandler.cs:100-118` (where `locationTask`/`activeLocation`/`zoomCommand` already live), following the exact same shape:

```csharp
var activeBoundary = chat?.ActiveBoundary;
Task<BoundaryResolutionOutcome>? boundaryTask = null;

// Only attempt boundary resolution once the location task's outcome is known, and only when
// the confirmed site is actually different from what's already active — this is what makes
// FR-009 ("without forcing a fresh resolution") concrete, not just descriptive.
```

Because boundary resolution depends on knowing *which* location was confirmed this turn, it cannot launch fully concurrently with `locationTask` the way `locationTask` launches concurrently with the model stream — it launches immediately after `locationOutcome` is known (still concurrently with the *remainder* of the model's streaming response, which is normally still in flight at that point, so first-byte latency is unaffected).

**Corrected ordering** (an earlier draft of this contract got this wrong — inserting a system message into `messages` *after* `StreamChatAsync(messages, ...)` has already been called has no effect, since the provider already consumed that list; caught before implementation by re-reading the actual call sequence): the "same site still active" context message must be injected **before** streaming, using the *turn-start* value of `chat.ActiveBoundary` — unconditionally, whenever one exists, regardless of what this turn turns out to do. The decision to actually *re-resolve* (because the site changed) can only happen **after** `locationOutcome` is known, post-stream.

Inserted alongside the existing zoom-intent message (`SendChatMessageCommandHandler.cs:108-118`), i.e. **before** `await foreach (var chunk in aiProvider.StreamChatAsync(...))`:

```csharp
var activeBoundary = chat?.ActiveBoundary;
if (activeBoundary is not null)
{
    // FR-009/FR-010: gives the model enough context to answer "how sure are you about that?"
    // or a correction request from context alone — no new tool call, no re-resolution — and
    // folds in the correction-acknowledgment guidance (BoundaryConfirmationTemplates
    // .CorrectionGuidance) in the same message rather than a second insert.
    messages.Insert(0, new ChatMessage(ChatRole.System,
        $"An active site boundary is already shown for '{activeBoundary.SiteName}' " +
        $"(confidence: {activeBoundary.ConfidenceLevel}, source: {activeBoundary.Source}). " +
        "If the user asks about its confidence or source, answer using this information " +
        $"directly — do not claim you cannot access it. {BoundaryConfirmationTemplates.CorrectionGuidance}"));
}
```

Sequence, inserted after the existing `locationOutcome` await (`SendChatMessageCommandHandler.cs:129-160`):

```csharp
BoundaryResolutionOutcome? boundaryOutcome = null;
var confirmedLocationThisTurn = locationOutcome.ConfirmedLocation;
if (confirmedLocationThisTurn is not null &&
    !string.Equals(confirmedLocationThisTurn.LocationName, activeBoundary?.SiteName, StringComparison.OrdinalIgnoreCase))
{
    boundaryOutcome = await boundaryResolutionService.ResolveAsync(
        confirmedLocationThisTurn, request.ChatId, cancellationToken);

    if (boundaryOutcome.ConfirmationText is not null)
    {
        yield return new ChatStreamChunk(boundaryOutcome.ConfirmationText, null);
    }
}

var confirmedBoundary = boundaryOutcome?.ConfirmedBoundary;
if (retrievalOutcome is not null || memoryOutcome is not null || confirmedLocation is not null || viewerZoom is not null || confirmedBoundary is not null)
{
    yield return new ChatStreamChunk(null, null, retrievalOutcome, memoryOutcome, confirmedLocation, viewerZoom, confirmedBoundary);
}
```
- ~~**No new time-budget ceiling is introduced for v1** — boundary resolution runs after `locationTask` already completed, so it doesn't compete with the model's first-byte latency; a future iteration MAY add its own `BoundaryResolutionOptions.ResolutionCeilingSeconds` mirroring `LocationResolutionOptions` if real-world latency (Overpass roundtrip + scoring) proves to need one. Not required to satisfy SC-001 (10s) in the common case.~~

  > **SUPERSEDED by [specs/044-location-viewer-regression](../../044-location-viewer-regression/spec.md) FR-003.** The "future iteration" this anticipated arrived as a production regression. Without an aggregate ceiling, per-dependency timeouts summed — Overpass 30s + ESRI imagery 30s + Gemini vision 30s ≈ 90s — and because the boundary step also sat *ahead* of the `__LOCATION__` emission (see the next correction), a slow boundary held the viewer update hostage for that whole window. A single `BoundaryScoring:BoundaryTimeoutSeconds` (default **45s**) now caps the entire step; on expiry it is abandoned and the turn completes without a boundary.
  >
  > The premise that it "doesn't compete with first-byte latency" was correct but insufficient: it competed with *viewer* latency and with turn completion, neither of which this analysis considered.

## Backend: `AiController`

Extends the existing accumulation/emission block (`AiController.cs:67-72`, `96-103`, `180-209`):

```csharp
ConfirmedSiteBoundaryData? confirmedBoundary = null;
// ...inside the existing await foreach...
if (chunk.ConfirmedBoundary is not null)
{
    confirmedBoundary = chunk.ConfirmedBoundary;
}
// ...after the stream, alongside the existing __LOCATION__ block...
if (confirmedBoundary is not null)
{
    await mediator.Send(new RecordActiveSiteBoundaryCommand(request.ChatId, confirmedBoundary), cancellationToken);

    var boundaryPayload = new
    {
        siteName = confirmedBoundary.SiteName,
        centroid = new { latitude = confirmedBoundary.CentroidLatitude, longitude = confirmedBoundary.CentroidLongitude },
        polygon = confirmedBoundary.Polygon.Select(p => new { latitude = p.Latitude, longitude = p.Longitude }),
        areaSquareMeters = confirmedBoundary.AreaSquareMeters,
        confidence = confirmedBoundary.Confidence,
        confidenceLevel = confirmedBoundary.ConfidenceLevel.ToString().ToLowerInvariant(),
        source = confirmedBoundary.Source.ToString(),
        sourceDetail = confirmedBoundary.SourceDetail,
        alternativeCandidateNames = confirmedBoundary.AlternativeCandidateNames,
    };
    await Response.WriteAsync($"data: __SITE_BOUNDARY__{JsonSerializer.Serialize(boundaryPayload)}\n\n", cancellationToken);
    await Response.Body.FlushAsync(cancellationToken);
}
```

- Same distinguishable-SSE-prefix pattern as `__RAG__`/`__MEMORY__`/`__LOCATION__` — the frontend's existing stream parser gains one more prefix to recognize, not a new parsing mechanism.
- `RecordActiveSiteBoundaryCommand` is sent (and awaited) before the trailing event is written — persistence happens server-side before the client is told about it, so a client that reloads immediately after sees consistent state.

  > **CORRECTED by [specs/044-location-viewer-regression](../../044-location-viewer-regression/spec.md) FR-001a.** This bullet used to add "matching `RecordActiveLocationCommand`'s ordering exactly". That symmetry no longer holds, deliberately: `RecordActiveLocationCommand` and its `__LOCATION__` event are now written **mid-stream**, the moment the handler yields the confirmed location and *before* the boundary step runs. The persist-then-notify rule still holds for both; what differs is *when* each fires.
  >
  > The two are no longer symmetric because they are no longer equally important: the location is the mandatory outcome and the boundary is an optional enhancement. Treating them identically is what let a boundary failure discard `__LOCATION__`, assistant-message persistence, and `[DONE]` along with it. See [contracts/chat-stream-events.md](../../044-location-viewer-regression/contracts/chat-stream-events.md) for the current ordering guarantees (C-1…C-6).

## Frontend: `aiApi.ts`

The existing stream parser (wherever it recognizes `data: __LOCATION__...`) gains a matching branch for `data: __SITE_BOUNDARY__...`, deserializing the JSON payload above and calling `useActiveSiteBoundaryStore.getState().setBoundary(...)`. No new transport mechanism — same SSE stream, same trailing-event convention.

## What this contract deliberately does NOT do

- It does not run boundary resolution on every single confirmed location — only when the confirmed site name differs from the chat's current `ActiveBoundary` (research.md #11's trigger logic). A user asking about a plain address with no site-like framing still gets *location* resolution (existing behavior, unchanged) and, per FR-001, a *boundary* attempt too — the trigger is "a location was confirmed," not "the user asked specifically for a boundary." (Re-check against real usage during implementation: if this proves too eager — e.g., firing on every trivial address lookup — narrowing the trigger to a stronger location-type signal, such as `LocationType`/`Viewport` fields already present on `ConfirmedLocationData`, is a same-file tuning change, not a redesign.)
- It does not add a second AI intent-classification call — it piggybacks entirely on `locationOutcome`.
