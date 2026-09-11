# Feature Specification: Buildings-Only Map Style

**Feature Branch**: `048-buildings-only-map-style`

**Created**: 2026-09-12

**Status**: Draft

**Input**: User description: "Add a 'Buildings only' map style option to the existing map style menu (Road map / Satellite / Hybrid) in the viewer's GIS content mode, hiding roads, points of interest, transit, administrative labels/borders, and natural landscape so building footprints/3D shapes dominate the view."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Isolate building footprints from map clutter (Priority: P1)

A user viewing a site in the map/GIS content mode wants to focus on building shapes and
footprints without roads, business/POI icons, transit lines, administrative borders, or
natural terrain features competing for attention. They open the map style menu and pick
a new "Buildings only" option, and the map immediately re-renders with those distracting
layers hidden.

**Why this priority**: This is the entire feature — without it there is nothing to ship.

**Independent Test**: Open the map style menu, select "Buildings only", and confirm
roads, POI icons, transit lines, administrative labels/borders, and natural landscape
are no longer visible on the map while buildings remain visible.

**Acceptance Scenarios**:

1. **Given** the map is showing the default road map style, **When** the user selects
   "Buildings only" from the map style menu, **Then** roads, points of interest,
   transit, administrative labels/borders, and natural landscape features are hidden,
   and the "Buildings only" option is shown as the active selection.
2. **Given** "Buildings only" is active, **When** the user selects "Road map",
   "Satellite", or "Hybrid" instead, **Then** the buildings-only styling is fully
   removed and the map returns to that style's normal appearance.
3. **Given** "Buildings only" is active, **When** the user pans, zooms, or rotates the
   map, **Then** the buildings-only styling persists (it is not reset by camera
   movement).

---

### User Story 2 - Style choice persists across a session's map interactions (Priority: P2)

A user who selects "Buildings only" continues working in the viewer — searching a new
location, toggling isometric/plan view, switching light/dark theme — without the style
silently reverting to a default.

**Why this priority**: Matters for usability but the feature still delivers its core
value (User Story 1) even if this isn't perfect on day one; existing map-style switches
(roadmap/satellite/hybrid) already carry this expectation, so it is a natural extension
rather than new scope.

**Independent Test**: With "Buildings only" active, search for a different location or
toggle the light/dark theme, then confirm the map is still in the buildings-only style
afterward.

**Acceptance Scenarios**:

1. **Given** "Buildings only" is active, **When** the user searches for and navigates to
   a different location, **Then** the map remains in the buildings-only style.
2. **Given** "Buildings only" is active, **When** the user toggles the app's light/dark
   theme, **Then** the map remains in the buildings-only style after the theme change.

---

### User Story 3 - Clear behavior when buildings-only styling cannot be applied (Priority: P3)

Custom client-supplied map styling (hiding roads/POI/transit/etc.) only has a visible
effect on Google's "raster" base map rendering; it does nothing when the deployment is
instead using Google's newer "vector" base map rendering, where the equivalent look
instead comes from a cloud-configured map style (research.md Decision 4) attached to a
dedicated Map ID. A user in a vector map deployment should never end up in a state that
silently looks unchanged after they picked "Buildings only" with no indication why —
either the cloud-styled alternative is configured and it works, or it isn't and the
option is never offered.

**Why this priority**: A real Maps API constraint (see Assumptions) that must be handled
deliberately, but it only matters for deployments running the vector rendering path —
lower priority than the core toggle itself, and can follow once P1 ships.

**Independent Test**: In a deployment configured for vector base-map rendering with no
buildings-only cloud style configured, confirm the user is never left thinking
"Buildings only" applied when it silently did not. In one with a buildings-only cloud
style configured, confirm selecting it produces the same buildings-only look.

**Acceptance Scenarios**:

1. **Given** the deployment is running vector base-map rendering with no buildings-only
   cloud style configured, **When** the user opens the map style menu, **Then**
   "Buildings only" is not offered as a selectable option (rather than being selectable
   but silently ineffective).
