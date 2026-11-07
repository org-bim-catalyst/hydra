# Contract: Feature Query Layer Event

**Spec**: SPEC-095 | **Requirements**: FR-008, FR-009, FR-010, FR-011, FR-013

Extends the existing `__SPATIAL_LAYERS__` trailing-event channel (spec 094) to support paginated
batch delivery for `feature-query` layer kind. The event shape is backward-compatible: clients that
do not recognise `feature-query` as a valid kind skip the layer (existing `isRenderable` guard).

---

## Event Type

`__SPATIAL_LAYERS__` — same event name as spec 094. Emitted once per batch by the coordinator's
trailing-event writer.

---

## Payload Shape

```json
{
  "analysisId": "a1b2c3d4-...",
  "bundleId": "0199f3a2-...",
  "status": "Partial",
  "layerMode": "keep",
  "batchIndex": 0,
  "totalCount": 180,
  "estimatedTotal": 420,
  "isFinal": false,
  "resultCount": 180,
  "resolvedSummary": "Land parcels larger than 5,000 m² within Al Khuwair Development Zone",
  "warnings": [],
  "layers": [
    {
      "layerId": "lyr-a1b2-parcels",
      "sourceOperation": "op-1",
      "name": "Parcels > 5,000 m² in Al Khuwair Zone",
      "legendLabel": "180 parcels (streaming…)",
      "kind": "feature-query",
      "style": {
        "color": "#1565C0",
        "opacity": 0.5,
        "strokeWidth": 1.5
      },
      "visible": true,
      "batchIndex": 0,
      "totalCount": 180,
      "estimatedTotal": 420,
      "isFinal": false,
      "markers": null,
      "ring": null,
      "features": [
        {
          "featureId": "parcel-00123",
          "name": "Parcel 00123",
          "geometry": {
            "kind": "polygon",
            "points": [
              { "lat": 23.588, "lon": 58.383 },
              { "lat": 23.589, "lon": 58.384 },
              { "lat": 23.588, "lon": 58.385 },
              { "lat": 23.587, "lon": 58.384 },
              { "lat": 23.588, "lon": 58.383 }
            ]
          },
          "attributes": {
            "area": 6200,
            "land-use": "Commercial",
            "zone": "Al Khuwair Development Zone"
          }
        }
      ]
    }
  ]
}
```

---

## New Fields (this spec only; all others unchanged from spec 094)

| Field (top-level) | Type | Rules |
|---|---|---|
| `batchIndex` | integer | Zero-based batch sequence number for this emission. |
| `totalCount` | integer | Running cumulative feature count across all batches emitted so far. |
| `estimatedTotal` | integer? | Provider hint for eventual total; may be null; advisory only. |
| `isFinal` | boolean | `true` on the last emission for this analysis. When `false`, the client shows a streaming progress indicator. |

| Field (per layer) | Type | Rules |
|---|---|---|
| `batchIndex` | integer | Mirrors the top-level value; identifies which batch this layer payload represents. |
| `totalCount` | integer | Running feature count for this layer specifically. |
| `estimatedTotal` | integer? | Provider hint for this layer's eventual total. |
| `isFinal` | boolean | `true` when this is the last batch for this layer. |
| `features` | array | New field — present only on `kind = "feature-query"` layers. Each item is a `QueryResultFeature` (see data-model). |

> `markers` and `ring` remain `null` for `feature-query` layers. The client extracts geometry from
> the `features[].geometry` field and renders accordingly.

---

## Client Handling Rules

| Rule | Behaviour |
|---|---|
| **First batch** (`batchIndex = 0`) | Store creates a new layer entry and renders the initial features. |
| **Subsequent batches** (`batchIndex > 0`) | Store calls `applyBatch()` which appends features to the existing layer rather than replacing it. |
| **`isFinal = false`** | Legend shows a streaming progress indicator and running count. |
| **`isFinal = true`** | Progress indicator is dismissed; legend shows final count and allows full layer interactions. |
| **Unknown kind** | Existing `isRenderable` guard returns `false`; layer is skipped — no error. |
| **Missing `features`** | Layer with `kind = "feature-query"` and no `features` array is treated as empty; no crash. |

---

## Attribute Panel Trigger

When a user clicks a rendered feature in a `feature-query` layer:

1. The viewer extension `spatialQueryExtension.ts` intercepts the click event and reads
   `feature.attributes` from the layer's feature list in the store.
2. `FeatureAttributePanel` opens (MUI Drawer or Panel) displaying the feature name and all
   `attributes` key-value pairs using the dataset's `AttributeField.displayName` as labels.
3. Closing the panel does not alter the layer or its feature set.

No network request is made on feature selection — all attribute data is already in the client store.
