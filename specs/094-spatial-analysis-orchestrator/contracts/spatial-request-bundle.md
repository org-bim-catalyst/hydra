# Contract: Spatial Request Bundle (v1)

**Spec**: SPEC-094 | **Requirements**: FR-002, FR-003, FR-004, FR-028

The versioned, structured interpretation of one natural-language spatial request. This is an **internal
Application contract** between the coordinator and its specialists; it is not an HTTP request body. It travels
in-process only, and is serialized solely for logging and tests.

## Shape

```json
{
  "bundleVersion": 1,
  "bundleId": "0199f3a2-...",
  "requestedAtUtc": "2026-11-06T09:30:00Z",
  "preferences": {
    "layerMode": "ask"
  },
  "mapContext": {
    "centerLatitude": 23.5880,
    "centerLongitude": 58.3829,
    "zoom": 12.4,
    "southWestLatitude": 23.52,
    "southWestLongitude": 58.30,
    "northEastLatitude": 23.65,
    "northEastLongitude": 58.46
  },
  "operations": [
    {
      "operationId": "op-1",
      "operationKind": "poi.discovery",
      "targets": [{ "category": "hospital" }],
      "studyArea": { "kind": "namedArea", "namedArea": "Muscat" },
      "parameters": {},
      "dependsOn": []
    }
  ]
}
```

## Field rules

| Field | Rule |
|---|---|
| `bundleVersion` | Exactly `1` for this release. Any other value → validation failure, no execution. |
| `bundleId` | Non-empty; unique per interpreted request. |
| `operations` | At least one; `operationId` unique within the bundle. |
| `operationKind` | Must resolve in `SpatialSpecialistRegistry`. Unknown kinds → reported as not performed; if none of the bundle's kinds resolve, the request fails with an explanation. |
| `targets` | At least one selector with `category` or `geometry` for result-producing kinds. |
| `studyArea` | Required for area-based kinds. Resolved/validated before dispatch; never an unbounded region. |
| `dependsOn` | References earlier `operationId`s only; cycles and forward references are rejected. |
| `preferences.layerMode` | `keep`, `replace`, or `ask`. `ask` triggers the clear-or-keep question when layers exist. |
| `mapContext` | Optional; when present, fully validated (finite, in-range, ordered bounds, supported zoom). |

## Versioning and extension

- Adding a specialist adds a new `operationKind` plus its parameter block. It must not change the meaning of
  any existing v1 field.
- Removing or repurposing a field, or changing an existing kind's semantics, increments `bundleVersion`.
- The coordinator recognises a fixed set of versions. A newer version from a future deployment must be rejected,
  not coerced: the failure message states that the request could not be interpreted.
- Specialists must ignore unknown *parameter* fields they do not use (forward compatibility inside a kind), but
  must reject unknown required semantics.

## Validation order

1. Version supported.
2. Shape: at least one operation, unique ids, dependency graph acyclic.
3. Every operation kind resolves to a specialist (unresolved ones are recorded, not dropped).
4. Study area resolves to an authorized, bounded area.
5. Operation parameters validate against the specialist's schema.
6. Map context validates if present.

Failure at steps 1, 2 or 4 aborts the whole bundle with a user-visible explanation (FR-004). Failure at step 3
executes the resolvable operations and reports the rest as not performed. Failure at step 5 fails that operation
only and is reported alongside the successful ones.

## What this contract does not do

- It does not authorize data access. Each specialist enforces the requesting user's authorization on its own
  provider calls (FR-010).
- It does not carry user-visible copy. The coordinator composes all narration; specialist failure reasons are
  diagnostic.
- It is not exposed by an endpoint. There is no public spatial REST surface in this release.
