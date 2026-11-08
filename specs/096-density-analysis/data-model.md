# Data Model: Density Analysis

All records are transient Application contracts or viewer-session state. No database entity or migration is introduced.

## DensityAnalysisParameters

Carried by `SpatialOperation` when `operationKind = "density.analysis"`.

| Field | Validation |
|---|---|
| `datasetId` | Required; must resolve in Density Dataset Catalog. |
| `intent` | `high`, `low`, `coverage-gap`, or `population`; required after interpretation. |
| `method` | `auto`, `point-count`, `point-kernel`, or `population`; auto only when unambiguous. |
| `searchRadiusMeters` | Point-kernel only; finite, positive, within limit after defaulting. |
| `cellSizeMeters` | Gridded point methods; finite, positive, within limit after defaulting. |
| `classification` | `continuous`, `equal-interval`, `quantile`, or `natural-breaks`. |
| `visualization` | Optional validated display preferences. |

`StudyArea` must resolve to a bounded `ResolvedStudyArea` before provider invocation.

## DensityDatasetEntry

| Field | Meaning |
|---|---|
| `id`, `displayName` | Stable identity and user-facing name. |
| `sourceId`, `providerId` | Provenance and provider routing. |
| `geometrySupport`, `supportedMethods` | Compatibility declaration. |
| `populationValueField`, `areaField` | Required only for population density. |
| `canonicalUnit` | Output unit, such as features/km² or people/km². |

## DensityAnalysisDefinition

Validated immutable input: correlation IDs, resolved dataset/study area, intent/method, effective radius/cell size, classification, and deadline.

## DensityCell

Stable cell ID; valid polygon clipped to the study area; finite non-negative value and canonical unit; optional class index/label and contributing source count.

## DensityClassificationResult

Classification method, ordered finite break values (empty for continuous scale), and accessible low-to-high labels mapped to rendered colours.

## DensityAnalysisResult

Distinguishable `complete`, `partial`, `empty`, `failed`, or `cancelled` status; cells, classification, hotspots/low-density summaries, coverage/provenance/warnings, and a renderable layer descriptor.

## DensityVisualizationPreferences

Session-only opacity (0–1), allowlisted palette, finite `visibleMin`/`visibleMax` range, and compatible display classification. These do not alter calculated values.

## DensityAuditEntry

Immutable structured record: authenticated user, bundle/operation/result IDs, resolved dataset/study area, requested/effective method and parameters, coverage, duration, result summary, provenance, and terminal outcome/reason.
