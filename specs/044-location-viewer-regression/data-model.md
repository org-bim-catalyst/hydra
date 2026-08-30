# Phase 1 Data Model: Location & Site-Boundary Regression Fix

**Feature**: `044-location-viewer-regression` | **Date**: 2026-08-29

**No database migration is required.** This feature changes the *lifecycle* of existing state, adds one in-memory configuration value, and splits one in-memory transport record. No column is added, removed, or retyped.

---

## Persisted entities (existing — lifecycle change only)

### `UserChat.ActiveBoundary` → `ActiveSiteBoundary` (owned entity)

Existing owned entity on `UserChats`, unchanged in shape.

| Field | Type | Notes |
|---|---|---|
| `SiteName` | string | The site this boundary describes |
| `CentroidLatitude` / `CentroidLongitude` | double | WGS-84 |
| `Polygon` | `IReadOnlyList<GeoPoint>` | Closed exterior ring |
| `AreaSquareMeters` | double | |
| `Confidence` | double | 0.0–1.0 |
| `ConfidenceLevel` | `BoundaryConfidenceLevel` | Low / Medium / High |
| `Source` | `SiteBoundarySource` | OsmBoundary / AiInterpretation / ManualFallback / … |
| `SourceDetail` | string | Human-readable provenance |

**New state transition (FR-009a)** — this is the whole data-model change:

```
                 confirmed location names the SAME site
        ┌────────────────────────────────────────────────┐
        │                                                │
        ▼                                                │
   [ boundary stored ] ──────────────────────────────────┘
        │
        │  confirmed location names a DIFFERENT site
        ▼
   [ cleared ] ──── boundary resolved successfully ────► [ boundary stored ]
        │
        └──── boundary failed / timed out / no candidates ────► [ cleared ]
```

**Invariant**: `ActiveBoundary` is non-null only while it describes the chat's currently active site. It must never outlive the site it names.

Before this feature the transition out of `[boundary stored]` existed only on success, so a failed resolution for a *new* site left the *previous* site's boundary stored — the stale-state defect FR-009a closes.

**New domain method**:

```
UserChat.ClearActiveBoundary(string actor)
  → sets ActiveBoundary = null, updates ModifiedAtUtc / ModifiedBy
```

Mirrors `SetActiveBoundary`'s actor-stamping exactly. Enforced in `RecordActiveLocationCommandHandler`, atomically with `SetActiveLocation` in one unit of work (research Decision 4).

**Comparison rule**: site names compare with `StringComparison.OrdinalIgnoreCase`, matching the existing reuse guard in `SendChatMessageCommandHandler` so the clear and the reuse decisions can never disagree.

### `UserChat.ActiveLocation` → `ActiveSiteLocation`

Unchanged in shape and lifecycle. Listed only to record that clearing a boundary never clears the location — the location is the mandatory outcome and outlives any boundary failure.

---

## Transport records (in-memory, not persisted)

### `ChatStreamChunk`

Existing record, unchanged in shape. What changes is **how many are emitted and when** (research Decision 1):

| | Before | After |
|---|---|---|
| Chunks carrying trailing data | One, after the boundary step | Two — location chunk before the boundary step, boundary chunk after |
| `ConfirmedLocation` populated on | the single trailing chunk | the **first** trailing chunk |
| `ConfirmedBoundary` populated on | the same single chunk | the **second** trailing chunk (omitted entirely when no boundary was produced) |

Consumers must treat the two as independent arrivals. `AiController` already accumulates per-chunk, so it tolerates both shapes; the change it needs is *when it writes*, not how it reads.

### `ConfirmedSiteBoundaryData`

Unchanged. Its **absence** is now an explicitly expected, non-exceptional state rather than an implied one.

### `BoundaryResolutionOutcome`

Unchanged. `BoundaryResolutionOutcomeType.Unavailable` becomes the outcome for vision-path failures too, not only candidate-provider failures.

---

## Configuration

### `BoundaryScoringOptions` (bound from `BoundaryScoring`)

| Field | Type | Default | Validation | Status |
|---|---|---|---|---|
| `BoundaryTimeoutSeconds` | int | **45** | `[Range(1, 300)]` | **NEW** — aggregate cap for the whole boundary step (FR-003) |
| `VisionTimeoutSeconds` | int | 30 | `[Range(1, 300)]` | Existing (committed in `8e83b8f`) — per-call vision budget |
| `EnableAiVisionVerification` | bool | true | — | Existing |
| `SearchRadiusMeters`, `MaxCandidates`, thresholds, weights | — | — | existing | Existing, untouched |

The two timeouts are deliberately independent: `VisionTimeoutSeconds` bounds one external call, `BoundaryTimeoutSeconds` bounds the pipeline. `BoundaryTimeoutSeconds` should always exceed `VisionTimeoutSeconds`, or vision can never complete within the aggregate budget — worth asserting in the existing `IValidatableObject.Validate` alongside the current weight-sum and threshold-ordering checks.

---

## Entity relationships

```
UserChat
├── ActiveLocation : ActiveSiteLocation?   ── mandatory outcome; survives all boundary failures
└── ActiveBoundary : ActiveSiteBoundary?   ── optional; cleared whenever ActiveLocation names a different site
```

The dependency is one-directional and asymmetric by design: a location change can clear a boundary, but a boundary outcome can never affect the stored location. That asymmetry is the data-model expression of the whole feature — the optional thing must never be able to damage the mandatory thing.
