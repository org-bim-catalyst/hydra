# Overlay Analysis Contract

Overlay Analysis is a registered operation in the versioned Spatial Request Bundle. It is invoked by the existing authenticated chat request and delivered by the existing normalized spatial result/layer event. It does not add a standalone HTTP route.

## Bundle operation payload

```json
{
  "operationId": "overlay-1",
  "operationKind": "overlay.analysis",
  "studyArea": { "source": "currentMapBoundary" },
  "overlay": {
    "inputs": [
      { "ref": "parcels", "datasetId": "land-parcels", "role": "candidate" },
      { "ref": "population", "datasetId": "population-areas", "role": "criterion" },
      { "ref": "roads", "datasetId": "major-roads", "role": "constraint" },
      { "ref": "clinics", "datasetId": "health-facilities", "role": "constraint" }
    ],
    "steps": [
      { "stepId": "parcel-population", "kind": "spatialIntersection", "inputRefs": ["parcels", "population"], "relationship": "intersects", "outputRef": "candidate-population" },
      { "stepId": "clinic-separation", "kind": "difference", "inputRefs": ["candidate-population", "clinics"], "outputRef": "eligible" }
    ],
    "criteria": [
      { "criterionId": "available", "kind": "attribute", "inputRef": "parcels", "field": "availability", "operator": "equals", "value": "available", "required": true },
      { "criterionId": "road-access", "kind": "distance", "inputRef": "roads", "operator": "within", "value": 500, "unit": "m", "required": true },
      { "criterionId": "population", "kind": "attribute", "inputRef": "population", "field": "density", "operator": "greaterThan", "value": 1000, "unit": "people/km2", "required": true }
    ],
    "scoring": null
  }
}
```

All dataset IDs, fields, values, units, operation kinds, geometry compatibility, references, dependencies, and study-area bounds are validated against server-side catalogs. The model-provided JSON is untrusted input. Unsupported bundle versions or invalid operations produce a visible validation outcome and no execution.

## Normalized result semantics

```json
{
  "version": 1,
  "analysisId": "analysis-id",
  "operationId": "overlay-1",
  "status": "complete",
  "studyArea": { "name": "Current map boundary" },
  "steps": [{ "stepId": "parcel-population", "kind": "spatialIntersection", "resultCount": 18 }],
  "inputs": [{ "ref": "parcels", "datasetId": "land-parcels", "coverage": "complete" }],
  "criteria": [{ "criterionId": "road-access", "required": true, "effectiveValue": 500, "unit": "m" }],
  "features": [{ "featureId": "parcel-1", "geometry": { "kind": "polygon", "points": [] }, "attributes": {}, "eligibility": {}, "criterionScores": {}, "score": null, "rank": null, "provenance": [] }],
  "counts": { "matched": 12, "excluded": 6, "invalid": 0 },
  "scoring": null,
  "coverage": [],
  "warnings": [],
  "layers": [{ "layerId": "overlay-layer-id", "name": "Suitable parcels", "role": "result", "visible": true }]
}
```

Use the existing outer result status vocabulary (`Complete`, `Partial`, `Empty`, `Failed`) and operation status vocabulary (`Succeeded`, `Empty`, `Failed`), mapping to descriptive contract values as needed. Preserve distinct operation status and source coverage. Geometry reuses the existing feature geometry contract; result attributes include bounded, safe display fields only. The viewer owns visibility and removal after delivery.

## Failure behavior

- Unsupported version, invalid/missing dataset, unbounded area, incompatible geometry, invalid predicate/weight, or cyclic step dependency: reject before execution and explain the correction.
- Source unavailable or partial: return partial/failed status and coverage details; never claim complete suitability.
- Valid computation with no eligible features: return an empty outcome, not a failure.
- Timeout/cancellation, provider exception, or rendering error: return or surface a visible failure and ensure no late/stale layer is silently added.
