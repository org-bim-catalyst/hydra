# Research: Voids and Drawing Shapes in the Outline Editor

## D1 - How voids are stored and sent: a parallel per-part list

- **Decision**: Keep `rings` (each part's outer edge) exactly as today, and add `voids`, indexed like `rings`: `voids[i]` holds part *i*'s void rings. An empty or absent value means no voids. The database gets a new JSON column `EditedVoidsJson`, defaulting to `[]`. DTOs, the stream event and Lucy's payload gain an optional `voids` field.
- **Rationale**: Every current reader of `polygon`, `additionalPolygons` and `EditedRings` (site analysis, the found-outline matcher, the solar overlay, the stream event, `WithCorrection`) keeps working without change. Saved rows need no rewrite, and older clients ignore the new field. It also matches Google's model, where a polygon's first path is the outer edge and the later paths are holes.
- **Alternatives**: Changing the ring type to a `{ outer, holes }` part everywhere. Cleaner, but it breaks every reader and every stored row in one release. Storing holes as extra rings with a flag. Rejected: it is ambiguous, and "rings" are already overloaded with parts.

## D2 - Holes in the server geometry

- **Decision**: `NtsSiteRingGeometry` builds each part's polygon with its voids as interior rings. `Combine` returns every resulting polygon's outer edge and interior rings, through `fromPolygon`, which replaces `fromShell`. The editor's `HoleNotSupported` refusal is removed.
  - Interior rings under 1 m² are dropped, as slivers already are.
  - Touching voids come out of NTS merged, because the union and difference results are already normalised.
  - `UnionArea(rings, voids)` subtracts the voids.
  - New `ValidateVoids(outer, voids)` results: `VoidOutsidePart` (a void touches or crosses the outer edge) and `VoidsTouch`. The existing per-ring checks also run on each void.
- **New failure `NothingChanged`**: when a result's area and shape equal the input (for example a cut drawn wholly inside an existing void), the response is a 422 `nothingChanged`, "That area is already outside the site."
- **Rationale**: NTS already produces holes; the code currently throws them away or refuses them. Keeping them costs a few lines and keeps the server authoritative for the result.
- **Alternatives**: Computing holes on the client. Rejected: the client has no robust polygon boolean operations, and spec 079 deliberately made the server compute combines.

## D3 - New shapes are client-built polygons sent to the existing combine endpoint

- **Decision**: The combine request accepts either today's `centre` and `radiusMeters` (circle), or a new `shape: GeoPoint[]` (any simple polygon). The validator requires exactly one of the two. The polygon must have 3 to 2,000 corners and pass `Validate`.
  - Rectangle and square are built on the client from the two dragged corners. Their sides run north-south and east-west, since the editor's map is flat and north-up. The square takes the larger side.
  - The free polygon is the corners the user placed.
- **Rationale**: One endpoint, one server path and one undo behaviour for every shape. Google's DrawingManager (the only built-in rectangle and polygon drawing) was deprecated in August 2025 and removed in the May 2026 Maps version.
- **Alternatives**: One endpoint per shape. Rejected as duplication, and the server needs no knowledge of the shape kind.

## D4 - Showing and editing voids on the map

- **Decision**: Each part stays one `google.maps.Polygon`, with `paths: [outer, ...voids]`. Each void is reversed to the opposite winding from the outer edge, as Google requires for holes (`isCounterClockwise` already exists).
  - The editable host adapts every path (`polygon.getPaths().getAt(k)`), and vertex events report `path` and `vertex` (Google's `PolyMouseEvent` carries both).
  - A corner is addressed as `(part, path, index)`, where path 0 is the outer edge and path k is void k-1.
  - The normal outline display passes voids as inner paths of the native polygon, and as extra rings to the gradient border renderer, which draws borders only.
- **Rationale**: Google draws and edits holes natively. One polygon per part keeps the fill correct, so the hole isn't filled, and keeps the existing per-part interaction.
- **Alternatives**: A separate editable polygon per void, drawn on top. Rejected: the part's fill would still cover the void, so the hole would never show.

## D5 - Selecting and moving between voids

- **Decision**: `[` and `]` still switch parts. Shift+`[` and Shift+`]` cycle the paths of the active part (the outer edge, then each void), announced as "void k of n". Clicking a void's corner makes that void active.
  - Box select and multi-move apply to the active path only, as they already apply to the active part only.
  - The corner menu adds "Remove void" for a void's corners. Remove void is one undo step.
- **Rationale**: Extends the existing part-scoped model with the least change. Selections never span paths, so group moves stay valid by construction.
- **Alternatives**: Selections spanning the outer edge and voids. Rejected for now: the validation and undo complexity buys little.

## D6 - Validating void edits on the client

- **Decision**: `ringGeometry.ts` gains `pointInRing`, `ringsTouch` (built on the existing segment-crossing code) and `validateVoid(outer, voids, k)`, with reasons `voidOutsidePart` and `voidsTouch`.
  - A void corner edit runs `validateChange` on the void, then `validateVoid`.
  - An outer-edge corner edit also checks that no void now crosses the edge.
  - A refused edit snaps back with a message, exactly as today.
- **Rationale**: Instant feedback during drags, matching spec 079's behaviour. The server checks again on save.

## D7 - Membership with voids

- **Decision**: `HandEditedMembershipComposer` keeps each part's voids through its join, cut and combine steps:
  - after a step, each void is clipped to the new outer edge;
  - an added building fills the part of a void it covers;
  - a void no longer strictly inside its part is dropped;
  - a combine that now produces a hole (for example, adding a U-shaped building around a courtyard) keeps it as a void instead of failing.

  Which buildings are members is not affected.
- **Rationale**: The user decided voids only reduce area, and membership is decided by outlines drawn in the same group (spec 077).

## D8 - Limits

- **Decision**: At most 50 voids per part and 2,000 corners per void. Void corners count toward the existing 5,000-corner total. The drift (25 m) and 3× growth checks still use outer edges only.
- **Rationale**: Bounds request size (constitution section 8) with room for real buildings; atriums are few and simple.

## D9 - What Lucy is told

- **Decision**: `SiteBoundaryPayload` writes `voids`, `voidCount` and `voidAreaSquareMeters`. `ActiveSiteNote` adds "with N void(s), X m² excluded" when there are voids, and `edit_site_boundary` returns `voidCount`. The area figure is already the correction's stored area everywhere, so it automatically excludes voids.
- **Rationale**: FR-012 and SC-007, with no new capability.

## D10 - Keyboard and touch for shapes

- **Decision**:
  - The shape dialog (today's "Make ring a circle" dialog) gains Rectangle (width × height in metres) and Square (side), placed at the active part's centre, with Add or Cut.
  - The free polygon tool places a corner at the map's centre with Space (a crosshair marks it); the arrow keys pan, Enter finishes, Backspace removes the last corner, and Escape cancels.
  - On touch, a tap places a corner.
- **Rationale**: FR-020 and constitution UI accessibility, reusing the existing dialog and keyboard patterns.
