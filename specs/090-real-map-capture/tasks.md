---
description: "Task list for SPEC-090 Real Map Capture for the Site Map Specialist"
---

# Tasks: Real Map Capture for the Site Map Specialist

**Input**: Design documents from `/specs/090-real-map-capture/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/site-map-capture-tool.md](./contracts/site-map-capture-tool.md)

**Tests**: Included. The constitution (§10) and SPEC-090 FR-033/FR-034 require tests in the same
change that introduces the behaviour.

**Organization**: By user story, so each increment is independently verifiable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5
- Every task names its exact file path

---

## ⚠️ READ THIS FIRST — 7 rules that override intuition

| # | Rule | Why |
|---|---|---|
| 1 | Resolve `ISiteAnalysisRepository`, `ISender`, `DocumentUploadFinalizer` and `IUnitOfWork` from **one isolated scope**. | Parallel workflow branches share the job's `DbContext`; the capture's handler reads settings through it. `SiteEnvironmentalContextTool.LoadSitePointAsync` is the precedent. |
| 2 | Pass the record's coordinates with `Location: null`. Never the site name. | A name path geocodes again and can resolve a different place; the point was frozen at dispatch. |
| 3 | Name **only** `Mode: "satellite"`. Leave `Source`, `CropPixels`, `Zoom`, `ViewSizeMetres`, `Overlays` null. | Those are administrator-controlled inside the capture; naming them forks control and drifts from the snapshot capability. |
| 4 | `MapImageResult` is success-with-`Png` **or** failure-with-`FailureReason`. Compose nothing and persist nothing unless `IsSuccess && Png is not null`. | A finding without a document is rejected by the relay (`AnalysisTypesRequiringDocument`). |
| 5 | Persist with `ownerId = analysis.UserId`, filename ending exactly `.png`, then call `IUnitOfWork.SaveChangesAsync`. | The validator sniffs magic bytes; the finalizer only stages. |
| 6 | The class name and `Name` (`site_analysis_schematic_image`) stay. The heading and display label change instead. | The published system workflow references the tool name; the enum is a stable API identifier. |
| 7 | Delete `SiteAnalysisPrompts`; do not keep a generated fallback. | The image is real imagery or nothing — a generated substitute is the one failure a user cannot detect. |

**Enum serialization**: enums serialize as strings API-wide. Do not add converters; do not compare
numerically on the client.

---

## Phase 1: Setup

- [X] T001 Verify the contracts this change consumes, reading only: `CaptureMapQuery`'s optional
  parameters and coordinate precedence in `src/AskLucy.Application/MapCaptures/Queries/CaptureMap/`,
  `MapImageResult`'s success-or-failure invariant, `DocumentUploadFinalizer.FinalizeAsync` in
  `src/AskLucy.Application/Documents/Commands/DocumentUploadFinalizer.cs`, and that no existing
  test asserts the string "schematic site map". Record any surprise before writing code.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the two additive lines that let the finding name the imagery source it actually used.

- [X] T002 Add `public MapCaptureSource? Source { get; private init; }` to
  `src/AskLucy.Application/MapCaptures/MapImageResult.cs`, documented as "the source that served a
  successful capture; null on failure", in the same read-only style as `ResolvedLocation`.
- [X] T003 Set it in `src/AskLucy.Application/MapCaptures/Queries/CaptureMap/CaptureMapQueryHandler.cs`
  on the success return (`result with { ResolvedLocation = locationName, Source = source }`).
  Change nothing on the failure path and nothing in a renderer.
- [X] T004 [P] Extend `tests/AskLucy.Application.Tests/MapCaptures/CaptureMapQueryHandlerTests.cs`:
  a coordinate capture reports the default source (Esri); an explicit `source: "google"` reports
  Google; a failed render leaves `Source` null.

**Checkpoint**: `dotnet test --filter FullyQualifiedName~CaptureMapQueryHandlerTests` green.

---

## Phase 3: User Story 1 — The site-map finding shows the real place (P1) 🎯

**Goal**: the finding shows the platform's real capture, centred on the frozen point, persisted as
a document, with complete provenance and High confidence.

**Independent test**: run an analysis with the `ImageGeneration` capability unassigned; the
site-map finding still delivers a real capture, resolves from `/documents/{fileId}/download`, and
reports `map-capture:{source}` + `High`.

- [X] T005 [US1] Rewrite the success path of
  `src/AskLucy.Application/SiteAnalysis/Tools/SiteSchematicImageGenerationTool.cs`: parse the id;
  create one isolated scope; read the `SiteAnalysis` record by id (missing → US3's failure path);
  send `new CaptureMapQuery(Location: null, Latitude: record.Latitude, Longitude: record.Longitude,
  ViewSizeMetres: null, Mode: "satellite", Overlays: null, CallerName: $"site-analysis:{id}",
  Source: null, CropPixels: null, Zoom: null)`; on success persist `result.Png` through
  `DocumentUploadFinalizer` (`ownerId: record.UserId`, `site-analysis-site-map-{guid}.png`, actor
  `system:site-analysis-map-capture`), `SaveChangesAsync`, resolve the id with the duplicate path,
  and report success through the relay. Keep the existing `OperationCanceledException` rethrow.
- [X] T006 [US1] Compose the finding in the same file via `SiteAnalysisContentComposer.ComposeFinding`:
  heading **Site Map**; body naming the capture and the site; one image block with the document id
  and meaningful alt text; provenance last with `DataSource = "map-capture:" +
  MapCaptureSourceWire.WireValueOf(source)` and confidence from
  `SiteAnalysisConfidence.ResolveConfidence(dataDirectlyDescribesSite: true,
  dataIsSparseOrApproximate: false)` with the comment explaining why. Rewrite the class XML doc and
  `Description`; keep `Name`, the schemas, the permissions and the risk level untouched.
- [X] T007 [US1] Delete `src/AskLucy.Application/SiteAnalysis/SiteAnalysisPrompts.cs` and every
  reference to `SiteAnalysisPrompts`/`BuildSchematicImagePrompt`/`IImageGenerationService` in the
  tool. Confirm a repo search returns nothing outside SPEC-057's history and the superseded
  contract. Leave `IImageGenerationService` and its other consumers untouched.
- [X] T008 [US1] Create `tests/AskLucy.Application.Tests/SiteAnalysis/SiteSchematicImageGenerationToolTests.cs`
  with a harness mirroring `EnvironmentalContextToolTests`: a `ServiceCollection` exposing
  `IServiceScopeFactory`, a substituted `ISiteAnalysisRepository` and `ISender`, and the **real**
  `DocumentUploadFinalizer` over substituted `IDocumentFileValidator`, `IFileStorage`,
  `IDocumentRepository`, `IDocumentStatisticsRepository`, `IProcessingNotifier` and `IUnitOfWork`
  (the pattern in `tests/AskLucy.Application.Tests/Documents/UploadValidationTests.cs`). Cover:
  delivery; the document was added and committed; the image block's `fileId` is the document id;
  exactly one image block; block order heading → text → image → provenance.
- [X] T009 [US1] In the same file, cover the capture request and the label: the query carries the
  record's coordinates and a null `Location`; mode is `satellite`; source, crop, zoom and view size
  are null; the record's site name is used even when the input names another place; confidence is
  `High`; `DataSource` is `map-capture:esri` (and `:google` when the result reports it).

**Checkpoint**: the finding is real, persisted, sourced and labelled High.

---

## Phase 4: User Story 2 — The finding is honest about what the image is (P1)

**Goal**: nothing in the heading, body, alt text, notice or provenance implies the image is live or
dated; the label no longer says "schematic"; attribution is exactly what is required (none today).

**Independent test**: read the delivered finding and its notice; no date, liveness or "schematic"
claim appears anywhere, and the provenance names the source.

- [X] T010 [US2] Finalise the user-facing wording in
  `src/AskLucy.Application/SiteAnalysis/Tools/SiteSchematicImageGenerationTool.cs`: the body states
  the image is the platform's map capture of the site, centred on its coordinates, the provider's
  most recent available capture, **not live**, and carrying **no capture date**; the alt text
  describes the map and the site (1–300 chars) with no liveness or date claim. Reserve the
  attribution position with a comment: no credit is emitted for the current sources, and a future
  source's required credit goes in the body.
- [X] T011 [US2] Change `FormatAnalysisTypeName` for `SiteAnalysisType.SchematicImage` from
  `"schematic site map"` to `"site map"` in
  `src/AskLucy.Application/SiteAnalysis/SiteAnalysisResultRelay.cs`. Change nothing else in the
  relay and nothing in the enum.
- [X] T012 [P] [US2] Extend `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisResultRelayTests.cs`
  to assert the notice/panel title for a schematic result reads **site map**.
- [X] T013 [US2] Extend `tests/AskLucy.Application.Tests/SiteAnalysis/SiteSchematicImageGenerationToolTests.cs`
  to assert: the heading is `Site Map`; the body contains the most-recent / not-live / no-date
  statements and contains no date; the alt text makes no liveness or date claim; no external
  address appears in the content; no attribution line is emitted for Esri or Google; the body
  carries no credit that the source did not require.

**Checkpoint**: the finding cannot be read as a live or dated view, and is not called schematic.

---

## Phase 5: User Story 3 — A failed capture is recorded, never shown (P2)

**Goal**: every capture outcome that is not a usable image becomes a recorded specialist failure
with the platform's reason, and delivers nothing.

**Independent test**: force `ZoomUnavailable` (Esri + zoom 19) and `ProviderUnavailable` (network
blocked); each produces a `Failed` row and no notice/panel, and the analysis still settles.

- [X] T014 [US3] Implement the failure path in
  `src/AskLucy.Application/SiteAnalysis/Tools/SiteSchematicImageGenerationTool.cs`: when the capture
  is unsuccessful, report `"{FailureReason}: {FailureDetail}"` through
  `ReportFailureAsync(..., cause: null, ...)` and return `AgentToolResult.Failure`; when the record
  is missing, the document write fails, or anything else throws, report the message with the
  exception as cause. Compose nothing, persist nothing, deliver nothing. Add no exception type and
  no new vocabulary.
- [X] T015 [US3] In `tests/AskLucy.Application.Tests/SiteAnalysis/SiteSchematicImageGenerationToolTests.cs`,
  add a theory over all eight `MapCaptureFailureReason` values: each records exactly one
  `ReportFailureAsync` whose reason starts with the enum value's name; no `ReportSuccessAsync`; no
  document added; `AgentToolResult.Succeeded` false.
- [X] T016 [US3] In the same file, cover the non-capture faults: missing analysis record; a
  document-write failure (validator/quota throws); invalid/missing `siteAnalysisId` (no relay call
  at all); and cancellation rethrown rather than recorded.

**Checkpoint**: no failure can produce a broken or empty finding, and every failure is diagnosable.

---

## Phase 6: User Story 4 — The image is a platform document like any other (P2)

**Goal**: the image is owned, validated, de-duplicated and durable exactly like the generated one.

**Independent test**: inspect the row and the document; navigate away, reload, and reopen — the
image renders from the platform route.

- [X] T017 [US4] In `tests/AskLucy.Application.Tests/SiteAnalysis/SiteSchematicImageGenerationToolTests.cs`,
  assert the document finalizer was called with the record's user id, a `.png` filename, and an
  image-typed stream; that the finding uses `DuplicateOfDocumentId` when the finalizer reports a
  duplicate; that no provider URL or external address appears in the stored content JSON.
- [ ] T018 [US4] Verify rehydration end to end using scenario 6 of
  `specs/090-real-map-capture/quickstart.md`: a delivered finding reopened after navigation/reload
  renders the same panel and the same document, and a finding stored before this change still
  renders unchanged. Record the result in the implementation notes; fix anything that fails.

**Checkpoint**: the finding survives a reload; nothing new is persisted except the Document.

---

## Phase 7: User Story 5 — The change did not disturb the fan-out (P3)

**Goal**: prove the change stayed inside the specialist plus the two recorded shared lines.

**Independent test**: `git diff --stat` shows none of the fan-out, provisioner, DI, enum, count,
schema or client surfaces changed, and a stored old finding renders.

- [X] T019 [US5] In `tests/AskLucy.Application.Tests/SiteAnalysis/SiteSchematicImageGenerationToolTests.cs`,
  pin the stable identity: `Name == "site_analysis_schematic_image"`, the input schema still carries
  `siteAnalysisId`/`siteName`/`siteLocation`, the output schema still reports `reported`, and
  `SiteAnalysisSpecialists.All` still contains that tool name.
- [X] T020 [US5] Run the additive-only check documented in `specs/090-real-map-capture/plan.md`:
  confirm no change under `src/AskLucy.Infrastructure`, `src/AskLucy.Persistence`,
  `src/AskLucy.Web` or `src/AskLucy.Web/ClientApp`, and no migration. Record the diff summary in
  the implementation notes.

**Checkpoint**: the next imagery-based specialist (Urban Context, Character Analysis) can follow the
same pattern without touching shared machinery.

---

## Phase 8: Polish & Documentation

- [X] T021 [P] Update `specs/057-site-analysis-agent/plan.md` Follow-ups: the map-capture paragraph
  records that SPEC-090 implements it, links the new spec, and no longer describes the specialist
  as generating its image.
- [X] T022 [P] Update `specs/057-site-analysis-agent/tasks.md`: the follow-up table/notes point at
  SPEC-090 and the "generated image" descriptions are corrected.
- [X] T023 [P] Mark `specs/057-site-analysis-agent/contracts/schematic-image-prompt.md` **Superseded
  (2026-10-25)**, stating what replaced it and why, and link
  [the new contract](./contracts/site-map-capture-tool.md). Keep the v1 prompt text as history.
- [X] T024 [P] Update `docs/ARCHITECTURE.md` §33: the finding is a real capture through SPEC-083's
  query persisted by the existing finalizer; confidence is High because the capture directly
  describes the site; the generated-image description is removed. Remove the site-analysis map from
  the `IImageGenerationService` consumer list in the same section.
- [X] T025 Review every new `catch` in the changed files
  (`src/AskLucy.Application/SiteAnalysis/Tools/SiteSchematicImageGenerationTool.cs`,
  `src/AskLucy.Application/MapCaptures/Queries/CaptureMap/CaptureMapQueryHandler.cs`) for
  constitution §2 VIII: nothing swallowed, failures reported with reason and cause, no unawaited
  async, cancellation rethrown.
- [X] T026 Run `dotnet format` (expect local `ENDOFLINE` false positives; verify against CI),
  `dotnet build`, and the full backend suite with `PERSISTENCE_TESTS_CONNECTION_STRING` set, from
  the repository root. Confirm `using System.*` directives come first in every changed `.cs` file.
- [ ] T027 Walk every scenario in `specs/090-real-map-capture/quickstart.md` against a running app
  and record each outcome, including the `ZoomUnavailable` failure and the Google-source provenance
  check.

---

## Dependencies

```
Phase 1 (T001)
   ↓
