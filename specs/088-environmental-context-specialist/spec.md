# Feature Specification: Environmental Context Specialist

**Feature Branch**: `088-environmental-context-specialist`

**Created**: 2026-10-25

**Status**: Draft

**Input**: User description: "Implement the Environmental Context sub-agent for SPEC-057's Site Analysis Agent. Follow the exact shape of the shipped SiteSchematicImageGenerationTool: an IAgentTool taking {siteAnalysisId, siteName, siteLocation}, doing its own work, composing content via SiteAnalysisContentComposer, and reporting exclusively through ISiteAnalysisResultRelay. Scope: flood risk and climate/wind conditions for the site. Use the existing stub interfaces IFloodDataProvider and IClimateDataProvider — they have no real implementation — and render 'no data source available' for any field they report as unavailable, per the existing confirmed|no_candidates|ambiguous|unavailable outcome pattern SiteBoundaryResolverTool already uses. Do not invent a data source: licensing a real source is explicitly deferred. This sub-agent must also cross-reference specs/052-solar-analysis's client-side solar analysis rather than re-deriving solar/sun data server-side, surfacing a reference to that existing analysis in this specialist's panel content rather than recomputing anything duplicative. Wire-up is additive: one new analysis type, one provisioner branch entry, one tool registration, content composed from the existing block vocabulary only (heading, text, keyValue, divider). Confidence is rule-based through SiteAnalysisConfidence.ResolveConfidence — never GeocodingCandidate.Importance — and an unavailable field pushes confidence down rather than being silently omitted. Tests mirror SiteAnalysisResultRelayTests and SiteAnalysisProgressiveDeliveryTests, including the case where both data providers report Unavailable."

## Context

Ask Lucy can already run a **Site Analysis** ([specs/057](../057-site-analysis-agent/spec.md)): the user asks for an analysis of a site, and a coordinating agent fans the site out to independent specialists, each of which reports its finding the moment it finishes. One specialist ships today — the **schematic site map**, a generated top-down image of the site — and a second, **Site Geometry**, is specified separately ([specs/085](../085-site-geometry-specialist/spec.md)).

SPEC-057 planned five further specialists and deliberately deferred all of them. Its own out-of-scope list names this one: **Environmental Context**. This feature gives it an owner.

The specialist answers the two environmental questions a site briefing asks before anything is drawn: **what is the flood risk here**, and **what are the climate and wind conditions** at this location. Today the platform cannot answer either, and that is the central fact shaping this specification.

**There is no environmental data source.** SPEC-057 anticipated exactly this: it declared `IFloodDataProvider` and `IClimateDataProvider` as the seam through which those figures would one day arrive, and deliberately shipped them with **no implementation** ([plan.md](../057-site-analysis-agent/plan.md) Complexity Tracking, [research.md](../057-site-analysis-agent/research.md) D11). Licensing a real flood or climate source is a separate commercial and legal decision — a new vendor, a new credential, a new licence — and is **not** made here. What ships here is the specialist, the seam it consumes, and the honest statement of what the platform cannot yet measure.

**What the domain covers, and what this release assesses.** "Environmental context" is a broader domain than the four figures this specialist reports. The table is the whole domain, and where each part of it is answered today:

| Dimension | This release |
|---|---|
| Sun path — solar altitude, azimuth, and shading | **Referenced** — already computed in the browser by [specs/052](../052-solar-analysis/spec.md) |
| Wind — prevailing direction and average speed | **Assessed** — the two climate figures this specialist reports |
| Flood risk — flood-zone classification and annual exceedance probability | **Assessed** — the two flood figures this specialist reports |
| Temperature, humidity, precipitation | Not assessed — no configured source |
| Topography — slope, soil, bedrock | Not assessed — no configured source |
| Hydrology — water table, surface water, stormwater | Not assessed — no configured source |
| Ecology — canopy, protected trees, habitats, ecosystem services | Not assessed — no configured source |
| Noise, air quality, site hazards | Not assessed — no configured source |

