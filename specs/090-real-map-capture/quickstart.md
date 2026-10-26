# Quickstart: Real Map Capture for the Site Map Specialist (SPEC-090)

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | Contract: [site-map-capture-tool.md](./contracts/site-map-capture-tool.md)

Runnable validation scenarios that prove the change end to end. Shape and rules live in the
contract and [data-model.md](./data-model.md); this file only says how to check them.

## Prerequisites

- .NET 10 SDK, repository restored (`dotnet restore`).
- SQL Server reachable with the configured connection string.
- A map-capture source that can serve the area (Esri by default; no key is required for the
  export endpoint, though one may be configured).
- An AI capability assignment is **no longer needed** for this specialist: it makes no image-
  generation call. Leave `ImageGeneration` unassigned and this finding must still succeed.
- For persistence-backed tests: `PERSISTENCE_TESTS_CONNECTION_STRING` set (destructive against a
  dev database; set `PERSISTENCE_TESTS_DEDICATED_DATABASE=1` only deliberately). This variable is a
  **GitHub Actions secret**, not something a local shell already has — derive it from
  `ConnectionStrings:DefaultConnection` in `src/AskLucy.Web/appsettings.Development.json` (the
  commands are in `docs/TESTING.md` § "Two test databases" and the project `CLAUDE.md`). An empty
  shell variable does not mean it is unconfigured.
- No frontend build is required for the feature; the client is unchanged.

## Build and automated checks

```bash
dotnet build
dotnet test tests/AskLucy.Application.Tests --filter FullyQualifiedName~SiteSchematicImageGenerationToolTests
dotnet test tests/AskLucy.Application.Tests --filter FullyQualifiedName~CaptureMapQueryHandlerTests
dotnet test tests/AskLucy.Application.Tests --filter FullyQualifiedName~SiteAnalysis
```

Expected:

- **Tool tests** green: capture success persists a document and reports the file id; content is
  heading/text/one image/provenance in that order; provenance names the source and reports High;
  each of the eight failure reasons is recorded as a specialist failure with nothing delivered;
  the capture receives the record's coordinates and `Location: null`; a document-write failure is
  recorded; a duplicate reuses the de-duplication result.
- **Capture handler tests** green, including the new assertion that a success carries the resolved
  `Source`.
- **SiteAnalysis tests** green, including the relay's display label ("site map") and the existing
  two-specialist progressive delivery.

Repo-wide gate when preparing to merge:

```bash
dotnet format --verify-no-changes
dotnet test        # with PERSISTENCE_TESTS_CONNECTION_STRING set
```

Known local noise: `dotnet format` reports `ENDOFLINE` false positives here; verify against CI
rather than chasing them. Ensure `using System.*` directives come first. The frontend is
unchanged, so no `tsc`/`npm test` is required by this feature — run the repo-wide client gate only
if something else changed.

## Scenario 1 — The finding shows the real site

1. Start the app (`dotnet run --project src/AskLucy.Web`) and sign in.
2. Put a site in the viewer (or name it), then ask Lucy to analyse it.
3. Open the **Site Map** finding.

Expected:

- The panel heading is **Site Map**, not "Schematic Site Map".
- The chat notice reads *"Receiving the site map…"*, not "schematic site map".
- The image is the platform's real imagery of the site, centred on the analysed point.
- The body states the image is the platform's map capture of the site, centred on its
  coordinates; that it is the provider's most recent available imagery; that it is **not live**;
  and that it carries **no capture date**.
- The provenance block is last and reads: `Analysis: SchematicImage`, `Data source:
  map-capture:esri` (or `:google`), `Confidence: High`, and a generation time.
- The image resolves from `/documents/{fileId}/download`; no external address appears in the
  panel or in the stored content.

## Scenario 2 — The capture is centred on the frozen point, not the name

1. Analyse a site whose name is ambiguous or would geocode elsewhere, passing explicit
   coordinates if your entry point allows it.
2. Inspect the capture's structured log line for this analysis. The caller name is
   `site-analysis:{analysisId}`.

Expected: the log shows the handle's coordinates and an empty/`null` location; there is **no**
geocoding call for the specialist's capture. The image is centred on the coordinates recorded on
the `SiteAnalysis` row:

```sql
SELECT SiteName, Latitude, Longitude FROM SiteAnalyses WHERE Id = '<analysis id>';
```

