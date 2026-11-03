# Contract: Site Geometry Specialist

**Consumer**: `SystemWorkflowProvisioner`'s published fan-out, through `NativeToolNodeExecutor`.
**Registered name**: `site_analysis_site_geometry` (node key `site_geometry`).

## Tool descriptor

| Member | Value |
|---|---|
| `Name` | `site_analysis_site_geometry` |
| `RiskLevel` | `Low` |
| `RequiredPermissions` | `[AgentToolPermission.ExternalNetwork]` |
| `InputSchemaJson` | `{"type":"object","required":["siteAnalysisId","siteName","siteLocation"],"properties":{"siteAnalysisId":{"type":"string"},"siteName":{"type":"string"},"siteLocation":{"type":"string"}}}` |
| `OutputSchemaJson` | `{"type":"object","required":["reported"],"properties":{"reported":{"type":"boolean"}}}` |

The schema is identical to the other two specialists': the workflow's shared node input template
already supplies these three tokens, and the tool reads coordinates and the boundary from the
`SiteAnalysis` record instead of adding a fourth input the provisioner would have to vary per branch.

## Execution contract

1. Parse `siteAnalysisId`; a missing/invalid id returns `AgentToolResult.Failure` **without** a relay
   call (there is nowhere to report it).
2. Resolve `ISiteAnalysisRepository` from a fresh `IServiceScopeFactory` scope and read the analysis.
   Not found → `ReportFailureAsync("The site analysis this finding belongs to could not be found.",
   cause: null)` + `AgentToolResult.Failure`.
3. Parse `BoundaryGeoJson` (`SiteBoundarySnapshot`). A parse failure is logged (Warning) and becomes an
   unavailable-figure reason — never a silent "no boundary".
4. Call `ISiteGeometryDataProvider.ResolveAsync(center, ring, ct)` — one bounded Overpass query.
5. Compute `SiteGeometryMeasurements` with `SiteGeometryCalculator`.
6. Compose the finding with `SiteAnalysisContentComposer.ComposeSections`.
7. `ISiteAnalysisResultRelay.ReportSuccessAsync(siteAnalysisId, SiteAnalysisType.SiteGeometry,
   metadata, content, documentId: null, ct)` — **inline**, as the last substantive step.
8. Return `AgentToolResult.Success({"reported":true})`.

## Result metadata

| Field | Value |
|---|---|
| `AnalysisType` | `SiteGeometry` |
| `SiteName` | from the record |
| `DataSource` | `"OpenStreetMap (Overpass)"` when any source figure was obtained; `"OpenStreetMap (Overpass) — unavailable"` when every source figure is unavailable; `"the analysis's resolved site boundary"` when no source call succeeded but boundary figures were measured |
| `ConfidenceLevel` | per the table in `data-model.md` § 5 |
| `GeneratedAtUtc` | `DateTime.UtcNow` |
| `Notes` | one line per unavailable figure, e.g. `Frontage: no named street within 25 m of the site boundary` |

## Failure contract

| Case | Relay call | Tool result |
|---|---|---|
| Analysis not found | `ReportFailureAsync(reason, cause: null)` | `Failure` |
| Unexpected exception | `ReportFailureAsync(ex.Message, ex)` | `Failure` |
| Caller cancellation | none — rethrow `OperationCanceledException` | — |
| Source unavailable | **`ReportSuccessAsync`** with source-dependent figures marked unavailable | `Success` |
| No boundary (none recorded / unreadable) | `ReportSuccessAsync` with shape figures unavailable | `Success` |

A source outage is not a specialist failure: the finding still carries every figure the platform can
compute without the network, and each withheld figure states why. This mirrors
`SiteEnvironmentalContextTool`'s unavailable-figure pattern and the spec's US3.

## Content contract

- Block kinds: `heading`, `text`, `divider`, `keyValue` only. No `image`.
- Provenance block last, produced by `SiteAnalysisContentComposer.BuildProvenanceBlock`.
- Sections: `Measurements`, `Frontage`, `Boundary`.
- The finding states, in the body, that figures are measured from the site boundary resolved for this
  analysis unless a recorded parcel outline exists.
- When the boundary looks like the platform's manual-fallback buffer, the finding says the figures
  describe that approximate buffer, not a measured parcel.

## Non-goals in this contract

- No chat message, no panel push, no SignalR call — delivery is the relay's alone.
- No document/file output, no map capture, no image generation.
- No second site resolution, no geocoding, no re-run of `IBoundaryResolutionService`.
