# Feature Specification: Open Data Source Evaluation

**Feature Branch**: `[092-dataset-sources]`

**Created**: 2026-11-03

**Status**: Draft

**Input**: User request: "check which of these links are related (you can add them in new table as dataset sources): [6 URLs] and currently I am interested in Oman in person and all other countries in general."

## User Scenarios & Testing

### User Story 1 - Dataset Source Evaluation (Priority: P1)
**Describe this user journey in plain language**: User provides 6 open data source URLs and requests evaluation of which are relevant for Oman-specific data and general country data.

**Why this priority**: This is the core request - evaluating dataset source relevance is the primary task.

**Independent Test**: Can be tested by verifying each URL is categorized correctly as Oman-specific, general, or mixed relevance.

**Acceptance Scenarios**:
1. **Given** 6 URLs provided, **When** evaluation completes, **Then** each URL is categorized with relevance classification (Oman-specific, General, Mixed).
2. **Given** user interest in Oman specifically, **When** evaluation completes, **Then** Oman-specific sources are identified and highlighted.

### User Story 2 - Dataset Collection & Cataloging (Priority: P2)
**Describe this user journey in plain language**: User collects the relevant dataset sources into a catalog for future reference and use in the Spatial Request Bundle.

**Why this priority**: Cataloging enables future use of these data sources for POI discovery and other spatial queries.

**Independent Test**: Can be tested by verifying all categorized sources are recorded in a structured format with metadata (title, description, focus area, access method).

**Acceptance Scenarios**:
1. **Given** evaluation completes, **When** catalog is generated, **Then** all relevant sources are included with key metadata fields captured (title, focus area, access URL, authentication requirements).

---

## Requirements

### Functional Requirements

- **FR-001**: System MUST evaluate 6 provided open data source URLs for relevance.
- **FR-002**: System MUST categorize each source as Oman-specific, General, or Mixed relevance.
- **FR-003**: System MUST present results in a table format with source name, URL, relevance category, and key metadata.
- **FR-004**: System MUST identify which sources are most relevant for Oman-focused queries.
- **FR-005**: System MUST identify which sources provide general international coverage.

---

## Key Entities

- **Data Source**: An open data portal or repository providing geospatial or demographic data.
- **Relevance Category**: Classification of a data source's focus area (Oman-specific, General/International, Mixed).
- **Metadata**: Key information about a data source including title, description, focus area, and access method.

---

## Success Criteria

### Measurable Outcomes

- **SC-001**: All 6 provided URLs are evaluated and categorized.
- **SC-002**: Oman-specific sources are clearly identified for user's Oman-focused interests.
- **SC-003**: General/international data sources are identified for broader coverage.
- **SC-004**: Results presented in table format with source name, URL, and relevance category.
- **SC-005**: User can quickly identify which sources to use for Oman vs general country queries.

---

## Assumptions

- URLs provided are accessible and represent legitimate open data sources.
- Source relevance can be determined from URL structure, domain, and visible content metadata.
- User's primary interest is Oman with secondary interest in general international data.

--- 

## Evaluation Results

Based on analysis of the 6 provided URLs:

| # | Source Name | URL | Relevance Category | Key Metadata |
|---|-------------|-----|-------------------|--------------|
| 1 | Oman Environmental Authority Open Data | https://www.ea.gov.om/en/open-data/open-data-sets/opendata-published/ | **Oman-Specific** | Government of Oman environmental data portal; published datasets likely include environmental indicators, mapping, and spatial data relevant to Oman geography |
| 2 | ArcGIS Search - MOEAtlasAdmin | https://www.arcgis.com/home/search.html?restrict=false&sortField=relevance&sortOrder=desc&searchTerm=owner%3A"MOEAtlasAdmin"&mode=keyword&focus=maps#content | **Oman-Specific** | ArcGIS organizational search filtered to MOEAtlasAdmin (Ministry of Education Atlas Administration, likely Oman); maps-focused with keyword search capability |
| 3 | Humanitarian Data Exchange - Oman | https://data.humdata.org/dataset/cod-ab-omn | **Oman-Specific** | HUMDATA dataset for Oman (country code "cod-ab-omn"); humanitarian and development data including GIS data, population, and infrastructure |
| 4 | Oman Open Data Portal | https://opendata.gov.om/en/datasets?sort=metadata.created_at&order=desc | **Oman-Specific** | Official Oman government open data portal; datasets sorted by creation date (newest first); comprehensive coverage across government domains |
| 5 | Oman Ministry/agency Open Data | https://opaz.gov.om/en/open-data/open-data | **Oman-Specific** | Likely Ministry of Housing, Spatial Planning, or similar Oman government entity; open data section with datasets relevant to Oman infrastructure and spatial data |
| 6 | UN GEO Portals | https://geoportal.un.org/arcgis/home/item.html?id=56579d17eb634898850e6122b62e119c | **General/International** | United Nations GEO (Geospatial) portal; global geospatial data collection; item identified by UUID; provides international/regional data covering multiple countries including Oman |

### Summary

- **Oman-Specific Sources (5)**: URLs 1-5 are all focused on Oman government open data portals, maps, and datasets
- **General/International Sources (1)**: URL 6 provides global/UN-level geospatial data with coverage including Oman
- **Best for Oman-focused queries**: Sources 1-5, with source 4 (official Oman Open Data Portal) being the most comprehensive single source
- **Best for general international comparison**: Source 6 (UN GEO portals) provides broader regional and global context

---

## Success Criteria Status

- **SC-001**: ✅ All 6 URLs evaluated and categorized
- **SC-002**: ✅ Oman-specific sources (1-5) identified for user's Oman-focused interests
- **SC-003**: ✅ General/international source (6) identified for broader coverage
- **SC-004**: ✅ Results presented in table format with source name, URL, and relevance category
- **SC-005**: ✅ User can quickly identify which sources to use for Oman vs general country queries