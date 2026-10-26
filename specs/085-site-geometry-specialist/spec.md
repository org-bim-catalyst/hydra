# Feature Specification: Site Geometry Specialist

**Feature Branch**: `085-site-geometry-specialist`

**Created**: 2026-10-23

**Status**: Draft

**Input**: User description: "Implement the Site Geometry sub-agent for SPEC-057's Site Analysis Agent, per the Follow-ups section of specs/057-site-analysis-agent/plan.md. It is the first of the five deferred specialists that SPEC-057 planned and explicitly deferred. It must follow the exact shape of the shipped SiteSchematicImageGenerationTool: an agent tool that receives {siteAnalysisId, siteName, siteLocation}, does its own work, composes panel content through the existing content composer, and reports through the existing result relay — never any other channel. Its scope is the site's own geometry: parcel/plot boundary, area, frontage, dimensions, and orientation, derived from the OpenStreetMap-derived geospatial source the platform already integrates, reusing SPEC-042's existing boundary-resolution machinery rather than duplicating it. If no shared capability for querying that source exists yet, this specialist introduces one, shared rather than duplicated. Wire-up is additive only: one new analysis type, one provisioner branch entry, one tool registration, and content composed from the existing block vocabulary with no image. Confidence is rule-based through the platform's existing confidence rule, never borrowed from a geocoder's importance score. Tests mirror the existing relay and progressive-delivery tests. Out of scope: cadastral or government land-registry data, three-dimensional or massing measurements, any metric that no integrated source can supply (floor-area ratio, setbacks, height limits), terrain and slope analysis, editing the boundary in the viewer, exporting the geometry as a file, and any rendered diagram of the geometry — the schematic-image specialist already owns imagery."

## Context

Ask Lucy can already run a **Site Analysis** ([specs/057](../057-site-analysis-agent/spec.md)): the user asks for an analysis of a site, and a coordinating agent fans the site out to independent specialists, each of which reports its finding the moment it finishes. One specialist ships today — the **schematic site map**, a generated top-down image of the site.

SPEC-057 planned five further specialists and deliberately deferred all of them. They are the first items of its follow-up list, and its own out-of-scope list names the first of them outright: **Site Geometry**. Until now, that list has had no owner; this feature is the first step down it.

Site Geometry is the right one to build first, for three reasons:

1. **It answers the question users actually ask first.** "How big is this site?", "which way does it face?", "how much of it fronts the street?" — these are the questions a site briefing opens with, and today Lucy answers them in prose with no measurements attached.
2. **The platform already holds the answer.** The site boundary is resolved once, when the analysis is dispatched, and frozen onto the analysis record. The geometry of the site is then arithmetic on that shape. SPEC-057's boundary machinery (SPEC-042) and its OpenStreetMap-derived geospatial source are already integrated, already hardened, and already carry the retry and mirror behaviour this platform learned the hard way.
3. **It makes the coordinator's promise real.** SPEC-057 shipped a two-specialist claim it could only demonstrate with a throwaway test specialist: findings arrive individually, as each finishes, never batched at the end. A second **real** specialist is what turns that from a test artifact into a production behaviour — and it is the first proof that adding a specialist genuinely needs no change to the shared machinery.

What this feature must **not** become is a second site-resolution path. The site was already resolved and confirmed before the fan-out began; a geometry specialist that went off and re-found the site by name would produce figures for a *different* place than the one being analyzed, and would quietly reintroduce the ambiguity the resolution step exists to remove.

## Clarifications

### Session 2026-10-23

