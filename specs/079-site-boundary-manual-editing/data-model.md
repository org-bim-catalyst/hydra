# Data Model: Hand-Edit the Site Outline

**Feature**: specs/079-site-boundary-manual-editing | **Design decisions**: [research.md](research.md)

## Overview

```text
UserChat (aggregate, existing)
 └─ ActiveBoundary (owned, existing)  ── the FOUND outline; never overwritten by a hand edit
      + Revision        (Guid)        ── D4
      + CorrectionId    (Guid?)  ─────────┐  by id only, no navigation
                                          ▼
SiteBoundaryCorrection (NEW aggregate, per user)
      EditedRings, FoundSnapshot, Members, Revision   ── the single source of truth for a hand edit

Outline in force = ActiveBoundary.WithCorrection(live correction owned by the chat's user) ?? ActiveBoundary
```

## Domain

### `SiteBoundarySource` (enum, existing): add `UserCorrected`

It is stored as a string (`HasConversion<string>()`, max length 30), so this adds no migration
of existing values. It is the source of the **outline in force** only. The chat's own columns
keep the found outline's source.

### `ActiveSiteBoundary` (owned record on `UserChat`, existing): additions

| Member | Type | Notes |
|--------|------|-------|
| `Revision` | `Guid` | New value on every change to the chat's found outline or link: resolve, membership choice, correction link or unlink. Existing rows get a fresh Guid in the migration. |
| `CorrectionId` | `Guid?` | The linked `SiteBoundaryCorrection`. `null` means not hand-edited in this chat. A link to a soft-deleted correction is treated as `null` (a dead link). |
| `WithCorrection(SiteBoundaryCorrection)` | method → `ActiveSiteBoundary` | Pure. Returns a copy with the fields listed below replaced. `SiteName`, `CorePolygon` and centroid are kept. |

`WithCorrection` replaces these fields:

- `Polygon` becomes the first edited ring;
- `AdditionalPolygons` becomes the other edited rings;
- `AreaSquareMeters` becomes the correction's area;
- `Source` becomes `UserCorrected`;
- `ConfidenceLevel` becomes `High`, per the spec assumption;
- `Members` becomes the correction's members;
- `Revision` becomes the correction's revision.

Derived: `IsHandEdited => Source == UserCorrected`, which is only true on an effective
outline.

### `UserChat` (existing): methods

- `LinkSiteBoundaryCorrection(Guid correctionId)`: sets `CorrectionId` and a new `Revision`.
- `UnlinkSiteBoundaryCorrection()`: clears `CorrectionId` and sets a new `Revision`.
- The existing outline-recording methods (`RecordActiveSiteBoundary`, membership recompose) set a
  new `Revision`. A **different** site clears `CorrectionId`: a new site replaces the outline as
  it does today (FR-024).

### `SiteBoundaryCorrection` (NEW aggregate root, `BaseEntity`)

| Field | Type | Rules |
|-------|------|-------|
| `Id` | `Guid` (v7) | PK |
| `UserId` | `string` (450) | Owner. Every query is filtered by it (FR-026). |
| `SiteName` | `string` (500) | As shown |
| `NormalizedSiteName` | `string` (500) | Lower-case invariant, diacritics folded, whitespace collapsed (research D5) |
| `FoundCentroidLatitude` / `FoundCentroidLongitude` | `double` | Place identity: the found outline's centre |
| `EditedRings` | `IReadOnlyList<IReadOnlyList<GeoPoint>>` (JSON) | 1–20 rings, 3–2,000 corners each, at most 5,000 in total. Simple, area > 1 m². The first ring is the one holding the site. |
| `AreaSquareMeters` | `double` | Union area of `EditedRings`: overlap counted once, computed by the server (FR-016) |
| `FoundSnapshot` | `FoundSiteBoundarySnapshot` (JSON) | The found outline when the correction was created or last re-based (below) |
| `Members` | `IReadOnlyList<SiteBoundaryMember>` (JSON) | The building choice `EditedRings` covers (D9) |
| `Revision` | `Guid` | New on every save or membership change (D4) |
| Audit, soft delete, `RowVersion` | | From `BaseEntity` and the interceptor |

`FoundSiteBoundarySnapshot` is a value object holding:

- `Polygon`, `AdditionalPolygons`, `CorePolygon`;
- `AreaSquareMeters`, `Confidence`, `ConfidenceLevel`;
- `Source`, `SourceDetail`;
- `Members`.

This is exactly what a chat needs to fill its own `ActiveBoundary` when it reuses the correction
without resolving.

**Behaviour**:

- `static Create(userId, siteName, foundCentroid, foundSnapshot, editedRings, area, members)`.
- `ReplaceRings(editedRings, area)`: new `Revision`.
- `ApplyMembership(editedRings, area, members, foundSnapshot)`: used by D9. It also re-bases
  `FoundSnapshot` to the recomposed found outline, so a reset from any chat restores the found
  outline with the building choice in force.
- `Delete()`: soft delete, used by reset.

**Invariants**, enforced by the domain constructors and methods, and again by the Application
validator:

- at least one ring;
- every ring has at least 3 corners;
- area > 0;
- `UserId` is not empty.

Geometric validity (simplicity, drift) is checked in Application through `ISiteRingGeometry`,
because the domain has no geometry engine.

**Lifecycle**:

```text
(none) ──first Done in any chat──▶ Live ──Done / building choice──▶ Live (new Revision)
                                   │
                                   └──Reset (any chat)──▶ Deleted (soft) → every link is dead → chats show their found outline
```

## Application

- `ISiteBoundaryCorrectionRepository` (port):
  - `GetByIdAsync(id, userId)`;
  - `FindCandidatesAsync(userId, normalizedName)`, which uses the index;
  - `Add`.