The assessed rows are exactly what the platform's two provider seams define; every not-assessed row would need a source that no seam models yet. Two consequences follow, and both are requirements below. A finding that is silent about topography reads as though topography were fine, so the finding must say what it did not assess (FR-041). And an unassessed dimension is not the same claim as an attempted figure that could not be resolved, so the two must not be conflated (FR-042).

None of the not-assessed rows can be supplied by a picture. The platform's map capture renders a real map of a location — useful imagery, but not a topography, hydrology, ecology, or noise dataset — and it feeds the imagery-based specialists, not this one.

All of this makes honesty the feature, not a side effect. A specialist that renders nothing when it has no source is indistinguishable from one that is broken; a specialist that fills the gaps with plausible regional figures is worse, because it presents inference as measurement. SPEC-057's FR-014 forbids the second. This specification forbids the first as well.

Two further constraints bound the design:

1. **The stub interfaces exist but nothing implements them.** They are declared in the Application layer and registered nowhere. A specialist that calls them cannot even be constructed, and an unconstructed tool breaks every request that enumerates the platform's tools. The seam must therefore acquire its first, deliberately empty implementation — one that reports *unavailable* with a reason — before any specialist can consume it.
2. **Sun and shadow are already computed, in the browser.** SPEC-052 built a client-side Solar Analysis — sun path, sunrise and sunset, day length, and real building shadows — and its research record states plainly that the backend performs no solar computation and carries no astronomy dependency ([D3](../052-solar-analysis/research.md)). This specialist must **not** re-derive any of it server-side. It points at the analysis that exists.

What this feature must **not** become is a second site-resolution path, a second solar engine, or a second content vocabulary. It analyses the site the analysis was started for, references the sun where the sun is already worked out, and composes only the blocks the platform already renders.

## Clarifications

### Session 2026-10-25

- Q: Which environmental figures does the finding carry? → A: Flood risk as a flood-zone classification and an annual exceedance probability; climate and wind as a prevailing wind direction and an average wind speed. These are exactly the figures the two existing provider seams define. No figure beyond them is introduced.
- Q: Is a real flood or climate data source licensed or implemented here? → A: No. Licensing a source is a separate decision with its own vendor, credential, and legal basis. This feature ships the specialist and the seam, and reports every figure as unavailable with the reason that no source is configured.
- Q: The interfaces have no implementation at all, so how does the specialist call them? → A: Each interface acquires one deliberately empty implementation that always reports *unavailable* with a stated reason, registered where a real source will later live. It invents no data; it is the explicit statement that none is configured. When a licensed source arrives, it replaces that registration and the specialist's calling code does not change.
- Q: What does the specialist render for a figure it cannot supply? → A: The literal statement that no data source is available, together with the reason the provider gave. The figure is never omitted and never estimated. The other outcomes of the existing vocabulary are rendered distinctly — no candidate data for this location, and conflicting data for this location — so "there is no source" is never confused with "the source had nothing".
- Q: How does the specialist obtain the coordinates the providers need? → A: From the site point frozen onto the analysis record when the analysis was dispatched. The specialist does not re-resolve the site, and does not accept coordinates as an alternative input path.
- Q: How is the sun and shadow question answered? → A: By reference. The panel states that sun path, sunrise and sunset, day length, and real building shadows are computed live in the viewer's existing Solar Analysis for the site currently shown there — and restates none of those figures, because the platform's backend does not compute them and must not start. The reference is a pointer, never a measurement.
- Q: Does the reference open the solar analysis? → A: No. The panel content vocabulary's action allowlist is a closed set of viewer commands and contains no command for it. A one-click hand-off would be a new client command and a new entry in a security-relevant allowlist — a separate change, deliberately not made here.
- Q: Does the reference claim the solar figures describe the analysed site? → A: No. The viewer's Solar Analysis acts on the site currently shown in the viewer, which may differ from the site being analysed. The reference must say which site it describes.
- Q: Does an unavailable figure lower confidence, or is it ignored? → A: It lowers it. Confidence is assigned from the grounding facts of every figure the finding presents: all grounded, partially grounded, or none grounded. A finding that could not measure anything is the least confident it is possible to be, and says so.
- Q: How is the specialist counted among the analysis's expected findings? → A: The analysis must expect both specialists from the same declaration that provisions the fan-out branches, not from a separately maintained count. Adding a branch without updating a hand-written count would close the analysis after the first finding and reject the second as arriving at a finished analysis.
- Q: Does the finding carry an image? → A: No. Content uses the existing block vocabulary only — heading, text, key-value, divider. The schematic-image specialist owns imagery.
- Q: Does "environmental context" mean the full professional taxonomy — climate, microclimate, topography, hydrology, ecology, pollutants, and hazards? → A: The whole taxonomy is acknowledged and recorded, but this release assesses only the two dimensions the platform's seams define — flood risk and wind — references solar, and states every other dimension as not assessed. It does not measure what it has no source for, and it does not stay silent about it either.
- Q: Can the platform's map capture supply any of the not-assessed dimensions? → A: No. The capture renders a real map; it is not a topography, hydrology, ecology, noise, or air-quality dataset, and reading those off pixels would be inference. The capture is designated as the imagery source for the imagery-based specialists instead.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The site's environmental context arrives as a finding (Priority: P1)

