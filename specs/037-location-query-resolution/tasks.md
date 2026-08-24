# Tasks: Location Query Resolution

**Input**: Design documents from `specs/037-location-query-resolution/`

**Feature**: SPEC-037 — Location Query Resolution (Backend) | **Date**: 2026-08-23

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

**Tech stack**: C# 12 / .NET 10, ASP.NET Core, MediatR, EF Core, xUnit — backend only (no frontend changes)

**Tests**: Included — flagged ⚠️ Required in plan.md Constitution Check (§10: tests ship in the same PR as the behavior they cover)

---

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no blocking dependency)
- **[Story]**: Which user story this task belongs to
- Exact file paths included in every task description

---

## Phase 1: Setup

**Purpose**: Confirm the solution builds cleanly before any changes — establishes the baseline that every task must preserve.

- [X] T001 Verify solution builds and all existing tests pass: `dotnet build src/AskLucy.sln` + `dotnet test` in `d:\Workshop\BIM Catalyst\Web Apps\Platform\Ask Lucy` (zero errors/warnings baseline before new files are added)

---

## Phase 2: Foundational — New Types, Interfaces & Infrastructure

**Purpose**: All new domain types, application interfaces, infrastructure classes, and EF Core migration that every user story depends on. The handler and service implementation from Phase 3 cannot compile until these exist.

**⚠️ CRITICAL**: No user story implementation can begin until T002–T012 are complete.

