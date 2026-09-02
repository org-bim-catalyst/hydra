# Feature Specification: Site Boundary Resolution

**Feature Branch**: `042-site-boundary-resolution`

**Created**: 2026-08-26

**Status**: Draft

**Input**: User description: "Site Boundary Resolution: give Lucy a reusable capability to identify a site/location, resolve its geographic boundary as a polygon with a confidence score and source (extending the existing Locations point-resolution module rather than duplicating it), and render that polygon in the Three.js/Google Maps viewer with an animated highlighted border so the site is clearly recognizable. Must communicate confidence level and source to the user, and must be generic/modular (callable as an Agent Tool for any site) so it can be reused for future urban-planning/design projects beyond the initial Al Safa Park 2 case. Based on the architecture proposal already written at docs/SITE_BOUNDARY_RESOLUTION_ARCHITECTURE.md, which ported the boundary-resolution pipeline (OSM candidate search, deterministic weighted scoring, confidence classification, optional pluggable AI-vision critique) from docs/AL_SAFA_PARK_2_AI_ANALYSIS_V5.ipynb, and the animated perimeter-highlight visual technique from docs/BORDER_HIGHLIGHT.html (generalized to shader-only additive glow, no EffectComposer/bloom pass, since the viewer's GIS render path runs no post-processing pipeline today). Scope is limited to identify -> bound -> polygonize -> display -> explain confidence/source; downstream analysis (KPI scoring, design generation, CAD/DXF override) is explicitly out of scope for this spec."

## Clarifications

### Session 2026-08-26

- Q: Should site boundary resolution be gated by subscription tier? → A: Available to all authenticated users, same as the existing location lookup — no tier restriction.
- Q: When multiple candidate boundaries are similarly plausible, does Lucy still render a default choice? → A: Yes — show the top-scored candidate immediately, but explicitly caveat that other similarly-plausible candidates exist and name them (not a hard gate, not showing all candidates at once).
- Q: What response-time target should SC-001 use for displaying a resolved boundary? → A: 10 seconds (confirmed).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See a named site's boundary highlighted on the map (Priority: P1)

A user mentions or asks about a specific site by name or address (e.g., "show me Al Safa Park 2"). Lucy resolves not just its location but its actual extent, and highlights that shape on the map so the user can immediately see where the site starts and ends — not just a pin on a point.

**Why this priority**: This is the entire value proposition. Without a visibly correct, recognizable boundary, nothing else in this feature matters — a confidence score or source citation is meaningless if the user can't see what it's describing.

**Independent Test**: Ask Lucy about a well-known, mappable site (e.g., a public park) and confirm a highlighted shape appears on the map that visually matches the site's real extent, not just a marker or a generic circle.

**Acceptance Scenarios**:

1. **Given** a user has not yet referenced any location in the conversation, **When** they ask Lucy about a named site that has a well-mapped shape available, **Then** Lucy displays that shape highlighted on the map, distinguishable from the surrounding area.
2. **Given** a boundary is already displayed for a site, **When** the user asks a follow-up question that refers back to "it"/"that site" without repeating the name, **Then** Lucy continues to reference the same previously-resolved boundary rather than re-resolving or losing it.
3. **Given** a user asks about a site type with no well-mapped shape available (e.g., an obscure or unmapped address), **Then** Lucy falls back to showing an approximate area around the point and clearly states that this is an approximation, not a confirmed boundary.

---

### User Story 2 - Understand how much to trust a shown boundary (Priority: P2)

Whenever Lucy shows a site boundary, the user can see and be told how confident Lucy is in it and where the shape came from, so they know whether to treat it as authoritative or double-check it themselves.

**Why this priority**: A boundary that looks equally confident whether it's a precisely-mapped park or a rough guess is actively misleading — this is what makes the feature trustworthy enough to use for real planning decisions, rather than just a nice-looking overlay.

**Independent Test**: Trigger both a high-confidence result (a well-mapped, clearly-named site) and a low-confidence result (an ambiguous or poorly-mapped site) and confirm the confidence level and data source are visibly different and both explicitly stated, not just implied by the shape's appearance.

**Acceptance Scenarios**:

1. **Given** Lucy resolves a boundary with strong supporting evidence (a named, tagged match close to the resolved point), **When** the boundary is displayed, **Then** Lucy states a "High" confidence level and names the data source it came from.
2. **Given** Lucy resolves a boundary with weak or conflicting supporting evidence, **When** the boundary is displayed, **Then** Lucy states a "Medium" or "Low" confidence level, visually distinguishes it from a high-confidence result, and explains what makes it uncertain (e.g., "no officially tagged boundary found nearby").
3. **Given** a user asks "how sure are you about this?" or "where did this come from?" after a boundary is shown, **When** they ask, **Then** Lucy can answer using the same confidence/source information already attached to that boundary, without needing to re-run resolution.