A user asks Lucy to analyze a site. Alongside the schematic map, an environmental finding arrives: the site's flood risk, and its climate and wind conditions. Each figure is either the measured value, or the plain statement that no data source is available for it together with the reason. The user reads one panel and knows exactly what is known about this site and what is not.

**Why this priority**: This is the feature. It is also the first specialist whose subject matter the platform genuinely cannot measure, so it is the one that proves the honesty rules hold when the data is absent — the condition under which they are easiest to break.

**Independent Test**: Run a site analysis and confirm an environmental finding is delivered in its own right — on its own notice and its own panel, separately from the other specialists — listing flood risk and climate and wind, with every field either carrying a figure or stating that no data source is available.

**Acceptance Scenarios**:

1. **Given** a completed site analysis, **When** the environmental specialist finishes, **Then** its finding is delivered on its own notice and its own panel, in addition to the other specialists', and the user is told which finding has arrived.
2. **Given** a delivered environmental finding, **When** the user reads it, **Then** it presents flood risk — a flood-zone classification and an annual exceedance probability — and climate and wind — a prevailing wind direction and an average wind speed.
3. **Given** no environmental data source is configured, **When** the finding is read, **Then** each of those four figures states that no data source is available, together with the reason, and none of them is omitted or estimated.
4. **Given** the environmental specialist is added, **When** an analysis runs, **Then** the analysis counts it among its expected findings and reaches its end state only once both specialists have settled.
5. **Given** a point-only site with no resolved boundary, **When** the environmental finding is read, **Then** the figures are unaffected — they describe the site's point — and no boundary-dependent claim is made.

---

### User Story 2 - Nothing is invented, and every gap says why (Priority: P1)

A user who is about to act on an environmental statement — a flood classification, a wind loading assumption — must be able to see whether it is a measurement, a figure the source could not supply, or a figure the source answered without carrying. The finding states which, every time, and the confidence it reports reflects how much of the finding is actually grounded.

**Why this priority**: A confident-looking flood classification that was inferred from a regional average is worse than no classification at all. This is the failure SPEC-057's honesty rules exist to prevent, and here it is the default risk because there is no source.

**Independent Test**: Open a delivered environmental finding and confirm it names the analysis that produced it, the data source behind it, and a confidence level; confirm that a figure with no source is marked unavailable with the reason rather than estimated; and confirm the reported confidence falls as figures become unavailable.

**Acceptance Scenarios**:

