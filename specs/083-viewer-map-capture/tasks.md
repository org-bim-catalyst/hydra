# Tasks: Viewer Map Capture (server-to-server)

**Input**: Design documents from `/specs/083-viewer-map-capture/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/map-capture-api.md](contracts/map-capture-api.md), [quickstart.md](quickstart.md)

**Tests**: Included — the plan's Constitution Check (Section 10) and the repo workflow require unit, infrastructure, and Web integration tests for every feature.

**Organization**: One P1 user story. Every task below is required for that story; Setup and Foundational phases are prerequisites, not separate deliverables.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1)
- Include exact file paths in descriptions

## Path Conventions

Existing Clean Architecture layout at the repository root: `src/AskLucy.Application`, `src/AskLucy.Infrastructure`, `src/AskLucy.Web`, `tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuration and options shared by every layer

- [X] T001 Add a `MapCapture` section with non-secret defaults to `src/AskLucy.Web/appsettings.json` (`DefaultViewSizeMetres: 1000`, `MinViewSizeMetres: 100`, `MaxViewSizeMetres: 20000`, `MaxImageBytes: 10485760`, `RateLimitPerMinute: 60`) and an empty `MapCapture:ApiKeys` array to `src/AskLucy.Web/appsettings.Production.json.example`. Never put a key value in a tracked file.
- [X] T002 [P] Create `src/AskLucy.Application/MapCaptures/MapCaptureOptions.cs` with `public const string SectionName = "MapCapture"`, an `ApiKeys` list of `MapCaptureApiKey(string Name, string Key)`, and `DefaultViewSizeMetres`, `MinViewSizeMetres`, `MaxViewSizeMetres`, `MaxImageBytes`, `RateLimitPerMinute` — each with the default from T001. Place it in Application (not Infrastructure as plan.md lists) so the validator, handler, and renderer share one type; Infrastructure already references Application.
- [X] T003 Register the options in `src/AskLucy.Application/DependencyInjection.cs` next to the existing `AddOptions<LocationResolutionOptions>()` block: `.AddOptions<MapCaptureOptions>().BindConfiguration(MapCaptureOptions.SectionName).Validate(o => o.MinViewSizeMetres > 0 && o.MaxViewSizeMetres >= o.MinViewSizeMetres && o.MaxImageBytes > 0 && o.RateLimitPerMinute > 0).ValidateOnStart()`. Validate only the numeric bounds — each has a code default, so startup cannot fail. `ApiKeys` stays optional and is **not** part of the validation, so a deployment with no service key returns `401` at request time instead of failing host startup. (Constitution §4 requires startup validation; the project-memory `IOptions` crash applies to *required* options with no default, not to these.)

**Checkpoint**: Options bind, host starts with and without keys.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared request/result types every implementation task compiles against

