# Density Operation Parameters Contract

**Carrier**: versioned `SpatialRequestBundle` operation. **Operation kind**: `density.analysis`.

```json
{
  "operationId": "density-01",
  "operationKind": "density.analysis",
  "studyArea": { "kind": "namedArea", "namedArea": "Al Khuwair" },
  "densityParameters": {
    "datasetId": "hospitals",
    "intent": "coverage-gap",
    "method": "point-kernel",
    "searchRadiusMeters": 1000,
    "cellSizeMeters": 250,
    "classification": "quantile",
    "visualization": { "opacity": 0.7, "palette": "warm", "visibleMin": 0, "visibleMax": 25 }
  }
}
```

Unknown bundle versions, missing bounded scope, unknown/incompatible dataset or method, invalid numeric values, invalid classification, and invalid visualization preferences produce a visible validation outcome and do not invoke a provider. Defaults are returned as effective parameters; supplied values are never silently changed.