1. **Given** a delivered environmental finding, **When** the user reads it, **Then** it shows which analysis produced it, which data source it used, its confidence level, and when it was generated.
2. **Given** a figure the provider cannot supply, **When** the finding is read, **Then** it is reported as unavailable together with the reason, and no estimated value stands in its place.
3. **Given** a provider that answered but carried no value for one figure, **When** the finding is read, **Then** that figure is reported as not reported by the source — not as unavailable-for-lack-of-a-source, and not as a value.
4. **Given** every presented figure is unavailable, **When** the finding is read, **Then** the reported confidence is the lowest level the platform defines, and the finding still states what it could not measure.
5. **Given** some figures are grounded and some are not, **When** the finding is read, **Then** the reported confidence reflects that partial grounding — it is neither the highest level nor the lowest.
6. **Given** an environmental finding, **When** its confidence is inspected, **Then** it was assigned by the platform's rule-based confidence assignment from grounding facts alone, and never from a geocoder's importance score.
7. **Given** an environmental finding, **When** the user reads it, **Then** it states which environmental dimensions were not assessed, and that statement is distinct from any figure that was attempted and found unavailable.

---

### User Story 3 - Sun and shadow are pointed to, not recomputed (Priority: P2)

A user reading the environmental context wants to know where the sun goes. The finding does not invent a second sun: it points at the analysis the platform already has — the viewer's Solar Analysis — and says plainly which site that analysis describes. No solar figure is computed or restated on the server.

**Why this priority**: It closes the environmental question without duplicating a working capability or adding an astronomy engine to the backend. It is P2 because it is a reference, not a measurement, and the feature is valuable without it.

**Independent Test**: Open a delivered environmental finding and confirm it names the viewer's Solar Analysis as the place sun path, sunrise and sunset, day length, and building shadows are shown; confirm the finding contains no solar figure of any kind and claims no sun position for the analysed site.

**Acceptance Scenarios**:

1. **Given** a delivered environmental finding, **When** the user reads it, **Then** it states that sun path, sunrise and sunset, day length, and real building shadows are computed live in the viewer's Solar Analysis.
2. **Given** the same finding, **When** its figures are inspected, **Then** it contains no sun position, no sunrise or sunset time, no day length, and no shadow measurement.
3. **Given** the same finding, **When** the reference is read, **Then** it says the viewer's Solar Analysis describes the site currently shown in the viewer, and does not claim those figures for the analysed site when the viewer shows a different one.
4. **Given** the reference is present, **When** the finding's provenance is read, **Then** the reference is not presented as a measured figure and does not raise the finding's confidence.

---

### User Story 4 - A missing or failing source does not sink the analysis (Priority: P2)

Environmental data is exactly the kind of source that is absent today and flaky tomorrow. When a provider is not configured, returns nothing for the location, returns conflicting data, or fails outright, the analysis carries on: the other specialists' findings still arrive, and the analysis still reaches a definite end state. An operator can afterwards determine exactly what happened.

**Why this priority**: Independent specialists mean independent failure modes, and this one has no configured upstream at all. It is P2 because it presupposes the delivery chain SPEC-057 already ships.

**Independent Test**: Force each provider outcome in turn — unavailable, no candidates, ambiguous, and a thrown error — and confirm that unavailability still delivers a finding, that a thrown error delivers none, that the other findings are unaffected, and that every failure is recorded with a reason.

**Acceptance Scenarios**:

1. **Given** a provider reports unavailable, **When** the specialist runs, **Then** its finding is still delivered, with that figure marked unavailable, and the analysis still completes.
2. **Given** a provider throws an error, **When** the specialist runs, **Then** no environmental finding is delivered, the other findings still are, and the analysis still reaches a completed state.
3. **Given** the environmental specialist failed, **When** an operator inspects the analysis, **Then** the failure and its reason are recorded and attributable to the environmental specialist, and are written to the structured logs with the same context.
4. **Given** the environmental specialist failed, **When** the user reads the conversation, **Then** no error notice or panel for that failure appears on its own — the reading of the successful findings is uninterrupted.

---

### User Story 5 - Adding this specialist changed no shared machinery (Priority: P3)

A platform engineer adding a further specialist alongside the existing ones can confirm that the coordinator, its validation gate, its delivery, its persistence, and the client's rehydration all worked unchanged, and that the specialist itself is one new tool, one new type, and one new branch declaration.