- [X] T004 [P] Create `src/AskLucy.Application/MapCaptures/MapCaptureMode.cs` — `Roadmap`, `Satellite`, `Hybrid`, `BuildingsOnly`, each with `[JsonStringEnumMemberName]` values `roadmap`, `satellite`, `hybrid`, `buildings-only`.
- [X] T005 [P] Create `src/AskLucy.Application/MapCaptures/MapCaptureOverlay.cs` — `BuildingFootprints` and `SiteBoundary` with wire values `building-footprints` and `site-boundary` (`SiteBoundary` is parsed so the failure can name it, then rejected as `OverlayUnavailable`).
- [X] T006 [P] Create `src/AskLucy.Application/MapCaptures/MapCaptureFailureReason.cs` with exactly the enum from [data-model.md](data-model.md#mapcapturefailurereason): `LocationNotFound`, `GeocodingUnavailable`, `ModeUnavailable`, `OverlayUnavailable`, `ProviderUnavailable`, `CropTooSmall`, `ImageTooLarge`.
- [X] T007 [P] Create `src/AskLucy.Application/MapCaptures/MapImageResult.cs` — `static MapImageResult Success(byte[] png, int width, int height)` and `static MapImageResult Failure(MapCaptureFailureReason reason, string detail)`, with an `IsSuccess`-style discriminant the controller can switch on.
- [X] T008 Create `src/AskLucy.Application/MapCaptures/MapImageRequest.cs` (`double Latitude`, `double Longitude`, `string LocationName`, `MapCaptureMode Mode`, `IReadOnlyList<MapCaptureOverlay> Overlays`, `int ViewSizeMetres`, `string CallerName`) and `src/AskLucy.Application/MapCaptures/IMapImageRenderer.cs` — `Task<MapImageResult> RenderAsync(MapImageRequest request, CancellationToken cancellationToken = default)`. Depends on T004–T007.

**Checkpoint**: Application types compile; no behaviour yet.

---

## Phase 3: User Story 1 — Server-to-server map capture (Priority: P1) 🎯 MVP

**Goal**: Trusted services call one endpoint with a location and receive a cropped PNG with no viewer, no persistence, and no silent failures.

**Independent Test**: With no viewer open and no database, `curl` the endpoint with a place name and no other parameters → `200 image/png`, unlabelled satellite base map centred on the geocoded point, 1252×1252; repeated with coordinates + `mode=hybrid` → hybrid render; nothing written to disk or the database.

### Tests for User Story 1 ⚠️ (write first; they must fail before implementation)

- [X] T009 [P] [US1] `tests/AskLucy.Application.Tests/MapCaptures/CaptureMapQueryHandlerTests.cs` — coordinates beat `Location` (geocoder never called); place name uses the highest-`Importance` candidate; zero candidates → `LocationNotFound`; `GeocodingProviderUnavailableException` → `GeocodingUnavailable`; no mode/overlays → `Satellite` + empty list; handler passes `CallerName` to the renderer; renderer failure is returned unchanged (no exception, no partial image); the handler's constructor takes only `IGeocodingProvider`, `IMapImageRenderer`, `IOptions<MapCaptureOptions>`, and a logger — no storage port, confirming FR-014/SC-004. Use NSubstitute for `IGeocodingProvider`/`IMapImageRenderer` and `TestContext.Current.CancellationToken` (xUnit v3).
- [X] T010 [P] [US1] `tests/AskLucy.Application.Tests/MapCaptures/CaptureMapQueryValidatorTests.cs` — missing both location forms fails; only one of `latitude`/`longitude` fails; out-of-range coordinate fails; `ViewSizeMetres` outside `MapCaptureOptions` bounds fails; undefined enum values fail.
- [X] T011 [P] [US1] `tests/AskLucy.Infrastructure.Tests/MapCaptures/GoogleStaticMapImageRendererTests.cs` — with the shared `StubHttpMessageHandler` (in `tests/AskLucy.Infrastructure.Tests/Ai/`) and a substituted `IHttpClientFactory` (mirror `GoogleSatelliteImageProviderTests.CreateProvider`): `mode` maps to the right `maptype`; `satellite` default and `format=png`; `buildings-only` adds `landscape.man_made` style rules; request is `size=640x640&scale=2`; a non-image or non-success response returns `ProviderUnavailable`; no Google key returns `ModeUnavailable`.
- [X] T012 [P] [US1] `tests/AskLucy.Infrastructure.Tests/MapCaptures/MapImageComposerTests.cs` — build a 1280×1280 PNG with Magick.NET in the test, assert the output is exactly 1252×1252 and that content 14 px from each edge is gone; assert an image whose encoded PNG exceeds `MaxImageBytes` returns `ImageTooLarge`; assert an image ≤ 28 px in either dimension returns `CropTooSmall`; assert a footprint ring maps to pixel positions inside the composed frame using `StaticMapFraming.CoveredBounds` for a known centre/zoom; run two compositions concurrently on separate images and assert each output matches its own input (FR-013 — no shared mutable state).
- [X] T013 [P] [US1] `tests/AskLucy.Infrastructure.Tests/MapCaptures/MapImageRendererRouterTests.cs` — Google is used when a key is configured; Esri is used only for `satellite` when no Google key is configured; a non-satellite mode with no Google key returns `ModeUnavailable`; an Esri request with `overlays=building-footprints` still succeeds (overlay painting is provider-independent).
- [X] T014 [P] [US1] `tests/AskLucy.Web.Tests/MapCaptures/MapCapturesEndpointTests.cs` plus `tests/AskLucy.Web.Tests/MapCaptures/MapCapturesWebApplicationFactory.cs` — derive the factory from `CustomWebApplicationFactory` (do **not** call `WithWebHostBuilder` per test: it builds a new host + Hangfire per test and stalls the suite) and use it as the `IClassFixture`; add `MapCapture:ApiKeys:0:Name/Key` in its configuration and replace `IGeocodingProvider`/`IMapImageRenderer` with fakes in `ConfigureTestServices`. Assert: missing key → `401`; wrong key → `401`; valid key → `200` with `Content-Type: image/png` and `X-Map-Capture-*` headers; hyphenated `mode=buildings-only` and `overlays=building-footprints` parse; `mode=terrain` → `400`; `location` not found → `404`; `site-boundary` → `422`; imagery failure → `502`; `latitude` without `longitude` → `400`. Also cover rate limiting: configure `MapCapture:RateLimitPerMinute` = `2` in the derived factory and assert the third request from one key returns `429` while a second key's request still returns `200` (partition per caller). Run with `PERSISTENCE_TESTS_CONNECTION_STRING` set (value in `appsettings.Development.json`).

### Implementation for User Story 1

- [X] T015 [P] [US1] Create `src/AskLucy.Application/MapCaptures/Queries/CaptureMap/CaptureMapQuery.cs` — `string? Location`, `double? Latitude`, `double? Longitude`, `int? ViewSizeMetres`, `string? Mode`, `string? Overlays`, `string CallerName`. Mode/overlays are strings so the controller can map wire names explicitly; the handler parses them into the enums.
- [X] T016 [US1] Create `src/AskLucy.Application/MapCaptures/Queries/CaptureMap/CaptureMapQueryValidator.cs` (FluentValidation) — the rules from [data-model.md](data-model.md#validation-rules-fluentvalidation) using `IOptions<MapCaptureOptions>` bounds; overlay names outside the known set fail `400`, while `site-boundary` passes here and is refused later as `OverlayUnavailable` so the message names it.
- [X] T017 [US1] Create `src/AskLucy.Application/MapCaptures/Queries/CaptureMap/CaptureMapQueryHandler.cs` — `IRequestHandler<CaptureMapQuery, MapImageResult>`: coordinates win; otherwise `IGeocodingProvider.SearchAsync` and take the max-`Importance` candidate; zero → `LocationNotFound`; `GeocodingProviderUnavailableException` → `GeocodingUnavailable`; substitute `DefaultViewSizeMetres` when null; reject `SiteBoundary` before calling the renderer with `OverlayUnavailable` (the detail must name the unsupported overlay); build `MapImageRequest` with `CallerName` from `ClaimTypes.NameIdentifier` (the controller puts it on the query) and call `IMapImageRenderer`; log caller, resolved location, mode, overlays, and elapsed time — never the API key. Depends on T008, T015, T016.
- [X] T018 [US1] Create `src/AskLucy.Infrastructure/MapCaptures/MapImageComposer.cs` — one Magick.NET pipeline used by both renderers: decode provider bytes; draw `BuildingFootprint.Ring` rings as a 2 px stroke mapped through `StaticMapFraming.CoveredBounds(center, zoom)` and the 1280 px frame; crop 14 px from each edge (`Crop(1252×1252 at 14,14)` computed from the real image size); encode PNG; reject `CropTooSmall` when either dimension ≤ 28 and `ImageTooLarge` when the encoded length exceeds `MapCaptureOptions.MaxImageBytes`. Depends on T008. (One class keeps the overlay geometry and the crop in the same file where the same pixel space is used.)
- [X] T019 [US1] Create `src/AskLucy.Infrastructure/MapCaptures/GoogleStaticMapImageRenderer.cs` — read the key from `IOptions<GoogleMapsGeocodingOptions>`, choose zoom with `StaticMapFraming.ChooseZoomToFit(latitude, viewSizeMetres / 2)`, GET `staticmap?center=…&zoom=…&size=640x640&scale=2&maptype=…&format=png&key=…` via the existing `GoogleStaticMaps` client (never dispose the factory client), reject non-success/non-image responses as `ProviderUnavailable`, fetch footprints over `viewSizeMetres / 2` when requested, and pass everything to `MapImageComposer`. Depends on T018.
- [X] T020 [US1] Create `src/AskLucy.Infrastructure/MapCaptures/EsriSatelliteImageRenderer.cs` — satellite only; reuse the `CoveredBounds` rectangle from the same zoom and request `1280,1280` from the World Imagery export endpoint (the existing `EsriWorldImagery` client, optional `X-Esri-Authorization`), so both providers land on the same 1280×1280 frame; any other mode → `ModeUnavailable`. Depends on T018.
- [X] T021 [US1] Create `src/AskLucy.Infrastructure/MapCaptures/MapImageRendererRouter.cs` — Google when `GoogleMapsApiKey` is non-blank, Esri for `satellite` otherwise, `ModeUnavailable` for every other combination. Depends on T019, T020.
- [X] T022 [US1] Register `IMapImageRenderer` → router, both renderers, and the composer in `src/AskLucy.Infrastructure/DependencyInjection.cs` (the `GoogleStaticMaps`/`EsriWorldImagery` clients already exist there). Depends on T021.
- [X] T023 [P] [US1] Create `src/AskLucy.Web/Auth/ServiceApiKeyAuthenticationOptions.cs` — `SchemeName = "ServiceApiKey"` and the claims issued on success.
- [X] T024 [US1] Create `src/AskLucy.Web/Auth/ServiceApiKeyAuthenticationHandler.cs` — read `X-Api-Key`, compare against every configured key with `CryptographicOperations.FixedTimeEquals`, and on success issue `ClaimTypes.NameIdentifier = entry.Name` plus a `Service` role; missing/unknown/unconfigured key → `NoResult()`/`Fail()`. Log every rejection at `Warning` with the request path and the matched caller name when known — never the presented key. The §8 immutable audit-trail requirement is a pre-existing platform gap (the JWT path has none either); record the deviation in T027 rather than building an audit subsystem here. Dependencies: T002, T023.
- [X] T025 [US1] Update `src/AskLucy.Web/Program.cs`: add `.AddScheme<ServiceApiKeyAuthenticationOptions, ServiceApiKeyAuthenticationHandler>(...)` to the existing `AddAuthentication(JwtBearerDefaults...)` builder (JWT stays the default scheme); add a `map-capture-endpoints` fixed-window rate-limit policy partitioned by `RateLimitPartitions.UserOrClientKey(context)` with `MapCaptureOptions.RateLimitPerMinute`; log a startup warning once when no keys are configured. Depends on T024.
- [X] T026 [US1] Create `src/AskLucy.Web/Controllers/v1/MapCapturesController.cs` — route `[Route("api/v1/map-captures")]`, `[Authorize(AuthenticationSchemes = ServiceApiKeyAuthenticationOptions.SchemeName)]`, `[EnableRateLimiting("map-capture-endpoints")]`, `GET` with query parameters `location`, `latitude`, `longitude`, `mode`, `overlays`, `viewSizeMeters`; parse `mode`/`overlays` as strings into the enums (default binding rejects `buildings-only`); set `CallerName` from `ClaimTypes.NameIdentifier`; send `CaptureMapQuery` through MediatR; on success return `File(png, "image/png")` plus `X-Map-Capture-Resolved-Location`, `X-Map-Capture-Mode`, `X-Map-Capture-Width`, and `X-Map-Capture-Height`; map each `MapCaptureFailureReason` to its ProblemDetails status/title from [contracts/map-capture-api.md](contracts/map-capture-api.md#error-responses), adding the `traceId` extension member from the correlation id exactly as `AiController` does (constitution §6). Declare the OpenAPI surface: `[Produces("image/png")]`, `[ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]`, and `[ProducesResponseType(typeof(ProblemDetails), …)]` for 400/401/404/422/429/502. Keep attributes directly on the class (project memory: an inserted record between attributes and class steals them). Depends on T017, T022, T025.

**Checkpoint**: All eight acceptance scenarios from [spec.md](spec.md#user-story-1---server-to-server-map-capture-priority-p1) pass end-to-end; quickstart scenarios 1–7 behave as documented.

---

## Phase 4: Polish & Cross-Cutting Concerns

- [X] T027 [P] Add `docs/adr/0020-service-api-key-authentication-for-map-capture.md` recording the decisions: static service API key over client-credentials JWT, no persistence, building-footprints-only overlays, Google primary / Esri satellite fallback, and the recorded deviation from constitution §8 (auth events go to structured logs, not an immutable audit trail — a platform-wide gap, not introduced here).
- [X] T028 Run `dotnet build`, the three filtered test commands from [quickstart.md](quickstart.md#automated-tests), and the `dotnet format` gate; fix any `\r\r\n` line-ending or analyzer failures. Confirm no new NuGet package, database entity, or migration was added (`git status` / `git diff --stat`).
- [X] T029 Validate quickstart scenarios 1–7 against a real Google key and confirm no file appears under the configured `FileStorage:RootPath` and no database rows change. Measure SC-001: time 20 sequential real-provider captures (geocoding included) and record the p95 — it must be under 10 seconds; if the repo's `RUN_SCALE_PERFORMANCE_TESTS` suite is being extended, add the same measurement there so CI can enforce it at go-live.

### Phase 5: AI Tools admin area (added after the initial 083 tasks)

The conversation capability `map_snapshot` is an `IAgentTool`, and an administrator had nowhere to see it: the platform's own screens showed AI providers, capabilities, and system agents, but never the tools an agent can call. This area is read-only by design — what a deployment can call is a fact about the deployment, not a setting an administrator edits (the same reasoning as System agents).

- [X] T030 [P] Add `AdminArea.AiTools` to `src/AskLucy.Domain/Authorization/AdminArea.cs`, `AdminPermissionCatalog.AiToolsView` (`"admin.ai-tools.view"`) to `src/AskLucy.Domain/Authorization/AdminPermissionCatalog.cs`, and raise `_allList` to `new(26)`. A new catalogue key needs no role wiring: `BuiltInRolePermissions.Administrator` derives from `PermissionSet.Full`, so the built-in Administrator holds it automatically.
- [X] T031 [P] Create `src/AskLucy.Application/Agents/Queries/ListAgentTools/AgentToolDto.cs`, `ListAgentToolsQuery.cs`, and `ListAgentToolsQueryHandler.cs` — read `AgentToolCatalog.All` (native tools plus the currently-active MCP tools), the same merged set the agent runtime dispatches on, so the admin view can never show a tool the runtime would refuse. The schemas are carried as the tool's own raw JSON, not re-serialized.
- [X] T032 [P] Create `src/AskLucy.Web/Controllers/v1/AdminAiToolsController.cs` — `GET api/v1/admin/ai-tools`, `[RequirePermission("admin.ai-tools.view")]`, `[EnableRateLimiting("admin-endpoints")]`, returning the DTO list. MediatR needs no explicit registration: the Application assembly is already scanned.
- [X] T033 [P] Mirror the catalogue entry in `src/AskLucy.Web/ClientApp/src/features/admin/adminPermissions.ts` (key, `area: 'AiTools'`, `areaLabel: 'AI tools'`, display name, description) and add the `aiToolsView` const-map key.
- [X] T034 [P] Add the nav entry to `src/AskLucy.Web/ClientApp/src/features/admin/adminNav.tsx` (after "AI capabilities", no `dividerAfter`) and `nav.aiTools` to the English and Arabic `admin/shell.ts` catalogs. The English nav label and `nav.aiTools` must be identical strings; the Arabic one must differ.
- [X] T035 [P] Add `en/admin/aiTools.ts` and `ar/admin/aiTools.ts` catalogs, register `'admin.aiTools'` in **both** maps of `src/AskLucy.Web/ClientApp/src/i18n/messages/index.ts`, and add `areas.AiTools` plus `permissions.aiToolsView` to the English and Arabic `admin/roles.ts` (the i18n strings must equal the catalogue's `areaLabel`/`displayName`/`description` verbatim — `usePermissionText.test.ts` asserts it).
- [X] T036 [P] Create `src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiToolsApi.ts` and `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiToolsPage.tsx` — a read-only table (tool, origin, description, risk, required permissions), mirroring `AdminSystemAgentsPage.tsx`. `useOuterT` for the shell's title/subtitle (the page renders outside the shell's language surface), `useT` for the body.
- [X] T037 [P] Wire the route in `src/AskLucy.Web/ClientApp/src/routes/router.tsx` — a lazy import and `/admin/ai-tools` behind `<AdminRoute permission="admin.ai-tools.view">`.
- [X] T038 [P] Update `tests/AskLucy.Domain.Tests/Authorization/AdminPermissionCatalogTests.cs` (counts 25 → 26, the new key's `[InlineData]`, `AdminArea.AiTools => "ai-tools"`, and the `ByArea` counts) and `tests/AskLucy.Domain.Tests/Authorization/PermissionSetTests.cs` (`HaveCount(25)` → `26`).
- [X] T039 [P] Create `tests/AskLucy.Web.Tests/Admin/AdminAiToolsEndpointTests.cs` — a principal holding only `admin.ai-tools.view` reads the catalogue; a principal holding none is refused. (The shared `PermissionEnforcementMatrixTests` also covers the endpoint, but its rows are flaky against the shared test database — every one of its 403-path cases writes an audit row — so the dedicated test is the one that proves this endpoint.)

### Phase 6: The imagery provider, the crop, the zoom, and the colour correction

Added after the first delivery, once the deployed capture came back pale and stamped with Google's logo. Two problems, one root cause each.

- [X] T040 [P] Fix the real bug in `src/AskLucy.Infrastructure/MapCaptures/EsriSatelliteImageRenderer.cs`: it asked `ibasemaps-api.arcgis.com/.../World_Imagery/MapServer/export`, which answers **404** — that host does not serve the export path, so the ESRI renderer could never have produced an image. The export lives on `services.arcgisonline.com`, which serves it and accepts the configured key as a `?token=` query parameter.
- [X] T041 [P] Add `MapCaptureSource` (`Esri`, `Google`) and `MapCaptureZoom` to `src/AskLucy.Application/MapCaptures/`, with the wire spellings and the per-provider ceilings measured against the live services: Google serves zoom 0–21 and clamps silently above it; Esri's export cannot be assembled past zoom 18 and answers with a server error. Both are refused explicitly rather than clamped.
- [X] T042 [P] Add `MapCaptureSource`, `CropPerEdge`, `Zoom` and `EnhanceColour` to `MapImageRequest` and `MapCaptureSource`/`CropPixels`/`Zoom` to `CaptureMapQuery`, with validation, and route the router by the requested source — never answering from the other provider when the chosen one cannot serve the request.
- [X] T043 [P] Make the crop configurable and correct the colour: `MapImageComposer` takes the crop per edge and applies the level + saturation restore. The parameters were fitted by minimising the difference against Esri's render of the identical frame (Google's blacks sit at ~52% instead of ~7%, its contrast is a third of the truth, its colour half saturated) and verified against the real Magick.NET output, not only a model of it.
- [X] T044 [P] Add the per-tool settings store: the `ToolSetting` entity and `ToolSettings` table (a new migration), `ToolSettingCatalog` declaring the map snapshot's four defaults, and `ToolSettingsReader` resolving them — a setting never saved, or whose saved value is no longer valid, falls back to the catalogue default.
- [X] T045 [P] Add `admin.ai-tools.manage` to the permission catalogue and the `GET`/`PUT /api/v1/admin/ai-tools/settings` endpoints, so an administrator can switch the provider from the admin panel.
- [X] T046 [P] Add the gear column to the admin AI Tools page, its settings dialog (a switch, a select and a number field, one per setting type), and the English and Arabic catalog entries for both.
- [X] T047 [P] Add the Esri key to the untracked `appsettings.Development.json` and `appsettings.Production.json`. (No tracked config changed: the per-tool defaults live in the database, and the `MapCapture` section in the tracked `appsettings.json` already carries the capture options.)
- [X] T048 Run the whole solution's build, the three test projects, the format gate and the frontend lint; confirm the new migration applies cleanly to the shared test database.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 → T002 → T003.
- **Foundational (Phase 2)**: T004–T007 in parallel; T008 after T004–T007. Blocks all US1 implementation.
- **User Story 1 (Phase 3)**: tests T009–T014 may be written in parallel after Phase 2; implementation order is T015/T016 → T017; T018 → T019/T020 → T021 → T022; T023 → T024 → T025 → T026. T026 is the integration point and needs T017 and T022 as well.
- **Polish (Phase 4)**: after all US1 tasks.

### Within User Story 1

- Tests first; confirm they fail before implementing.
- Application types → validator → handler.
- Composer → renderers → router → DI → controller.
- Auth options → handler → `Program.cs` registration → controller.

### Parallel Opportunities

- T002 with T004–T007 (different files, no dependency).
- T004–T007 together.
- T009–T014 together (six independent test files).
- T015 with T018 and T023 (different layers, no dependency).
- T019 with T020 once T018 lands.
- T027 with T028.

---

## Parallel Example: User Story 1

```text
# Tests (after Phase 2):
Task: "CaptureMapQueryHandlerTests.cs — coordinates win, geocoding failure mapping"
Task: "CaptureMapQueryValidatorTests.cs — location/coordinate/view-size rules"
Task: "GoogleStaticMapImageRendererTests.cs — mode→maptype, style, 640/scale=2"
Task: "MapImageComposerTests.cs — 14 px crop, 10 MB cap, overlay pixels"
Task: "MapImageRendererRouterTests.cs — Google/Esri selection"
Task: "MapCapturesEndpointTests.cs + derived factory — auth matrix, ProblemDetails"

# Implementation (after tests are red):
Task: "CaptureMapQuery.cs"
Task: "MapImageComposer.cs"
Task: "ServiceApiKeyAuthenticationOptions.cs"
```

---

## Implementation Strategy

### MVP

The single P1 story **is** the MVP: options → shared types → handler/renderers → service-key auth → controller. Stop after T026, run the quickstart, and only then do the ADR and full-suite gates.

### Incremental

1. Phase 1 + 2 → compiles with no behaviour.
2. T015–T022 → `dotnet test` on Application + Infrastructure goes green (no HTTP surface yet).
3. T023–T026 → Web tests green; quickstart scenarios pass.
4. Phase 4 → documentation and gates.

---

## Notes

- [P] tasks = different files, no dependency on an incomplete task.
- The feature is stateless by design: **no** `DbSet`, repository, `IFileStorage` write, temp file, or migration. Any diff adding one is a bug.
- `StaticMapFraming` is `internal` to Infrastructure; tests see it through the existing `InternalsVisibleTo`.
- Wire parsing is explicit: default ASP.NET enum binding accepts `BuildingsOnly`, not `buildings-only`.
- Every failure path returns ProblemDetails; no empty `200`, no partial image, no swallowed exception.
- `MapCaptureOptions` lives in Application (T002) so the validator, handler, and renderer share one type; plan.md reflects this.
- Web.Tests requires `PERSISTENCE_TESTS_CONNECTION_STRING`; use a derived factory as `IClassFixture`, never `WithWebHostBuilder` per test.
