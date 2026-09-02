# Phase 0 Research: Site Boundary Resolution

All unknowns from the Technical Context were resolvable from the existing codebase, the reference notebook/shader, the architecture proposal (`docs/SITE_BOUNDARY_RESOLUTION_ARCHITECTURE.md`), and the spec's clarification session — no `NEEDS CLARIFICATION` markers remain. Findings below are recorded as Decision/Rationale/Alternatives per the standard research format.

## 1. Point resolution: reuse vs. reimplement

**Decision**: Reuse `ILocationResolutionService` (spec 037) as-is for name/address → coordinates; boundary resolution starts from its output.

**Rationale**: It already does intent classification, geocoding (`IGeocodingProvider`, Google/Nominatim), a confidence/dominance-margin algorithm, and WGS-84 validation. Duplicating any of that would violate constitution §III (DRY) and §18 ("never duplicate business logic that already exists elsewhere — find and reuse or extend it instead").

**Alternatives considered**: A standalone geocoding call inside the new module — rejected, pure duplication with no benefit; would also risk the exact bug the notebook itself documents (a free-tier geocoder resolving a name differently than an already-verified point).

## 2. Boundary candidate source

**Decision**: OSM Overpass API (free, no key) as the sole `IBoundaryCandidateProvider` implementation for v1, behind the interface.

**Rationale**: Matches the notebook's own "source-first" default and its `SOURCE_RELIABILITY` table (OSM = 0.80, second only to hypothetical government/cadastral data). Free, no key management, same data family already trusted by `NominatimGeocodingProvider`. The provider abstraction (`IBoundaryCandidateProvider`) means a future authoritative source is an additive `Infrastructure` implementation, not a rewrite (constitution: Infrastructure isolation).

**Alternatives considered**: Google Maps "place details" geometry — inconsistent/sparse polygon coverage outside curated place types, and would introduce a second paid-API dependency for a feature spec explicitly says must stay available to every tier (FR-014) with no new cost surface. Deferred, not rejected outright — a `GovernmentCadastralBoundaryProvider` remains a documented future `SiteBoundarySource` value.

## 3. Scoring approach

**Decision**: Deterministic, config-bound weighted scorer — direct port of the notebook's `SITE_BOUNDARY_CONFIG`/`score_candidate` (source reliability, name match, geometry plausibility, center proximity, land-use tag agreement; weights sum to 1.0, validated).

**Rationale**: Explainable and testable — every score decomposes into named factors that can be asserted in unit tests and narrated to the user (FR-005). An ML/black-box scorer would violate the spec's explicit confidence/source transparency requirements (FR-004, FR-005) and the constitution's "no silent failures"/transparency values.

**Alternatives considered**: Delegating ranking entirely to the (optional) AI vision step — rejected as the primary mechanism; the notebook itself treats AI as a critique/tie-breaker over deterministic candidates, never the sole judge, precisely because it must degrade gracefully when unavailable (FR-007/FR-012 require exactly this).

## 4. Confidence classification thresholds

**Decision**: Three-level classification (High ≥ 0.85, Medium ≥ 0.65, Low below), same thresholds as the notebook, bound via `IOptions<BoundaryScoringOptions>` (not hardcoded).

**Rationale**: FR-004 requires at minimum High/Medium/Low; reusing the notebook's already-considered thresholds avoids inventing new, untested cutoffs. Configurability lets a future project/typology retune without a code change (constitution §VII, Convention over Configuration — configuration reserved for values that genuinely vary).

**Alternatives considered**: A raw numeric confidence score shown to the user instead of a label — rejected; spec FR-004 explicitly calls for a level, and a bare 0–1 float is harder for a non-technical user to act on than "Medium."

## 5. Multiple-similarly-plausible-candidates behavior

**Decision** (from spec clarification): Always render the top-scored candidate by default; explicitly name other similarly-plausible candidates in the accompanying explanation. No hard gate, no simultaneous multi-candidate overlay.

**Rationale**: Keeps the feature useful in the common case while still satisfying FR-008's disclosure requirement; avoids introducing a new interactive "pick one" conversational turn not present in any user story, per the clarification session's explicit choice.

**Alternatives considered**: Block rendering until the user disambiguates (rejected — adds friction not requested); render all plausible candidates simultaneously (rejected for v1 — more complex viewer/UX work than the spec's priority order justifies; could be revisited if User Story 3's acceptance bar proves insufficient in practice).

## 6. AI-vision critique phasing

**Decision**: Not built in this feature. A narrow, optional `IBoundaryVisionAnalyzer` interface is documented as the Phase-2 seam (architecture doc §7.2) but is out of scope for `042`.

