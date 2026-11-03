# Feature Specification: Dynamic Point of Interest (POI) Discovery

**Feature Branch**: `[090-poi-discovery]`

**Created**: 2026-11-03

**Status**: Draft

**Input**: User description: "Extend Lucy's existing Spatial Request Bundle to support dynamic Point of Interest (POI) discovery, visualization, and layer management.

When a user asks Lucy to locate places such as hospitals, schools, mosques, or other POI categories within a geographic area, Lucy should identify the requested category and location, resolve the location through geocoding when necessary, and invoke a spatial endpoint to retrieve matching POIs through an appropriate API or open-data provider.

The endpoint should return geographic coordinates and available attributes. Lucy should visualize the results as markers in the existing Google Maps 2D/3D viewer."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Search for POI Category (Priority: P1)
**Describe this user journey in plain language**: User asks Lucy to find hospitals in a specific city or geographic area.

**Why this priority**: This is the core POI discovery functionality - the primary reason users would use this feature.

**Independent Test**: Can be tested by invoking Lucy with "Find hospitals in [location]" and verifying markers appear on the map with correct coordinates.

**Acceptance Scenarios**:
1. **Given** user requests "Find hospitals in Muscat", **When** Lucy processes the request, **Then** markers appear on the map at hospital locations with hospital names displayed.
2. **Given** user requests "Find schools in Salalah", **When** Lucy processes the request, **Then** markers appear at school locations.

### User Story 2 - Manage Multiple POI Categories (Priority: P2)
**Describe this user journey in plain language**: User has hospitals displayed and then requests schools to be shown alongside or instead of hospitals.

**Why this priority**: Layer management is a key UX requirement - users need control over multiple POI categories.

**Independent Test**: Can be tested by showing hospitals, then requesting schools, and verifying user is asked whether to clear or keep both categories.

**Acceptance Scenarios**:
1. **Given** hospitals are displayed, **When** user requests schools, **Then** Lucy asks: "Clear hospital markers and show schools, or keep both hospital and school markers alongside each other?"
2. **Given** user chooses to keep both, **When** schools are retrieved, **Then** both hospital and school markers display simultaneously with distinct colors/shapes.
3. **Given** both categories are displayed, **When** user removes one category, **Then** only that category's markers are removed and the legend updates.

### User Story 3 - Geographic Context & Geocoding (Priority: P3)
**Describe this user journey in plain language**: User requests POI categories for a location that needs geocoding (e.g., "find mosques near this address").

**Why this priority**: Geocoding support expands the feature's usability across different input formats.

**Independent Test**: Can be tested by requesting POIs for a named location, address, or coordinates and verifying correct geocoding and POI retrieval.

**Acceptance Scenarios**:
1. **Given** user requests "Find mosques near Sultan Qaboos Hospital, Muscat", **When** Lucy geocodes the address and searches, **Then** mosque markers appear near the specified location.
2. **Given** user provides coordinates directly, **When** Lucy processes the request, **Then** POIs are retrieved for the given location without redundant geocoding.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow users to search for POI categories by name (e.g., "hospitals", "schools", "mosques") within a specified geographic area.
- **FR-002**: System MUST resolve geographic locations through geocoding when users provide place names or addresses instead of coordinates.
- **FR-003**: System MUST invoke a spatial endpoint to retrieve matching POIs through an appropriate API or open-data provider.
- **FR-004**: System MUST return geographic coordinates and available attributes for each retrieved POI.
- **FR-005**: System MUST visualize POI results as markers in the existing Google Maps 2D/3D viewer.
- **FR-006**: System MUST display visually distinct marker shapes or colors for each POI category.
- **FR-007**: System MUST display a floating legend panel identifying each active POI category and its corresponding marker style.
- **FR-008**: System MUST ask users whether to clear previous POI markers or keep them alongside new results when a new category is requested while another is already displayed.
- **FR-009**: System MUST keep the legend synchronized with displayed POI layers; removing or clearing a category must update the legend.
- **FR-010**: System MUST preserve the existing map viewer's interaction patterns and support both 2D and 3D visualization.
- **FR-011**: System MUST handle unsupported POI categories gracefully with user-visible feedback.
- **FR-012**: System MUST handle empty retrieval results gracefully without errors.
- **FR-013**: System MUST handle retrieval failures gracefully with user-visible feedback.

---

## Key Entities

- **Point of Interest (POI)**: A geographic location with attributes including name, category, coordinates, and optional details (address, phone, hours).
- **POI Category**: A type classification for POIs (e.g., hospital, school, mosque, pharmacy, park).
- **Geocoded Location**: A resolved geographic location with latitude and longitude coordinates.
- **POI Layer**: A named collection of POI markers on the map, each associated with a category and marker style.
- **Legend Panel**: A floating UI component that identifies active POI categories and their marker styles.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can search for a POI category by name and see markers displayed on the map within 3 seconds.
- **SC-002**: Users can see location and attribute information for each retrieved POI within 5 seconds of search completion.
- **SC-003**: Users are prompted to choose whether to clear or keep existing POI markers when requesting a new category while another is displayed.
- **SC-004**: Legend panel accurately reflects all active POI categories and updates when categories are added or removed.
- **SC-005**: System handles unsupported categories, empty results, and retrieval failures without crashing or showing raw errors; user receives visible feedback in all cases.
- **SC-006**: Users can view POI markers on the map across both 2D and 3D visualization modes.
- **SC-007**: Feature works across geographic locations including Oman.

---

## Assumptions

- Existing Google Maps 2D/3D viewer infrastructure is available and integrable.
- An appropriate spatial API or open-data provider is available for POI retrieval.
- Geocoding service can resolve locations to coordinates, including addresses and place names in Oman.
- User can be presented with a confirmation dialog when managing multiple POI categories.
- Marker shapes and colors can be distinguished visually for different categories.
- API quotas and licensing restrictions will be respected during the PoC phase.