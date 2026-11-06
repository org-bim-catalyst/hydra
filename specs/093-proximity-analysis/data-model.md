# Data Model: Proximity Analysis

This feature adds transient request/result contracts and viewer-layer state. It does not require a new persisted database entity in v1.

## ProximityRequest

Represents one validated request to analyze the distance relationship between geographic features.

| Field | Type | Rules |
|---|---|---|
| operation | enum: Within, Beyond, Buffer, DistanceBands | Required; supported operations only. |
| sourceFeature | FeatureSelector | Required: category or explicit geometry/dataset selector. |
| targetFeature | FeatureSelector | Required for relational operations; may be an explicit geometry or category. |
| studyArea | Geometry/reference | Required for area selection and bounded complement (“beyond”) results; must be authorized and bounded. |
| thresholds | DistanceThreshold[] | One or more positive, finite distances; convert to meters for calculation. |
| requestedUnit | enum/string | Supported units include m and km; preserve for display. |
| requestContext | existing conversation/map context reference | Optional; use only when unambiguous and authorized. |

### FeatureSelector

- `category`: normalized category identifier or user-facing category text.
- `geometry`: optional point, line, or polygon geometry when explicitly provided/resolved.
- `dataSource`: optional source selection constrained to available/configured sources.

### DistanceThreshold

- `lowerBoundMeters`: optional non-negative finite lower bound.
- `upperBoundMeters`: required positive finite upper bound when applicable.
- `label`: optional display label; otherwise derived from normalized bounds.
- Ranges are lower-inclusive and upper-exclusive; the final requested range includes its upper bound. Ranges MUST be ordered, non-overlapping, and have positive width.

## ProximityAnalysisResult

An ephemeral result returned to Lucy and the viewer.

| Field | Type | Rules |
|---|---|---|
| analysisId | opaque identifier | Unique for the active analysis/session. |
| resolvedRequest | ProximityRequest summary | Describes operation, categories/geometries, thresholds, units, and area actually used. |
| status | enum: Complete, Partial, Empty | Empty means successful analysis with zero matches; failures are errors, not empty success. |
| resultFeatures | SpatialFeature[] | Geometry plus safe, relevant attributes; geometries must be valid and within output scope. |
| resultCount | integer | Equals returned feature count after duplicate handling. |
| distanceBands | DistanceBandResult[] | Present when multiple bands requested; each feature assigned consistently to a single band. |
| dataSources | DataSourceCoverage[] | Source identity and coverage/completeness information. |
| warnings | string[] | User-readable limitations, excluded invalid geometry counts when material, or partial coverage. |

### SpatialFeature

- `id`: stable source identifier when available; otherwise result-scoped identifier.
- `category`: normalized feature category.
- `geometry`: supported point/line/polygon geometry.
- `attributes`: allowlisted relevant source attributes; no physical paths or unauthorized metadata.
- `distanceMeters`: nearest relevant distance when meaningful.
- `bandId`: optional associated distance band.

### DataSourceCoverage

- `sourceId`, `displayName`, `coverageStatus` (Complete, Partial, Unavailable), and optional user-safe explanation.
- Unavailable sources do not silently count as successful complete coverage.

## AnalysisLayer (viewer session state)

- `id`: unique layer identifier.
- `analysisId`: owning analysis.
- `name`: descriptive, user-visible layer name.
- `kind`: Buffer, MatchingFeatures, QualifyingArea, or DistanceBand.
- `geometry/features`: renderable result geometry/features.
- `visible`: whether currently displayed.
- `styleKey`: stable category/band style reference compatible with viewer theming.
- `legendLabel`: synchronized display label.

Layer transitions: created visible after successful analysis; visibility toggles without changing result data; remove deletes one layer from the active viewer; clear removes all layers for the selected analysis. Switching 2D/3D must preserve layer state.

## Validation and calculation invariants

- Reject missing required selectors, missing bounds, non-finite/non-positive distances, reversed/overlapping bands, unsupported units, invalid geometry, and unbounded area-complement requests with actionable user feedback.
- Convert units before processing; calculate spatial distances in meters with geodesic or locally appropriate projected methods.
- `within`: distance <= threshold. `beyond`: distance > threshold, clipped to the defined study area for area outputs.
- Deduplicate by stable source identity when available; do not merge distinct features merely because geometries coincide.
- Include partial-coverage status/warnings whenever a required source is incomplete; cancellation and provider errors must not be represented as Empty.