---

### User Story 3 - Get a clear answer when no reliable boundary exists (Priority: P3)

When Lucy cannot find a boundary she's reasonably confident in — or finds several conflicting candidates — she says so plainly and offers the user a way forward, instead of silently guessing or showing nothing.

**Why this priority**: Silence or an unstated low-confidence guess is worse than an explicit "I'm not sure" — this preserves trust in every other result the feature produces, and prevents a wrong boundary from being mistaken for a confirmed one.

**Independent Test**: Ask Lucy about a location with no discoverable mapped shape, and separately about a location where multiple similarly-plausible candidates exist, and confirm both cases produce an explicit, distinguishable response rather than an unexplained or incorrect shape.

**Acceptance Scenarios**:

1. **Given** no plausible boundary candidate can be found for a resolved location, **When** Lucy responds, **Then** she explicitly states that no reliable boundary was found and offers the best available fallback (e.g., an approximate area, or asking the user for more detail) rather than showing nothing or fabricating a shape.
2. **Given** two or more candidate boundaries are similarly plausible with no clear winner, **When** Lucy responds, **Then** she still displays the top-scored candidate but explicitly names the other similarly-plausible candidates rather than silently picking one with no disclosure.
3. **Given** a user says a displayed boundary looks wrong, **When** they flag it, **Then** Lucy acknowledges the correction request and either reconsiders using more specific input from the user or clearly states she cannot resolve it more precisely with the information available — she does not simply repeat the same result.

---

### User Story 4 - Use the same capability for a different site or project (Priority: P4)

A user working on a different site than the original reference project (a different park, a building, a plaza) gets the same boundary-resolution experience without anyone having to configure or customize anything for that new site first.

**Why this priority**: This is what turns a one-off feature built for a single park into a reusable platform capability — it's explicitly called out as a requirement, but it delivers no standalone user-visible value beyond what Story 1–3 already demonstrate, so it's validated last, as a generalization check on those stories rather than new behavior.

**Independent Test**: Repeat the Story 1 and Story 2 acceptance scenarios against at least two sites unrelated to the original reference project (different location, different site type) and confirm the same behavior, quality bar, and explanations apply with no project-specific setup step.

**Acceptance Scenarios**:

1. **Given** the feature has already been used successfully for one site, **When** a user asks about an entirely unrelated site by name, **Then** Lucy resolves and displays its boundary using the same process, with no indication that the feature was "set up" for a different, specific site.
2. **Given** a second, unrelated urban-planning project references a different site, **When** that site is looked up, **Then** the confidence scoring and source explanation behave consistently with the first project's results for a comparably well- or poorly-mapped site.

### Edge Cases

