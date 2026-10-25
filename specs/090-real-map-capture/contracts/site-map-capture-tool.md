# Contract: Site Map Capture Tool

**Spec**: SPEC-090 | **Replaces**: [schematic-image-prompt.md](../../057-site-analysis-agent/contracts/schematic-image-prompt.md) (Superseded) | **Requirements**: FR-001–FR-038

The contract for the site-analysis specialist that shows the platform's **real map capture** of
the analysed site. It keeps the specialist's identity and delivery route from SPEC-057; what
changes is where the picture comes from, what the finding says about it, and what is recorded when
it cannot be produced.

## Identity (unchanged)

| Property | Value |
|---|---|
| Tool name | `site_analysis_schematic_image` |
| Workflow node key | `schematic_image` |
| Analysis type | `SiteAnalysisType.SchematicImage` |
| Registration | `AddScoped<IAgentTool, SiteSchematicImageGenerationTool>()` in `Application/DependencyInjection.cs` |
| Required permissions | `ExternalNetwork`, `WriteFile` |
| Input schema | `{"siteAnalysisId","siteName","siteLocation"}` — unchanged |
| Output schema | `{"reported": boolean}` — unchanged |

The class name is kept. Its XML documentation, `Description` and behaviour describe the capture,
not generation.

## Sequence

1. Parse `siteAnalysisId` from the input. An invalid id returns `AgentToolResult.Failure` **without**
   touching the relay (there is no analysis to report against).
2. Create **one** isolated DI scope. From it resolve `ISiteAnalysisRepository`, `ISender`,
   `DocumentUploadFinalizer`, `IUnitOfWork`.
3. Read the `SiteAnalysis` record by id. Missing → record a specialist failure, return failure.
4. Dispatch `CaptureMapQuery` (below).
5. Not successful → record a specialist failure, return failure. Compose nothing, persist nothing.
6. Persist `result.Png` through `DocumentUploadFinalizer` (`ownerId = record.UserId`, actor
   `system:site-analysis-map-capture`, filename `site-analysis-site-map-{guid}.png`), then
   `IUnitOfWork.SaveChangesAsync`. Persisted document id = `IsDuplicate ? DuplicateOfDocumentId :
   Document.Id`.
7. Compose the finding; call `ISiteAnalysisResultRelay.ReportSuccessAsync` inline with the document
   id; return `AgentToolResult.Success`.

Cancellation is rethrown. Any other exception is recorded through `ReportFailureAsync` (with the
exception as its cause) and returned as `AgentToolResult.Failure`. Nothing is swallowed.

## Capture query

`new CaptureMapQuery(Location: null, Latitude: record.Latitude, Longitude: record.Longitude,
ViewSizeMetres: null, Mode: "satellite", Overlays: null, CallerName: "site-analysis:{id}",
Source: null, CropPixels: null, Zoom: null)`

- **In-process only.** Never the HTTP endpoint; no service key, no rate-limit bucket.
- **No name path.** `Location` is null. The capture must not geocode: the point is already frozen
  on the record, and a name could resolve a different place.
- **No administrator value is named.** Source, crop, zoom and view size resolve inside the
  capture. Only the mode is named — satellite, because it is the default source's only mode and
  the imagery that shows the site's physical condition.
- **No overlays.** Nothing is painted onto the image.

The capture persists nothing (SPEC-083 FR-014). The specialist's Document write is the only
persistence.

## Content

| # | Block | Rule |
|---|---|---|
| 1 | `heading` (level 2) | **Site Map** |
| 2 | `text` | States: the platform's map capture of `{record.SiteName}`; centred on the site's coordinates; the imagery is the provider's most recent available capture; **not live**; **no capture date**. Appends the source's required credit if one is required. |
| 3 | `image` | `{"kind":"image","fileId":"{documentId}","alt":"..."}`. The alt text describes the map and the site meaningfully (1–300 chars) and makes no liveness or date claim. |
| 4 | `keyValue` | Provenance, always last. |

One image only. No divider, no gallery, no comparison, and no URL or external address anywhere in
the finding.

## Provenance

| Field | Value |
|---|---|
| Analysis | `SchematicImage` (enum identifier; the display label is "site map") |
| Data source | `"map-capture:{wire}"`, from `MapImageResult.Source` — `esri` or `google` |
| Confidence | `High` |
| Generated | UTC timestamp of the composition |

The source comes from the capture, never from a second settings read: the specialist names no
source, so only the capture knows which provider served the image.

## Confidence

`SiteAnalysisConfidence.ResolveConfidence(dataDirectlyDescribesSite: true,
dataIsSparseOrApproximate: false)` → `High`.

The frame is centred on the exact frozen coordinates with no geocoding; the pixels are a real
record of the place; nothing was interpolated or generated. Undatedness limits the temporal claim
— which block 2 states — not whether the image describes the site. The rule helper is the only
authority; `GeocodingCandidate.Importance` is never consulted.

## Attribution

Checked against SPEC-083 (no attribution requirement), SPEC-084 (FR-015 applies to historical,
dated images only — not this path) and the source configuration (no attribution setting). Neither
current renderer leaves an uncarried credit: the Esri export has no burned-in logo; Google's
branded overlay is removed by the configured crop (ADR 0020).

Therefore **no attribution line is required today**. The provenance names the source. If a source
whose terms require a credit is configured, block 2 carries that credit — that is the position the
finding reserves, not a line added pre-emptively.

## Failure handling

Every unsuccessful capture is a specialist failure:

- `ReportFailureAsync(siteAnalysisId, SchematicImage, "{reason}: {detail}", cause: null, ct)` for a
  returned `MapCaptureFailureReason`.
- `ReportFailureAsync(..., exception.Message, exception, ct)` for a fault.
- `AgentToolResult.Failure(...)` in both cases, so the workflow's audit trail and the
  `AnyCompleted` merge see the failure.

Reasons covered (the mapping is total): `LocationNotFound`, `GeocodingUnavailable`,
`ModeUnavailable`, `OverlayUnavailable`, `ZoomUnavailable`, `ProviderUnavailable`,
`CropTooSmall`, `ImageTooLarge`. No new exception type, no new vocabulary.

A failure delivers **nothing**: no content, no document, no notice, no panel. The analysis still
settles; the closing outcome reports the partial result as it does for any specialist failure.

## Additive change inside SPEC-083

`MapImageResult` gains `MapCaptureSource? Source { get; init; }`, set by
`CaptureMapQueryHandler` on success. Additive and read-only; no provider, endpoint, wire contract,
credential or capture-path change. It exists so the finding can name the source it actually used
and so an attribution requirement can be satisfied without a second configuration read.

## Verification items

1. `DocumentUploadFinalizer` accepts the capture PNG under a `.png` name (SPEC-057 T026 verified
   PNG support; re-confirm the filename rule — a misnamed PNG is rejected).
2. `MapImageResult.Source` is populated on the success path by the handler, for both configured
   sources.
3. `FormatAnalysisTypeName(SiteAnalysisType.SchematicImage)` returns `"site map"`, and no test or
   snapshot asserts the old string.
4. No reference to `SiteAnalysisPrompts` or `BuildSchematicImagePrompt` remains outside the
   superseded contract and SPEC-057's history.
