# Data Model: Real Map Capture for the Site Map Specialist

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

This feature adds **no persisted entity, column, index or migration**. It changes what one
specialist writes into an existing result row and content document, and adds one read-only field
to an in-memory result type.

## Persistence impact

| Type | Change |
|---|---|
| `SiteAnalysis` (aggregate) | None. Read for `UserId`, `SiteName`, `Latitude`, `Longitude`. |
| `SiteAnalysisResult` (child) | None. The `SchematicImage` row is written as today: `ContentJson`, `DataSource`, `ConfidenceLevel`, `DocumentId`, `GeneratedAtUtc`-equivalent `CreatedAtUtc`. |
| `Document` / `DocumentVersion` | None. One row per capture, or the existing row when the bytes de-duplicate. |
| `Workflow` / `WorkflowExecution` | None. The branch and its input are untouched. |
| EF migration | **None.** |

## Capture request (values the specialist supplies)

| `CaptureMapQuery` field | Value | Why |
|---|---|---|
| `Location` | `null` | The name path is not used: no geocoding, no candidate, no chance of a different place. |
| `Latitude` / `Longitude` | The record's frozen point | Exact site, fixed at dispatch. |
| `ViewSizeMetres` | `null` | The capture's configured default (1 km neighbourhood scale) applies. |
| `Mode` | `"satellite"` | The only mode the default source serves; named explicitly so it cannot drift. |
| `Overlays` | `null` | No annotations; the image is shown, not read. |
| `CallerName` | `"site-analysis:{siteAnalysisId}"` | Ties the capture's own structured logs to the finding. |
| `Source` | `null` | Administrator-configured default. |
| `CropPixels` | `null` | Administrator-configured default. |
| `Zoom` | `null` | Administrator-configured default (or view-size-to-fit). |

The handler resolves `Source` to a concrete `MapCaptureSource`; on success it reports it back on
`MapImageResult.Source` (new, read-only).

## The finding (`SiteAnalysisResult` of type `SchematicImage`)

| Field | Value |
|---|---|
| `AnalysisType` | `SchematicImage` (unchanged) |
| `Status` | `Completed` when delivered; `Failed` when the capture or the write fails; `Rejected` if the relay's gate refuses it (existing behaviour) |
| `ContentJson` | The content-block document below |
| `DataSource` | `"map-capture:{wire}"` — `map-capture:esri` or `map-capture:google` |
| `ConfidenceLevel` | `High` on a successful capture (rule below) |
| `DocumentId` | The persisted capture document — required for this type |
| `FailureReason` | Populated only for `Failed`/`Rejected`; carries the capture reason and its detail |

### Content document

Envelope: `{"version":1,"blocks":[...]}` — the existing vocabulary and envelope; no new block
kind.

| # | Block | Required content |
|---|---|---|
| 1 | `heading` (`level: 2`) | `Site Map` |
| 2 | `text` | The capture statement: the platform's map capture of the site, centred on the site's coordinates; the provider's most recent available imagery; not live; no capture date. Plus a source credit if the configured source requires one. |
| 3 | `image` | `fileId` = the persisted document id; `alt` = a meaningful 1–300 character description of the map and site, with no liveness or date claim. |
| 4 | `keyValue` (provenance) | `Analysis: SchematicImage`, `Data source: map-capture:{wire}`, `Confidence: High`, `Generated: {UTC timestamp}`. Always last. |

No `divider`, no second image, and no URL anywhere.

## Confidence rule

Evaluated by `SiteAnalysisConfidence.ResolveConfidence`; the arguments are the literal grounding
facts of this path.

| Grounding fact | Value | Basis |
|---|---|---|
| `dataDirectlyDescribesSite` | `true` | The frame is centred on the exact frozen coordinates; no geocoding, no candidate; the pixels are a real record of the place. |
| `dataIsSparseOrApproximate` | `false` | Nothing was interpolated, substituted from a nearby point, or generated. |
| Result | `High` | The helper's rule: directly describes → High. |

Undatedness and non-liveness affect the **temporal** claim only; the finding states that limit,
so the label is not a currency claim. Confidence does not depend on the imagery source.
`GeocodingCandidate.Importance` is not available on this path and is never used.

## Failure rows

A capture failure is persisted by the relay as a `Failed` result — no content, no document, no
notice, no panel. The recorded reason names the capture's own value and retains its detail.

| `MapCaptureFailureReason` | Reaches this path when | Recorded reason |
|---|---|---|
| `LocationNotFound` | Not expected with coordinates; mapped uniformly | `LocationNotFound: {detail}` |
| `GeocodingUnavailable` | Not expected with coordinates; mapped uniformly | `GeocodingUnavailable: {detail}` |
| `ModeUnavailable` | The configured source cannot serve satellite (not either current source) | `ModeUnavailable: {detail}` |
| `OverlayUnavailable` | Not expected with no overlays; mapped uniformly | `OverlayUnavailable: {detail}` |
| `ZoomUnavailable` | The administrator's configured zoom exceeds the source's range | `ZoomUnavailable: {detail}` |
| `ProviderUnavailable` | The imagery provider fails, times out, or returns a non-image | `ProviderUnavailable: {detail}` |
| `CropTooSmall` | The configured crop leaves no image | `CropTooSmall: {detail}` |
| `ImageTooLarge` | The encoded PNG exceeds the cap | `ImageTooLarge: {detail}` |

If the analysis record cannot be read, or the document write fails, or any other fault occurs,
the failure is recorded with the fault's message (and the exception as its cause); the capture
vocabulary is not misused for those.

## Invariants

1. A finding is delivered **only** when the capture succeeded **and** its bytes were persisted
   **and** the document id is non-null.
2. The image block's `fileId` is the persisted document id — never a provider URL, data URL or
   path.
3. The document's owner is the `SiteAnalysis` record's user.
4. Exactly one `SchematicImage` result exists per analysis (the aggregate enforces it).
5. Exactly one image block exists per finding, and the provenance block is last.
6. The capture itself persists nothing; the Document is the only trace.
7. Confidence is a function of grounding facts alone.
