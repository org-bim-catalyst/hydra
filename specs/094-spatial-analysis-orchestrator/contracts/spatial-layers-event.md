# Contract: `__SPATIAL_LAYERS__` Stream Event

**Spec**: SPEC-094 | **Requirements**: FR-011, FR-018, FR-019, FR-020, FR-022, FR-023

The single viewer-delivery channel for spatial results. It replaces `__POI_LAYERS__`; no second spatial event is
emitted.

## Transport

- Server-Sent Events on the existing authenticated chat stream (`POST /api/v1/ai/chat`), written the
  moment the chunk is yielded — the same rule as `__LOCATION__`, `__SITE_BOUNDARY__` and `__POI_LAYERS__`.
- One line: `data: __SPATIAL_LAYERS__{json}\n\n`, flushed immediately
  (`src/AskLucy.Web/Controllers/v1/AiController.cs:323-326, 930-950` is the current POI implementation).
- Fired at most once per completed analysis. Clarification outcomes (`ask` keep/replace, which-area) emit **no**
  event, because nothing was drawn.

## Payload

```json
{
  "analysisId": "0199f3a2-...",
  "status": "Complete",
  "mode": "replace",
  "count": 1,
  "areaName": "Muscat",
  "layers": [
    {
      "layerId": "op-1:hospital",
      "sourceOperation": "op-1",
      "name": "Hospitals in Muscat",
      "legendLabel": "Hospitals",
      "kind": "markers",
      "markers": [
        { "id": "osm_node_123", "name": "Royal Hospital", "latitude": 23.60, "longitude": 58.54 }
      ],
      "style": { "color": "#D32F2F", "markerShape": "simple-dot" },
      "visible": true
    },
    {
      "layerId": "op-2:buffer-1km",
      "sourceOperation": "op-2",
      "name": "Within 1 km of clinics",
      "legendLabel": "Within 1 km",
      "kind": "buffer",
      "ring": [
        { "latitude": 23.58, "longitude": 58.38 },
        { "latitude": 23.58, "longitude": 58.39 },
        { "latitude": 23.59, "longitude": 58.39 }
      ],
      "style": { "color": "#1976D2", "opacity": 0.25, "strokeWidth": 2 },
      "visible": true
    }
  ],
  "warnings": [],
  "coverage": [
    { "sourceId": "overpass", "displayName": "OpenStreetMap", "coverageStatus": "Complete" }
  ]
}
```

## Field rules

| Field | Rule |
|---|---|
| `analysisId` | Non-empty; matches the layers' owner for clear-analysis. |
| `status` | `Complete` \| `Partial` \| `Empty`. `Failed` analyses emit no event. |
| `mode` | `replace` clears other analyses' layers first; `keep` draws beside them; `ask` is never emitted (the question precedes execution). |
| `layers` | May be empty when `status = Empty` and `mode = replace` — the client must clear the replaced analysis. |
| `layerId` | Unique within the payload; same id across repeated searches replaces that layer in place. |
| `kind` | `markers` \| `buffer` \| `area` \| `band`. Unknown kinds are ignored by the client, logged, and never rendered. |
| `markers` | Marker layers only: `{ id, name, latitude, longitude }[]` in WGS-84. |
| `ring` | Buffer/area/band layers only: `{ latitude, longitude }[]`, closed, ≥ 3 distinct vertices, WGS-84. |
| `style` | Server-assigned (`color`, `markerShape`, `opacity`, `strokeWidth`). Client must not accept free-form CSS or HTML. |
| `warnings` | User-readable; the client may display them but the chat narration is authoritative. |
| `coverage` | Source id, display name, status and optional explanation. |

## Client behaviour

- Parse failures must be caught and surfaced as a visible fallback (no layer rendered, narration still shown);
  they must never become unhandled promise rejections (constitution §2 VIII).
- `mode: "replace"` with an empty `layers` array clears the active analysis's layers.
- Layers are session-scoped; a page reload starts with none.
- Legend visibility toggles and removal are client-local; the server's layer state store is reconciled on the
  next turn via the existing active-layers input (generalized `ActivePoiCategories`).

## Compatibility

`__POI_LAYERS__` is removed in the same change that introduces this event. The repository has exactly one
consumer (this client), so a dual-emission transition period is not warranted; keeping both would preserve the
parallel path FR-001 removes.