2. **Given** the deployment is running vector base-map rendering with a buildings-only
   cloud style configured, **When** the user selects "Buildings only", **Then** the map
   shows the same buildings-only look (roads/POI/transit/administrative/natural
   landscape hidden, buildings dominant) as on a raster deployment.

---

### Edge Cases

- What happens if the user rapidly switches between "Buildings only" and the other
  three styles multiple times in a row? The final selection made must be the one
  reflected on the map, with no flicker back to an intermediate style.
- What happens if "Buildings only" is selected while a site boundary highlight is
  already shown on the map? The boundary highlight must remain visible and unaffected,
  since it is drawn as a separate overlay, not a base-map feature.
- What happens on a page reload? The map style is not currently required to persist
  across a full reload for any of the existing three styles (roadmap/satellite/hybrid
  already reset to the default on reload), so "Buildings only" follows the same
  behavior — no new persistence requirement introduced by this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The map style menu MUST offer a fourth option, "Buildings only", alongside
  the existing "Road map", "Satellite", and "Hybrid" options, in deployments where it is
  supported (see FR-006).
- **FR-002**: Selecting "Buildings only" MUST hide roads, points of interest, transit
  lines/stations, administrative labels and borders, and natural landscape features on
  the map, leaving building footprints/shapes and other base-map content visible.
- **FR-003**: The map style menu MUST visually indicate "Buildings only" as the active
  selection whenever it is the current style, using the same highlighting convention as
  the other three style options.
- **FR-004**: Selecting any of the other three existing styles while "Buildings only" is
  active MUST fully remove the buildings-only hiding effect and restore that style's
  normal appearance.
- **FR-005**: The buildings-only styling MUST persist across camera movement (pan, zoom,
  rotate, tilt) and across navigating to a different location, exactly as the existing
  three styles already do.
- **FR-006**: In a deployment where client-supplied custom map styling has no effect
  (vector base-map rendering, see Assumptions) and no equivalent cloud-configured
  buildings-only style is available for that deployment, the system MUST NOT present
  "Buildings only" as a selectable option, so a user is never left believing it applied
  when it silently did not. When an equivalent cloud-configured style is available
  (research.md Decision 4), the option MUST be offered and MUST work.

### Key Entities

- **Map style selection**: The single currently-active base-map rendering style for the
  map/GIS content mode. Extends the existing four-way choice from three values
  (road map / satellite / hybrid) to four (adding buildings-only).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can go from wanting an uncluttered, building-focused view to seeing
  one in a single selection from the map style menu.
- **SC-002**: 100% of the existing map style menu's behaviors (active-selection
  highlighting, persistence through pan/zoom/rotate/location changes, instant switching
  between styles) work identically for the new "Buildings only" option as they do for
  the three existing options.
- **SC-003**: Zero deployments end up in a state where a user selected "Buildings only"
  but observes no visual change with no explanation — either the styling visibly applies
  or the option is not offered at all.

## Assumptions

- Google Maps' custom map styling (used to hide feature categories like roads or POIs)
  only takes effect on Google's "raster" base-map rendering; it is a well-documented
  no-op on the newer "vector" base-map rendering, where equivalent visual changes
  require a style configured in Google Cloud Console instead of being sent from the
  client. This feature assumes both rendering paths exist across current or future
  deployments and addresses it via FR-006, rather than by trying to replicate the styling
  through the cloud-configured path — that would be a separate configuration effort
  outside this feature's scope.
- "Buildings only" hides categories rather than exhaustively curating individual map
  labels — some incidental labels within a left-visible category (e.g., a road's
  highway-name shield, if left partially visible by Google's own default rendering
  nuances) are acceptable as long as the five named categories (roads, POI, transit,
  administrative, natural landscape) are hidden.
- No new user-facing settings/persistence beyond the map style menu itself are required;
  the existing three styles do not persist across a full page reload today, and this
  feature does not change that.
- This feature only affects the map/GIS content mode's base-map rendering; it does not
  affect the 3D viewer's other content modes or any overlay drawn on top of the map
  (e.g., the current-location marker or a resolved site boundary highlight).
