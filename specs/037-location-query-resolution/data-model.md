# Data Model: Location Query Resolution

**Branch**: `037-location-query-resolution` | **Date**: 2026-08-23

Two entities already exist and are **not modified** by this feature (specs 035/036):
`ConfirmedLocationData` (`src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs`)
and the `__LOCATION__` SSE wire format (`specs/036-startup-geolocation/contracts/location-sse-event.md`).
Everything below is new.

---

## `ActiveSiteLocation` (Domain value object)

**Location**: `src/AskLucy.Domain/Chats/ActiveSiteLocation.cs`

The session-scoped snapshot of the last agent-confirmed location for one conversation —
the minimal slice of spec 035's `ActiveSiteContext` this feature needs (research.md
Decision 6). Immutable, structural equality — a `record`, mapped as an EF Core owned
type on `UserChat`.

| Field | Type | Constraints | Notes |
|---|---|---|---|
| `Latitude` | `double` | −90 ≤ x ≤ 90 | WGS-84 |
| `Longitude` | `double` | −180 ≤ x ≤ 180 | WGS-84 |
| `LocationName` | `string` | non-empty | Echoed as returned by the geocoding source (spec.md Assumptions) |
| `Confidence` | `double` | 0.0 ≤ x ≤ 1.0 | The confidence the original resolution produced |

**Validation**: Constructed only from an already-validated `ConfirmedLocationData` (FR-007
range checks already applied upstream) — `ActiveSiteLocation` itself does not re-validate;
it is never constructed from unvalidated input.

## `UserChat` (Domain entity — modified)

**Location**: `src/AskLucy.Domain/Chats/UserChat.cs`

**Added**:
- `ActiveLocation` — `ActiveSiteLocation?`, private setter, nullable owned type. `null`
  means "no agent-confirmed location yet this session" (FR-014's "nothing to resolve
  against" case).
- `SetActiveLocation(double latitude, double longitude, string locationName, double
  confidence, string actor)` — same shape/convention as the existing
  `SetModelSelection`: assigns `ActiveLocation`, stamps `ModifiedAtUtc`/`ModifiedBy`.
  Called only by `RecordActiveLocationCommandHandler`.

**Unchanged**: every other property/method. No new required constructor parameters —
`ActiveLocation` starts `null` for every chat, existing and new.

**Persistence** (EF Core Fluent API, `Infrastructure`): `OwnsOne(c => c.ActiveLocation, ...)`
mapping to four new nullable columns on the existing `UserChats` table
(`ActiveLocationLatitude`, `ActiveLocationLongitude`, `ActiveLocationName`,
`ActiveLocationConfidence`). One additive, reversible migration; no destructive change
(constitution §5).

---

## `LocationResolutionOutcome` (Application)

**Location**: `src/AskLucy.Application/Locations/LocationResolutionOutcome.cs`

The result of one resolution attempt for one chat message (research.md Decisions 2/5/7) —
mirrors `RagRetrievalOutcome`/`MemoryRetrievalOutcome`'s shape.

```csharp
public enum LocationResolutionOutcomeType
{
    NoIntent,   // message had no location intent (and no back-reference) — nothing to do
    Confirmed,  // single confident match, or a resolved back-reference
    Ambiguous,  // 2+ candidates with no dominant leader (research.md Decision 5)
    NotFound,   // zero candidates above the importance floor
    Unavailable,// classification or geocoding failed/timed out
}

public sealed record LocationResolutionOutcome(
    LocationResolutionOutcomeType Type,
    ConfirmedLocationData? ConfirmedLocation,   // set only when Type == Confirmed
    string? ConfirmationText);                  // null only when Type == NoIntent (FR-005/FR-012)
```

**State/outcome rules** (spec.md FR-001–FR-014):

| Condition | `Type` | `ConfirmedLocation` | `ConfirmationText` |
|---|---|---|---|
| No location intent, not a back-reference | `NoIntent` | `null` | `null` |
| Back-reference, `activeLocation` present | `Confirmed` | built from `activeLocation` (FR-014) | confirmation sentence |
| Back-reference, `activeLocation` absent | `Unavailable`* | `null` | "nothing to point to yet" sentence (FR-014 edge case) |
| New query, exactly one dominant candidate ≥ floor | `Confirmed` | from the winning `GeocodingCandidate` | confirmation sentence |
| New query, 2+ places named in the message (FR-009) | `Ambiguous` | `null` | ambiguity sentence |
| New query, 2+ candidates with no dominant leader | `Ambiguous` | `null` | ambiguity sentence |
| New query, zero candidates above floor | `NotFound` | `null` | not-found sentence |
| Classification call or geocoding call throws/times out | `Unavailable` | `null` | lookup-failed sentence |

*A back-reference with nothing active is logically distinct from a geocoding failure, but
shares `Unavailable`'s "nothing to confirm, tell the user why" shape — `ConfirmationText`
carries the distinguishing message; no separate enum value is needed since no other code
path branches on the difference (YAGNI, constitution §2.III).

## `GeocodingCandidate` (Application)

**Location**: `src/AskLucy.Application/Locations/IGeocodingProvider.cs`

One result from a geocoding search, before the confidence algorithm (research.md
Decision 5) picks a winner or declares ambiguity.

| Field | Type | Notes |
|---|---|---|
| `LocationName` | `string` | Nominatim's `display_name`, verbatim (spec.md Assumptions — no transliteration) |
| `Latitude` | `double` | From Nominatim's `lat` |
| `Longitude` | `double` | From Nominatim's `lon` |
| `Importance` | `double` | Nominatim's own 0–1 relevance score, used as-is for `Confidence` on the winner |

## `IGeocodingProvider` (Application interface / `NominatimGeocodingProvider` Infrastructure impl)

```csharp
public interface IGeocodingProvider
{
    Task<IReadOnlyList<GeocodingCandidate>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
```

Never throws for a provider-side failure in the way `WeatherProviderUnavailableException`
signals weather failures — instead throws a parallel `GeocodingProviderUnavailableException`
(same convention, distinct type) which `LocationResolutionService` catches and maps to
`Unavailable` (constitution §2.VIII: the failure is still surfaced, just one layer up, as a
user-visible outcome rather than an unhandled exception reaching the stream).

## `ILocationResolutionService` (Application interface / `LocationResolutionService` impl)

```csharp
public interface ILocationResolutionService
{
    Task<LocationResolutionOutcome> ResolveAsync(
        string? userId, Guid userChatId, string latestUserMessage,
        ActiveSiteLocation? activeLocation, CancellationToken cancellationToken = default);
}
```

Called once per turn from `SendChatMessageCommandHandler`, at the same call site RAG/Memory
already occupy, but — per research.md Decision 1 — its returned `Task` is stored and
awaited *after* the content-delta loop, not inline.

## `RecordActiveLocationCommand` (Application)

**Location**: `src/AskLucy.Application/Chats/Commands/RecordActiveLocation/`

```csharp
public sealed record RecordActiveLocationCommand(Guid UserChatId, ConfirmedLocationData ConfirmedLocation)
    : IRequest;
```

Dispatched from `AiController` only when the trailing chunk's `ConfirmedLocation` is
non-null (mirroring exactly where `RecordMemoryReferencesCommand` is dispatched for
`memoryOutcome`). Handler loads the `UserChat`, calls `SetActiveLocation(...)`, commits via
`IUnitOfWork` — the same one-command-one-transaction shape every other mutating command in
this codebase already follows.