Compare with the image's centre. Renaming the site in the tool input must not move it — the tool
test asserts this directly.

## Scenario 3 — Only a platform document reaches the client

```sql
SELECT r.AnalysisType, r.Status, r.DataSource, r.ConfidenceLevel, r.DocumentId,
       d.FileName, d.OwnerId
FROM SiteAnalysisResults r
LEFT JOIN Documents d ON d.Id = r.DocumentId
WHERE r.SiteAnalysisId = '<analysis id>' AND r.AnalysisType = 'SchematicImage';
```

Expected: `Status = Completed`; `DataSource` starts with `map-capture:`; `ConfidenceLevel = High`;
`DocumentId` is populated; the document's `OwnerId` is the analysis's user. The stored
`ContentJson` contains one `image` block whose `fileId` is that document id and no URL.

Expected elsewhere: **no** capture table, capture row, or provider URL anywhere — the Document is
the only trace (SPEC-083 FR-014).

## Scenario 4 — No image generation is involved

Leave the `ImageGeneration` AI capability unassigned (Admin → AI Capabilities). Run an analysis.

Expected: the site-map finding still succeeds. There is no `AiCapabilityNotConfiguredException`
for this branch, no image-model usage entry for it, and no generated image. The prompt no longer
exists in the codebase: a search for `SiteAnalysisPrompts` / `BuildSchematicImagePrompt` returns
nothing outside the superseded contract and SPEC-057's history.

## Scenario 5 — A failed capture is recorded and silent

Reproduce a failure deterministically: on the admin AI Tools page, set the capture source to
**Esri** and the zoom to **19** (above Esri's maximum of 18), then run an analysis. Restore the
zoom afterwards. (Alternatively, block the host's network access for `ProviderUnavailable`.)

Expected:

- No panel and no chat notice for the site-map finding.
- The stored result is `Failed` with a reason beginning `ZoomUnavailable:` and the capture's
  detail. (With the network blocked: `ProviderUnavailable:`.)
- The environmental (or other) finding is unaffected.
- The analysis still reaches `Completed` and reports the partial result ("finished, with *n* of
  *m* completed") — it is not `Failed`.
- Nothing broken, empty, or placeholder appears in the user's conversation.

Repeat for a second reason (for example a zoom of 19 against Esri, then a crop too large) to
confirm the mapping is not special-cased per reason.

## Scenario 6 — Reload and rehydration

1. Deliver a finding, navigate to another conversation, then return (or reload the page).
2. Reopen the conversation.

Expected: the same panel reopens from the stored `ContentJson`, and the image renders from the
platform document route with no external request. Compare against a finding stored before this
change: it must still render unchanged.

## Scenario 7 — The source is named, and attribution is only what is required

1. Switch the configured capture source to **Google** on the admin AI Tools page and run an
   analysis.
2. Read the provenance.

Expected: `Data source: map-capture:google`, `Confidence: High` (the source changes rendering, not
grounding). No attribution line is present, because no configured source requires one today. If a
source requiring a credit is configured in future, the body text is where its credit goes — the
contract reserves that position.

## Scenario 8 — The fan-out is untouched

```bash
git diff --stat
```

Expected: no change under `src/AskLucy.Infrastructure`, `src/AskLucy.Persistence`,
`src/AskLucy.Web`, or `ClientApp`; no migration; no change to `SiteAnalysisSpecialists`, the
provisioner, `SiteAnalysisType`, the expected-result count, the tool's DI registration, or its
input/output schemas. The only shared lines changed are `MapImageResult.Source` (with the handler
setting it) and `FormatAnalysisTypeName`.

## Scenario 9 — Documentation is part of the change

- [SPEC-057 plan Follow-ups](../057-site-analysis-agent/plan.md#follow-ups) records that this
  change implements its map-capture paragraph and links this specification.
- [SPEC-057 tasks](../057-site-analysis-agent/tasks.md) follow-up notes no longer describe the
  specialist as generating its image.
- [The schematic prompt contract](../057-site-analysis-agent/contracts/schematic-image-prompt.md)
  is marked **Superseded** and points at [the new contract](./contracts/site-map-capture-tool.md).
- `docs/ARCHITECTURE.md` §33 describes the finding as a real capture and no longer lists the
  site-analysis map among `IImageGenerationService`'s callers.