**Why this priority**: It is the whole point of the design SPEC-057 committed to, and it is P3 because it is verified by inspection rather than by end-user-visible change.

**Independent Test**: Confirm the shared coordination, validation, delivery, persistence, and client files are unchanged in behaviour by this feature, and observe the findings of an analysis arriving at their own times, each on its own specialist's schedule.

**Acceptance Scenarios**:

1. **Given** the site analysis coordinator and its delivery chain exist, **When** the environmental specialist is added alongside the existing specialists, **Then** no shared coordination, validation, delivery, persistence, or rehydration logic changes.
2. **Given** an analysis with several specialists running, **When** they finish at different times, **Then** each finding is delivered at its own time, and none waits for another.
3. **Given** the environmental specialist is added, **When** an analysis completes, **Then** the analysis's closing outcome reflects that every dispatched specialist was counted, including when one of them failed.

---

### Edge Cases

- **No provider implementation is reachable.** The specialist must still be constructible and must still deliver a finding. A missing dependency that prevents the platform's tool catalog from being enumerated is a platform outage, not a finding, and is unacceptable.
- **Every figure is unavailable and nothing else in the finding is measurable.** The finding is still delivered: what it could not measure, why, and the sun reference are themselves the finding.
- **A provider answers but the analysis record it belongs to cannot be found.** The specialist fails with a recorded reason; it does not invent a site point or fall back to the conversation's active location.
- **A provider throws rather than reporting unavailable.** These are different states and must not be conflated: an unavailable provider is a fact about the world, a thrown error is a fault. Only the second is a specialist failure.
- **One provider is configured and the other is not.** The finding reports the grounded figures and the unavailable ones side by side, and its confidence reflects the partial grounding.
- **The viewer shows a different site from the analysed site.** The sun reference must not present the viewer's figures as though they described the analysed site.
- **The site is a point with no resolved boundary.** Environmental figures describe the point and are unaffected; the finding must not imply it needed a shape.
- **A provider resolves values outside the range the figure can meaningfully take** (for example a wind direction outside 0–360 degrees). The finding reports the figure as unavailable with the reason rather than printing a nonsensical value.
- **The analysis closes before the specialist reports.** The specialist's report is rejected by the coordinator's validation gate as belonging to a finished analysis; the rejection is recorded, and nothing is delivered.

## Requirements *(mandatory)*

### Functional Requirements

**Adding the specialist**

- **FR-001**: The system MUST add an environmental-context specialist to the site analysis fan-out, delivered through the existing specialist chain.
- **FR-002**: Adding the specialist MUST require only a new specialist registration and a new parallel branch declaration. The coordination, validation, delivery, persistence, and rehydration behaviour MUST be unchanged.
- **FR-003**: The system MUST count the environmental specialist among the findings an analysis expects, so an analysis that dispatches it reaches its end state only once every dispatched specialist has settled.
- **FR-004**: The expected number of findings MUST be derived from the same declaration that provisions the fan-out branches, and MUST NOT be a separately maintained hand-written count. A specialist added to the fan-out MUST NOT be able to leave the expected count stale.
- **FR-005**: The environmental contextual finding MUST be attributable to its own analysis type, distinguishable from every other specialist's finding in the analysis record, the conversation notice, and the rehydration path.

**Analysing the right site**

- **FR-006**: The specialist MUST analyse the site the analysis was started for, using the site point frozen onto the analysis record when the analysis was dispatched.
- **FR-007**: The specialist MUST NOT resolve the site again by name, and MUST NOT introduce a second site-identification path or accept coordinates as an alternative input.
- **FR-008**: The specialist MUST receive the identity of the analysis it is reporting into, so that its finding is attached to that analysis and its owning conversation.

**The figures**