- Q: What is the source of the site's geometry? → A: The OpenStreetMap-derived geospatial source the platform already integrates, queried through the same machinery SPEC-042 uses. No new external service, no new credential, no second site-identification path.
- Q: Which figures does the finding carry? → A: The parcel/plot boundary where the map records one, the site area, the site frontage, the overall dimensions, and the orientation. Metric units throughout.
- Q: What if the map data records no parcel/plot boundary for the site? → A: Say so, and fall back to the site's already-resolved site boundary, labelled as the site boundary rather than as a parcel boundary. A parcel-level shape is never inferred from a site-level shape.
- Q: What if the site has no resolved boundary at all (a point-only site)? → A: The figures that depend on a shape are reported as unavailable with the reason. The analysis still succeeds; the other specialist still delivers.
- Q: How is frontage defined when several streets bound the site? → A: Frontage is reported per identifiable street, with a total. If street adjacency cannot be established from the source, frontage is unavailable with the reason — never approximated from the longest edge.
- Q: Does the finding include an image? → A: No. Content uses the existing block vocabulary only (heading, text, key-value, divider). The schematic-image specialist owns imagery, and a second image in the same analysis would duplicate it.
- Q: Is a shared query capability for the map source introduced here? → A: Only if one does not already exist, and then it is introduced as a shared capability rather than a private helper of this specialist, so the remaining four deferred specialists can reuse it.
- Q: How is confidence assigned? → A: By the platform's existing rule-based confidence helper, from grounding facts only. A geocoder's importance score is never used, for the reason SPEC-057 recorded.
- Q: Does the site's existing boundary snapshot get re-resolved? → A: No. The geometry specialist analyses the boundary frozen onto the analysis record at dispatch time, so the figures always describe the same site as the rest of the analysis.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The site's own geometry arrives as a finding (Priority: P1)

A user asks Lucy to analyze a site. Alongside the schematic map, a second finding describes the site's own shape: how much area it covers, how big it is, how much of it fronts a street, which way it faces, and where its parcel or plot boundary lies. The user can read the numbers and see where they came from, without asking Lucy a follow-up question and without leaving the analysis view.

**Why this priority**: This is the feature. Every other deferred specialist adds a dimension the site has; this one adds the measurements of the site itself, which is the fact most design and planning work starts from.

**Independent Test**: Run a site analysis against a site whose boundary the platform can resolve, and confirm a geometry finding is delivered in its own right — with area, dimensions, frontage, orientation, and a boundary — and that it is delivered as its own notice and panel rather than folded into the schematic map.

**Acceptance Scenarios**:

1. **Given** a site whose boundary resolves, **When** a site analysis runs, **Then** a geometry finding is delivered stating the site's area, its overall dimensions, its frontage, and its orientation.
2. **Given** a site analysis running, **When** the geometry specialist finishes, **Then** its finding is delivered on its own notice and its own panel, in addition to the schematic map's, and the user is told which finding has arrived.
3. **Given** a site whose map data records a parcel or plot boundary, **When** the geometry finding is read, **Then** the parcel or plot boundary is identified as such and is distinguishable from the site's own boundary.
4. **Given** a site whose map data records no parcel or plot boundary, **When** the geometry finding is read, **Then** it says the map data records no parcel boundary for this site and presents the site's resolved boundary as the site boundary — it does not present one as the other.
5. **Given** a site with no resolved boundary at all, **When** the analysis runs, **Then** the geometry finding reports the shape-dependent figures as unavailable with the reason, the analysis still completes, and the other findings are still delivered.

---

### User Story 2 - Every geometry figure says where it came from (Priority: P1)

A user who is about to act on a measurement — a site area, a frontage length, a facing direction — must be able to see whether it is a measured figure from map data, a figure derived from the site's resolved shape, or a figure that could not be established. The finding states which, every time, and never presents an approximation in the place of a figure it does not have.

**Why this priority**: A confident-looking area figure that is really an estimate is worse than no area figure at all, and this is exactly the failure SPEC-057's honesty rules were written to prevent. It ships with the first finding, not after.

**Independent Test**: Open a delivered geometry finding and confirm it names the analysis that produced it, the data source behind it, and a confidence level, and confirm that any figure without a source is marked unavailable with a reason rather than estimated.

**Acceptance Scenarios**:

