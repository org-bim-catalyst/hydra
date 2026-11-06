# Quickstart Validation Guide: Proximity Analysis

This guide defines the runnable scenarios to prove the proximity analysis feature works end-to-end. Implementation details belong in `tasks.md`; this document focuses on verification.

## Implementation path (SPEC-094)

Proximity runs as the `proximity.analysis` **specialist** of the Spatial Request Bundle — there is
no separate REST route. A chat turn is interpreted into a `SpatialRequestBundle`, the coordinator
resolves the study area, dispatches `ProximityAnalysisSpecialist`, and delivers one
`__SPATIAL_LAYERS__` event that the existing `SpatialLayersOverlay` and legend render and manage.
Adding the specialist is the only registration this feature required.

## Prerequisites

- A configured environment with active spatial data providers (e.g., OpenStreetMap/Overpass).
- A map viewer session with a visible study area (e.g., Muscat, Oman).
- Authentication as a user authorized for spatial analysis.

## Validation Scenarios

### 1. Basic "Within" Discovery (Point-to-Point)
**Setup**: Identify a target category (e.g., "hospitals") and a source category (e.g., "schools").
**Action**: Ask Lucy: "Find schools within 1 km of hospitals in Muscat."
**Expected Outcome**:
- Lucy clarifies the request if any category is ambiguous.
- The map displays highlighted school markers and circular buffers around hospitals.
- A legend panel identifies the "Schools within 1 km" layer.
- Lucy reports the total count of matching schools and the distance used.

### 2. "Beyond" Analysis (point-based v1)
**Setup**: A bounded study area (e.g., Muscat) and a target category (e.g., "schools").
**Action**: Ask Lucy: "Find schools more than 2 km from any school in Muscat."
**Expected Outcome**:
- Qualifying source points are marked; the 2 km buffers are deliberately not drawn (they would
  invert the meaning of a "beyond" result).
- Lucy explains the findings and mentions the 2 km threshold.
- **Known v1 limitation**: area outputs ("residential *areas* beyond …") need polygon features;
  the current provider returns points. A polygon provider is tracked as a separate change (T015).

### 3. Distance Band Comparison
**Setup**: Identify a target category (e.g., "hospitals").
**Action**: Ask Lucy: "Find schools near hospitals in Muscat in bands of 0–500 m and 500 m–1 km."
**Expected Outcome**:
- The map renders one marker layer per band, each with its own colour.
- The legend shows both bands clearly.
- A single school does not appear in both bands; one exactly on 500 m belongs to the first band.
- Lucy provides a summary count for each band.

### 4. Layer Management & Session State
**Setup**: Execute Scenarios 1 and 3.
**Action**: 
1. Hide the "Schools" layer via the legend.
2. Remove the "Metro Cafe 0-500m" layer.
3. Clear all proximity analysis layers.
4. Switch from 2D to 3D view and back.
**Expected Outcome**:
- Hiding a layer removes it from the map but keeps it in the legend.
- Removing a layer deletes it from both map and legend.
- Clearing removes all analysis-related layers; existing POIs/boundaries remain.
- Layer visibility and contents are preserved during 2D/3D toggle.

### 5. Error and Edge Case Handling
**Action**: 
1. Request a proximity search for an unsupported category (e.g., "underwater volcanoes").
2. Request an invalid distance (e.g., "-500 meters").
3. Request a proximity search in an area with no data coverage.
4. Omit the study area ("schools within 1 km of hospitals").
**Expected Outcome**:
- Unsupported categories: Lucy lists the supported place types.
- Invalid distance: the operation is rejected with an actionable message.
- No data/Empty results: Lucy reports "no results found" and the map remains clear.
- Missing area: Lucy asks which area to search instead of guessing from a stale pin.
- Provider timeout or outage: Lucy says the lookup failed — never that the area has none.
- No silent failures or crashes.

## Reference Documents
- **Contracts**: [proximity-analysis-api.md](contracts/proximity-analysis-api.md)
- **Data Model**: [data-model.md](../data-model.md)