- [X] T002 [P] Create `ActiveSiteLocation` value object in `src/AskLucy.Domain/Chats/ActiveSiteLocation.cs` — immutable `record` with four fields: `double Latitude`, `double Longitude`, `string LocationName`, `double Confidence`; no EF Core or ASP.NET attributes (domain purity — constitution §3); structural equality from `record` semantics
- [X] T003 [P] Create `LocationResolutionOutcomeType` enum + `LocationResolutionOutcome` record in `src/AskLucy.Application/Locations/LocationResolutionOutcome.cs` — enum values: `NoIntent / Confirmed / Ambiguous / NotFound / Unavailable`; record: `(LocationResolutionOutcomeType Type, ConfirmedLocationData? ConfirmedLocation, string? ConfirmationText)` where `ConfirmationText` is null only for `NoIntent` and `ConfirmedLocation` is non-null only for `Confirmed` (data-model.md outcome table)
- [X] T004 [P] Create `GeocodingCandidate` record + `IGeocodingProvider` interface in `src/AskLucy.Application/Locations/IGeocodingProvider.cs` — `GeocodingCandidate(string LocationName, double Latitude, double Longitude, double Importance)`; `IGeocodingProvider.SearchAsync(string query, CancellationToken) → Task<IReadOnlyList<GeocodingCandidate>>`; throws `GeocodingProviderUnavailableException` (new exception class in same file) on provider failure — mirrors `WeatherProviderUnavailableException` shape
- [X] T005 [P] Create `ILocationResolutionService` interface in `src/AskLucy.Application/Locations/ILocationResolutionService.cs` — single method: `Task<LocationResolutionOutcome> ResolveAsync(string? userId, Guid userChatId, string latestUserMessage, ActiveSiteLocation? activeLocation, CancellationToken cancellationToken = default)` — mirrors `IRagService`/`IMemoryService` signature shape
- [X] T006 [P] Create `LocationConfirmationTemplates` static class in `src/AskLucy.Application/Locations/LocationConfirmationTemplates.cs` — deterministic, template-based sentences (not model-generated text) for each non-`NoIntent` outcome: Confirmed ("I've located {locationName} and centred the viewer on it."), Ambiguous, NotFound, Unavailable, BackReferenceNoActive ("I don't have an active location yet — please name the place you'd like to view."); defensive: Nominatim's `LocationName` is embedded as data using `string.Create`/interpolation into a fixed template, never re-fed to any LLM call
- [X] T007 [P] Create `GeocodingOptions` configuration class in `src/AskLucy.Infrastructure/Geocoding/GeocodingOptions.cs` — two properties: `string SearchBaseUrl { get; set; } = "https://nominatim.openstreetmap.org/"` and `int ResolutionCeilingSeconds { get; set; } = 15` (the named constant for FR-013's timeout ceiling — replaces any inline `15` literal in T020; matches the "Weather" HttpClient's already-configured 15 s timeout); independently configurable from `WeatherOptions` (constitution §3 infrastructure isolation); bound via `IOptions<GeocodingOptions>` in Infrastructure DI
- [X] T008 Modify `UserChat` entity in `src/AskLucy.Domain/Chats/UserChat.cs` — add: (a) `public ActiveSiteLocation? ActiveLocation { get; private set; }` property; (b) `public void SetActiveLocation(double latitude, double longitude, string locationName, double confidence, string actor)` domain method — same shape as the existing `SetModelSelection`: assigns all four fields to a new `ActiveSiteLocation` instance, stamps `ModifiedAtUtc = DateTime.UtcNow` and `ModifiedBy = actor`; `ActiveLocation` starts `null` for every chat — no migration data backfill needed (depends on T002)
- [X] T009 Create `NominatimGeocodingProvider` in `src/AskLucy.Infrastructure/Geocoding/NominatimGeocodingProvider.cs` — `IGeocodingProvider` implementation: (a) inject `IHttpClientFactory` + `IOptions<GeocodingOptions>` + `ILogger<NominatimGeocodingProvider>`; (b) `CreateClient("Weather")` (reuses existing named client — same Nominatim host, 15 s timeout already configured); (c) build request: `GET {SearchBaseUrl}search?q={Uri.EscapeDataString(query)}&format=json&addressdetails=1&limit=5`; (d) add `User-Agent: AskLucy/1.0 (+https://hydra.bimcatalyst.com)` header (Nominatim usage policy — same literal string WeatherProvider uses); (e) parse JSON array; map each element to `GeocodingCandidate` using `display_name`, `lat` (parse to double), `lon` (parse to double), `importance`; drop any result where lat/lon parse fails; (f) on non-success HTTP status, timeout, or JSON exception: log warning with `{Query}` and exception, throw `GeocodingProviderUnavailableException` (depends on T004, T007)
- [X] T010 Add EF Core Fluent API owned-type configuration for `UserChat.ActiveLocation` — locate the existing `UserChat` entity configuration file under `src/AskLucy.Infrastructure/` and add `entity.OwnsOne(c => c.ActiveLocation, owned => { owned.Property(a => a.Latitude).HasColumnName("ActiveLocationLatitude"); owned.Property(a => a.Longitude).HasColumnName("ActiveLocationLongitude"); owned.Property(a => a.LocationName).HasColumnName("ActiveLocationName"); owned.Property(a => a.Confidence).HasColumnName("ActiveLocationConfidence"); })` — four new nullable columns on the existing `UserChats` table (depends on T008)
- [X] T011 Generate EF Core migration: run `dotnet ef migrations add AddActiveLocationToUserChat --project src/AskLucy.Infrastructure --startup-project src/AskLucy.Web` from the repo root; verify the generated migration adds four nullable columns and provides a working `Down` method (constitution §5 reversible migrations) (depends on T008, T010)
- [X] T012 [P] Register new types in DI: (a) in `src/AskLucy.Infrastructure/DependencyInjection.cs` — `services.AddScoped<IGeocodingProvider, NominatimGeocodingProvider>()` + `services.AddOptions<GeocodingOptions>().BindConfiguration("Geocoding").ValidateOnStart()`; (b) `ILocationResolutionService` registration is done in Phase 3 Task T019 (Application DI) — only Infrastructure registrations here (depends on T007, T009)

**Checkpoint**: `dotnet build` passes; migration file exists; DI registrations compile. Phase 3 can begin.

---

## Phase 3: User Story 1 — Naming a Known Place Recenters the Viewer (Priority: P1) 🎯 MVP