1. **Given** a delivered geometry finding, **When** the user reads it, **Then** it shows which analysis produced it, which data source it used, its confidence level, and when it was generated.
2. **Given** a figure the source can supply, **When** the finding is read, **Then** the figure is stated as a measured value derived from the map data, not as an approximation.
3. **Given** a figure the source cannot supply, **When** the finding is read, **Then** that figure is reported as unavailable together with the reason, and no estimated value stands in its place.
4. **Given** a finding whose figures were derived from the site's resolved shape rather than read directly from a parcel record, **When** the finding is read, **Then** the distinction between the two is visible to the reader.
5. **Given** the site's map data is unusually incomplete, **When** the finding is read, **Then** the incompleteness is stated in the finding itself rather than being left for the reader to infer from a short answer.

---

### User Story 3 - A geometry specialist that fails does not sink the analysis (Priority: P2)

The map source is a free, shared, frequently-saturated service. When it is slow, rate-limited, or returns something unusable, the geometry finding simply does not arrive — while the schematic map still does, and the analysis still reaches a definite end state. An operator can afterwards determine exactly what went wrong, from the record or from the logs alone.

**Why this priority**: Independent specialists mean independent failure modes, and this one has a genuinely flaky upstream. It is P2 only because it presupposes the delivery chain that SPEC-057 already ships.

**Independent Test**: Make the map source fail during an analysis, and confirm the other finding is still delivered, the analysis still completes, no error interrupts the user, and the failure is recorded with a reason and diagnosable from the logs.

**Acceptance Scenarios**:

1. **Given** the map source is unavailable, **When** the geometry specialist runs, **Then** no geometry finding is delivered, the other finding still is, and the analysis still reaches a completed state.
2. **Given** the geometry specialist failed, **When** an operator inspects the analysis, **Then** the failure and its reason are recorded and attributable to the geometry specialist.
3. **Given** the geometry specialist failed, **When** the user reads the conversation, **Then** no error notice or panel for that failure appears on its own — the user's reading of the successful findings is uninterrupted.
4. **Given** the map source fails transiently, **When** the geometry specialist runs, **Then** it retries the same way the platform's other map queries do, rather than failing on the first transient error.

---

### User Story 4 - Adding this specialist changed no shared machinery (Priority: P3)

A platform engineer adding the second of the five planned specialists can confirm that the coordinator, its validation gate, its delivery, its persistence, and the client's rehydration all worked unchanged. The second specialist's findings arrive independently of the first's, each on its own schedule.

**Why this priority**: It is the whole point of the design SPEC-057 committed to, and it is P3 because it is verified by inspection and by this feature's own behaviour rather than by end-user-visible change. This feature is also what makes SPEC-057's progressive-arrival promise demonstrable in production rather than only in a test.

**Independent Test**: Confirm the shared coordination, validation, delivery, persistence, and client files are unchanged by this feature, and observe two findings arriving at two different times from one analysis.

**Acceptance Scenarios**:

1. **Given** the site analysis coordinator and its delivery chain exist, **When** the geometry specialist is added alongside the schematic-image specialist, **Then** no shared coordination, validation, delivery, persistence, or rehydration logic changes.
2. **Given** an analysis with both specialists running, **When** they finish at different times, **Then** each finding is delivered at its own time, and neither waits for the other.
3. **Given** the geometry specialist is added, **When** an analysis completes, **Then** the analysis's closing outcome reflects that both specialists were counted, including when one of them failed.

## Requirements *(mandatory)*

### Functional Requirements

**Adding the specialist**

- **FR-001**: The system MUST add a site-geometry specialist to the site analysis fan-out, delivered through the existing specialist chain.
- **FR-002**: Adding the specialist MUST require only a new specialist registration and a new parallel branch declaration. The coordination, validation, delivery, persistence, and rehydration behaviour MUST be unchanged.
- **FR-003**: The system MUST count the geometry specialist among the findings an analysis expects, so an analysis that dispatches it reaches its end state only once both specialists have settled.

**Analysing the right site**

- **FR-004**: The geometry specialist MUST analyse the site the analysis was started for, using the site geometry resolved and frozen when the analysis was dispatched.
- **FR-005**: The geometry specialist MUST NOT resolve the site again by name, and MUST NOT introduce a second site-identification path.
- **FR-006**: The geometry specialist MUST receive the identity of the analysis it is reporting into, so that its finding is attached to that analysis and its owning conversation.