- **FR-009**: The system MUST report flood risk as a **flood-zone classification** and an **annual exceedance probability**, and climate and wind as a **prevailing wind direction** and an **average wind speed**. These are the figures the two provider seams define; no additional figure is introduced.
- **FR-010**: Where a provider reports that no data source is available, the system MUST state exactly that for every figure that provider owns, together with the reason the provider gave. The figure MUST NOT be omitted and MUST NOT be estimated.
- **FR-011**: Where a provider reports that it has no data for this location, the system MUST state that distinctly, and MUST NOT describe it as a missing data source.
- **FR-012**: Where a provider reports conflicting data for this location, the system MUST state that distinctly, and MUST NOT choose one of the conflicting values.
- **FR-013**: Where a provider answers but carries no value for one of its figures, the system MUST report that figure as not reported by the source, distinct from both unavailability and a value.
- **FR-014**: A figure MUST NOT be derived from another figure, interpolated, or inferred from regional knowledge in order to fill a gap.
- **FR-015**: A resolved figure that falls outside the range the figure can meaningfully take MUST be reported as unavailable with the reason, never rendered as-is.

**Data source**

- **FR-016**: The system MUST consume the existing flood and climate provider seams, and MUST NOT introduce a second seam for either.
- **FR-017**: Because neither seam has an implementation, the system MUST supply one deliberately empty implementation of each that reports *unavailable* with a stated reason, so that the specialist is constructible and the absence of a source is expressed rather than assumed.
- **FR-018**: The empty implementations MUST live where a licensed implementation will live, and MUST be registered such that replacing them with a real source requires no change to the specialist's calling code.
- **FR-019**: The system MUST NOT license, select, or invent a data source in this feature. No vendor, credential, licence, or per-user configuration item is introduced.
- **FR-020**: A licensed implementation added later MUST resolve its credential from the platform's stored configuration first and read configuration only as a fallback, consistent with the platform-wide credential rule. This feature introduces no credential of its own.

**Sun and shadow**

- **FR-021**: The finding MUST reference the platform's existing client-side Solar Analysis as the place where sun path, sunrise and sunset, day length, and real building shadows are shown.
- **FR-022**: The finding MUST NOT contain any solar figure — no sun position, no sunrise or sunset time, no day length, no shadow measurement — and the system MUST NOT compute any of them server-side.
- **FR-023**: The reference MUST state that the viewer's Solar Analysis describes the site currently shown in the viewer, and MUST NOT claim those figures for the analysed site when the two differ.
- **FR-024**: The reference MUST NOT be presented as a measured figure and MUST NOT raise the finding's confidence.
- **FR-025**: The reference MUST NOT attempt to open the Solar Analysis. No new viewer command and no new action entry are introduced by this feature.

**Composition and delivery**

- **FR-026**: The environmental finding MUST be presented using the platform's existing panel content vocabulary, and MUST NOT introduce a new kind of content block.
- **FR-027**: The environmental finding MUST NOT contain an image. Imagery is the schematic specialist's output.
- **FR-028**: The environmental specialist MUST report its finding through the existing specialist reporting channel — the parent coordinator's result intake — and MUST NOT post to the conversation, open a panel, or otherwise place anything before the user itself.
- **FR-029**: The environmental finding MUST carry its provenance: the analysis that produced it, the data source behind it, its confidence level, and its generation time.
- **FR-030**: The data source recorded for the finding MUST be truthful about what was used. Where no source is configured, it MUST say so rather than naming a source that was not consulted.

**Confidence**

- **FR-031**: The confidence level of an environmental finding MUST be assigned by the platform's existing rule-based confidence assignment, from grounding facts alone.
- **FR-032**: Confidence MUST NOT be derived from a geocoder's importance score, which is populated on incompatible scales by different providers.
- **FR-033**: An unavailable or unconfirmed figure MUST lower the finding's confidence rather than being omitted from the calculation. A finding with every presented figure unavailable MUST report the lowest defined confidence level; a finding with some grounded and some ungrounded figures MUST report an intermediate level; a finding with every figure grounded MUST report the highest.

**Failure behaviour**

