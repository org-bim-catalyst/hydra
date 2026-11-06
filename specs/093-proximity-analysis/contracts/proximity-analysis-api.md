# Proximity Analysis Contract (Design)

> **Implementation alignment (SPEC-094, 2026-11-06):** proximity is executed as the
> `proximity.analysis` specialist of the Spatial Request Bundle. There is no separate REST route;
> the versioned contract is `SpatialRequestBundle` v1 (`contracts/spatial-request-bundle.md` in
> specs/094) plus the `__SPATIAL_LAYERS__` delivery event. The request shape below is the chat
> capability's interpretation input; fields marked *bands* are the additive US3 extension.

This contract describes the required request/response semantics for the existing versioned spatial endpoint and Spatial Request Bundle. Exact route, naming, and transport MUST follow the canonical existing spatial contract discovered during implementation; no parallel endpoint is intended.

## Request

Authenticated caller submits a proximity operation through the existing spatial request path.

```json
{
  "operation": "Within",
  "sourceFeature": { "category": "school" },
  "targetFeature": { "category": "hospital" },
  "studyArea": { "type": "Feature", "geometry": {}, "properties": {} },
  "thresholds": [
    { "lowerBound": null, "upperBound": 1, "unit": "km", "label": "Within 1 km" }
  ]
}
```

- `operation`: `Within`, `Beyond`, `Buffer`, or `DistanceBands`.
- Feature selectors identify a supported category or supplied/resolved geometry and may include an allowed source selector.
- `studyArea` is required for bounded area outputs and must be an authorized, bounded geometry or an existing validated area reference.
- Thresholds must be finite and positive, use supported units (`m`, `km`), and form ordered, non-overlapping ranges for `DistanceBands`.
- Omitted/ambiguous required fields are clarified by Lucy before submission when possible; otherwise validation returns a standard client error.

## Success response

The result contains the data-model fields in [data-model.md](../data-model.md): `analysisId`, `resolvedRequest`, `status`, `resultFeatures`, `resultCount`, optional `distanceBands`, `dataSources`, and `warnings`.

Each feature provides renderable geometry, stable identifier where available, normalized category, safe relevant attributes, optional `distanceMeters`, and optional `bandId`. Coordinates/geometry use the canonical spatial endpoint's coordinate reference and encoding. `status` is `Complete`, `Partial`, or `Empty`; an empty successful result is not a provider failure.

## Failure behavior

- Authentication/authorization failures use existing API status and Problem Details conventions.
- Invalid operation, geometry, units, thresholds, or request limits return a caller-actionable validation error.
- Unsupported categories and unavailable data produce explicit user-safe failure/coverage information.
- Provider or processing failures never return `Empty` as success. Partial results are explicitly marked `Partial` and carry warnings/source coverage.
- Cancellation is propagated through the existing request path.

## Viewer behavior contract

The viewer consumes the structured result to create distinct buffer, feature, qualifying-area, and distance-band layers. It MUST support show/hide, remove-one, clear-analysis, synchronized legend, and 2D/3D continuity using existing viewer conventions. Layer state is session-scoped in v1.

## Compatibility

Add fields/operation support within the existing versioned spatial contract where backward-compatible. Any incompatible route, bundle, or payload change requires the platform's normal API versioning process. Concrete route/schema names must be aligned with the actual existing endpoint before implementation; they are intentionally not invented here.