**The figures**

- **FR-007**: The system MUST report the site's **area**, its **overall dimensions**, its **frontage**, and its **orientation**, in metric units.
- **FR-008**: The system MUST report the **parcel or plot boundary** where the map data records one for the site, and MUST distinguish it from the site's own resolved boundary.
- **FR-009**: Where the map data records no parcel or plot boundary, the system MUST say so and present the site's resolved boundary as the site boundary. A parcel-level shape MUST NOT be inferred from, or presented as, a site-level shape.
- **FR-010**: The system MUST report frontage per identifiable street, together with a total, and MUST report which streets they are.
- **FR-011**: Where street adjacency cannot be established from the source, the system MUST report frontage as unavailable with the reason. Frontage MUST NOT be approximated from the site's longest edge.
- **FR-012**: Where the site has no resolved boundary, the system MUST report every shape-dependent figure as unavailable with the reason, and MUST NOT report a shape it does not have.
- **FR-013**: Every figure MUST be computed from resolved geometry. A figure MUST NOT be estimated, and an approximation MUST NOT be presented as though it were measured.
- **FR-014**: The system MUST report the orientation of the site as a compass direction the user can act on, not as an unexplained number.

**Data source**

- **FR-015**: The system MUST derive the site's geometry from the OpenStreetMap-derived geospatial source the platform already integrates, using the platform's existing query machinery for it.
- **FR-016**: The system MUST NOT introduce a second external geospatial service, a new credential, or a new site-identification path.
- **FR-017**: The system MUST inherit the platform's existing behaviour for that source — its retry budget, its endpoint rotation, its timeout, and its unavailability classification — rather than re-inventing any of it.
- **FR-018**: Where the platform has no shared capability for querying that source for this purpose, the system MUST introduce one as a shared capability, so that the remaining deferred specialists can reuse it rather than each adding a private one.

**Composition and delivery**

- **FR-019**: The geometry finding MUST be presented using the platform's existing panel content vocabulary, and MUST NOT introduce a new kind of content block.
- **FR-020**: The geometry finding MUST NOT contain an image. Imagery is the schematic specialist's output.
- **FR-021**: The geometry specialist MUST report its finding through the existing specialist reporting channel — the parent coordinator's result intake — and MUST NOT post to the conversation, open a panel, or otherwise place anything before the user itself.
- **FR-022**: The geometry finding MUST carry its provenance: the analysis that produced it, the data source behind it, its confidence level, and its generation time.

**Confidence**

- **FR-023**: The confidence level of a geometry finding MUST be assigned by the platform's existing rule-based confidence assignment, from grounding facts alone.
- **FR-024**: Confidence MUST NOT be derived from a geocoder's importance score, which is populated on incompatible scales by different providers.

**Failure behaviour**

- **FR-025**: A failure of the geometry specialist MUST be captured at the point it occurs, recorded with its reason and its originating error detail, and written to the platform's structured logs with enough context to diagnose it — never swallowed, discarded, or left unobserved.
- **FR-026**: A failure of the geometry specialist MUST NOT prevent the other specialists' findings from being delivered, and MUST NOT prevent the analysis from reaching a definite end state.
- **FR-027**: A failure of the geometry specialist MUST NOT produce a notice, a panel, or any other interruption for the user on its own.
- **FR-028**: The system MUST NOT introduce a new kind of failure for this specialist; it MUST reuse the platform's existing failure vocabulary and classification.

**Limits**

- **FR-029**: The geometry specialist's execution MUST remain bounded by the platform's existing agent and workflow execution limits, and MUST NOT introduce a new budget.
- **FR-030**: The number of source queries the geometry specialist issues per run MUST be bounded, and the bound MUST be a documented value rather than an incidental one.

### Key Entities