- **FR-034**: A failure of the environmental specialist MUST be captured at the point it occurs, recorded with its reason and its originating error detail, and written to the platform's structured logs with enough context to diagnose it — never swallowed, discarded, or left unobserved.
- **FR-035**: A provider reporting *unavailable*, *no candidates*, or *ambiguous* MUST NOT be treated as a specialist failure. Those are facts about the data; the finding is delivered.
- **FR-036**: A failure of the environmental specialist MUST NOT prevent the other specialists' findings from being delivered, and MUST NOT prevent the analysis from reaching a definite end state.
- **FR-037**: A failure of the environmental specialist MUST NOT produce a notice, a panel, or any other interruption for the user on its own.
- **FR-038**: The system MUST NOT introduce a new kind of failure for this specialist; it MUST reuse the platform's existing failure vocabulary and classification.

**Limits**

- **FR-039**: The specialist's execution MUST remain bounded by the platform's existing agent and workflow execution limits, and MUST NOT introduce a new budget.
- **FR-040**: The number of provider calls the specialist issues per run MUST be fixed and documented — one per configured seam — and MUST NOT scale with the site, the analysis, or the user.

**Assessment scope**

- **FR-041**: The finding MUST state which environmental dimensions it did not assess, so that a dimension absent from the finding is never read as a dimension with nothing to report.
- **FR-042**: The statement of what was not assessed MUST be distinguishable from a figure the specialist attempted and could not resolve. "Not assessed" and "no data source available" are different statements and MUST NOT be conflated.

### Key Entities

- **Environmental Context Finding**: One environmental specialist's outcome within a site analysis. Carries the flood-risk and climate/wind figures, the sun reference, its provenance, and — when unsuccessful — its failure reason. It is one of the analysis's results, not a separate record type.
- **Flood Risk Figure**: A flood-zone classification and an annual exceedance probability for the site's point. Carried either as a resolved value or as the statement that it could not be established, with the reason.
- **Climate and Wind Figure**: A prevailing wind direction and an average wind speed for the site's point. Carried either as a resolved value or as the statement that it could not be established, with the reason.
- **Data Resolution Outcome**: The existing vocabulary describing how a lookup ended — confirmed, no candidate data, ambiguous, or unavailable. The specialist renders each outcome distinctly; none collapses into another.
- **Configured Data Source**: The implementation behind a provider seam. Today there is none: the registered implementation's only behaviour is to report that no source is configured. It names itself truthfully in the finding's provenance.
- **Solar Analysis Reference**: A pointer from the finding to the viewer's existing client-side Solar Analysis. It is a reference, never a figure, and it never affects confidence.
- **Unavailable Figure**: A figure that could not be established, carried with the reason it could not be, in place of a value.
- **Not-Assessed Dimension**: A dimension of environmental context that this release does not model at all — no seam, no source, no figure. Named in the finding so that its absence is explicit; never rendered as an unavailable figure, which would imply a source that could one day answer it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of site analyses that dispatch the environmental specialist deliver an environmental finding — including when every figure in it is unavailable — unless the specialist itself failed.
- **SC-002**: 100% of delivered environmental findings display the analysis that produced them, the data source behind them, and a confidence level.
- **SC-003**: 0 environmental figures are presented as measured values that were estimated, inferred, or derived from a figure the source did not supply.
- **SC-004**: 100% of figures without a configured source state that no data source is available, together with the reason, and 0 of them are omitted from the finding.
- **SC-005**: 0 delivered environmental findings contain a solar figure, and 0 restate a sun-path, sunrise, sunset, day-length, or shadow value computed server-side.
- **SC-006**: 100% of environmental findings whose every figure is unavailable report the lowest defined confidence level; 100% whose figures are partially grounded report an intermediate level; 100% whose figures are fully grounded report the highest.
- **SC-007**: When an environmental provider throws, 100% of the other findings are still delivered and the analysis still reaches a completed state.
- **SC-008**: 100% of environmental-specialist failures are recorded with a reason attributable to that specialist, and are diagnosable from the platform's operational logs alone — no failure path discards its error.
- **SC-009**: 0 notices or panels are produced for an environmental-specialist failure on its own.
- **SC-010**: Adding the environmental specialist requires 0 changes to the coordinator, the validation gate, the delivery path, the persistence layer, and the client's rehydration path — verified against the change itself.
- **SC-011**: In an analysis where specialists finish at different times, each finding reaches the user at its own time, and none is held back for another — verified by observing findings arriving at correspondingly different times.
- **SC-012**: 0 new external services, credentials, licences, or per-user configuration items are required for the environmental specialist to work.
- **SC-013**: 100% of analyses that dispatch the environmental specialist reach their end state only after every dispatched specialist has settled, so no finding is rejected as arriving at an analysis that already closed.
- **SC-014**: 100% of delivered environmental findings state which environmental dimensions were not assessed, and 0 present an unassessed dimension as an unavailable figure or as a measured one.