- `SiteBoundaryCorrectionMatcher`: chooses among candidates by place (research D5: the point is
  inside the found rings grown by 100 m, or within 250 m of the found centroid).
- `EffectiveSiteBoundary.ResolveAsync(UserChat)` → `ActiveSiteBoundary?`. This is the outline in
  force. It is used by `TurnContextFactory`, `GetChatById`, the save and reset handlers, and the
  site-extent capabilities.
- `ISiteRingGeometry` (port): `Validate`, `UnionArea`, `Intersects(rings, found, growMeters)`,
  `Join`, `Cut` (research D3, D9).

## Persistence

### Table `SiteBoundaryCorrections` (NEW)

| Column | Type | Null |
|--------|------|------|
| `Id` | `uniqueidentifier` PK | no |
| `UserId` | `nvarchar(450)` | no |
| `SiteName` | `nvarchar(500)` | no |
| `NormalizedSiteName` | `nvarchar(500)` | no |
| `FoundCentroidLatitude`, `FoundCentroidLongitude` | `float` | no |
| `EditedRingsJson` | `nvarchar(max)` | no |
| `AreaSquareMeters` | `float` | no |
| `FoundSnapshotJson` | `nvarchar(max)` | no |
| `MembersJson` | `nvarchar(max)` | no |
| `Revision` | `uniqueidentifier` | no |
| `CreatedAtUtc`, `CreatedBy`, `LastModifiedAtUtc`, `LastModifiedBy`, `IsDeleted`, `DeletedAtUtc`, `RowVersion` | as `BaseEntity` | |

Index `IX_SiteBoundaryCorrections_UserId_NormalizedSiteName` on (`UserId`, `NormalizedSiteName`)
`WHERE DeletedAtUtc IS NULL` covers the only query path. A global query filter hides deleted
rows. There is no FK to `AspNetUsers`, matching how the other per-user tables store `UserId`.

### Table `UserChats` (existing): two owned columns

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `ActiveBoundaryRevision` | `uniqueidentifier` | yes | Null when there is no outline. The migration backfills `NEWID()` where `ActiveBoundarySiteName IS NOT NULL`. |
| `ActiveBoundaryCorrectionId` | `uniqueidentifier` | yes | Indexed (`IX_UserChats_ActiveBoundaryCorrectionId`, filtered `IS NOT NULL`). There is no DB foreign key, because the correction is another aggregate and a soft-deleted correction must leave a dead link, not cascade. |

### Migration `<timestamp>_AddSiteBoundaryCorrections`

- **Up**: create the table and index, add the two columns and the filtered index, and backfill
  `ActiveBoundaryRevision`.
- **Down**: drop the index, the columns and the table. It is reversible: the data lost is
  feature-only.
- It is applied automatically at startup, as for specs/077. **Migrate the test2 database by
  hand** before Persistence.Tests runs in CI (memory: persistence tests DB).
- Watch the migration file's BOM and line endings (memory: CI gotchas).

## API DTOs (see [contracts/site-boundary-edit-api.md](contracts/site-boundary-edit-api.md))

`ChatActiveBoundaryDto` gains:

- `revision: string` (Guid);
- `isHandEdited: boolean`.

It is built from the **effective** outline. `source` reads `"UserCorrected"` when hand-edited.

## Client state (never sent to the server except the rings on Done)

### `activeSiteBoundaryStore` (existing): additions

It gains `revision: string` and `isHandEdited: boolean`, set from the SSE `siteBoundary` event,
chat detail, and save and reset responses.

### `siteBoundaryEditStore` (NEW, Zustand, module-level so it survives navigation)

```ts
type LatLng = { lat: number; lng: number }

interface ViewState {
  mode: 'isometric' | 'plan'
  rotationEnabled: boolean
  center: LatLng
  zoom: number
  heading: number
  tilt: number
}

type RingChange =
  | { op: 'move'; ring: number; index: number; before: LatLng; after: LatLng }
  | { op: 'insert'; ring: number; index: number; after: LatLng }
  | { op: 'delete'; ring: number; index: number; before: LatLng }

interface SiteBoundaryEditSession {
  chatId: string
  siteName: string
  baseRevision: string          // expectedRevision on Done / Reset
  startRings: LatLng[][]        // Cancel target (FR-013)
  rings: LatLng[][]             // current
  undo: RingChange[]            // FR-012
  redo: RingChange[]
  activeRing: number
  selectedCorner: number | null
  viewState: ViewState          // captured once on entry (research D2)
  approxAreaSquareMeters: number
  status:
    | { kind: 'editing' }
    | { kind: 'saving' }
    | { kind: 'error'; message: string }                     // FR-018: stays open, Retry
    | { kind: 'conflict'; currentRevision: string }          // FR-019: "Load latest"
  refusal: string | null        // last refused local change, shown then cleared
}
```

**State transitions**:

```text
idle ──enter(from offer | map control | siteBoundaryEdit event)──▶ editing
editing ──change ok──▶ editing (undo += change, redo = [])
editing ──change refused──▶ editing (refusal set, path reverted)
editing ──Undo/Redo──▶ editing
editing ──Cancel──▶ idle (restore startRings + viewState)
editing ──Done──▶ saving ──200──▶ idle (outline store ← response, animated redraw, restore viewState)
                         ├─409──▶ conflict ──Load latest──▶ editing (rebased on fetched outline) | Cancel
                         └─error─▶ error ──Retry──▶ saving | Cancel
editing|error|conflict ──different site arrives──▶ idle (restore viewState, notice "unsaved changes dropped") (FR-030)
editing ──switch chat──▶ confirm dialog: Save → saving | Discard → idle | Stay
```

The session keys on `chatId`. Leaving `/studio` keeps it, since the store is module-level
(FR-029, memory: keep workspace state).
