# Implementation Plan: Location Query Resolution

**Branch**: `037-location-query-resolution` | **Date**: 2026-08-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/037-location-query-resolution/spec.md`

## Summary

Give Lucy the missing backend half of the location-aware viewer: when a chat message expresses intent to view/locate a place, classify that intent and extract the place name via a small structured LLM call, geocode it through the Nominatim host already used for weather reverse-geocoding, and — for a single confident match — populate the existing `ConfirmedLocationData` on the final `ChatStreamChunk` (spec 036's `__LOCATION__` contract, already implemented end-to-end in `AiController`/frontend). Classification + geocoding run concurrently with the model's own streamed text (never blocking the first byte); a deterministic, outcome-specific sentence is appended as additional content deltas once the model's text finishes, since the underlying `IAIProvider` abstraction has no mid-stream tool-calling the model could use to narrate a result that wasn't ready when it started generating. A minimal `ActiveSiteLocation` snapshot is added to `UserChat` so a simple back-reference ("zoom in on it") can re-confirm the session's last agent-confirmed location without a new geocode call.

## Technical Context

**Language/Version**: C# 12 / .NET 10 (backend only — the frontend contract, `ConfirmedLocationData`/`ChatStreamChunk`/`__LOCATION__` SSE event/`activeLocationStore`/`ViewerSurface`, already exists per specs 035/036 and is not modified)

**Primary Dependencies**: ASP.NET Core 10, MediatR, existing `IAIProvider` multi-provider abstraction (used for the structured intent-classification call, same pattern as `MemoryExtractionJob`), `IHttpClientFactory` (Nominatim, reusing the existing "Weather" named client), EF Core 10

**Storage**: SQL Server via EF Core — four new nullable columns (one owned type, `ActiveSiteLocation`) added to the existing `UserChat` table via one additive migration; no new tables

**Testing**: xUnit — `AskLucy.Application.Tests` (mirrors `SendChatMessage*Tests.cs`, `RagServiceTests.cs` shape) and `AskLucy.Infrastructure.Tests` (mirrors `WeatherProviderTests.cs` shape for the new Nominatim geocoding client)

**Target Platform**: ASP.NET Core 10 server (existing deployment; no new infrastructure)

**Project Type**: Backend addition to an existing web application — no frontend changes (spec 036 already built and shipped the consuming side)

**Performance Goals**: Text streaming's first byte is unaffected by location detection (FR-008); a confirmed/ambiguous/not-found/unavailable outcome is appended within the same response, bounded by the existing 15 s geocoding ceiling (FR-013/SC-006); a back-reference re-confirms with no added geocoding latency (SC-007)

**Constraints**: Must not change the shape of `ConfirmedLocationData`, `ChatStreamChunk`, or the `__LOCATION__` wire format (specs 035/036 contracts are fixed); `IAIProvider` gains no new tool-calling capability (out of scope — see research.md Decision 1); geocoding reuses the existing keyless Nominatim host already wired for weather reverse-geocoding

**Scale/Scope**: Single conversational turn; at most one classification call + one geocoding call per turn with detected intent (zero for turns with none); no schema growth beyond one owned-type addition to `UserChat`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| Clean Architecture — dependency rule (§3) | ✅ Pass | `ILocationResolutionService`/`IGeocodingProvider` declared in `Application`; `NominatimGeocodingProvider` (the only new external I/O) lives in `Infrastructure`; `UserChat`/`ActiveSiteLocation` stay attribute-free (EF Core Fluent API owned-type mapping only) |
| No Silent Failures (§2.VIII / spec FR-005, FR-012) | ✅ Pass | Every non-`NoIntent` outcome (Confirmed/Ambiguous/NotFound/Unavailable/back-reference-missing) yields both a user-visible `ConfirmationText` content delta and a structured log entry; classification/geocoding exceptions are caught and mapped to `Unavailable`, never thrown into the stream or swallowed |
| SOLID / OCP (§2.II) | ✅ Pass | Additive: new service/provider classes plus one new inserted block in `SendChatMessageCommandHandler`; the existing RAG/Memory retrieval blocks are untouched |
| Dependency Inversion & Testability (§2.V) | ✅ Pass | `ILocationResolutionService` is a fakeable seam for `SendChatMessageCommandHandlerTests`, the same shape as the already-faked `IRagService`/`IMemoryService` |
| Convention over Configuration (§2.VII) | ✅ Pass | Reuses `DefaultProviderResolver` (classification call), the existing "Weather" named `HttpClient` (Nominatim host), the `IUnitOfWork`-per-command persistence convention, and the `RetrievalPromptFraming`-style versioned-constant prompt pattern — no parallel bespoke mechanism introduced |
| Magic values / value objects (§4) | ✅ Pass | `ActiveSiteLocation` modeled as an EF Core owned-type value object (not four loose primitives); confidence thresholds (`MinimumImportanceFloor`, `DominanceMargin`) and the geocoding timeout are named constants, not inline literals |
| Database & migrations (§5) | ✅ Pass | One additive, reversible migration (nullable owned-type columns on the existing `UserChat` table); no destructive change, no new table |
| Streaming (§6/§9) | ✅ Pass | First byte of the response is never delayed by location resolution (FR-008); the deterministic confirmation sentence rides ordinary content-delta chunks and `ConfirmedLocationData` rides the existing final-chunk/`__LOCATION__` contract, unchanged from specs 035/036 |
| Prompt injection (§8) | ⚠️ Required | Nominatim's `display_name`/address text is untrusted external data folded into the deterministic confirmation sentence (not re-fed to the model as instructions) — implementation must keep it data-only, matching the defensive-framing precedent `BuildToolResultSystemMessage` already sets for other tool output |
| Test coverage (§10) | ⚠️ Required | New unit tests needed for `LocationResolutionService` (classification parsing, the confidence/dominance algorithm, the back-reference path), `NominatimGeocodingProvider` (HTTP mocking, error mapping), `UserChat.SetActiveLocation`, and a `SendChatMessageCommandHandler` integration test mirroring `SendChatMessageRagIntegrationTests.cs` |

## Project Structure

### Documentation (this feature)

```text
specs/037-location-query-resolution/
├── plan.md              ← this file
├── research.md          ← Phase 0 decisions
├── data-model.md        ← Phase 1 entities and outcome shapes
├── quickstart.md        ← Phase 1 validation guide
├── contracts/
│   ├── geocoding-provider-contract.md          ← Nominatim search request/response + confidence algorithm
│   └── location-intent-classification-contract.md ← structured LLM classification call prompt/response shape
└── tasks.md              ← Phase 2 (/speckit-tasks)
```

### Source Code

This is a backend-only addition to the existing ASP.NET Core Clean Architecture solution
(`Domain` / `Application` / `Infrastructure` / `Web`). No frontend changes — specs
035/036 already built and shipped `activeLocationStore`, the `__LOCATION__` SSE parser,
and `ViewerSurface`'s re-centering logic; this feature only ever needs to populate
`ChatStreamChunk.ConfirmedLocation`, which `AiController` already forwards.

```text
src/AskLucy.Domain/Chats/
├── UserChat.cs                        ← MODIFY: add ActiveLocation (nullable owned type) + SetActiveLocation(...)
└── ActiveSiteLocation.cs              ← NEW: value object (latitude, longitude, locationName, confidence)

