# Implementation Plan: Real Map Capture for the Site Map Specialist

**Branch**: `090-real-map-capture` | **Date**: 2026-10-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/090-real-map-capture/spec.md`

## Summary

Replace the site-map specialist's AI-generated image with the platform's **real map capture**
([specs/083](../083-viewer-map-capture/spec.md)), obtained in-process through the existing
`CaptureMapQuery` and centred on the coordinates frozen on the persisted `SiteAnalysis` record.
The capture's PNG is persisted as a platform `Document` through the existing
`DocumentUploadFinalizer`, exactly as the generated image is today, and the finding keeps its
shape — heading, text, one image block, provenance last. Capture replaces generation outright:
there is no generated fallback, and the versioned image prompt is superseded and removed from the
code (contract retained, marked Superseded).

This is an internal change to one shipped specialist. It adds no analysis type, no fan-out
branch, no DI registration, no configuration key, no database change, no endpoint, and no client
work. It touches three shared lines — the relay's display label for the type, and two additive
fields on the capture's own result so the finding can name the imagery source it actually used.

## Technical Context

**Language/Version**: C# / .NET 10 (backend only; the React client is untouched)

**Primary Dependencies**: MediatR (the capture query is dispatched in-process), EF Core (the
analysis record read and the document write), NSubstitute/xUnit (tests). No new package.

**Storage**: SQL Server via EF Core; the capture bytes land in `IFileStorage` through the existing
`DocumentUploadFinalizer`. **No migration.**

**Testing**: xUnit + NSubstitute. Tool tests mirror `EnvironmentalContextToolTests` (real composer,
substituted repository/relay, scope factory over a `ServiceCollection`); the capture query's
`Source` is asserted in `CaptureMapQueryHandlerTests`.

**Target Platform**: ASP.NET Core web application, background workflow branch.

**Project Type**: Web application — Clean Architecture backend; no frontend change.

**Performance Goals**: None new. The finding is delivered when the capture finishes; the path is
bounded by the existing capture's own provider timeouts and the workflow's execution limits. The
capture is one provider request, replacing one image-generation request — not an added cost.

**Constraints**: In-process only (no HTTP endpoint, no service key, no rate-limit bucket); exact
frozen coordinates (no geocoding); administrator-configured source/crop/zoom/view size (name none
of them); satellite mode only; one image; provenance last; every failure recorded through the
relay and silent to the user; confidence through `SiteAnalysisConfidence.ResolveConfidence`; the
capture stays ephemeral, the Document is the only trace.

**Scale/Scope**: One tool class rewritten, one prompt class deleted, three shared lines changed,
one new test class, two test files extended, four documentation files updated. Per-user,
per-conversation analyses, as today.

**Unknowns**: None outstanding. The attribution question was resolved by reading SPEC-083,
SPEC-084, `MapCaptureDefaults` and ADR 0020 (research D11); the source-reporting question by
reading `CaptureMapQueryHandler` and `MapImageResult` (research D9).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| # | Principle | Assessment |
|---|---|---|
| §2 I | Clean Architecture / Dependency Rule | **PASS.** The tool stays in Application and composes existing Application ports (capture query, repository, relay, finalizer). Infrastructure is untouched — no renderer, hub, or provider changes. |
| §2 II | SOLID | **PASS.** One class's internals change; the capture remains behind its existing query and is reused rather than reimplemented (OCP). The specialist's public identity is unchanged. |
| §2 III | DRY / KISS / YAGNI | **PASS.** Reuses the capture, the finalizer, the composer, the relay and the confidence helper. Deletes dead prompt code rather than retaining it "just in case". No fallback path, no second provider, no new configuration. |
| §2 IV | Composition over inheritance | **PASS.** No inheritance introduced; the tool composes injected ports as before. |
| §2 V | Dependency inversion & testability | **PASS.** Every dependency is an Application abstraction and is faked in tests; no live imagery provider is required (FR-034). |
| §2 VI | Separation of concerns | **PASS.** The tool orchestrates; the capture renders; the finalizer persists; the relay validates, persists and delivers. No business logic moves into a controller (no controller changes at all). |
| §2 VII | Convention over configuration | **PASS.** Mirrors SPEC-088's tool shape — isolated scope for DB work, composer for content, relay for reporting, `MapCaptureFailureReason` for failures. No new convention. |
| §2 VIII | No Silent Failures (NON-NEGOTIABLE) | **PASS.** Every capture failure and every unexpected fault is recorded through `ReportFailureAsync` with its reason and detail, structure-logged by the relay, and returned as an `AgentToolResult.Failure` for the workflow's audit trail. User silence is SPEC-057 FR-024's deliberate contract, not a swallowed error. No async call is left unawaited. |
| §3 | Architecture rules (CQRS, repositories, DI) | **PASS.** The capture is a query dispatched through MediatR; the analysis record is read through its aggregate repository; the document is committed once through `IUnitOfWork`. No `IQueryable` escape hatch. |
| §5 | Database | **PASS.** No schema change, no migration, no new entity. The Document is an existing platform row. |
| §6 | API standards | **PASS.** No endpoint is added or changed; the capture endpoint's wire contract is unchanged. |
| §7 | UI principles | **PASS.** The client is unchanged — the finding uses the existing block vocabulary and the existing document route. Accessibility is covered by the required alt text. |
| §8 | Security | **PASS.** The capture location comes from the server-side record, never user/model text; the image reaches the client only as a platform file id; the document is owned by the analysis's user and validated by magic bytes; no provider URL is persisted or exposed. The external endpoint's service-key and rate-limit controls are neither used nor modified. |
| §9 | AI principles | **PASS.** This change **removes** an AI call; the versioned prompt is superseded and deleted from code, with its contract marked Superseded. No prompt, no model, no provider is introduced. |
| §10 | Testing | **PASS.** Unit tests ship with the change (FR-033/FR-034): success and persistence, each failure reason, coordinate sourcing, confidence, document failure, provenance. No performance goal is stated, so no performance test is required. |
| §13 | Documentation | **PASS.** SPEC-057's plan Follow-ups and tasks notes, the schematic contract, this feature's contract, and `docs/ARCHITECTURE.md` §33 are updated as requirements (FR-035–FR-038). |
| §14 | Observability | **PASS.** `CaptureMapQueryHandler` already logs start/finish with the caller name; the specialist passes a caller name identifying the analysis, so a capture is traceable to its finding. The relay logs the failure with structured properties. |
| §17 | Decision making | **PASS — no ADR required.** Contained, reversible, introduces no datastore and no cross-cutting pattern. The decision record lives in the spec's Clarifications and in §33. |

**Gate result: PASS.** No violations to track.

**Post-Phase 1 re-check: PASS.** The design adds no cross-aggregate reference and no new port. The
one additive change inside SPEC-083's code (`MapImageResult.Source`) is a read-only field on an
Application result type; it changes no provider, endpoint, credential or capture path.

## Project Structure

### Documentation (this feature)

```text
specs/090-real-map-capture/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 — decisions and their basis
├── data-model.md        # Phase 1 — the finding's data, no schema change
├── quickstart.md        # Phase 1 — end-to-end validation guide
├── contracts/
│   └── site-map-capture-tool.md   # The specialist's contract (replaces the prompt contract)
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── AskLucy.Application/
│   ├── SiteAnalysis/
│   │   ├── Tools/
│   │   │   └── SiteSchematicImageGenerationTool.cs      # REWRITTEN (class name and tool name kept)
│   │   ├── SiteAnalysisPrompts.cs                       # DELETED (only consumer is removed)
│   │   └── SiteAnalysisResultRelay.cs                   # MODIFIED — display label only ("site map")
│   └── MapCaptures/
│       ├── MapImageResult.cs                            # MODIFIED — additive read-only Source
│       └── Queries/CaptureMap/CaptureMapQueryHandler.cs # MODIFIED — sets Source on success

