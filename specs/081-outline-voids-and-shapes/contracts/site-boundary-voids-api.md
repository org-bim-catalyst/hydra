# Contract: Site boundary voids and shapes (changes to the spec 079 API)

All changes are additive. Clients that don't send `voids` are treated as having none. Clients that ignore `voids` in responses keep working.

## POST `/api/v1/chats/{id}/site-boundary/actions/combine`

Request: either a circle or a polygon shape. Exactly one of the two is required.

```json
{ "rings": [[{ "latitude": 25.25, "longitude": 55.3 }, ...]],
  "voids": [[ [{ "latitude": ..., "longitude": ... }, ...] ]],
  "operation": "Cut",
  "centre": { "latitude": 25.25, "longitude": 55.3 }, "radiusMeters": 12 }
```

```json
{ "rings": [...], "voids": [...], "operation": "Add",
  "shape": [{ "latitude": ... }, { ... }, { ... }, { ... }] }
```

- **200**: `{ "rings": [...], "voids": [...] }`, both open, with the site's part first. A cut wholly inside a part now returns that part with a new void.
- **422**: `{ reason, ringIndex?, voidIndex?, detail }`. `reason` is one of:
  - `selfCrossing`, `degenerate`, `duplicateCorner` (in an input ring, an input void, or the shape);
  - `nothingLeft`;
  - `tooManyRings`;
  - `tooManyVoids`;
  - `nothingChanged`, with the detail "That area is already outside the site.".
- **400**: both shapes or neither; a shape outside 3 to 2,000 corners; radius out of range.

## PUT `/api/v1/chats/{id}/site-boundary`

Request: `{ "expectedRevision": "...", "rings": [...], "voids": [...] }`

- **200**: `{ activeBoundary, message }`.
  - `activeBoundary.voids` is the saved voids, one list per ring index.
  - `activeBoundary.areaSquareMeters` excludes them.
  - The chat line reads "now N m²", with voids excluded.
- **422**: adds `voidOutsidePart`, `voidsTouch` and `tooManyVoids`, with `ringIndex` and `voidIndex`.
- **409**: unchanged (revision conflict).

## Read paths that gain `voids`

- `GET /api/v1/chats/{id}`: `activeBoundary.voids`.
- The chat stream's `__SITE_BOUNDARY__` event: `voids`.
- Lucy's site-boundary payload (`resolve_site_boundary`, `set_site_boundary_members`, `reset_site_boundary`): `voids`, `voidCount` and `voidAreaSquareMeters`.
- `edit_site_boundary`: `voidCount`.