src/AskLucy.Application/Locations/
├── IGeocodingProvider.cs              ← NEW: SearchAsync(query) → candidates; GeocodingCandidate record
├── ILocationResolutionService.cs      ← NEW: ResolveAsync(userId, userChatId, latestMessage, activeLocation) → LocationResolutionOutcome
├── LocationResolutionService.cs       ← NEW: classification call → (geocode | back-reference) → outcome + confidence algorithm
├── LocationResolutionOutcome.cs       ← NEW: outcome record + LocationResolutionOutcomeType enum
└── LocationConfirmationTemplates.cs   ← NEW: deterministic per-outcome confirmation/failure sentences (FR-005/FR-014)

src/AskLucy.Application/Ai/Commands/SendChatMessage/
└── SendChatMessageCommandHandler.cs   ← MODIFY: launch location resolution concurrently with StreamChatAsync (FR-008);
                                          after the content-delta loop, await it bounded by the remaining FR-013 budget;
                                          yield the confirmation text, then ConfirmedLocation on the trailing chunk
                                          (ChatStreamChunk.cs itself is unchanged — ConfirmedLocation already exists)

src/AskLucy.Application/Chats/Commands/RecordActiveLocation/  ← NEW (mirrors RecordMemoryReferences)
├── RecordActiveLocationCommand.cs
└── RecordActiveLocationCommandHandler.cs  ← loads UserChat, calls SetActiveLocation, commits via IUnitOfWork

src/AskLucy.Application/DependencyInjection.cs  ← MODIFY: register ILocationResolutionService → LocationResolutionService

src/AskLucy.Infrastructure/Geocoding/
├── GeocodingOptions.cs                ← NEW: SearchBaseUrl (defaults to the same Nominatim host WeatherOptions uses)
└── NominatimGeocodingProvider.cs      ← NEW: IGeocodingProvider impl, reuses the "Weather" named HttpClient

src/AskLucy.Infrastructure/DependencyInjection.cs  ← MODIFY: register IGeocodingProvider → NominatimGeocodingProvider,
                                                      bind GeocodingOptions

src/AskLucy.Web/Controllers/v1/AiController.cs  ← MODIFY: dispatch RecordActiveLocationCommand when the stream's
                                                    trailing chunk carries a ConfirmedLocationData (same place the
                                                    existing __LOCATION__ SSE write already reads it from)

tests/AskLucy.Application.Tests/Locations/         ← NEW: LocationResolutionServiceTests, confidence-algorithm tests
tests/AskLucy.Application.Tests/Ai/
└── SendChatMessageLocationIntegrationTests.cs      ← NEW, mirrors SendChatMessageRagIntegrationTests.cs
tests/AskLucy.Infrastructure.Tests/Geocoding/
└── NominatimGeocodingProviderTests.cs              ← NEW, mirrors WeatherProviderTests.cs
tests/AskLucy.Domain.Tests/Chats/
└── UserChatTests.cs                                 ← MODIFY: add SetActiveLocation coverage
```

**Structure Decision**: Standard 4-layer Clean Architecture solution already in place;
this feature follows the exact call-site shape `IRagService`/`IMemoryService` established
in `SendChatMessageCommandHandler` (spec.md FR-002/FR-003/FR-010 reuse that model
directly), plus one new `RecordActiveLocation` MediatR command dispatched from the
controller after streaming completes, mirroring `RecordMemoryReferencesCommand`'s
existing post-stream persistence pattern.

## Complexity Tracking

No constitution violations requiring justification. Both ⚠️ rows in the Constitution
Check above are standard, already-precedented implementation obligations (defensive
framing of external data, new unit tests) — not deviations from an architectural rule.