tests/
└── AskLucy.Application.Tests/
    ├── SiteAnalysis/
    │   ├── SiteSchematicImageGenerationToolTests.cs     # NEW
    │   └── SiteAnalysisResultRelayTests.cs              # MODIFIED — display-label assertion
    └── MapCaptures/CaptureMapQueryHandlerTests.cs       # MODIFIED — Source assertion

specs/057-site-analysis-agent/
├── plan.md                                              # MODIFIED — Follow-ups records this change
├── tasks.md                                             # MODIFIED — follow-up notes
└── contracts/schematic-image-prompt.md                  # MODIFIED — marked Superseded

docs/ARCHITECTURE.md                                     # MODIFIED — §33 + the image-generation note
```

**Structure Decision**: existing Clean Architecture layout, by feature. No file is added except
the tool's test class; no folder is added. No change under `Infrastructure`, `Persistence`, `Web`,
or `ClientApp`.

## Key Implementation Notes

These are the traps; each one is verified against the shipped code.

1. **Resolve everything that touches the database from one isolated scope — including `ISender`.**
   Parallel workflow branches share the job's scoped `DbContext`, which is not safe for concurrent
   use even for reads (SPEC-057 research D2; `SiteEnvironmentalContextTool` does the same). The
   capture's handler resolves `IToolSettingsReader` → `IToolSettingRepository`, so dispatching
   `ISender` from the branch's scope would run the capture on the shared `DbContext`. Create one
   scope, and resolve `ISiteAnalysisRepository`, `ISender`, `DocumentUploadFinalizer` and
   `IUnitOfWork` from it.
2. **Pass coordinates, never the name.** `CaptureMapQuery` takes `Location` for the geocoding path.
   Pass `Location: null` and the record's `Latitude`/`Longitude`. The handler's precedence is
   coordinates-win, but a populated `Location` would invite a future refactor to start geocoding a
   point that is already exact.
3. **Name no source, crop, zoom or view size.** Pass `Source: null`, `CropPixels: null`,
   `Zoom: null`, `ViewSizeMetres: null` so `CaptureMapQueryHandler` resolves the administrator's
   configured defaults (and the catalogue's defaults for anything never saved). Name only
   `Mode: "satellite"` — explicitly, so a change of capture default cannot silently change the
   finding — and pass no overlays.
4. **A `MapImageResult` is success-with-bytes or failure-with-reason, never both.** Do not compose
   content, do not create a document, and do not call `ReportSuccessAsync` unless `IsSuccess` and
   `Png` is non-null. If that impossible state occurs, report a specialist failure rather than
   delivering an empty finding (FR-020).
5. **The document belongs to the analysis's user.** Use the record's `UserId`, not
   `context.UserId`. They should agree, but the record is the aggregate's own owner, and the
   relay's document requirement exists to bind the finding to the analysis.
6. **The file extension must be exactly `.png`.** The upload validator sniffs magic bytes and
   rejects a PNG whose name says otherwise (`MapSnapshotCapability` carries the same note).
7. **The finalizer only stages.** Call `IUnitOfWork.SaveChangesAsync` after it, as the current
   tool does, or the document id points at a row that was never written.
8. **Handle the duplicate path.** `result.IsDuplicate ? result.DuplicateOfDocumentId!.Value :
   result.Document!.Id` — the checksum de-duplicator may already hold the same bytes.
9. **Rethrow cancellation; catch everything else.** Keep the existing
   `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }`
   and the generic catch that reports through the relay. Do not add a catch that only logs.
10. **Confidence comes from the helper with literal grounding facts.** Call
    `SiteAnalysisConfidence.ResolveConfidence(dataDirectlyDescribesSite: true,
    dataIsSparseOrApproximate: false)` with a comment stating why (exact frozen point, real
    imagery, nothing interpolated) and that `GeocodingCandidate.Importance` is never involved.
11. **The relay still requires a document for this type.** `AnalysisTypesRequiringDocument`
    contains `SchematicImage`; a finding without a persisted document is rejected. This is one
    more reason not to report success before the write commits.
12. **Delete the prompt completely.** Remove `SiteAnalysisPrompts` and every reference
    (`BuildSchematicImagePrompt`); `grep` for both names must return nothing outside the
    superseded contract and the 057 history.
13. **Keep the class name.** `SiteSchematicImageGenerationTool` and its `Name`
    (`site_analysis_schematic_image`) stay, so the published system workflow, the provisioner's
    branch and the DI registration are untouched. The class name mirrors the stable tool name in
    this codebase (`SiteEnvironmentalContextTool` ↔ `site_analysis_environmental_context`); its
    XML documentation and `Description` are rewritten to describe the capture.
14. **The heading and the chat notice must stop saying "schematic".** The heading becomes
    **Site Map**; `FormatAnalysisTypeName` in the relay returns **site map**. The enum member is
    unchanged, so stored rows, the API and the client are unaffected.

## Additive-only check

| Wire-up element | Change |
|---|---|
| `SiteAnalysisType.SchematicImage` | None |
| `SiteAnalysisSpecialists` declaration | None |
| `SystemWorkflowProvisioner` branch (`site_analysis_schematic_image` / `schematic_image`) | None |
| Tool DI registration (`AddScoped<IAgentTool, SiteSchematicImageGenerationTool>`) | None |
| `RequestSiteAnalysisCapability` / expected-result count | None |
| Tool input/output schemas | None |
| Client | None |
| Database / migration | None |
| Configuration keys | None |

Two shared lines change, both recorded in the spec: `MapImageResult.Source` (FR-017) and the
relay's display label (FR-014). Neither is a wire-up change; both are required by the finding's
honesty requirements.

## Complexity Tracking

No constitution violations. No ADR required. The two shared-line changes are recorded and
justified in [spec.md](./spec.md) FR-014/FR-017 and in research D9/D10.