**Rationale**: `IAIProvider`/`ChatMessage` is text-only across all four providers today (confirmed by codebase search — no call site anywhere sends image bytes). Widening the *core* chat abstraction to be multimodal for every provider is separate, larger work with its own blast radius, and the spec's Assumptions explicitly say AI vision "may improve confidence... but is not required for this feature to deliver its core value."

**Alternatives considered**: Widening `IAIProvider` now to unblock vision — rejected as scope creep against YAGNI (constitution §III); would touch all four provider implementations and every existing `ChatAsync` call site for a capability not required to satisfy any FR in this spec.

## 7. Geometry representation & math

**Decision**: A plain WGS84 vertex-ring record (`SiteBoundaryPolygon` → `IReadOnlyList<GeoPoint>`) plus a small static `GeometryMath` helper (shoelace-formula area over an equirectangular local-meters projection around the centroid, centroid, bbox, distance). No new NuGet package.

**Rationale**: Confirmed no GIS/geometry library (e.g., NetTopologySuite) exists anywhere in the solution today. Constitution §III forbids unnecessary dependencies; this feature's actual geometry needs (score plausibility, render vertices) don't require survey-grade precision or a full computational-geometry library — the notebook made the identical trade-off for its own non-geopandas fallback path.

**Alternatives considered**: Adding NetTopologySuite — rejected for v1 as disproportionate to current needs; revisit if a future feature (e.g., manual polygon editing, tracked separately per project memory) needs real topological operations (intersection, buffering, snapping) that hand-rolled math can't reasonably cover.

## 8. Viewer rendering integration point

**Decision**: Extend `GoogleMapsGisLayer.ts`'s existing `WebGLOverlayView`-owned `THREE.Scene`, via a new `SiteBoundaryRenderer.ts` module that projects each polygon vertex through the transformer already used for camera positioning (`transformer.fromLatLngAltitude`).

**Rationale**: This is the only place in the viewer that already does lat/lon → 3D-scene-space conversion and owns a live Three.js scene tied to the real map. The existing `createOverlay`/`OverlayInput` API (`viewer/api/layers.ts`) was confirmed to be pure bookkeeping — nothing renders it — so building on it would require adding the actual rendering logic anyway, with no benefit over using the primitive that already works.

