# Density Surface Layer Event Contract

**Transport**: existing authenticated `__SPATIAL_LAYERS__` event. **Layer kind**: `density-surface`.

```json
{
  "layerId": "density-density-01",
  "sourceOperation": "density-01",
  "name": "Hospital density in Al Khuwair",
  "legendLabel": "Hospitals — kernel density",
  "kind": "density-surface",
  "visible": true,
  "density": {
    "method": "point-kernel", "unit": "features/km²",
    "effectiveSearchRadiusMeters": 1000, "effectiveCellSizeMeters": 250,
    "classification": { "method": "quantile", "breaks": [2.1, 5.4, 9.8], "labels": ["Low", "Medium", "High", "Very high"] },
    "cells": [{ "id": "cell-1", "ring": [], "value": 9.8, "classificationIndex": 3, "classificationLabel": "Very high", "sourceCount": 4 }],
    "coverage": "complete", "warnings": []
  },
  "visualization": { "opacity": 0.7, "palette": "warm", "visibleMin": 0, "visibleMax": 25 }
}
```

The client validates finite values, rings, break order, class indices, palette, and opacity before state update. It renders a heatmap where supported or graduated cells otherwise, with the same accessible legend. Invalid payload or rendering errors surface visibly with retry/re-run guidance, never console-only.
