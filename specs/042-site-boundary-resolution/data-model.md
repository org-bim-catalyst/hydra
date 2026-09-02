# Phase 1 Data Model: Site Boundary Resolution

No database schema changes in this feature (§ research.md #10 — no persistence). Everything below is an in-memory/transport shape: Domain value objects and records, plus the Application-layer orchestration DTOs that flow from resolution → agent tool output → frontend store. Types are grouped by the layer that owns them, per the Dependency Rule.

## Domain (`AskLucy.Domain.SiteBoundaries`)

### `GeoPoint`

```csharp
public sealed record GeoPoint(double Latitude, double Longitude);
```

- **Validation**: `Latitude` ∈ [-90, 90], `Longitude` ∈ [-180, 180] — same WGS-84 range check `LocationResolutionService` already performs (FR-007's "no reliable boundary found" path covers a value that fails this).

### `SiteBoundaryPolygon`

```csharp
public sealed record SiteBoundaryPolygon(IReadOnlyList<GeoPoint> ExteriorRing);
```

- **Validation**: at least 3 distinct points; first and last point are either identical (explicitly closed) or the ring is treated as implicitly closed — normalized to one convention at construction. No self-intersection check in v1 (that level of geometric rigor is explicitly not required — research.md #7).
- **Relationships**: owned exclusively by a `SiteBoundaryResult`; not independently persisted or referenced.

### `SiteBoundarySource` (enum)

```csharp
public enum SiteBoundarySource
{
    OsmBoundary,
    GovernmentCadastral,   // reserved — no provider implements this in v1
    AiInterpretation,      // reserved — no provider implements this in v1 (Phase 2)
    UploadedBoundary,      // reserved — no upload path exists in v1
    ManualFallback,        // the circular-buffer-around-a-point approximation
}
```

- Only `OsmBoundary` and `ManualFallback` are actually produced by v1 code paths; the others exist so `SiteBoundaryResult.Source` doesn't need a breaking enum change when Phase 2/future providers land (OCP).

### `BoundaryConfidenceLevel` (enum)

```csharp
public enum BoundaryConfidenceLevel { Low, Medium, High }
```

- Derived, never set directly by a caller — `BoundaryCandidateScorer`/`BoundaryResolutionService` computes it from a numeric score via `BoundaryScoringOptions`' thresholds.

### `SiteBoundaryResult`

```csharp
public sealed record SiteBoundaryResult(
    string SiteName,
    GeoPoint Centroid,
    SiteBoundaryPolygon Polygon,
    double AreaSquareMeters,
    double Confidence,                  // 0.0–1.0, combined score
    BoundaryConfidenceLevel ConfidenceLevel,
    SiteBoundarySource Source,
    string SourceDetail,                // e.g. "OpenStreetMap way 123456 (leisure=park)"
    IReadOnlyList<string> Notes,        // explainability trail — always non-empty
    IReadOnlyList<string> AlternativeCandidateNames); // FR-008 disclosure — empty when no ambiguity existed
```

- **Validation**: `Confidence` ∈ [0.0, 1.0]; `Notes` MUST contain at least one entry (constitution §VIII — a result with no explanation is itself a silent-failure-adjacent gap, since FR-005 requires the source to always be stated); `ConfidenceLevel` MUST be internally consistent with `Confidence` against the active `BoundaryScoringOptions` thresholds (enforced by construction only going through the scorer, never set ad hoc).
- **Lifecycle**: created fresh per resolution request; not mutated after construction; superseded (not updated in place) the moment a new site is referenced in the same conversation (edge case: "conversation switches to an entirely new site mid-conversation").

### `ActiveSiteBoundary` — **persisted**, mirrors `ActiveSiteLocation` exactly (see research.md #10)

```csharp
// src/AskLucy.Domain/Chats/ActiveSiteBoundary.cs — alongside ActiveSiteLocation.cs
public sealed record ActiveSiteBoundary(
    string SiteName,
    double CentroidLatitude,
    double CentroidLongitude,
    IReadOnlyList<GeoPoint> Polygon,
    double AreaSquareMeters,
    double Confidence,
    BoundaryConfidenceLevel ConfidenceLevel,
    SiteBoundarySource Source,
    string SourceDetail);
```

- Owned by `UserChat` via a new `ActiveBoundary` property + `SetActiveBoundary(...)` method, exactly mirroring `UserChat.ActiveLocation`/`SetActiveLocation` (`src/AskLucy.Domain/Chats/UserChat.cs:240-248`).
- **Persistence** (corrected from the original "no persistence" call — see research.md #10): a new EF Core migration adds nullable columns to the existing `UserChats` table via `builder.OwnsOne(c => c.ActiveBoundary, owned => {...})` in `UserChatConfiguration.cs`, matching `ActiveLocation`'s flat-column style for every scalar field; only `Polygon` needs a `HasConversion` value converter (JSON string ⇄ `IReadOnlyList<GeoPoint>`, converter code lives in `Persistence`, never in `Domain`). `ConfidenceLevel`/`Source` are stored as their string names (`HasConversion<string>()`), matching how enums are conventionally persisted elsewhere in this codebase rather than as raw integers (avoids silent meaning-shift if the enum is reordered).
- **Lifecycle**: null until the first successful boundary resolution for a chat; replaced wholesale (never patched) when a *different* site is confirmed; left untouched (not re-written) when the same site is referenced again — this is the concrete mechanism behind FR-009's "without forcing a fresh resolution."

## Application (`AskLucy.Application.SiteBoundaries`)

### `BoundaryCandidate`

```csharp
public sealed record BoundaryCandidate(
    string Id,
    SiteBoundaryPolygon Polygon,
    SiteBoundarySource Source,
    string Name,
    IReadOnlyDictionary<string, string> Tags,
    double DistanceToCenterMeters,
    double AreaSquareMeters);
```

- One instance per raw shape returned by `IBoundaryCandidateProvider.SearchAsync`, before scoring. Ephemeral — exists only for the duration of one resolution call; never returned directly to the agent tool caller (only the winning `SiteBoundaryResult` plus `AlternativeCandidateNames` is).

### `ScoredBoundaryCandidate`

```csharp
public sealed record ScoredBoundaryCandidate(
    BoundaryCandidate Candidate,
    double Score,
    IReadOnlyDictionary<string, double> ScoreBreakdown); // one entry per BoundaryScoringOptions weight
```

- Produced by `BoundaryCandidateScorer`; the ranked list of these is what `BoundaryResolutionService` picks the winner from and derives `AlternativeCandidateNames` from (FR-008).

### `BoundaryScoringOptions` (bound via `IOptions<T>`)

```csharp
public sealed class BoundaryScoringOptions
{
    public int SearchRadiusMeters { get; init; } = 500;
    public int MaxCandidates { get; init; } = 10;
    public double HighConfidenceThreshold { get; init; } = 0.85;
    public double MediumConfidenceThreshold { get; init; } = 0.65;
    public double SourceReliabilityWeight { get; init; } = 0.35;
    public double NameMatchWeight { get; init; } = 0.20;
    public double GeometryQualityWeight { get; init; } = 0.15;
    public double CenterProximityWeight { get; init; } = 0.20;
    public double LandUseAgreementWeight { get; init; } = 0.10;
}
```

- **Validation** (enforced by an `IValidateOptions<BoundaryScoringOptions>`, `ValidateOnStart`): the five `*Weight` properties MUST sum to 1.0 (±1e-6); `HighConfidenceThreshold` MUST be > `MediumConfidenceThreshold`; both MUST be within (0.0, 1.0]; `SearchRadiusMeters`/`MaxCandidates` MUST be positive.

### `BoundaryResolutionOutcome`

```csharp
public enum BoundaryResolutionOutcomeType { Confirmed, NoCandidates, Unavailable }

public sealed record BoundaryResolutionOutcome(
    BoundaryResolutionOutcomeType Type,
    ConfirmedSiteBoundaryData? ConfirmedBoundary,   // non-null for Confirmed AND NoCandidates (see below); null only for Unavailable
    string? ConfirmationText);                       // always populated — constitution §VIII
```

- Field names deliberately match `LocationResolutionOutcome`'s exactly (`Type`/`Confirmed*`/`ConfirmationText`) — same discriminated-outcome idiom, same reason: never throw into the calling chat turn (FR-007/FR-012), and a reader who already knows `LocationResolutionOutcome` recognizes this shape immediately (constitution §VII).
- `NoCandidates` ⇒ FR-007 ("no reliable boundary found") — **still carries a `ConfirmedBoundary`**: a Low-confidence, `ManualFallback`-sourced circular buffer around the point (User Story 1 acceptance scenario 3 requires an actual approximate area to render, not just an apologetic sentence with nothing shown — text promising a fallback shape with no shape attached would itself be a constitution §VIII gap). `Type` stays distinct from `Confirmed` so a caller/test can tell "a real match was found" from "had to fall back," even though both render something.
- There is no separate `Ambiguous` case here (unlike `LocationResolutionOutcomeType`) — an ambiguous *point* is already handled upstream by `LocationResolutionService` before boundary resolution ever runs; ambiguity *among boundary candidates for an already-unambiguous point* (FR-008) always still resolves to `Confirmed`, with `AlternativeCandidateNames` populated on the result. `Unavailable` ⇒ FR-012 (data source unreachable) — the one case with **no** `ConfirmedBoundary`, since FR-012 explicitly forbids returning "a default result" when the source itself couldn't be reached. There is likewise no `NoIntent` case — boundary resolution is only ever invoked when location resolution already confirmed a point this turn (research.md #11's trigger logic), so "no intent" is implicitly "boundary resolution wasn't invoked at all," not an outcome value.

### `ConfirmedSiteBoundaryData` (rides the final `ChatStreamChunk`, mirrors `ConfirmedLocationData`)

```csharp
// src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs — added alongside ConfirmedLocationData
public sealed record ConfirmedSiteBoundaryData(
    string SiteName,
    double CentroidLatitude,
    double CentroidLongitude,
    IReadOnlyList<GeoPoint> Polygon,
    double AreaSquareMeters,
    double Confidence,
    BoundaryConfidenceLevel ConfidenceLevel,
    SiteBoundarySource Source,
    string SourceDetail,
    IReadOnlyList<string> AlternativeCandidateNames);
```

- `ChatStreamChunk` gains one new optional property, `ConfirmedBoundary`, alongside the existing `ConfirmedLocation`/`ViewerZoom` — same "rides the final chunk only" convention already documented on that type.

## Frontend (`ClientApp/src/store/activeSiteBoundaryStore.ts`)

```ts
interface ActiveSiteBoundary {
  siteName: string
  centroid: { latitude: number; longitude: number }
  polygon: { latitude: number; longitude: number }[]   // exterior ring, closed
  confidence: number
  confidenceLevel: 'low' | 'medium' | 'high'
  source: 'osm-boundary' | 'manual-fallback' | 'government-cadastral' | 'ai-interpretation' | 'uploaded-boundary'
  sourceDetail: string
  alternativeCandidateNames: string[]
}
```

- Mirrors `ConfirmedSiteBoundaryData` field-for-field (camelCase, string-enum) — the same transport-shape mirroring `activeLocationStore.ts` already does for `ConfirmedLocationData`.
- **Populated via**: a new `__SITE_BOUNDARY__` SSE trailing event on the chat stream (mirroring `__LOCATION__`, `AiController.cs` lines ~180-209) — parsed by `aiApi.ts`'s existing stream parser, **not** delivered as an `IAgentTool` result (see research.md #11 for why the primary mechanism is the chat pipeline, not the agent tool).
- **Lifecycle**: one instance per active conversation; replaced wholesale (never merged/patched) when a new resolution arrives, matching the domain-level "superseded, not updated in place" rule above and the edge case requiring the old boundary to disappear when a new site is referenced. **Confirmed by checking `activeLocationStore`'s actual wiring**: the persisted `chat.ActiveBoundary` (like `chat.ActiveLocation`) is write-only from the frontend's perspective — no chat-load query exposes it, so reopening an existing conversation does **not** repopulate this store. It resurfaces only the next time the backend emits a fresh `ConfirmedSiteBoundaryData` on the stream (e.g., the user references the same site again mid-conversation, triggering `LocationResolutionService`'s back-reference path). This is an existing product-wide gap this feature inherits unchanged, not a new one introduced here — out of scope to fix as part of this spec.