**Goal**: A user message naming a specific real-world place triggers intent classification → Nominatim geocoding → `Confirmed` outcome → deterministic confirmation sentence appended to the stream → `ConfirmedLocationData` on the final `ChatStreamChunk` (picked up by the existing `__LOCATION__` SSE emission in `AiController`, which is already implemented) → `UserChat.ActiveLocation` persisted for back-references. All of this runs concurrently with the model's text generation (FR-008).

**Independent Test**: POST `/api/v1/ai/chat` with message `"Show me Al Safa 2 Park"` → `__LOCATION__` SSE trailing event appears with correct lat/lon/locationName; `UserChat` DB row shows `ActiveLocation*` columns populated. Then send `"Zoom in on it"` on the same chat → same `__LOCATION__` data re-appears; no Nominatim HTTP call in logs for the second message (back-reference path).

### Tests for User Story 1

- [X] T013 [P] [US1] Write `NominatimGeocodingProviderTests` in `tests/AskLucy.Infrastructure.Tests/Geocoding/NominatimGeocodingProviderTests.cs` — test cases: (a) single valid result → one `GeocodingCandidate` with correct lat/lon/name/importance; (b) empty JSON array `[]` → empty list (not an error); (c) result where `lat` or `lon` is non-numeric → that result is dropped, others kept; (d) HTTP 500 response → throws `GeocodingProviderUnavailableException`; (e) `HttpRequestException` (simulated timeout) → throws `GeocodingProviderUnavailableException`; (f) response body is not valid JSON → throws `GeocodingProviderUnavailableException`; use `HttpMessageHandler` mock/stub pattern mirroring `WeatherProviderTests.cs`
- [X] T014 [P] [US1] Add `SetActiveLocation` coverage to `tests/AskLucy.Domain.Tests/Chats/UserChatTests.cs` (add to the existing file, do not replace it) — test cases: (a) `SetActiveLocation` sets all four fields on `ActiveLocation` correctly; (b) `ModifiedAtUtc` and `ModifiedBy` are updated; (c) calling `SetActiveLocation` a second time replaces the previous value; (d) a freshly constructed `UserChat` has `ActiveLocation == null`
- [X] T015 [P] [US1] Write `LocationResolutionServiceTests` (Confirmed and back-reference paths) in `tests/AskLucy.Application.Tests/Locations/LocationResolutionServiceTests.cs` — test cases for this phase: (a) classifier returns `new_query` + one geocoding result above `MinimumImportanceFloor=0.1` → `Confirmed` with correct `ConfirmedLocationData`; (b) classifier returns `new_query` + two results where top-result importance − second-result importance ≥ `DominanceMargin=0.2` → `Confirmed` with leader's data; (c) classifier returns `back_reference` + `activeLocation` is non-null → `Confirmed` with same lat/lon/name/confidence as `activeLocation`; no `IGeocodingProvider.SearchAsync` call made; (d) coordinate outside WGS-84 (lat > 90) → result dropped (treated as no result); (e) `ConfirmationText` is non-null for Confirmed outcome; (f) **FR-011 — repeat identical request**: given the same place-name message sent twice (same `latestUserMessage`, non-null `activeLocation` or faked geocoding returning the same result), both calls return `Confirmed` independently — no deduplication or duplicate-rejection logic fires; fake `IGeocodingProvider` and `IAIProvider` (via `DefaultProviderResolver`) per constitution §2.V
- [X] T016 [US1] Write `SendChatMessageLocationIntegrationTests` (US1 scenarios) in `tests/AskLucy.Application.Tests/Ai/SendChatMessageLocationIntegrationTests.cs` — test cases: (a) message with location intent → `ILocationResolutionService.ResolveAsync` is called with the correct `latestUserMessage` and `activeLocation`; (b) `Confirmed` outcome → a `ContentDelta` chunk carrying `ConfirmationText` is yielded after the model's content chunks; (c) `Confirmed` outcome → the final `ChatStreamChunk` has `ConfirmedLocation` non-null with correct lat/lon/name/confidence; (d) `NoIntent` outcome → no extra `ContentDelta` chunk appended; `ConfirmedLocation` null on final chunk; mirrors `SendChatMessageRagIntegrationTests.cs` structure; fake `ILocationResolutionService` returning pre-built outcomes

