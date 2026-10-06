# Data Model: Voids and Drawing Shapes in the Outline Editor

## SiteBoundaryCorrection (table `SiteBoundaryCorrections`, from spec 079)

| Field | Type | Change |
|---|---|---|
| `EditedRings` (`EditedRingsJson`) | list of rings (each a closed list of points) | unchanged: each part's outer edge, the site's part first |
| `EditedVoids` (`EditedVoidsJson`) | list (per part) of lists of rings | **new**, `nvarchar(max) NOT NULL DEFAULT '[]'`; `EditedVoids[i]` holds part *i*'s voids, closed like the rings |
| `AreaSquareMeters` | float | now the union of the parts **minus** their voids |

### Rules (enforced in the domain and in validators)

- `EditedVoids` has no more entries than `EditedRings`. Missing entries mean no voids.
- Every void has at least 3 corners, lies strictly inside its part's outer edge, and doesn't touch other voids of that part.
- Limits:
  - 1 to 20 parts;
  - 3 to 2,000 corners per ring, void or outer edge;
  - at most 50 voids per part;
  - at most 5,000 corners in total, voids included.
- Rows saved before this feature read as having no voids, through the column default.

### Lifecycle (unchanged from spec 079)

- **Save:** `ReplaceRings(rings, voids, area, actor)`.
- **Building choice:** `ApplyMembership(rings, voids, area, members, snapshot, actor)`.
- **Reset:** soft delete, so voids go with the rest of the correction.

## ActiveSiteBoundary / ChatActiveBoundaryDto / ConfirmedSiteBoundaryData

- New optional field `Voids`: one list per ring index (index 0 is `Polygon`, the rest follow `AdditionalPolygons`), each a list of void rings. Empty when there are none.
- `WithCorrection(correction)` and `CorrectionOutline.ToConfirmed` copy `EditedVoids` into it. Found outlines always have `Voids = []`.

## Combine request (calculation only, nothing stored)

| Field | Notes |
|---|---|
| `rings` | each part's outer edge, open |
| `voids` | optional, per part, open |
| `operation` | `Add` or `Cut` |
| `centre` + `radiusMeters` | circle, **or** |
| `shape` | **new**: a simple polygon, 3 to 2,000 corners, open |

The result has `rings` and `voids`, open, with the site's part first.

## Client edit session (`siteBoundaryEditStore`)

- New fields:
  - `voids: GeoPoint[][][]` (per part, open);
  - `activePath: number` (0 is the outer edge, *k* is void *k*-1).
- Changes:
  - Every corner change (`move`, `moveMany`, `insert`, `delete`, `replace`) gains `path` (default 0).
  - `replaceAll` carries `{ rings, voids }`.
  - New changes `removeVoid` and its undo, `addVoid`.
- Tools: `EditTool` becomes `'edit' | 'select' | 'arc' | 'shape' | 'polygon'`, with `shapeKind` (`circle | rectangle | square`) and `shapeOperation` (`add | cut`). The free polygon tool has `polygonCorners` and `polygonOperation`.
- Area is the sum over parts of (outer area minus void areas).

## Refusal reasons

- Server 422 `reason` gains:
  - `voidOutsidePart`
  - `voidsTouch`
  - `nothingChanged`
  - `tooManyVoids`
- The response also gains `voidIndex` beside `ringIndex`.
- `holeNotSupported` is no longer produced by the editor's combine. The constant stays, for any other caller.