Phase 2 (T002 → T003 → T004) ← blocks every story
   ↓
Phase 3 (US1: T005 → T006 → T007 → T008 → T009)
   ↓
├── Phase 4 (US2: T010 → T011 → T012 [P] → T013)
├── Phase 5 (US3: T014 → T015 → T016)
└── Phase 6 (US4: T017 → T018)
             ↓
Phase 7 (US5: T019 → T020)
             ↓
Phase 8 (Polish: T021–T027)
```

**Story independence**: US2, US3 and US4 each extend US1's tool; none can be demonstrated before
US1 delivers a finding. US2 is P1 because without it the finding could imply a live, dated view.
US5 is a verification story and needs all three.

**Within US1**: T005 → T006 → T008 (the harness needs the composed content); T007 is independent
of T006 and can be done any time after T005.

**Within US2**: T011 → T012; T010 → T013.

**Within US3**: T014 → T015/T016 (tests are only meaningful once the path exists).

## Parallel execution examples

```text
# Phase 4/5/6, once US1's tool compiles:
T010–T013 (US2 wording + relay label)  ‖  T014–T016 (US3 failure path)  ‖  T017 (US4 document tests)

# Phase 8 documentation, independent files:
T021 (057 plan)  ‖  T022 (057 tasks)  ‖  T023 (057 contract)  ‖  T024 (ARCHITECTURE)
```

## Implementation strategy

**MVP = US1 + US2.** US1 alone delivers the real image; US2 is what makes its claim honest (not
live, no date, not schematic). Shipping US1 without US2 would deliver a real photograph under a
label that invites a currency assumption, which is the opposite of the feature's purpose.

**Then**: US3 makes failure safe, US4 proves the document lifecycle, US5 proves additive
containment. Documentation (Phase 8) is part of the change, not a follow-up.

## Notes

- Do not add a fallback to `IImageGenerationService` under any circumstance.
- Do not add a migration or a capture table; the Document is the only persistence.
- Do not rename the enum, tool name, node key, DI registration or class.
- Do not read pixels or derive any figure from the image.
- The quickstart's failure reproduction is deterministic: Esri + zoom 19 → `ZoomUnavailable`.

---

## Implementation notes (2026-10-25)

**Delivered**: T001–T017 and T019–T026. T018 and T027 are manual walks against a running app and
remain open. See the diff summary below for the additive check.

**Test result**: `dotnet build "Ask Lucy.sln"` — 0 warnings, 0 errors.
`dotnet test tests/AskLucy.Application.Tests` — **2849 passed, 0 failed**, including the 25 new
tool tests, the module-wide capture-handler additions, and the relay display-label test.

**T018 — not run manually.** Rehydration replays the stored `ContentJson` through the unchanged
document route; the tests assert the content shape and the document id, and no client code changed.
The manual reload walk needs a running app, SQL Server and an imagery provider.

**T026 — run.** `dotnet build "Ask Lucy.sln"`: 0 warnings, 0 errors. `AskLucy.Application.Tests`:
**2849 passed, 0 failed**; `AskLucy.Domain.Tests`: **761 passed**; `AskLucy.Web.Tests`: **1058
passed, 21 skipped, 0 failed** against the local test database (`ConnectionStrings:DefaultConnection`
from `src/AskLucy.Web/appsettings.Development.json`, exported as
`PERSISTENCE_TESTS_CONNECTION_STRING` for the run — the CI secret is not present in a local shell,
which is why the first attempt without it failed at host startup). `Persistence.Tests` were
deliberately not run: they are destructive and gated behind `PERSISTENCE_TESTS_DEDICATED_DATABASE`,
and this change has no schema impact. `dotnet format --verify-no-changes` reports only `ENDOFLINE`
on the newly written files, the repository's known local false positive (LF working copy vs CI's
CRLF checkout); no whitespace, style, analyzer or using-order errors were reported.

**T027 — not run.** The quickstart scenarios need a running app, SQL Server, an imagery provider
and an admin session. The deterministic failure reproduction it documents is Esri + zoom 19 →
`ZoomUnavailable`.

**Diff summary (T020)**: changed files are `MapImageResult.cs`, `CaptureMapQueryHandler.cs`,
`SiteAnalysisPrompts.cs` (deleted), `SiteAnalysisResultRelay.cs`,
`SiteSchematicImageGenerationTool.cs`, three test files, `docs/ARCHITECTURE.md`, the SPEC-057
plan/tasks/contract, and `.specify/feature.json`. No change under `Infrastructure`, `Persistence`,
`Web`, `ClientApp`; no migration; no fan-out, provisioner, DI, enum or schema change.