- What happens when the site name is ambiguous (multiple distinct real-world places share the name, e.g., a common park name that exists in several cities)? Lucy should surface the ambiguity rather than guessing which one was meant, consistent with how she already handles ambiguous location names today.
- What happens when the underlying map data source is temporarily unavailable? Lucy must tell the user boundary resolution is unavailable right now, not show a stale, empty, or default result silently.
- What happens when a resolved boundary is implausibly large (e.g., an entire city district matched instead of a single site) or implausibly small (a mapping error)? These must be scored down and not presented as a confident match.
- What happens when the user's conversation switches to an entirely new site mid-conversation? The previously displayed boundary must be replaced, not left overlaid alongside the new one.
- What happens if a site spans a shape too complex or irregular to render clearly at the user's current map zoom level? The boundary should still render recognizably rather than being skipped or simplified into something misleading.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Lucy MUST be able to attempt boundary resolution for any site or location the user references by name, address, or coordinates during a conversation — not only a pre-configured or specific reference site.
- **FR-002**: When a boundary is resolved, Lucy MUST display it as a highlighted shape on the map/viewer, visually distinguished from the surrounding area, so the site is immediately recognizable.
- **FR-003**: Lucy MUST reuse the existing point/location resolution (name or address → coordinates) rather than re-implementing it — boundary resolution is an additional step on top of an already-resolved location, not a replacement for it.
- **FR-004**: Every displayed boundary MUST have an associated confidence level (at minimum: High, Medium, Low) that is communicated to the user, not just computed internally.
- **FR-005**: Every displayed boundary MUST have an associated data source stated to the user (e.g., which public mapping source supplied the shape), so the user can judge its reliability.
- **FR-006**: A boundary classified as Medium or Low confidence MUST be visually and verbally distinguished from a High-confidence boundary — it MUST NOT be presented as if it were certain.
- **FR-007**: When no boundary candidate can be found with at least minimal supporting evidence, Lucy MUST explicitly inform the user rather than silently showing nothing or fabricating a shape.
- **FR-008**: When multiple candidate boundaries are similarly plausible with no clear best match, Lucy MUST still display the top-scored candidate by default, but MUST explicitly caveat that other similarly-plausible candidates exist and name them — she MUST NOT silently select one with no disclosure, and MUST NOT block display entirely pending user confirmation.
- **FR-009**: The currently displayed boundary MUST persist as part of the conversation's context so that follow-up questions referring back to "it"/"that site" can use the same result without forcing a fresh resolution.
- **FR-010**: When a user indicates a displayed boundary appears incorrect, Lucy MUST acknowledge the feedback and either attempt reconsideration with any additional detail the user provides, or state plainly that she cannot improve on the result with the information available — she MUST NOT silently repeat the same unexamined result.
- **FR-011**: The boundary-resolution capability MUST behave identically regardless of which site or project it is invoked for — no part of the resolution or scoring logic may be specific to, or hardcoded for, the initial reference project.
- **FR-012**: When the underlying mapping data source needed for resolution is unavailable, Lucy MUST inform the user that boundary resolution could not be completed right now, rather than returning an empty, stale, or default result without explanation.
- **FR-013**: Implausible candidate boundaries (far larger or smaller than reasonable for the type of site being described) MUST score lower than plausible ones and MUST NOT be presented as confident matches.
- **FR-014**: Site boundary resolution MUST be available to every authenticated user regardless of subscription tier, consistent with the existing location-lookup capability it builds on — it MUST NOT be gated as a premium-only feature.

### Key Entities

- **Site Boundary**: The resolved outer shape representing a location's real-world extent, together with its confidence level, its data source, and a plain-language explanation of how it was determined. This is what gets displayed and what the user is told about.
- **Boundary Candidate**: One possible shape considered while resolving a Site Boundary, evaluated against the others before the best-supported one (or an explicit "no confident match" outcome) is chosen. Not necessarily shown to the user directly, but its existence is what makes the ambiguity- and confidence-related requirements possible.
- **Confidence Level**: A simple, user-facing classification (High/Medium/Low) attached to a Site Boundary, derived from how strong the supporting evidence was.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When a user asks about a well-known, publicly-mapped site, Lucy displays a recognizable boundary highlighted on the map within 10 seconds, without the user needing to ask twice or rephrase.
- **SC-002**: 100% of displayed site boundaries are accompanied by a stated confidence level and data source — there is no scenario in which a boundary appears with neither.
- **SC-003**: 0% of Low-confidence boundaries are visually indistinguishable from High-confidence ones — a user can tell them apart by appearance alone, before reading any accompanying text.
- **SC-004**: The same resolution behavior and quality bar hold across at least three distinct kinds of sites (e.g., a park, an institutional/building site, and a generic street address) with no site-specific configuration required.
- **SC-005**: 0% of "no reliable boundary found" situations result in a silent empty response — every such case produces an explicit statement to the user.

## Assumptions

- Publicly available mapping data (e.g., OpenStreetMap-sourced boundaries) is an acceptable and sufficient default source of boundary shapes for this feature; access to authoritative government/cadastral boundary data is a future enhancement, not a requirement here.
- The existing capability that resolves a place name/address to coordinates is reused as-is; this feature adds boundary-shape resolution on top of it and does not change how point resolution itself works.
- An optional AI-assisted visual check against satellite imagery may improve confidence in some cases, but is not required for this feature to deliver its core value — a boundary can be resolved and scored using map data alone.
- User correction is limited to acknowledging feedback and re-attempting resolution with more detail; free-form manual boundary drawing/editing by the user is out of scope for this feature. This is a deliberate, known future direction (letting a user manually adjust a displayed boundary's polygon — adding/removing/moving vertices directly in the viewer) rather than an oversight; it is intentionally deferred to a later feature, not part of this one.
- This feature covers identifying, scoring, shaping, and displaying a site's boundary only. Any downstream site analysis (scoring the site against planning criteria, generating design concepts, cost modeling, or importing CAD/survey drawings) is explicitly out of scope and may be addressed by future features.
- The map/3D viewer surface already used elsewhere in the product is reused for displaying the boundary; no new, separate visualization surface is introduced.
- A resolved boundary is treated as conversation-scoped, similar to how a resolved location already persists for back-references within a conversation; long-term storage or a history of previously-resolved boundaries is not required for this feature.