- **Site Geometry Finding**: One geometry specialist's outcome within a site analysis. Carries the figures the site measurement produced, the boundary it was measured from, its provenance, and — when unsuccessful — its failure reason. It is one of the analysis's results, not a separate record type.
- **Parcel or Plot Boundary**: The map-data-recorded outline of the site's own plot, when one is recorded. Distinct from the site's resolved boundary; a site may have one, or only the latter.
- **Site Area**: The area the site's resolved shape encloses, in square metres.
- **Frontage**: The length of the site's boundary that fronts a given identifiable street, and the total across those streets.
- **Site Dimensions**: The site's overall extent — its two principal extents and the rectangle they bound.
- **Orientation**: The direction the site's principal axis points, expressed as a compass direction.
- **Unavailable Figure**: A figure that could not be established, carried with the reason it could not be, in place of a value.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of site analyses that dispatch the geometry specialist deliver a geometry finding carrying area, dimensions, frontage, and orientation — or, where the source cannot supply one, carrying that figure marked unavailable with its reason.
- **SC-002**: 100% of delivered geometry findings display the analysis that produced them, the data source behind them, and a confidence level.
- **SC-003**: 0 geometry figures are presented as measured values that were estimated, interpolated, or derived from a figure the source did not supply.
- **SC-004**: 100% of sites whose map data records no parcel or plot boundary have findings that say so, and 0 findings present a site-level shape as a parcel-level one.
- **SC-005**: When the map source fails during an analysis, 100% of the other findings are still delivered and the analysis still reaches a completed state.
- **SC-006**: 100% of geometry-specialist failures are recorded with a reason attributable to that specialist, and are diagnosable from the platform's operational logs alone — no failure path discards its error.
- **SC-007**: 0 notices or panels are produced for a geometry-specialist failure on its own.
- **SC-008**: Adding the geometry specialist requires 0 changes to the coordinator, the validation gate, the delivery path, the persistence layer, and the client's rehydration path — verified against the change itself.
- **SC-009**: In an analysis where the two specialists finish at different times, each finding reaches the user at its own time, and neither is held back for the other — verified by observing two findings arriving at correspondingly different times.
- **SC-010**: 0 new external services, credentials, or per-user configuration items are required for the geometry specialist to work.

## Assumptions

- **The site's boundary is the measurement basis.** The site boundary resolved and frozen when the analysis was dispatched is the shape the figures describe. The geometry specialist does not attempt a more precise shape.
- **The map source is the only parcel/plot source available.** Parcel and plot coverage in OpenStreetMap is partial and uneven. That is a property of the source, and the finding must express it rather than paper over it.
- **Metric units throughout.** Area in square metres; frontage and dimensions in metres.
- **Orientation is the principal axis.** The site's principal axis is the direction its longer extent runs, reported as a compass direction.
- **Frontage requires street adjacency.** A frontage figure is only reported where the source can establish which street an edge fronts.
- **Figures that depend on a shape need a shape.** A point-only site has no area, no dimensions, no frontage, and no parcel boundary, and the finding says so rather than omitting them silently.
- **The analysis is per-user and per-conversation.** Findings are delivered only to the user who initiated the analysis, as they already are for every other specialist.
- **Existing execution limits apply.** The specialist's duration, concurrency, and cost are bounded by the platform's existing agent and workflow execution limits.

## Out of Scope

Deliberately excluded from this release:

- The remaining four deferred specialists — Urban Context, Connectivity & Access, Environmental Context, and Character Analysis. This feature establishes the shared source-query capability they will reuse; it does not implement them.
- Cadastral, land-registry, or any other government-authoritative parcel data. It would be a new data source with its own licensing and credential, and a new decision rather than an extension.
- Three-dimensional measurement — heights, massing, floor areas, and building volumes.
- Any metric no integrated source can supply: floor-area ratio, setbacks, height limits, flood modelling, hydrology, wind, and climate.
- Terrain, slope, and elevation analysis.
- Editing, redrawing, or annotating the site's boundary in the viewer.
- Exporting the geometry as a file, document, or report.
- Any rendered diagram, plan, or picture of the site's geometry. The schematic-image specialist owns imagery for an analysis.
- A rendered geometry preview inside the finding panel, and a boundary drawn on the map as part of this finding.
