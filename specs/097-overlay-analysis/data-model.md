# Data Model: Overlay Analysis

The overlay model is request-scoped and carried within the current Spatial Request Bundle. It introduces no durable analysis tables.

## OverlayOperation

One coordinated operation request.

| Field | Type | Rules |
|---|---|---|
| operationId | string | Required, unique within its bundle. |
| operationType | string | `overlay.analysis`. |
| studyArea | resolved area reference | Required and bounded before provider execution. |
| inputs | OverlayInput[] | At least two catalogued inputs for geometric operations; every reference is unique. |
| steps | OverlayStep[] | At least one supported step; referenced inputs/steps must exist and be dependency ordered. |
| criteria | OverlayCriterion[] | Typed, catalog-backed condition; threshold and unit must be compatible with the selected field/source. |
| scoring | ScoringProfile? | Optional; valid only for rankable candidate results. |

## OverlayInput

| Field | Type | Rules |
|---|---|---|
| ref | string | Unique request-local name used by steps and criteria. |
| datasetId | string | Must resolve in the existing catalog. |
| role | enum | Candidate, criterion, constraint, or reference. |
| geometryType | point/polygon/linestring | Resolved from provider feature geometry; must be supported by the operation. |
| coordinateReference | implicit WGS84 in v1 | Existing `GeoPoint` and map contracts carry WGS84 latitude/longitude; sources that cannot provide WGS84 coordinates are unsupported until provider CRS metadata/transforms exist. |
| coverage | complete/partial/unavailable | Preserved from the source and reflected in final result status. |

## OverlayStep

| Field | Type | Rules |
|---|---|---|
| stepId | string | Required, unique within the operation. |
| kind | enum | Intersection, union, difference, or spatialIntersection. |
| inputRefs | string[] | Must refer to declared inputs or completed prior steps. |
| relationship | intersects/within/contains | Required for spatialIntersection; must be supported by the source geometry. |
| differenceDirection | ordered refs | Required for difference; first input is retained and second is subtracted. |
| outputRef | string | Names the result consumed by dependent steps and criteria. |

## OverlayCriterion

| Field | Type | Rules |
|---|---|---|
| criterionId | string | Unique within operation. |
| kind | attribute/area/distance/spatialRelationship | Must match available catalog field or declared layer geometry. |
| inputRef | string | Must identify an input or prior output. |
| operator/value/unit | typed predicate | Operator and unit must be compatible; thresholds must be finite and within operational limits. |
| required | boolean | Required criteria are hard eligibility conditions; false denotes an optional ranking preference. |
| scoreDirection | higher/lower/band | Required for scored criteria; states how normalized values contribute. |

## ScoringProfile

Contains criterion ID to weight mappings and the normalization strategy. All weights must be finite, non-negative, sum to a positive value, and name criteria that exist. The effective normalized weights, per-criterion scores, aggregate score, and deterministic rank are returned. A failed required criterion excludes the candidate before scoring.

## OverlayResult / OverlayResultFeature

An outcome carries request and operation IDs, status, resolved study area, effective inputs and criteria, per-step counts, result features, coverage, warnings, provenance, and renderable layer descriptors. Each result feature carries stable source/output identity, supported geometry, display attributes, eligibility outcomes, optional criterion scores/aggregate/rank, and source lineage. Empty results have empty features with `empty` status; unavailable or invalid execution does not masquerade as empty.
