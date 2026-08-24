# Quickstart Validation Guide: POI Viewer Zoom & Focus (SPEC-038)

## Prerequisites

1. App running locally with Google Maps API key set in `appsettings.Development.json` (or the Geocoding key in `Geocoding:GoogleMapsApiKey`).
2. `VITE_GOOGLE_MAPS_API_KEY` set in `ClientApp/.env.development` (or `.env.local`).
3. The 3D Viewer (`/viewer`) loads successfully and shows the Google Maps 3D map.
4. A conversation is active (any chat will do).

---

## Scenario 1 — US1: Automatic POI Zoom on Location Resolve

**What is being validated**: SC-001, FR-001, FR-002, FR-002a, FR-005, FR-008, FR-010, FR-011, FR-012, FR-013.

### Steps

1. Open the viewer page (ensure 3D map is visible at city/country zoom level).
2. In the chat input, type: **"Show me Dubai Mall"** and send.
3. Observe:

**Expected — viewer behaviour**:
- The viewer re-centres on the Dubai Mall coordinates.
- The viewer zooms to a level where the mall's footprint (or block) is clearly visible — **not** country or region level.
- If the geocoding response includes a bounding box, the zoom fits that box to the viewport.
- If no bounding box, the zoom uses the `ROOFTOP`/`GEOMETRIC_CENTER` fallback altitude (≤ 800 m).
- A visual POI marker appears at the resolved coordinates, by default a pulsing ring.
- The marker has a floating label reading **"Dubai Mall"** (not "Burj Khalifa" or a surrounding area address).

**Expected — Lucy's reply**:
- Lucy confirms the action concisely, e.g.: *"I've zoomed in on Dubai Mall."*
- The reply contains **no** references to Google Maps or instructions to use third-party apps.

---

### Sub-scenario 1a — City-level POI

Send: **"Show me Dubai"**

- Viewer zooms to city level (wide enough to see the city outline, not street level).
- Marker placed at the city geocode point with label "Dubai".

---

### Sub-scenario 1b — Country/Region

Send: **"Show me the UAE"**

- Viewer zooms to country level (the whole UAE visible).
- Marker placed at the country centroid with label "UAE" or "United Arab Emirates".

---

## Scenario 2 — US2: Explicit Zoom Commands

**What is being validated**: SC-002, SC-003, SC-005, FR-003, FR-004, FR-006, FR-007, FR-009.

### Steps

1. First navigate to a location (run Scenario 1 to establish active location).
2. In the chat input, type: **"zoom in"** and send.

**Expected**:
- Viewer animates smoothly to a closer altitude (roughly half the previous altitude).
- Animation completes within 1.5 seconds with no visual glitch.
- Lucy's reply is a brief confirmation (one sentence), e.g.: *"Zoomed in."* or *"Done — closer view of Dubai Mall."*
- No mention of third-party map services.

3. Type: **"pull back"** and send.

**Expected**:
- Viewer animates to a wider altitude (roughly double the previous altitude).
- Lucy confirms briefly.

4. Type: **"get closer"** and send — confirm this also zooms in.

5. Try **"zoom in"** when no location is active (clear the active location or start fresh):

**Expected**:
- Lucy responds that no active location exists and prompts the user to name a place.
- The viewer does not change altitude.

---

## Scenario 3 — US3: Accurate POI Display Name

**What is being validated**: SC-004, FR-005.

### Steps

1. Send: **"Take me to Al Safa Park 2"**

**Expected**:
- The marker label and Lucy's confirmation both say **"Al Safa Park 2"**, not the geocoding formatted address (which might be a surrounding area or district name).

2. Send: **"Find Global Village Dubai"**

**Expected**:
- The marker label reads **"Global Village Dubai"** — the user's query — not an address like "Sheikh Mohammed Bin Zayed Road".

---

## Scenario 4 — Marker Style Selector

**What is being validated**: FR-012a, FR-012b.

### Steps

1. Navigate to any location (run Scenario 1).
2. In the viewer control panel, find the marker style selector.
3. Switch from "Pulsing Ring" to "Classic Pin".

**Expected**:
- The POI marker in the viewport immediately changes to a classic pin geometry.
- No page reload needed.

4. Navigate away and back to the viewer (or refresh the page).

**Expected**:
- The "Classic Pin" style is still selected (persisted in localStorage).

5. Switch back to "Pulsing Ring".

**Expected**:
- Pulsing ring is restored.

---

## Scenario 5 — Marker Replacement on New Location

**What is being validated**: FR-013.

### Steps

1. Navigate to "Dubai Mall".
2. Confirm the marker appears.
3. Send: **"Now show me Burj Khalifa"**.

**Expected**:
- The Dubai Mall marker disappears.
- A new marker appears at Burj Khalifa's coordinates.
- Only one marker is visible at a time.

---

## Backend Unit Test Validation

Run the geocoding tests to confirm `GoogleMapsGeocodingProvider` now captures `viewport`:

```powershell
dotnet test tests/AskLucy.Infrastructure.Tests --filter "Geocoding"
```

Expected: all `GoogleMapsGeocodingProviderTests` pass, including new tests for:
- `viewport` populated when Google API returns bounding box
- `viewport` null when `geometry.viewport` is absent from response
- `LocationType` mapped correctly

Run zoom detector tests:

```powershell
dotnet test tests/AskLucy.Application.Tests --filter "ViewerZoomDetector"
```

Expected: all zoom keyword detection tests pass for both directions.

---

## Frontend Type-Check Validation

```powershell
cd src/AskLucy.Web/ClientApp
npx tsc -b --noEmit
```

Expected: zero TypeScript errors. The new `viewport` and `locationType` fields in `activeLocationStore`, `ViewportBounds`, and `useMarkerStyleStore` must all be typed correctly.

---

## Edge Case Checklist

- [ ] Zoom command when viewer not yet loaded → Lucy responds with "No active location", viewer unchanged.
- [ ] `fitBounds` called with degenerate box (NE == SW) → falls back to `zoomToAltitude(200)`.
- [ ] `zoomBy('in')` at minimum altitude (50 m) → clamped, no crash.
- [ ] `zoomBy('out')` at maximum altitude (500 000 m) → clamped, no crash.
- [ ] `localStorage` unavailable (private window) → `markerStyle` defaults to `'pulsing-ring'` without throwing.
- [ ] Two zoom commands sent in rapid succession → second cancels/queues the first's animation gracefully.