## Assumptions

- **The site's point is the measurement basis.** The site point frozen onto the analysis record when the analysis was dispatched is the location every environmental figure describes. The specialist does not attempt a more precise location and does not require a boundary.
- **No environmental source exists today, and that is the shipped state.** Every figure is unavailable until a source is licensed. The finding is the platform's honest statement of that, not a degraded mode.
- **The provider seams are the figures' definition.** The flood-zone classification, annual exceedance probability, prevailing wind direction, and average wind speed are the figures the existing seams define; this feature neither widens nor narrows them.
- **A licensed source is a separate decision.** It brings its own vendor, licence, credential, and validation, and it replaces the empty implementation without changing the specialist.
- **Unavailability lowers confidence.** A finding that measured nothing is less trustworthy than one that measured everything, and its reported confidence says so.
- **The sun is already computed, in the browser.** The platform's backend performs no solar computation by design; this specialist references the existing client-side analysis rather than starting a second one.
- **The viewer's Solar Analysis is bound to the site shown in the viewer.** The server cannot know which site that is, so the reference says what it covers rather than asserting a match.
- **The analysis is per-user and per-conversation.** Findings are delivered only to the user who initiated the analysis, as they already are for every other specialist.
- **The map capture is imagery, not environmental data.** The platform's capture endpoint renders a real map of a location. It supplies none of the figures here, and its designated consumers are the imagery-based specialists.
- **Existing execution limits apply.** The specialist's duration, concurrency, and cost are bounded by the platform's existing agent and workflow execution limits.

## Out of Scope

Deliberately excluded from this release:

- **Licensing, selecting, procuring, or implementing a real flood, hydrology, climate, or wind data source.** That decision carries its own vendor evaluation, licence terms, cost, and credential, and is the next step after this feature — not part of it.
- **Any invented, estimated, or proxy environmental figure.** No figure is inferred from terrain, latitude, nearby stations, regional averages, or model knowledge to stand in for a measurement.
- **Assessing the environmental dimensions no seam models** — temperature, humidity, precipitation, topography, slope, soil, bedrock, water table, surface water, stormwater, ecology, protected status, noise, air quality, and site hazards. The finding names them as not assessed; it does not measure them.
- **Deriving any environmental figure from imagery**, including the platform's own map captures. Imagery is not a flood, wind, topography, hydrology, ecology, noise, or air-quality dataset.
- **Any server-side solar, sun-position, day-length, or shadow computation.** The existing client-side Solar Analysis is the platform's only source of those figures, and it stays that way.
- **Opening the Solar Analysis from the finding.** No new viewer command and no new entry in the panel content action allowlist.
- **A rendered diagram, map, chart, or image of any environmental condition.** The schematic-image specialist owns imagery for an analysis. The platform's map-capture endpoint is designated as the imagery source for that specialist and for the imagery-based follow-ups (Urban Context, Character Analysis), not for this one.
- **Zoning figures — floor-area ratio, setbacks, and height limits.** They belong to the platform's separate zoning seam, not to this specialist.
- **Air quality, noise, ecology, contamination, daylight factor, and detailed hydrology or drainage modelling.** None is defined by the existing seams, and each would be a new source and a new figure.
- **Exporting the finding as a file, document, or report**, and **sharing it with another user**.
- **The three remaining deferred specialists** — Urban Context, Connectivity & Access, and Character Analysis.