### Implementation for User Story 1

- [X] T017 [US1] Create `LocationResolutionService` in `src/AskLucy.Application/Locations/LocationResolutionService.cs` — implement `ILocationResolutionService.ResolveAsync`: (a) inject `DefaultProviderResolver`, `IAIProviderRepository`, `IAIModelRepository`, `IAIProviderResolver`, `IGeocodingProvider`, `ILogger<LocationResolutionService>`; (b) call `DefaultProviderResolver.ResolveAsync` → fetch provider + model → `aiProvider.ChatAsync(messages, modelKey, parameters: null, ct)` with `LocationIntentClassificationPromptV1` system prompt and latest user message; (c) deserialize response JSON to private `LocationIntentPayload { string Intent; IReadOnlyList<string> PlaceQueries }` — any `JsonException` or unrecognized `intent` value → return `Unavailable` (not `NoIntent` — contract spec); (d) `intent == "none"` → return `NoIntent` (no log, no geocoding); (e) `intent == "back_reference"` + `activeLocation != null` → return `Confirmed` from `activeLocation` fields (no geocoding call, FR-014); `activeLocation == null` → return `Unavailable` with `LocationConfirmationTemplates.BackReferenceNoActive`; (f) `intent == "new_query"`, `placeQueries.Count >= 2` → return `Ambiguous` immediately (FR-009, no geocoding); `placeQueries.Count == 1` → call `IGeocodingProvider.SearchAsync(placeQueries[0], ct)`; apply confidence algorithm (see below); (g) **confidence algorithm**: filter out results with `Importance < MinimumImportanceFloor (0.1)`; 0 remaining → `NotFound`; 1 remaining → `Confirmed` with its data; 2+ remaining: if `results[0].Importance − results[1].Importance >= DominanceMargin (0.2)` → `Confirmed` with results[0], else `Ambiguous`; validate winner's lat/lon against WGS-84 (drop and treat as `NotFound` if out of range); (h) catch `GeocodingProviderUnavailableException` → `Unavailable`; catch any other exception from classification call → `Unavailable`; (i) log one structured event per non-`NoIntent` outcome via `ILogger`: `{UserChatId}`, `{Query}`, `{OutcomeType}`, `{Confidence}` (when Confirmed), `{Source}` (when Confirmed); (j) return `LocationResolutionOutcome` with `ConfirmationText` from `LocationConfirmationTemplates` (depends on T003–T006)
- [X] T018 [US1] Define `LocationIntentClassificationPromptV1` versioned system-prompt constant in `LocationResolutionService` (or a nested static class `LocationResolutionService.Prompts`) — prompt must: distinguish navigational intent ("show me X", "where is X", "take me to X", "center on X") from incidental mention (past-tense recollection, comparison, fact); recognize simple back-references ("zoom in on it", "center on that place", "go there") as `"back_reference"` without requiring the model to know what "it" refers to; when message names two or more distinct places with no single navigational target, return all in `placeQueries`; respond with **only** a single JSON object `{"intent":"none"|"new_query"|"back_reference","placeQueries":[...]}`, no markdown wrapper; version the constant per constitution §9 — name it with the `V1` suffix so a future revision is a new constant, not a replacement (depends on T017)
- [X] T019 [US1] Register `ILocationResolutionService → LocationResolutionService` (scoped) in `src/AskLucy.Application/DependencyInjection.cs` — mirrors the existing `AddScoped<IRagService, RagService>()` + `AddScoped<IMemoryService, MemoryService>()` registrations (depends on T017)
- [X] T020 [US1] Modify `SendChatMessageCommandHandler` in `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs` — (a) inject `ILocationResolutionService` and `IOptions<GeocodingOptions>`; (b) before starting the `StreamChatAsync` loop, read `userChat.ActiveLocation`; (c) record `var turnStartUtc = DateTime.UtcNow`; (d) store but do NOT await the resolution task: `var locationTask = _locationResolutionService.ResolveAsync(userId, chatId, latestUserMessage, activeLocation, ct)`; (e) run the content-delta loop as today (RAG/Memory already inline before it, streaming unchanged); (f) after the loop ends, compute remaining budget using `_geocodingOptions.ResolutionCeilingSeconds` (the named constant from T007 — do NOT use an inline `15` literal): `var remaining = TimeSpan.FromSeconds(_geocodingOptions.ResolutionCeilingSeconds) − (DateTime.UtcNow − turnStartUtc)`; use `locationTask.WaitAsync(remaining)` (without `ct`) — this fires `TimeoutException` when the budget expires; if budget already elapsed, treat immediately as `Unavailable`; **(I2 — cancellation safety)**: `OperationCanceledException` where `ct.IsCancellationRequested == true` (client disconnect) MUST be re-thrown, not swallowed as `Unavailable` — catch `TimeoutException` only (or `OperationCanceledException` where `!ct.IsCancellationRequested`, e.g., internal task cancellation) and map to `Unavailable`; (g) if `locationOutcome.Type != NoIntent`, yield one `ChatStreamChunk` with `ContentDelta = locationOutcome.ConfirmationText`; (h) on the trailing usage chunk (or a dedicated final chunk), set `ConfirmedLocation = locationOutcome.ConfirmedLocation` (non-null only when `Type == Confirmed`); `ChatStreamChunk.cs` itself is unchanged (depends on T005, T007, T017, T019)
- [X] T021 [US1] Create `RecordActiveLocationCommand` in `src/AskLucy.Application/Chats/Commands/RecordActiveLocation/RecordActiveLocationCommand.cs` — `public sealed record RecordActiveLocationCommand(Guid UserChatId, ConfirmedLocationData ConfirmedLocation) : IRequest;`
- [X] T022 [US1] Create `RecordActiveLocationCommandHandler` in `src/AskLucy.Application/Chats/Commands/RecordActiveLocation/RecordActiveLocationCommandHandler.cs` — inject `IUserChatRepository`, `IUnitOfWork`, `ICurrentUserService`; load `UserChat` by `UserChatId`; call `userChat.SetActiveLocation(cmd.ConfirmedLocation.Latitude, cmd.ConfirmedLocation.Longitude, cmd.ConfirmedLocation.LocationName, cmd.ConfirmedLocation.Confidence, currentUserId)`; commit via `_unitOfWork.CommitAsync(ct)` — same one-command-one-transaction shape as every other mutating command in this codebase; mirrors `RecordMemoryReferencesCommandHandler` structure (depends on T008, T021)
- [X] T023 [US1] Modify `AiController` in `src/AskLucy.Web/Controllers/v1/AiController.cs` — after the stream loop ends, in the same block where `RecordMemoryReferencesCommand` is dispatched: if the trailing chunk's `ConfirmedLocation` is non-null, dispatch `await _mediator.Send(new RecordActiveLocationCommand(chatId, confirmedLocation), ct)` — the `__LOCATION__` SSE event write already exists at this location (spec 036); this adds the DB persistence dispatch immediately after or alongside it (depends on T021, T022)

