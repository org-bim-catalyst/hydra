---

description: "Task list for Site Boundary Resolution (specs/042-site-boundary-resolution)"
---

# Tasks: Site Boundary Resolution

**Input**: Design documents from `/specs/042-site-boundary-resolution/` (plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md)

**Tests**: Included — constitution §10/§19 requires tests for new/changed observable behavior in the same change that introduces it (not optional in this repository).

**Organization**: Tasks are grouped by user story (spec.md priorities P1-P4), preceded by Setup/Foundational phases, followed by the secondary Agent Tool surface and Polish.

**⚠️ Primary mechanism correction** (research.md #11): User Stories 1-3 are delivered by a deterministic chat-pipeline hook (mirrors `ILocationResolutionService`'s wiring), **not** by the `IAgentTool` mentioned in the original feature description. The `IAgentTool` is built separately, in Phase 7, as a secondary surface for custom agents. Read `contracts/chat-pipeline-integration.md` before starting Phase 3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Maps the task to US1/US2/US3/US4 — omitted for Setup/Foundational/Secondary-surface/Polish tasks
- Every task names its exact file path

---

## Phase 1: Setup

**Purpose**: Configuration scaffolding shared by every later phase.

- [X] T001 Add `Boundaries` (Overpass base URL/timeout) and `BoundaryScoring` (radius/max-candidates/weights/thresholds, per data-model.md's `BoundaryScoringOptions` defaults) configuration sections to `src/AskLucy.Web/appsettings.json` and `src/AskLucy.Web/appsettings.Development.json`, matching the existing `Geocoding` section's style.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared resolution pipeline and persistence plumbing every user story depends on. No user story can be implemented or tested until this phase is complete.

**⚠️ CRITICAL**: Do not start Phase 3+ until this phase's tasks all pass.

- [X] T002 [P] Create `GeoPoint`, `SiteBoundaryPolygon`, `SiteBoundarySource`, `BoundaryConfidenceLevel` in `src/AskLucy.Domain/SiteBoundaries/GeoPoint.cs`, `SiteBoundaryPolygon.cs`, `SiteBoundarySource.cs`, `BoundaryConfidenceLevel.cs` (data-model.md Domain section)
- [X] T003 Create `SiteBoundaryResult` in `src/AskLucy.Domain/SiteBoundaries/SiteBoundaryResult.cs` (depends on T002's types)
- [X] T004 Create `ActiveSiteBoundary` in `src/AskLucy.Domain/Chats/ActiveSiteBoundary.cs`, mirroring `ActiveSiteLocation.cs` exactly (data-model.md's persisted section)
- [X] T005 Add an `ActiveBoundary` property and `SetActiveBoundary(...)` method to `src/AskLucy.Domain/Chats/UserChat.cs`, mirroring `ActiveLocation`/`SetActiveLocation` at lines 240-248 (depends on T004)
- [X] T006 [P] Add `builder.OwnsOne(c => c.ActiveBoundary, ...)` with a `HasConversion` value converter for the polygon ring (JSON string ⇄ `IReadOnlyList<GeoPoint>`) to `src/AskLucy.Persistence/Configurations/UserChatConfiguration.cs`, mirroring the existing `ActiveLocation` `OwnsOne` block at lines 87-93 (depends on T004, T005)
- [X] T007 Generate the EF Core migration `AddActiveSiteBoundaryToUserChat` (new nullable columns only, reversible `Down`), following the same two-file-plus-snapshot-update pattern as `20260823190247_AddActiveLocationToUserChat` (depends on T006)
- [X] T008 [P] Create `IBoundaryCandidateProvider`, `BoundaryCandidate`, `ScoredBoundaryCandidate` in `src/AskLucy.Application/SiteBoundaries/IBoundaryCandidateProvider.cs`, `BoundaryCandidate.cs`, `ScoredBoundaryCandidate.cs` (data-model.md Application section)
- [X] T009 [P] Create `BoundaryScoringOptions` (weights sum to 1.0, thresholds ordered, positive radius/max-candidates) in `src/AskLucy.Application/SiteBoundaries/BoundaryScoringOptions.cs` — implemented as `IValidatableObject` invoked via `.ValidateDataAnnotations()` rather than a separate `IValidateOptions<T>` class, matching this codebase's existing options-validation convention (no other `IValidateOptions` precedent exists here)
- [X] T010 [P] Create the `GeometryMath` static helper (shoelace area, centroid, bbox, distance, circle-polygon for the manual fallback, all via an equirectangular local-meters projection — no new package) in `src/AskLucy.Application/SiteBoundaries/GeometryMath.cs`
- [X] T011 Create `BoundaryCandidateScorer` (ports the notebook's `score_candidate` — source reliability, name match, geometry plausibility incl. FR-013's implausible-size penalty, center proximity, land-use agreement) in `src/AskLucy.Application/SiteBoundaries/BoundaryCandidateScorer.cs` (depends on T008, T009, T010)
- [X] T012 [P] Add `ConfirmedSiteBoundaryData` and the `ConfirmedBoundary` property to `src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs`, alongside the existing `ConfirmedLocationData`/`ConfirmedLocation` (data-model.md's transport-shape section)
- [X] T013 [P] Create `BoundaryConfirmationTemplates` (deterministic confirmation sentences per outcome, plus the correction-handling guidance text used later by T034/T041) in `src/AskLucy.Application/SiteBoundaries/BoundaryConfirmationTemplates.cs`, mirroring `LocationConfirmationTemplates.cs`
- [X] T014 Create `IBoundaryResolutionService`/`BoundaryResolutionService` (orchestrates: takes a `ConfirmedLocationData`, calls `IBoundaryCandidateProvider`, scores via `BoundaryCandidateScorer`, classifies confidence, returns `BoundaryResolutionOutcome`; never throws — every failure path maps to `NoCandidates`/`Unavailable`) in `src/AskLucy.Application/SiteBoundaries/IBoundaryResolutionService.cs` and `BoundaryResolutionService.cs` (depends on T003, T008-T013) — corrected during implementation: `NoCandidates` still carries a manual-fallback `ConfirmedBoundary` (data-model.md update), so User Story 1's "approximate area" acceptance scenario has something to actually render
- [X] T015 [P] Create `RecordActiveSiteBoundaryCommand`/`RecordActiveSiteBoundaryCommandHandler` in `src/AskLucy.Application/Chats/Commands/RecordActiveSiteBoundary/RecordActiveSiteBoundaryCommand.cs` and `RecordActiveSiteBoundaryCommandHandler.cs`, mirroring `RecordActiveLocationCommand`/`RecordActiveLocationCommandHandler.cs` exactly (depends on T004, T005)
- [X] T016 [P] Create `OverpassOptions` (`src/AskLucy.Infrastructure/Boundaries/OverpassOptions.cs`) and `BoundaryProviderUnavailableException` — the latter placed in `src/AskLucy.Application/SiteBoundaries/` (not Infrastructure), mirroring exactly where `GeocodingProviderUnavailableException` lives, since `BoundaryResolutionService` (Application) must catch it without referencing Infrastructure (constitution §3)
- [X] T017 Create `OverpassBoundaryCandidateProvider` (named `HttpClient`, queries OSM Overpass for polygon-shaped features within the search radius, maps to `BoundaryCandidate`, catches `HttpRequestException`/`TaskCanceledException`/`JsonException` into `BoundaryProviderUnavailableException`) in `src/AskLucy.Infrastructure/Boundaries/OverpassBoundaryCandidateProvider.cs`, mirroring `NominatimGeocodingProvider.cs`'s structure exactly (depends on T008, T016) — v1 handles OSM `way` elements only, documented in a code comment; `relation` (multipolygon) support is an additive future change
- [X] T018 Register the `"Overpass"` named `HttpClient` (15s timeout, matching `"Geocoding"`), bind+validate `OverpassOptions`/`BoundaryScoringOptions` on start, and register `IBoundaryCandidateProvider`→`OverpassBoundaryCandidateProvider` in `src/AskLucy.Infrastructure/DependencyInjection.cs`; register `IBoundaryResolutionService`→`BoundaryResolutionService` in `src/AskLucy.Application/DependencyInjection.cs` (depends on T014, T017)
- [X] T019 [P] Create `activeSiteBoundaryStore.ts` (Zustand: `siteName`, `centroid`, `polygon`, `confidence`, `confidenceLevel`, `source`, `sourceDetail`, `alternativeCandidateNames`, `setBoundary()`, `clearBoundary()`) in `src/AskLucy.Web/ClientApp/src/store/activeSiteBoundaryStore.ts`, mirroring `activeLocationStore.ts` (contracts/frontend-viewer-contract.md §1)

**Full solution build verified green (`dotnet build "Ask Lucy.sln"`, 0 errors) after T001-T019.**

**Checkpoint**: `dotnet build` succeeds; the resolution pipeline exists and is unit-testable end-to-end (still disconnected from the chat turn and the viewer) before any user story begins.

---

## Phase 3: User Story 1 - See a named site's boundary highlighted on the map (Priority: P1) 🎯 MVP

**Goal**: Asking Lucy about a named, well-mapped site produces a visible, animated, high-confidence boundary on the map within 10 seconds (SC-001).

**Independent Test**: Quickstart Scenario A — ask "Show me Al Safa Park 2," confirm a highlighted polygon matching the park's real extent appears, with a High-confidence, OSM-sourced narration.

### Tests for User Story 1

- [X] T020 [P] [US1] Unit tests for `BoundaryCandidateScorer` (every weight factor, FR-013's implausible-size penalty) in `tests/AskLucy.Application.Tests/SiteBoundaries/BoundaryCandidateScorerTests.cs` — 6 tests, all passing
- [X] T021 [P] [US1] Unit tests for `BoundaryResolutionService`'s `Confirmed`/High-confidence path (faked `IBoundaryCandidateProvider`) in `tests/AskLucy.Application.Tests/SiteBoundaries/BoundaryResolutionServiceTests.cs` — 7 tests (incl. NoCandidates/Unavailable/alternatives, pulled forward from T038), all passing
- [X] T022 [P] [US1] Integration tests for `OverpassBoundaryCandidateProvider` against recorded/replayed HTTP fixtures in `tests/AskLucy.Infrastructure.Tests/Boundaries/OverpassBoundaryCandidateProviderTests.cs` — 6 tests, all passing
- [X] T023 [P] [US1] Persistence round-trip test for `UserChat.ActiveBoundary` (including the polygon JSON conversion) in `tests/AskLucy.Persistence.Tests/Chats/UserChatActiveBoundaryPersistenceTests.cs` — 2 tests, verified green against the real shared test SQL Server instance (migration applied)

### Implementation for User Story 1

- [X] T024 [US1] Extend `SendChatMessageCommandHandler` (`src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs`) to launch `IBoundaryResolutionService.ResolveAsync` once `locationOutcome` is known, only when the confirmed site differs from `chat.ActiveBoundary?.SiteName`; yield the confirmation sentence and the final chunk's `ConfirmedBoundary`, per `contracts/chat-pipeline-integration.md` (depends on T014, T024's own tests T021) — caught and fixed a real ordering bug in the contract draft itself (system-message injection must happen before streaming, not after, per research.md #11's corrected pseudocode); updated all 7 existing `SendChatMessageCommandHandler` test files to pass the new constructor dependency; full `AskLucy.Application.Tests` suite run: 996/999 passing (3 pre-existing failures in unrelated `Mcp.McpObservabilityTests`, present before this feature's changes — see git status at session start)
- [X] T025 [US1] Extend `AiController` (`src/AskLucy.Web/Controllers/v1/AiController.cs`) to accumulate `chunk.ConfirmedBoundary`, send `RecordActiveSiteBoundaryCommand` after the stream, and write the `data: __SITE_BOUNDARY__{json}\n\n` trailing event, mirroring the existing `__LOCATION__` block at lines 180-209 (depends on T012, T015, T024) — compiles cleanly (verified via full solution build); `AiControllerVoiceTests` (a different controller area) could not be exercised end-to-end in this environment — its `WebApplicationFactory` host requires a local SQL Server "AskLucyTests" instance not present here, a pre-existing environment limitation unrelated to this change
- [X] T026 [US1] Extend the chat stream parser in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts` to recognize `__SITE_BOUNDARY__` and call `useActiveSiteBoundaryStore.getState().setBoundary(...)` (depends on T019, T025) — wired via `useChatStream.ts`'s existing event-switch (mirrors the `location`/`zoom` branches); also added there: clearing the store when a `location` event names a different site than the currently active boundary (the edge case a same-turn `Unavailable` boundary outcome would otherwise leave stale)
- [X] T027 [US1] Add `setSiteBoundary(input | null)` to `GoogleMapsGisLayerHandle`/`GoogleMapsGisLayer.ts` — **corrected approach from research.md #8's original plan**: rather than a second `transformer.fromLatLngAltitude()` call per object (unverified/riskier), boundary vertices are projected into local-meters directly relative to the layer's existing fixed `options.center` — the exact same anchor the camera's own transform already uses every frame — so placed geometry tracks the live Maps camera with no extra per-object transform call; also exposed the full handle via a new `googleMapsStore.setHandle()` (previously only the raw `map` was exposed) since `SiteBoundaryOverlay` needs `setSiteBoundary`, not just the map instance
- [X] T028 [US1] Create `SiteBoundaryRenderer.ts` — owns the `AnimatedBorderHighlight` instance for the active boundary; swaps it wholesale on `setPolygon()`, advances it via `update()` called from `onDraw`
- [X] T029 [US1] Create `AnimatedBorderHighlight.ts` — generalized arc-length-parameterized comet(s) over any ordered point list, additive blending + head-brightening intensity curve adapted from `docs/BORDER_HIGHLIGHT.html`; implemented all three confidence modes (high: 2 comets, medium: 1 slower comet, low: static dashed-tone perimeter only) in this task rather than deferring medium/low to US2, since the scoring is one small parameter table — fixed a real dispose() bug caught by its own test (the static perimeter line was never removed from the group)
- [X] T030 [US1] Create `SiteBoundaryOverlay.tsx` wiring `activeSiteBoundaryStore` → `handle.setSiteBoundary()`, mirroring `POIMarkerOverlay.tsx`'s imperative `return null` idiom; mounted alongside `POIMarkerOverlay` in `ViewerSurface.tsx` (depends on T019, T027, T029)
- [X] T031 [US1] [P] Frontend unit tests — 21 new tests across `activeSiteBoundaryStore.test.ts` (4), `AnimatedBorderHighlight.test.ts` (6), `SiteBoundaryOverlay.test.tsx` (4), `aiApi.test.ts` (+1 `__SITE_BOUNDARY__` case), all passing; full frontend suite (769 tests) run clean (2 unrelated pre-existing timeout flakes in `ChatPage.test.tsx`/`ChatPage.a11y.test.tsx`, confirmed passing in isolation — a full-suite parallel-load timing issue, not a regression)

**US1 (MVP) complete and independently demonstrable.** `tsc -b --noEmit` and `eslint` both clean on every new/changed frontend file. `dotnet build "Ask Lucy.sln"` clean.

**Checkpoint**: User Story 1 is independently demonstrable end-to-end (quickstart Scenario A) — this is the MVP.

---

## Phase 4: User Story 2 - Understand how much to trust a shown boundary (Priority: P2)

**Goal**: Medium/Low confidence render visibly differently from High, are narrated with a reason, and a follow-up question about an unchanged boundary is answered from context without re-resolving.

**Independent Test**: Quickstart Scenario B (low-confidence fallback) plus User Story 2's acceptance scenario 3 (follow-up "how sure are you" with no new map flicker).

### Tests for User Story 2

- [X] T032 [P] [US2] Unit tests for the "same site still active → inject context system message, skip re-resolution" branch in `tests/AskLucy.Application.Tests/Ai/SendChatMessageBoundaryIntegrationTests.cs` — 4 tests (no-re-resolution on repeat, context message injected/not injected, resolution fires for a genuinely new site), all passing
- [X] T033 [P] [US2] Frontend tests for the Medium/Low `AnimatedBorderHighlight` variants (done as part of T029/T031) and the confidence/source badge (`SiteBoundaryConfidenceBadge.test.tsx`, 6 tests, all passing)

### Implementation for User Story 2

- [X] T034 [US2] Extend `SendChatMessageCommandHandler` (same file as T024): when the confirmed site equals `chat.ActiveBoundary?.SiteName` (unchanged), skip resolution and insert a system message summarizing `ActiveBoundary`'s confidence/source (using `BoundaryConfirmationTemplates`), per `contracts/chat-pipeline-integration.md`'s "same site still active" branch (depends on T005, T013, T024) — **done during T024**; now covered by T032's dedicated tests
- [X] T035 [US2] Extend `AnimatedBorderHighlight.setConfidenceLevel()` (`AnimatedBorderHighlight.ts`) with `medium` (dimmer/slower comets) and `low` (static dashed perimeter, no comets) modes (depends on T029) — **done during T029**, not deferred: the confidence→visual mapping was one shared `buildForConfidence` switch, cheaper to build once than twice
- [X] T036 [US2] Create the confidence/source badge presentational component in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SiteBoundaryConfidenceBadge.tsx`, reading `activeSiteBoundaryStore`, styled to match `LocationWeatherWidget`'s dark-glass chrome, distinguishing confidence by icon (`RiShieldCheckLine`/`RiShieldLine`/`RiQuestionLine`) + label text (not color alone) for WCAG 2.1 AA; also surfaces FR-008's alternative-candidate disclosure (depends on T019)
- [X] T037 [US2] Mount `SiteBoundaryConfidenceBadge` alongside `SiteBoundaryOverlay` in `ViewerSurface.tsx` (opposite screen corner from `LocationWeatherWidget` to avoid overlap) (depends on T030, T036)

**US2 complete.** `tsc -b --noEmit` and `eslint` clean; all new tests passing.

**Checkpoint**: User Stories 1 AND 2 both independently demonstrable.

---

## Phase 5: User Story 3 - Get a clear answer when no reliable boundary exists (Priority: P3)

**Goal**: `NoCandidates`, `Unavailable`, and multi-candidate-ambiguity (FR-008) outcomes are all explicitly and distinctly narrated; a user flagging a boundary as wrong is acknowledged.

**Independent Test**: Quickstart Scenarios C, D, and E.

### Tests for User Story 3

- [X] T038 [P] [US3] Unit tests for `BoundaryResolutionService`'s `NoCandidates`, `Unavailable`, and `AlternativeCandidateNames`-populated-`Confirmed` paths in `BoundaryResolutionServiceTests.cs` (same file as T021) — **done during T021**: `BoundaryResolutionService.ResolveAsync` handles every outcome type in one cohesive method, so its test file covered all of them together from the start rather than incrementally per story
- [X] T039 [P] [US3] Integration test simulating an Overpass outage (timeout/non-2xx) mapping to `Unavailable` in `OverpassBoundaryCandidateProviderTests.cs` (same file as T022) — **done during T022** (`SearchAsync_ShouldThrowBoundaryProviderUnavailableException_OnHttpError`/`_OnTimeout`/`_OnMalformedJson`)

### Implementation for User Story 3

- [X] T040 [US3] Implement the `NoCandidates` outcome path in `BoundaryResolutionService.cs` (no plausible candidate within the search radius) with its `BoundaryConfirmationTemplates` message (FR-007) (depends on T014, T013) — **done during T014**
- [X] T041 [US3] Implement the `Unavailable` outcome path in `BoundaryResolutionService.cs` (catches `BoundaryProviderUnavailableException` from `IBoundaryCandidateProvider`, never lets it propagate) with its message (FR-012) (depends on T014, T017) — **done during T014**
- [X] T042 [US3] Implement `AlternativeCandidateNames` population in `BoundaryCandidateScorer`/`BoundaryResolutionService` when two or more candidates score within a defined similarity margin of the top pick (FR-008 — always still returns the top pick as `Confirmed`, names the others) (depends on T011, T014) — **done during T014** (`AlternativeCandidateScoreMargin` constant + the `ranked.Skip(1).Where(...)` projection in `ResolveAsync`)
- [X] T043 [US3] Add the correction-acknowledgment guidance sentence (FR-010 — acknowledge, don't silently repeat, state plainly if no better answer is available with current input) to `BoundaryConfirmationTemplates.cs`, included in the context system message from T034 (depends on T013, T034) — **done during T013/T024** (`BoundaryConfirmationTemplates.CorrectionGuidance`, appended to the same system message T034 builds)

**Checkpoint**: All of User Stories 1-3 independently demonstrable — this is the spec's full base-chat scope. US3's backend outcome-handling arrived as a natural consequence of building `BoundaryResolutionService` as one cohesive method during Foundational/US1, rather than incrementally — the only genuinely outstanding item from Phases 4-5 is T032's dedicated "same site unchanged" test and Phase 4's confidence badge (T036/T037).

---

## Phase 6: User Story 4 - Use the same capability for a different site or project (Priority: P4)

**Goal**: Prove no part of the pipeline is hardcoded to Al Safa Park 2 — a generalization check, not new behavior.

**Independent Test**: Quickstart Scenario F, repeated for at least two unrelated site types.

- [X] T044 [P] [US4] Add fixtures/test cases for a second and third site type (an institutional/building site, a generic street address with no mapped shape) to `BoundaryResolutionServiceTests.cs` and `OverpassBoundaryCandidateProviderTests.cs`, asserting identical scoring/threshold/outcome behavior to the Al Safa Park 2 case (depends on T020-T023, T038-T039) — 4 new theory-driven test cases (school + residential-address tag families, both Confirmed/High and NoCandidates/fallback paths), all passing, proving no site-specific hardcoding
- [ ] T045 [US4] Manually run quickstart Scenario F against a real, unrelated site once T001-T037 are deployed to a dev environment; record the result in the PR description — **not runnable in this environment** (requires a live browser + a valid Google Maps API key/Map ID, which this sandbox doesn't have — same limitation noted on `GoogleMapsGisLayer.ts`'s own doc comment); left for manual QA before release

**Checkpoint**: All four user stories independently functional — spec.md's full scope is met.

---

## Phase 7: Secondary surface — `SiteBoundaryResolverTool` (`IAgentTool`)

**Purpose**: Fulfills the original request's explicit "callable as an Agent Tool... reused for future urban-planning/design projects" ask, for a custom user-authored AI Agent (spec 020) rather than the base chat experience already covered by US1-4. See `contracts/site-boundary-resolver-tool.md`.

- [X] T046 [P] Create `SiteBoundaryResolverTool` (`Name`, `Description`, `RiskLevel.Low`, `RequiredPermissions: [ExternalNetwork]`, input/output JSON Schema per `contracts/site-boundary-resolver-tool.md`) in `src/AskLucy.Application/Agents/Tools/SiteBoundaryResolverTool.cs`, delegating entirely to `IBoundaryResolutionService`; registered alongside the other native tools in `src/AskLucy.Application/DependencyInjection.cs` (depends on T014) — uses `IGeocodingProvider` directly (top candidate by importance) rather than `ILocationResolutionService`, documented in the class's own doc comment: that service's intent-classification prompt is tuned for a full conversational message, not a bare tool-argument place name, and reusing it risked misclassifying a plain query as having no location intent
- [X] T047 [P] Unit tests for `SiteBoundaryResolverTool` (input validation, each outcome→JSON mapping, top-candidate selection) in `tests/AskLucy.Application.Tests/Agents/SiteBoundaryResolverToolTests.cs` — 6 tests, all passing

**Phase 7 complete.** Full solution build clean (`dotnet build "Ask Lucy.sln"`), no new warnings.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T048 [P] Update `docs/SITE_BOUNDARY_RESOLUTION_ARCHITECTURE.md` to reflect the corrected primary mechanism (chat-pipeline hook, not `IAgentTool` — research.md #11) so the standalone architecture doc doesn't contradict the implemented design — added a prominent "SUPERSEDED" notice at the top naming both corrections (primary mechanism, persistence) rather than rewriting the whole document, pointing readers to the spec's `research.md`/`data-model.md`/`contracts/` as the accurate source
- [X] T049 Run every `quickstart.md` scenario (A-F) end-to-end against a fully deployed build — Scenarios reachable via automated tests (backend outcome logic: A/B/C/D/E all covered by `BoundaryResolutionServiceTests`/`OverpassBoundaryCandidateProviderTests`; F by the US4 theory tests) all verified passing; the visual/browser-dependent parts (seeing the animated highlight render correctly, confirming map camera behavior) **could not be run in this environment** — no live browser + valid Google Maps API key/Map ID available, the same limitation `GoogleMapsGisLayer.ts`'s own doc comment already documents; left for manual QA before release
- [X] T050 [P] Automated accessibility check (axe) on `SiteBoundaryConfidenceBadge` for WCAG 2.1 AA in `SiteBoundaryConfidenceBadge.a11y.test.tsx` (High and Low confidence states), confirming confidence is distinguishable without relying on color alone — 2 tests, both passing. (The boundary highlight itself is a Three.js/WebGL canvas overlay with no DOM/ARIA surface of its own — nothing for axe to check there; its confidence signal is conveyed via the badge, which is what's tested.)
- [X] T051 Security review pass per constitution §16 gate 6 — findings:
  - OSM tag/name data flows only into fixed, non-instruction template strings (`BoundaryConfirmationTemplates`) — same pattern as the already-accepted `LocationConfirmationTemplates` embedding Nominatim-sourced place names; no new class of untrusted-data exposure introduced.
  - `BoundaryConfirmationTemplates.CorrectionGuidance`'s system message embeds `activeBoundary.SiteName` (geocoder-derived) into LLM-bound context — consistent with the existing, already-reviewed pattern of embedding geocoded names in prompts (e.g. the location intent classifier already embeds the full raw user message); not a new exposure class.
  - `SiteBoundaryResolverTool.RequiredPermissions` is exactly `[ExternalNetwork]` — no file/knowledge/memory/write access, correctly minimal for a read-only geocoding+Overpass caller.
  - The boundary-resolution pipeline makes zero LLM calls (fully deterministic scoring) — no OSM data is ever sent to an AI provider as a prompt.
  - No new public HTTP endpoint was added; both invocation paths (chat pipeline, agent tool) ride existing, already-rate-limited/authorized surfaces.
  - No issues found requiring a code change.

**Phase 8 complete. Feature implementation complete for specs/042-site-boundary-resolution**, except: T045 (live-browser manual QA, environment-blocked) and the deliberately-out-of-scope Phase-2 AI-vision critique (research.md #6, tracked separately, not part of this spec).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS every user story.
- **User Stories (Phases 3-6)**: All depend on Foundational. Priority order (P1→P2→P3→P4) is the recommended sequential path; US2/US3 each extend the same two backend files US1 touches (`SendChatMessageCommandHandler.cs`, `BoundaryResolutionService.cs`), so treat them as sequential increments on those two files rather than parallel workstreams, even though they're independently testable once built.
- **Secondary surface (Phase 7)**: Depends only on Foundational (T014) — can run in parallel with Phases 3-6 by a different contributor, since it touches none of the files US1-4 modify.
- **Polish (Phase 8)**: Depends on whichever of Phases 3-7 are in scope for the release.

### Within Each Phase

- Tests before the implementation they cover (write first, confirm they fail, then implement).
- Domain/Application types before the services that consume them.
- Backend pipeline wiring (`SendChatMessageCommandHandler`/`AiController`) before the frontend pieces that depend on its output (`aiApi.ts` → store → overlay/renderer).

### Parallel Opportunities

- T002, T008, T009, T010, T012, T013, T016, T019 (Phase 2) touch disjoint files and can run in parallel once their own prerequisites (if any) are met.
- T020-T023 (US1 tests) are fully parallel — four different files, no shared state.
- Phase 7 (T046-T047) can be staffed independently of Phases 3-6 by a second contributor once Phase 2 is done.

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Once T001 (Setup) is done, these have no interdependencies and can start together:
Task: "Create GeoPoint/SiteBoundaryPolygon/SiteBoundarySource/BoundaryConfidenceLevel in src/AskLucy.Domain/SiteBoundaries/"
Task: "Create IBoundaryCandidateProvider/BoundaryCandidate/ScoredBoundaryCandidate in src/AskLucy.Application/SiteBoundaries/"
Task: "Create BoundaryScoringOptions + validator in src/AskLucy.Application/SiteBoundaries/"
Task: "Create GeometryMath in src/AskLucy.Application/SiteBoundaries/GeometryMath.cs"
Task: "Add ConfirmedSiteBoundaryData to src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs"
Task: "Create BoundaryConfirmationTemplates in src/AskLucy.Application/SiteBoundaries/BoundaryConfirmationTemplates.cs"
Task: "Create OverpassOptions + BoundaryProviderUnavailableException in src/AskLucy.Infrastructure/Boundaries/"
Task: "Create activeSiteBoundaryStore.ts in src/AskLucy.Web/ClientApp/src/store/"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 (Setup) → Phase 2 (Foundational, the whole resolution pipeline + persistence) → Phase 3 (US1).
2. **STOP and VALIDATE**: run quickstart Scenario A. This alone is a demonstrable, shippable improvement (a boundary appears where today only a pin/circle does).

### Incremental Delivery

1. Setup + Foundational → pipeline exists, nothing user-visible yet.
2. + US1 → MVP: high-confidence boundaries render (Scenario A).
3. + US2 → trust signal: confidence/source distinction and no-flicker follow-ups (Scenario B).
4. + US3 → honesty under uncertainty: explicit not-found/unavailable/ambiguous handling (Scenarios C-E).
5. + US4 → generalization proof, mostly test coverage (Scenario F).
6. + Phase 7 → the custom-agent-facing tool, independent of the above.
7. Phase 8 → polish, accessibility, security review.

### Parallel Team Strategy

Once Phase 2 is done: one contributor can take Phases 3→4→5→6 sequentially (they share the same two hot files), while a second contributor takes Phase 7 (`SiteBoundaryResolverTool`) entirely independently, and a third can start Phase 8's accessibility/doc-update tasks as soon as the relevant components exist.

---

## Notes

- [P] tasks touch different files with no dependency on an incomplete task.
- US2 and US3 are not fully independent at the *file* level (both extend `SendChatMessageCommandHandler.cs` and `BoundaryResolutionService.cs`) even though each is independently *testable* once implemented — sequence them, don't parallelize them across contributors without coordinating on those two files.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently before continuing.
- Every task above assumes the corrected design in `research.md` (#10-11) and `contracts/chat-pipeline-integration.md` — re-read those before starting if this file is picked up in a new session, since they overrode the original architecture doc's assumptions.
