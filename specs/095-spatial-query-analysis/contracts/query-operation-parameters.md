# Contract: Query Operation Parameters

**Spec**: SPEC-095 | **Requirements**: FR-001, FR-002, FR-003, FR-004, FR-005, FR-006

The parameter block placed inside a `SpatialOperation` with `operationKind = "spatial.query"`.
This extends the existing `SpatialRequestBundle` (spec 094) without altering any v1 field meaning.

---

## Bundle Operation Shape

```json
{
  "operationId": "op-1",
  "operationKind": "spatial.query",
  "targets": [],
  "studyArea": {
    "kind": "namedArea",
    "namedArea": "Al Khuwair"
  },
  "parameters": {
    "datasetId": "land-parcels",
    "attributePredicates": [
      {
        "field": "area",
        "operator": "gt",
        "value": 5000,
        "unit": "m2"
      }
    ],
    "attributeCombinator": "and",
    "spatialPredicates": [
      {
        "relationship": "intersecting",
        "referenceKind": "namedFeature",
        "referenceName": "Al Khuwair Development Zone"
      }
    ],
    "spatialCombinator": "and"
  },
  "dependsOn": []
}
```

---

## Field Rules

### Top-level `parameters` object

| Field | Type | Required | Rules |
|---|---|---|---|
| `datasetId` | string | Yes | Must match a registered `DatasetEntry.Id`. Unknown id → validation error returned to user with list of available datasets. |
| `attributePredicates` | array | Conditional | At least one required when `spatialPredicates` is absent or empty. |
| `attributeCombinator` | `"and"` \| `"or"` | No | Defaults to `"and"`. Governs within-group combination of `attributePredicates` only. |
| `spatialPredicates` | array | Conditional | At least one required when `attributePredicates` is absent or empty. |
| `spatialCombinator` | `"and"` \| `"or"` | No | Defaults to `"and"`. Governs within-group combination of `spatialPredicates` only. |

**Mixed-type OR rule** (clarification Q4): `attributeCombinator` and `spatialCombinator` are
independently settable. A query that attempts to OR an attribute predicate with a spatial predicate
cannot be expressed in this parameter schema; the two groups are always AND'd. If the coordinator
detects the user intended a mixed-type OR, it sets `parameters` to null and returns a `Failed`
outcome with a user-readable explanation offering to run two separate queries.

### `attributePredicates[]` items

| Field | Type | Required | Rules |
|---|---|---|---|
| `field` | string | Yes | Must match `AttributeField.name` on the target dataset. |
| `operator` | string | Yes | One of: `gt`, `gte`, `lt`, `lte`, `eq`, `neq`, `between`, `contains`, `startsWith`, `endsWith`, `inYearRange`. |
| `value` | string \| number | Yes | Coerced to the field's declared type before comparison. |
| `valueTo` | string \| number | Conditional | Required for `between` and `inYearRange`. |
| `unit` | string | No | Supported values: `m`, `m2`, `km`, `km2`, `ft`, `ft2`, `ha`, `acre`. Converted to the field's canonical unit before comparison. |

### `spatialPredicates[]` items

| Field | Type | Required | Rules |
|---|---|---|---|
| `relationship` | string | Yes | One of: `within`, `outside`, `intersecting`, `containing`, `near`. Must be in the target dataset's `supportedRelationships` list. |
| `referenceKind` | string | Yes | One of: `namedFeature`, `activeLayer`, `studyArea`, `explicitGeometry`. |
| `referenceName` | string | Conditional | Required when `referenceKind = "namedFeature"`. |
| `referenceLayerId` | string | Conditional | Required when `referenceKind = "activeLayer"`. Must match an active layer in `SpatialExecutionContext.ActiveLayers`. |
| `referenceGeometry` | array of `{lat, lon}` | Conditional | Required when `referenceKind = "explicitGeometry"`. |
| `distanceMeters` | number | Conditional | Required when `relationship = "near"`. Positive, finite double. User-supplied distance converted to metres before dispatch. |

---

## Validation Order

1. `datasetId` resolves to a registered entry.
2. At least one of `attributePredicates` or `spatialPredicates` is non-empty.
3. All attribute field names exist on the target dataset.
4. All attribute operators are valid for the field's type.
5. All spatial relationships are in the target dataset's `supportedRelationships` list.
6. Reference geometry/name/layerId is present for the stated `referenceKind`.
7. `distanceMeters` is present, positive, and finite when `relationship = "near"`.

Failure at any step → `Failed` outcome with a user-visible explanation. The specialist never
executes a partially valid query.

---

## Extension Rule

Adding a new dataset does not change this parameter schema. Adding a new attribute field type or
spatial relationship operator that does not fit existing operators increments the `bundleVersion`
(following spec 094 versioning policy) and is a separate specification change.