**Checkpoint**: User Story 1 is fully functional — confident location requests recenter the viewer in real time; back-references re-confirm from the persisted snapshot without a new geocoding call; `UserChat.ActiveLocation` is updated post-stream.

---

## Phase 4: User Story 2 — Passing Mention Does Not Move the Viewer (Priority: P2)

**Goal**: Verify the intent classifier correctly returns `NoIntent` for messages that mention a place name without navigational intent, so the viewer never moves and no confirmation text is appended.

**Independent Test**: POST `/api/v1/ai/chat` with `"I read that Al Safa Park was renovated last year"` → no `__LOCATION__` SSE event; no extra ConfirmationText content-delta line; `UserChat.ActiveLocation` unchanged.

*No new source files — the `NoIntent` path is fully handled by `LocationResolutionService` from Phase 3. These tasks verify it via targeted tests.*

### Tests for User Story 2

- [X] T024 [P] [US2] Add `NoIntent` path tests to `tests/AskLucy.Application.Tests/Locations/LocationResolutionServiceTests.cs` (add to existing file from T015) — test cases: (a) classifier returns `"none"` → `LocationResolutionOutcomeType.NoIntent`; `ConfirmationText` is null; no `IGeocodingProvider.SearchAsync` call made; (b) comparison-phrased message ("how does Al Safa Park compare to Zabeel Park?") where classifier returns `"none"` → `NoIntent`; (c) past-tense recollection message → `NoIntent`; (d) `NoIntent` → structured log entry is NOT written (Decision 7: only non-NoIntent outcomes are logged)
- [X] T025 [P] [US2] Add US2 scenarios to `tests/AskLucy.Application.Tests/Ai/SendChatMessageLocationIntegrationTests.cs` (add to existing file from T016) — test cases: (a) `ILocationResolutionService` faked to return `NoIntent` → no extra `ContentDelta` chunk appended to stream; (b) `NoIntent` → `ConfirmedLocation` is null on final chunk; (c) `NoIntent` → `ILocationResolutionService.ResolveAsync` is still called (the service decides, the handler doesn't pre-screen)

**Checkpoint**: Passing mentions produce no location events, no viewer movement, and no confirmation text.

---

## Phase 5: User Story 3 — Uncertain Place Is Never Silently Guessed (Priority: P2)

**Goal**: Verify that `Ambiguous`, `NotFound`, and `Unavailable` outcomes each produce a user-visible explanation sentence in the stream and emit no `ConfirmedLocationData` — never a silent no-op and never an incorrect auto-confirmation.

**Independent Test**: Send `"Show me Springfield"` (ambiguous) → no `__LOCATION__` event; ambiguity sentence visible in stream. Send `"Show me Xyzzyplorp"` (not found) → not-found sentence visible. Block Nominatim → unavailable sentence within 15 s and `data: [DONE]` still emitted (response completes).

*No new source files — these paths are all handled by `LocationResolutionService` from Phase 3.*

### Tests for User Story 3

- [X] T026 [P] [US3] Add `Ambiguous`/`NotFound`/`Unavailable` path tests to `tests/AskLucy.Application.Tests/Locations/LocationResolutionServiceTests.cs` (add to existing file from T015, T024) — test cases: (a) two geocoding results where `importance` margin < `DominanceMargin=0.2` → `Ambiguous`; (b) classifier returns `placeQueries.Count >= 2` → `Ambiguous` with no `IGeocodingProvider` call; (c) zero results remaining after `MinimumImportanceFloor=0.1` filter → `NotFound`; (d) all results have `importance < 0.1` → `NotFound`; (e) `IGeocodingProvider` throws `GeocodingProviderUnavailableException` → `Unavailable`; (f) classification `ChatAsync` throws any exception → `Unavailable`; (g) classification returns unrecognized `intent` value (e.g. `"maybe"`) → `Unavailable` (not `NoIntent`); (h) `back_reference` intent + `activeLocation == null` → `Unavailable` with `LocationConfirmationTemplates.BackReferenceNoActive` text; (i) geocoding result with lat outside `[−90, 90]` → result dropped, treated as no result; for all non-`NoIntent` outcomes: `ConfirmationText` is non-null; `ConfirmedLocation` is null for `Ambiguous`/`NotFound`/`Unavailable`
- [X] T027 [P] [US3] Add US3 scenarios to `tests/AskLucy.Application.Tests/Ai/SendChatMessageLocationIntegrationTests.cs` (add to existing file from T016, T025) — test cases: (a) `Ambiguous` outcome faked → `ConfirmationText` `ContentDelta` chunk IS appended; `ConfirmedLocation` null on final chunk; (b) `NotFound` faked → same; (c) `Unavailable` faked → same; (d) for all three: the stream still completes (`yield break` reached — handler does not hang); `[DONE]` marker is written

**Checkpoint**: Uncertain/unresolvable requests produce a clear explanation and never move the viewer — constitution §2.VIII (no silent failures) satisfied across all non-`NoIntent` outcome types.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Structured logging review, prompt injection safety sign-off, build/test verification, and quickstart manual validation.

- [X] T028 [P] Review structured log calls in `src/AskLucy.Application/Locations/LocationResolutionService.cs` against constitution §4 Logging — confirm: named Serilog properties (`{UserChatId}`, `{Query}`, `{OutcomeType}`, `{Confidence}`, `{Source}`); no string-concatenated log messages; no prompt content, geocoding `display_name`, or user PII logged at `Information` or above; `Unavailable` branch logs at `Warning` with the originating exception
- [X] T029 [P] Prompt injection safety review (constitution §8 ⚠️ Required) — read `LocationConfirmationTemplates.cs` and confirm: (a) Nominatim's `display_name` (the `LocationName` field of `GeocodingCandidate`) is embedded only in deterministic template strings, never passed as an instruction to `ChatAsync` or any other model call; (b) the `LocationIntentClassificationPromptV1` system prompt contains no reference to any geocoding response text — only the user's own message goes to the classifier; (c) no external content from the geocoding API response flows into the `messages` list for any AI call
- [X] T030 [P] Run `tsc -b --noEmit` in `src/AskLucy.Web/ClientApp` — confirm zero TypeScript errors (no frontend files were changed; this is a regression guard per `frontend_tsc_noemit_silent_noop` memory entry — bare `tsc --noEmit` is a no-op on this project's tsconfig, must use `-b`)
- [X] T031 Run full build and test suite: `dotnet build src/AskLucy.sln` (zero errors/warnings) then `dotnet test` — all pre-existing tests pass; all 4 new test files (T013, T014–T016 updated, T024–T027 accumulated) pass; verify coverage of `LocationResolutionService` and `NominatimGeocodingProvider` is meaningful (constitution §10 floor: Domain + Application ≥ 80% line coverage as a floor)
- [ ] T032 Execute quickstart.md Scenario 1 (confident single match) against the running local backend — confirm `__LOCATION__` SSE trailing event appears with correct lat/lon/locationName, confirmation sentence visible in stream, and `UserChat` DB row's `ActiveLocation*` columns populated after the response completes
- [ ] T033 Execute quickstart.md Scenario 5 (back-reference) against the running local backend — confirm the `__LOCATION__` event re-appears with the same lat/lon/locationName as Scenario 1; verify no `NominatimGeocodingProvider` log line appears for the second message (logs should show only the classification call, not a geocoding HTTP call)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1**: No dependencies — start immediately.
- **Phase 2**: Depends on Phase 1 baseline. Tasks T002–T007 are fully parallel. T008 depends on T002. T009 depends on T004 + T007. T010 depends on T008. T011 depends on T008 + T010. T012 depends on T007 + T009. **BLOCKS all phases beyond Phase 2.**
- **Phase 3 (US1)**: Depends on Phase 2 complete. Test tasks T013–T016 can start in parallel with the implementation tasks T017–T023 (tests target interfaces defined in Phase 2). T017 (service) before T019 (DI) before T020 (handler modification). T021 (command record) before T022 (handler) before T023 (controller modification). T018 depends on T017.
- **Phase 4 (US2)**: Depends on Phase 3 (LocationResolutionService must exist). T024 and T025 are parallel with each other. Can overlap with Phase 5 once Phase 3 is done.
- **Phase 5 (US3)**: Depends on Phase 3. T026 and T027 are parallel. Can run in parallel with Phase 4.
- **Phase 6 (Polish)**: Depends on Phases 3–5. T028–T030 are parallel with each other. T031 depends on T028–T030. T032–T033 depend on T031.

### User Story Dependencies

- **US1 (P1)**: Starts after Phase 2 — no dependency on US2 or US3.
- **US2 (P2)**: Depends on Phase 3 (`LocationResolutionService` must exist to test its `NoIntent` path).
- **US3 (P2)**: Depends on Phase 3. Runs in parallel with US2.

### Within Phase 3

- Interfaces (T004, T005) before their implementations (T009, T017).
- `LocationResolutionService` (T017) → DI registration (T019) → handler modification (T020).
- `RecordActiveLocationCommand` (T021) → handler (T022) → controller (T023).

---

## Parallel Execution Guide: User Story 1

```
# Phase 2 — launch all concurrently:
T002  ActiveSiteLocation domain record
T003  LocationResolutionOutcome + enum
T004  GeocodingCandidate + IGeocodingProvider interface
T005  ILocationResolutionService interface
T006  LocationConfirmationTemplates
T007  GeocodingOptions

# Then (T008 needs T002):
T008  UserChat.ActiveLocation + SetActiveLocation()

# Then concurrently:
T009  NominatimGeocodingProvider  (needs T004, T007)
T010  EF Core owned-type config   (needs T008)

# Then concurrently:
T011  Generate migration           (needs T008, T010)
T012  Infrastructure DI            (needs T007, T009)

# Phase 3 — once Phase 2 is done, launch concurrently:
T013  NominatimGeocodingProviderTests
T014  UserChatActiveLocationTests
T015  LocationResolutionServiceTests (Confirmed + back-reference)
T016  SendChatMessageLocationIntegrationTests (US1)
T017  LocationResolutionService implementation

# Then:
T018  LocationIntentClassificationPromptV1  (needs T017)
T019  Application DI registration           (needs T017)
T021  RecordActiveLocationCommand           (parallel with T018, T019)

# Then:
T020  SendChatMessageCommandHandler modification  (needs T019)
T022  RecordActiveLocationCommandHandler          (needs T021)

# Then:
T023  AiController modification  (needs T022)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (baseline build check)
2. Complete Phase 2 (all blocking prerequisites — T002–T012)
3. Complete Phase 3 (T013–T023)
4. **STOP and VALIDATE**: execute quickstart.md Scenarios 1 and 5
5. Deploy/demo if validated

### Incremental Delivery

1. Phase 1 + Phase 2 → Types/interfaces/infrastructure + migration ready
2. Phase 3 (US1) → Confirmed location pipeline functional end-to-end → **MVP deploy**
3. Phase 4 (US2) + Phase 5 (US3) in parallel → Intent precision and failure-path correctness verified
4. Phase 6 (Polish) → Logging review, security review, full test run, quickstart validation → Production-ready merge

### Parallel Team Strategy

After Phase 2 completes, two developers can work in parallel:

- **Developer A**: T013/T015 (tests) + T017–T020 (LocationResolutionService + SendChatMessageCommandHandler) — the resolution pipeline
- **Developer B**: T014 (tests) + T021–T023 (RecordActiveLocation command/handler/controller) — the persistence pipeline

Both merge independently, then Phases 4–6 proceed together.

---

## Notes

- **[P]** = task targets a different file from currently active tasks and has no unmet dependency — safe to parallelize
- **No frontend changes** — specs 035/036 already shipped `activeLocationStore`, the `__LOCATION__` SSE parser, and `ViewerSurface` re-centering; this feature only needs to populate `ChatStreamChunk.ConfirmedLocation`
- **FR-010 (geocoding cache from spec 035) is explicitly out of scope** — do NOT add a caching decorator to `NominatimGeocodingProvider`; spec.md Assumptions and plan.md both defer that to a future spec 035 implementation
- **`LocationIntentClassificationPromptV1`** (T018) — exact prompt wording is an implementation detail left to the developer; the behavioral contract is specified in `contracts/location-intent-classification-contract.md`; the `V1` suffix is mandatory per constitution §9
- **`ChatStreamChunk.cs` is not modified** — `ConfirmedLocation ConfirmedLocationData?` already exists on the record from spec 036
- **`AiController`'s `__LOCATION__` SSE write is not modified** — only the new `RecordActiveLocationCommand` dispatch (T023) is added alongside the already-existing event write
- **Test strategy**: Unit tests fake `IGeocodingProvider` and `IAIProvider` per constitution §2.V; integration-level tests for `NominatimGeocodingProvider` use an `HttpMessageHandler` mock (no live outbound calls in tests)