**Alternatives considered**: A `google.maps.Polygon` (Maps JS's own vector overlay) — simpler for a static line, but can't host the animated additive-glow shader material from `BORDER_HIGHLIGHT.html`, which needs a real Three.js `Line`/`ShaderMaterial`. Rejected because the animated highlight is an explicit requirement (FR-002, "clearly recognizable").

**Important implementation detail confirmed by code inspection**: the `transformer` object that converts lat/lng/altitude to scene-space is only available as an argument inside `GoogleMapsGisLayer.ts`'s own `onDraw` callback (`overlay.onDraw = ({ transformer }) => {...}`, line 136) — it is per-frame and **not** exposed on `GoogleMapsGisLayerHandle` today. `SiteBoundaryRenderer.ts` therefore cannot independently project coordinates from outside that file. The plan is a small, additive change to `GoogleMapsGisLayerHandle`: a new `setSiteBoundary(polygon: SiteBoundaryPolygon | null)` method. Internally, `GoogleMapsGisLayer.ts` keeps a `THREE.Group` for the boundary in its scene and, inside its existing `onDraw`, calls `transformer.fromLatLngAltitude(centroid)` each frame to position that group's matrix — exactly the same pattern already used to position the camera. `SiteBoundaryRenderer.ts` owns *what* geometry goes inside that group (the perimeter line + animated comet segments, built from `GeometryMath`'s local-meters offsets from the polygon centroid — the same helper used for area scoring, reused here for vertex placement); `GoogleMapsGisLayer.ts` owns *where* the group sits in world space, preserving its existing invariant that the transformer never leaks outside the file that owns the WebGLOverlayView lifecycle.

## 9. Border-highlight animation technique

**Decision**: Shader-only additive glow — a static dim perimeter `THREE.Line` plus animated arc-length-parameterized "comet" segments using `AdditiveBlending` and the head-brightening intensity curve from `BORDER_HIGHLIGHT.html`. **No `EffectComposer`/`UnrealBloomPass`.**

**Rationale**: Confirmed by direct code inspection that `GoogleMapsGisLayer.ts` renders via a plain `renderer.render(scene, camera)` with no post-processing pipeline today. The only bloom pipeline anywhere in the codebase, `features/chat/scene/ParticleSphereBloom.tsx`, is react-three-fiber's `@react-three/postprocessing` `SelectiveBloom` running inside an R3F-managed canvas for the unrelated chat-avatar particle sphere — a different rendering stack, not reachable from the GIS layer's imperative, Google-driven render loop. Adding a full `EffectComposer` to a live, shared GL context (shared with Google's own map tiles, driven by Google's per-frame `onDraw`) would be an invasive, unbudgeted change to a performance-sensitive path. Most of the reference file's visual impact already comes from the shader's own additive blending and head-brightening math, not the bloom pass layered on top of it.

**Alternatives considered**: Adding `EffectComposer`/`UnrealBloomPass` to `GoogleMapsGisLayer` — deferred, not rejected outright; documented as a future, deliberately-scoped polish pass if shader-only glow proves insufficient once built and visually reviewed.

## 10. Persistence — corrected after reading the actual `Locations` wiring

**Decision (revised)**: No *history* table (unchanged from the original call) — but FR-009 ("without forcing a fresh resolution") requires the current boundary to survive across chat turns, and the codebase's own precedent for exactly this — `ActiveSiteLocation` — turns out to be a real, persisted, single-slot owned value on `UserChat` (`UserChat.SetActiveLocation`/`ActiveLocation`, `builder.OwnsOne(c => c.ActiveLocation, ...)` in `UserChatConfiguration.cs`, added via migration `20260823190247_AddActiveLocationToUserChat`), not an in-memory-only value. Mirroring that precedent exactly, this feature adds one small, additive `ActiveSiteBoundary` owned type + a new migration on the existing `UserChats` table — **not** a new table, **not** a history/audit log, just the same single-current-value pattern `Locations` already established.

**Correction to the original research pass**: the initial "no persistence" call was reasoned by analogy to `ActiveSiteLocation`'s *lifetime* without actually reading how `ActiveSiteLocation` is implemented — it assumed "conversation-scoped" meant "not in the database," but `UserChat` rows are themselves permanently persisted (every chat is stored, per the platform's baseline), so "the same lifetime as `ActiveSiteLocation`" necessarily means "a persisted column on `UserChat`," not an in-memory value. This is exactly the kind of assumption the project's memory-system discipline requires verifying before building on it — caught here, before task generation, by reading `UserChat.cs`/`UserChatConfiguration.cs`/`RecordActiveLocationCommandHandler.cs` directly rather than trusting the earlier by-analogy claim.

**Storage shape**: `ActiveSiteBoundary` (new `Domain/Chats/ActiveSiteBoundary.cs`, alongside `ActiveSiteLocation.cs`) — `SiteName`, `CentroidLatitude`, `CentroidLongitude`, `AreaSquareMeters`, `Confidence`, `ConfidenceLevel` (stored as string), `Source` (stored as string), `SourceDetail`, and `PolygonJson` (the exterior ring serialized to a JSON string). All other `ActiveSiteLocation` fields map to flat scalar columns via `OwnsOne(...).Property(...).HasColumnName(...)`, exactly like `ActiveLocation`'s configuration; only the polygon ring needs a `HasConversion` value converter (JSON string ⇄ `IReadOnlyList<GeoPoint>`) since EF's `OwnsOne` flat-column style can't represent a variable-length list as scalar columns — the converter lives entirely in `Infrastructure`/`Persistence` configuration, so `Domain` stays free of any JSON-serialization reference (constitution §3 Domain purity).

**Alternatives considered**: An EF Core `.OwnsOne(...).ToJson()` mapping for the whole `ActiveSiteBoundary` (stores it as one JSON column) — rejected in favor of matching `ActiveLocation`'s established flat-per-field convention as closely as possible (constitution §VII, Convention over Configuration), reserving JSON-conversion for only the one field (the polygon ring) that genuinely can't be flattened. A separate `SiteBoundaryRecord` history table — still explicitly deferred (unchanged), since no FR requires cross-session history, only same-conversation persistence.

## 11. Primary invocation mechanism — corrected: not `IAgentTool`

**Decision (revised)**: The primary path satisfying User Stories 1–3 is **not** the `SiteBoundaryResolverTool` `IAgentTool` (as the original spec input's wording and the initial architecture doc both assumed) — it is a new deterministic pipeline stage inside `SendChatMessageCommandHandler`, added the same way `ILocationResolutionService` already is: launched concurrently with the model's text stream, awaited against a time budget, its confirmation sentence appended to the stream, and its confirmed result carried on the final `ChatStreamChunk` for the controller to persist (`RecordActiveSiteBoundaryCommand`, mirroring `RecordActiveLocationCommand`) and forward to the frontend as a distinguishable SSE trailing event (`__SITE_BOUNDARY__`, mirroring `__LOCATION__`).

**Why this was wrong initially**: `IAgentTool` is the mechanism through which a *user-authored AI Agent* (spec 020's Agent Builder/Runtime) calls a capability the model decides to invoke mid-reasoning — `DocumentSearchTool`, `KnowledgeSearchTool`, etc. Base "Lucy" chat (the experience every user story in this spec actually describes — "a user asks Lucy about a specific site") does not go through that framework at all for its existing geo-awareness feature; `LocationResolutionService` is invoked directly and deterministically from `SendChatMessageCommandHandler`, with its own lightweight one-shot intent-classification call, entirely separate from the Agent Runtime. This was missed in the original exploration (which correctly established "native capabilities are exposed via `IAgentTool`" as a general statement about the Agent framework, but didn't surface that this specific category of "mid-chat spatial awareness" feature bypasses that framework by established precedent). Building the boundary capability as *only* an `IAgentTool` would have meant it never actually fires during a normal conversation — a real, load-bearing correction caught before implementation, not after.

**Trigger logic**: Boundary resolution runs (or refreshes) only when this turn's confirmed location (from `LocationResolutionOutcome.ConfirmedLocation`, either a new query or a back-reference) names a *different* site than `chat.ActiveBoundary?.SiteName` — i.e., only on an actual site change, never re-running for a repeat reference to the same site (this is what makes FR-009's "without forcing a fresh resolution" concrete: same site referenced again ⇒ no new Overpass call, no new scoring, the existing persisted `ActiveBoundary` is simply still there). When the site does change, no separate LLM intent-classification call is added for boundary resolution itself — it piggybacks entirely on location resolution's already-run classification (FR-003: boundary resolution is "an additional step on top of an already-resolved location," never its own parallel NLU pass).

**Follow-up questions about an unchanged boundary** (User Story 2 acceptance scenario 3 — "how sure are you," "where did this come from") are answered by the model directly from conversation context: whenever `chat.ActiveBoundary is not null`, a short system message summarizing its confidence level and source is injected before the model call — the same pattern already used for the zoom-intent guidance message injected when `activeLocation is not null` (`SendChatMessageCommandHandler.cs` lines 111-118). No new tool call, no re-resolution.

**Where `SiteBoundaryResolverTool : IAgentTool` still fits**: kept, but demoted to a secondary, optional invocation surface — for a user-authored custom AI Agent (e.g. a future "Urban Planning Agent" built via spec 020's Agent Builder) that wants to call boundary resolution explicitly as one of its tools, outside a normal Lucy chat turn. It wraps the exact same `IBoundaryResolutionService` the pipeline hook calls — zero duplicated logic either way (constitution §III DRY). This is what actually satisfies the original ask's "callable as an Agent Tool... reused for future urban-planning/design projects" framing once read as being about custom agents, not about how the base chat experience works.

**Alternatives considered**: Building only the `IAgentTool` and skipping the pipeline hook — rejected, since it would never fire for the plain-chat experience every user story describes (the actual acceptance bar). Building only the pipeline hook and skipping the `IAgentTool` — rejected, since it would leave no way for a custom-built agent to invoke this capability directly, which the original request explicitly asked for.

## 11. Access / tier gating

**Decision** (from spec clarification, FR-014): No subscription-tier gating — available to every authenticated user, same as the existing location lookup.

**Rationale**: The capability is a natural extension of an already-ungated feature; gating it would be an unrequested scope/business-model expansion (YAGNI), and no cost driver (paid API, heavy compute) justifies restricting it — Overpass is free and the scoring is cheap local computation.

**Alternatives considered**: Professional/Enterprise-only gating, or stricter Free-tier rate limits — both explicitly declined during clarification.

## 12. Testing external HTTP dependency

**Decision**: `OverpassBoundaryCandidateProvider` is unit/integration-tested against recorded/replayed HTTP responses (constitution §10's stated pattern for provider adapters), following exactly the shape `NominatimGeocodingProvider` already uses — named `HttpClient` (`"Overpass"`), `IOptions<OverpassOptions>` with `ValidateOnStart`, a dedicated `BoundaryProviderUnavailableException` mapped from `HttpRequestException`/`TaskCanceledException`/`JsonException`.

**Rationale**: Matches existing, reviewed conventions exactly (`NominatimGeocodingProvider.cs`) — no new testing pattern to invent or justify.

**Alternatives considered**: Hitting the live Overpass API in CI — rejected; flaky, slow, and against constitution §10's requirement that integration tests run reliably in CI.
