# Feature Specification: Conversational Park Site Analysis Agent

**Feature Branch**: `050-park-site-analysis-agent`

**Created**: 2026-08-22

**Status**: Draft

**Input**: User description: "Migrate the existing 'park-redesign' project's notebook-driven urban-park site-analysis pipeline into Ask Lucy's own Agent Engine + MCP Tool Engine, so the analysis is driven conversationally by chat rather than by running a Jupyter notebook end-to-end. Wrap the notebook's Python GIS/scoring logic as an external MCP server exposing per-module tools (boundary resolution, data-layer connectors, scoring) through the existing MCP Tool Engine. Drive the existing Immersive Viewer Platform and AI Floating Panel framework as the only presentation surfaces — no new bespoke frontend; the user works in Ask Lucy's own SPA, entered either via a Project-linked deep link from TheDigitalCore or by starting a fresh conversation where the assistant welcomes the user, verifies a described site's physical existence via maps, searches TheDigitalCore for a matching digital project, and — only on explicit user confirmation — creates one (matching the reference conversational mockup's Welcome → AI Understanding steps). Scope this first slice to conversational site input + boundary resolution, plus end-to-end data collection, scoring, and cited presentation of results for the Recreation and Social categories only (Environmental, Sustainability, Accessibility, Safety, Smart City, AI-generated design concepts, and report generation are deferred). The interaction must be turn-by-turn/incremental across a single conversation, not one upfront batch run of the whole pipeline — validate this against the Agent Engine's current plan-then-execute execution model. Guardrails: no invented values for missing data (explicit data-gap signal instead), every finding/recommendation cites its triggering metric and supporting evidence, conflicting/ambiguous results are surfaced via existing risk-gating rather than resolved silently, and no Project is ever created in TheDigitalCore without explicit user confirmation. Narrative/citation content is modeled as an Ask Lucy Knowledge Base via the existing RAG pipeline. TheDigitalCore (a separate, existing platform) remains the system of record for Project, Company, Attachments, and the resulting SiteAnalysis/DesignConcept/DesignRecommendation records — Ask Lucy does not own or duplicate that persistence. TheDigitalCore's backend calls Ask Lucy server-to-server, authenticated with an API key, to relay analysis results back into the originating Project. Existing per-user isolation, JWT auth, and audit/approval mechanisms apply as-is."

## Clarifications

### Session 2026-08-22 (reconciliation against `docs/AI Urban Design Copilot — Ask Lucy User Stories & Agent Workflow.md`)

- Q: That document's User Stories 07/08 allow a digital Project to exist (or be created) for a site with no confirmed built physical asset (planned/proposed/under-construction); spec 050's original FR-001b hard-blocked TheDigitalCore search/creation whenever physical existence wasn't confirmed. Which behavior is correct? → A: Adopt the document's rule. A resolvable location with no confirmed built asset is not a blocking failure — the assistant tells the user the site appears planned/proposed and still proceeds to search/offer-create a digital Project. Only a location that cannot be resolved at all still blocks (FR-001b/FR-001g).

### Session 2026-08-22

- Q: How should Ask Lucy's plan-then-execute Agent Engine support this feature's turn-by-turn tool invocation (resolve site → later score Recreation → later score Social, each its own chat message)? → A: Each qualifying user turn starts its own new, small `AgentPlanner`/`AgentExecutionOrchestrator` run (a short 1–2 step plan), with prior conversation state (resolved boundary, earlier results) passed in as context. No Agent Engine changes needed.
- Q: Should project creation through this conversational flow enforce TheDigitalCore's per-user role restrictions (SuperUser/Administrator/Editor/Viewer)? → A: Not applicable — Ask Lucy talks to TheDigitalCore exclusively through a single, dedicated Ask Lucy service account; no individual Ask Lucy user needs or holds a TheDigitalCore user account, so there is no per-user TheDigitalCore role to check. Every TheDigitalCore API call this feature makes (searching for a matching Project, creating one, relaying results) is made by Ask Lucy's backend under that one service account.
- Q: What should count as an existing TheDigitalCore Project "matching" the site the user described (FR-001c/FR-001d)? → A: First site name, then geolocation — the assistant searches TheDigitalCore by site name first; if that isn't conclusive, it uses the resolved geolocation (coordinates) as a secondary signal to confirm or narrow the match.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verify a real site and create its digital project on request (Priority: P1)

A user opens the application and is welcomed by the assistant. They describe, in plain language, a real-world park or site they want to work on (e.g., "I want to redesign Al Safa Park in Dubai — it exists physically but has no digital project here"). The assistant checks a maps/geocoding source to confirm the site is a real, physically-existing place, then checks TheDigitalCore to see whether a digital Project already represents that site. If none exists, the assistant tells the user so and offers to create one; only once the user explicitly confirms does the assistant create a new Project in TheDigitalCore and continue the conversation linked to it. If a matching Project already exists, the assistant links the conversation to it instead of creating a duplicate.

**Why this priority**: This is the literal entry point of the feature for a brand-new site. Every other capability (boundary rendering, category analysis) needs a Project to attach its results to, and this is the smallest slice that proves the chat → maps existence check → TheDigitalCore lookup → confirmed project creation path works end to end, without which User Story 2 has no Project to link its boundary/results to.

**Independent Test**: Can be fully tested by opening a new conversation with no Project yet linked, describing a real, physically-existing named site that has no matching Project in TheDigitalCore, and confirming the assistant reports both the physical-existence check and the "no digital project found" result, offers to create one, and — only once confirmed — a new Project appears in TheDigitalCore linked to that conversation.

**Acceptance Scenarios**:

1. **Given** a new conversation with no Project yet linked, **When** the user describes a real, physically-existing site by name, **Then** the assistant checks a maps/geocoding source and confirms the site's physical existence before doing anything else.
2. **Given** a confirmed physically-existing site, **When** the assistant checks TheDigitalCore, **Then** it reports whether a digital Project already represents that site.
3. **Given** no matching digital Project exists, **When** the assistant reports this to the user, **Then** it offers to create one and waits for explicit user confirmation before creating anything.
4. **Given** the user confirms, **When** the assistant creates the Project, **Then** a new Project is created in TheDigitalCore and the current conversation becomes linked to it.
5. **Given** the user names a site whose location cannot be resolved at all (ambiguous or unrecognizable), **When** the assistant reports this, **Then** it does not proceed to search TheDigitalCore or offer project creation, and instead asks the user to clarify or correct the site.
6. **Given** TheDigitalCore already has a digital Project matching the described site, **When** the assistant checks, **Then** it reports the existing Project instead of offering to create a duplicate, and links the conversation to that existing Project.
7. **Given** the user describes a site whose location resolves but no built physical asset is confirmed there (e.g., a planned or not-yet-built park), **When** the assistant reports this, **Then** it does not block progress — it tells the user the site appears to be planned/proposed rather than an existing built asset, and still proceeds to search/offer-create a digital Project for it on the same terms as a confirmed-existing site.

---

### User Story 2 - Describe a site in chat and see its boundary appear in the Immersive Viewer (Priority: P1)

A user tells the assistant, in plain chat, which park or site they want to analyze — by name or by coordinates. The assistant resolves the site to a geographic boundary and shows it in the Immersive Viewer (an outline or marker) so the user can visually confirm it found the right place before anything else happens.

**Why this priority**: Every other capability in this feature depends on a resolved site boundary existing first. Without this, there is nothing to analyze or score, and it is the smallest slice that already proves the chat → Agent Engine → MCP tool → Immersive Viewer path works end to end.

**Independent Test**: Can be fully tested by typing a known place name (or a latitude/longitude pair) into chat and confirming a corresponding boundary outline or marker appears in the Immersive Viewer, independent of any scoring or category analysis.

**Acceptance Scenarios**:

1. **Given** a user opens a Project-linked deep link from TheDigitalCore, **When** Ask Lucy's SPA loads, **Then** the user lands in a conversation already associated with that Project, without needing to separately identify the Project.
2. **Given** an open conversation with no site yet established, **When** the user names a real, unambiguous site (e.g., a specific named park with a city), **Then** the assistant resolves it to a boundary and the Immersive Viewer displays that boundary as an outline or marker.
3. **Given** an open conversation, **When** the user instead provides a latitude/longitude pair, **Then** the assistant resolves and displays a boundary centered on that location the same way.
4. **Given** a site boundary has already been resolved and shown for the current conversation, **When** the user asks a follow-up question about that site, **Then** the assistant reuses the already-resolved boundary rather than re-resolving it from scratch.

---

### User Story 3 - Request a Recreation analysis and see cited, scored results (Priority: P2)

Once a site's boundary is established, the user asks the assistant to analyze the site's recreational quality (e.g., "how good is this park for recreation?"). The assistant collects the relevant data and computes a Recreation score, then presents the score, key findings, and recommendations — each backed by a citation — without requiring the user to ask for anything else first.

**Why this priority**: This is the first complete "ask a question, get a grounded analytical answer" slice, and it proves the scoring/citation/guardrail behavior that the rest of the feature depends on.

**Independent Test**: Can be fully tested, after a boundary is resolved, by asking for a Recreation analysis and confirming a Floating Panel appears showing a Recreation score, findings, and at least one citation per finding — independent of any Social-category behavior.

**Acceptance Scenarios**:

1. **Given** a resolved site boundary, **When** the user asks for a Recreation analysis, **Then** the assistant collects only the data needed for Recreation, computes a Recreation score, and presents it in a Floating Panel with findings and citations.
2. **Given** a completed Recreation analysis, **When** the user opens a cited finding, **Then** they can see the metric/score that triggered it and the supporting evidence it is based on.
3. **Given** the Recreation analysis requires a data input that cannot be obtained, **When** the assistant presents the results, **Then** the affected finding is visibly marked as having a data gap rather than silently omitted or guessed.

---

### User Story 4 - Request a Social analysis of the same site (Priority: P2)

Later in the same conversation, the user asks for a Social-category analysis of the same site. The assistant does not re-resolve the boundary or repeat unrelated work — it collects only the additional data needed for the Social category and presents a separate, cited Social score alongside (not instead of) the earlier Recreation results.

**Why this priority**: Together with User Story 3, this proves the feature supports more than one category in the reference scope and that categories are independently invocable, which is central to the "no monolithic batch run" requirement.

**Independent Test**: Can be fully tested, after a boundary is resolved, by asking directly for a Social analysis (with or without having first requested Recreation) and confirming a Floating Panel with a Social score, findings, and citations appears.

**Acceptance Scenarios**:

1. **Given** a resolved site boundary, **When** the user asks for a Social analysis, **Then** the assistant collects only the data needed for Social, computes a Social score, and presents it with findings and citations.
2. **Given** a Recreation analysis already exists earlier in the conversation, **When** the user then asks for a Social analysis, **Then** the earlier Recreation results remain visible/available and are not recomputed or overwritten.

---

### User Story 5 - Keep the conversation going, turn by turn, across categories (Priority: P3)

The user treats the whole thing as an ongoing conversation rather than a single request: they resolve a site, ask for Recreation, review it, then later ask for Social, and could later still ask about either category again — each ask triggering only the work needed for that ask, at the time it is asked.

**Why this priority**: This scenario is the explicit validation of the "turn-by-turn, not one upfront batch plan" requirement that distinguishes this feature from simply porting the notebook pipeline as-is; it is prioritized after the individual category slices (User Stories 3-4) because it depends on both existing first.

**Independent Test**: Can be fully tested by carrying out a single conversation across multiple turns — resolve boundary, request Recreation, wait for and review its result, then request Social — and confirming each ask is handled as its own scoped unit of work rather than requiring the whole sequence to be declared upfront.

**Acceptance Scenarios**:

1. **Given** a conversation where only a boundary has been resolved so far, **When** the user asks for one category and later asks for the other in a separate message, **Then** each ask is handled independently at the time it is made, without the user having had to request both categories together.
2. **Given** an in-progress conversation, **When** the user asks an unrelated question in between two category requests, **Then** the unrelated question does not interfere with or reset the established site boundary or prior category results.

---

### User Story 6 - Transparent handling of data gaps and conflicting results (Priority: P3)

While a category analysis is running, some required data cannot be obtained, or two data sources disagree in a way that would change the outcome. Instead of guessing or silently picking one source, the assistant tells the user there is a gap or asks for confirmation before proceeding.

**Why this priority**: This is a trust/guardrail requirement rather than new user-facing capability; it is validated after the core category flows (User Stories 3-4) exist to have something to attach the guardrail behavior to.

**Independent Test**: Can be fully tested by forcing a scenario where a required data layer is unavailable (or where two sources disagree) and confirming the user sees an explicit data-gap notice or a pause-for-input request rather than a completed-looking result.

**Acceptance Scenarios**:

1. **Given** a category analysis is running, **When** a required data input cannot be obtained from any available source, **Then** the presented results explicitly flag that gap rather than omitting the affected field or fabricating a value.
2. **Given** a category analysis produces conflicting or materially ambiguous results from different data sources, **When** the assistant would otherwise have to pick one silently, **Then** the assistant instead pauses and asks the user for input before finalizing the result.

### Edge Cases

- What happens when the user names a site whose location cannot be resolved at all (ambiguous, unrecognizable, or no plausible real-world match)? The assistant MUST report the check failed and ask the user to clarify or correct the site, and MUST NOT search or create anything in TheDigitalCore for it.
- What happens when a site's location resolves but no built physical asset is confirmed there (a planned, proposed, or not-yet-built site)? The assistant MUST NOT block progress — it tells the user the site appears planned/proposed rather than confirmed-built, and proceeds to search/offer-create a digital Project for it on the same terms as any other resolved site (spec Assumptions).
- What happens when the user describes a site that already has a matching digital Project in TheDigitalCore? The assistant MUST link the conversation to the existing Project rather than offering or creating a duplicate.
- What happens when the name-then-geolocation search in TheDigitalCore returns more than one plausible candidate Project? The assistant MUST ask the user to confirm which one (if any) is the same site rather than silently picking one or creating a duplicate.
- What happens when the user names a site that matches multiple real, ambiguous locations (e.g., a common park name that exists in several cities)? The assistant MUST ask a clarifying question rather than guessing which one was meant.
- What happens when the user asks for a category analysis (Recreation or Social) before any site boundary has been established in the conversation? The assistant MUST ask for a site first rather than attempting to analyze nothing.
- What happens when the user asks for an out-of-scope category (e.g., Environmental or Safety)? The assistant MUST clearly state that category is not yet supported rather than silently attempting it or fabricating a score.
- What happens when the external analysis capability (the new MCP server) is unreachable or a data-layer connector's entire fallback chain fails? The assistant MUST surface this as a visible, actionable failure rather than presenting an incomplete result as if it were complete.
- What happens when a resolved boundary is valid but no relevant Recreation or Social data exists at all for that location? The assistant MUST say so explicitly rather than presenting a fabricated or default-looking score.
- How does the system handle a user who asks for the same category analysis twice in the same conversation? The assistant MUST re-run and refresh the result rather than silently reusing a possibly stale prior answer without indicating it did so.

## Requirements *(mandatory)*

### Functional Requirements

**Site input & boundary resolution**

- **FR-001**: Users MUST be able to specify a site to analyze by typing a place name or a latitude/longitude pair as a plain chat message.
- **FR-001a**: When a conversation has no Project yet linked, and the user describes a real-world site by name, the assistant MUST first attempt to resolve the site's location and built-asset status using a maps/geocoding tool before searching or creating anything in TheDigitalCore.
- **FR-001b**: If the site's location cannot be resolved at all (ambiguous or unrecognizable), the assistant MUST report this to the user and ask them to clarify or correct the site, and MUST NOT search TheDigitalCore or offer to create a Project for it.
- **FR-001g**: If the site's location resolves but no built physical asset is confirmed there (a planned, proposed, or not-yet-built site), the assistant MUST NOT treat this as a blocking failure — it MUST tell the user the site appears planned/proposed rather than confirmed-built, and MUST proceed to FR-001c (TheDigitalCore search) on the same terms as a confirmed-built site, consistent with the source product spec's Story 07/08 (a digital Project may legitimately represent a conceptual, planned, or under-construction site with no confirmed existing physical asset).
- **FR-001c**: Once a site's location is resolved (whether confirmed-built or planned/proposed, per FR-001a/FR-001g), the assistant MUST search TheDigitalCore for an existing digital Project matching that site before offering to create a new one, searching by the site's name first and using its resolved geolocation (coordinates) as a secondary signal to confirm or narrow the match when the name search alone is not conclusive.
- **FR-001d**: If a matching digital Project already exists in TheDigitalCore, the assistant MUST link the current conversation to that existing Project rather than offering or creating a duplicate.
- **FR-001e**: If no matching digital Project exists, the assistant MUST tell the user so and offer to create one, and MUST NOT create a Project in TheDigitalCore without the user's explicit confirmation.
- **FR-001f**: Once the user confirms, the assistant MUST create a new Project in TheDigitalCore (via the server-to-server integration in FR-026/FR-027) and link the current conversation to the newly created Project.
- **FR-002**: System MUST resolve a user-specified site to a geographic boundary (an outline or a point-based marker) using a dedicated site-boundary-resolution tool.
- **FR-003**: Once a boundary is resolved, the assistant MUST render it in the existing Immersive Viewer (as an outline or marker) using the viewer's existing programmatic command/event API, without requiring any new viewer surface to be built for this feature.
- **FR-004**: If a site name resolves to more than one plausible real-world location, or resolution otherwise fails, the assistant MUST ask the user a clarifying question in chat rather than guessing which location was meant.
- **FR-005**: A site boundary already resolved earlier in a conversation MUST be reused for subsequent category requests in that same conversation rather than re-resolved from scratch, unless the user names a different site.

**MCP tool wrapping of the analysis pipeline**

- **FR-006**: The site-analysis pipeline's Python-based analysis capabilities (boundary resolution, data-layer collection, metrics/scoring) MUST be exposed as a new external MCP server registered through Ask Lucy's existing MCP Tool Engine, following the same admin-registration, credential-storage, and inactive-until-activated lifecycle used for every other MCP server.
- **FR-007**: Each in-scope pipeline stage (site-boundary resolution; Recreation-relevant data-layer collection; Social-relevant data-layer collection; Recreation scoring; Social scoring) MUST be exposed as its own separately invocable MCP tool rather than as a single "run the whole pipeline" tool.
- **FR-008**: MCP tools introduced by this feature MUST start inactive and MUST require explicit administrator activation before any user's agent can invoke them, per the existing MCP Tool Engine's activation lifecycle.
- **FR-009**: All external data-provider credentials (mapping, imagery, or AI-vision providers used by the new MCP server) MUST be stored and managed through the existing MCP credential-storage mechanism; no such credential MUST ever be embedded in agent configuration, workflow definitions, or the frontend.

**Turn-by-turn conversational invocation**

- **FR-010**: Users MUST be able to request a category analysis (Recreation or Social) as its own, later chat message in an already-open conversation, without having to declare every category or step upfront.
- **FR-011**: A category analysis request MUST result in only the tools needed for that specific category being invoked; it MUST NOT trigger analysis of a category the user did not ask for.
- **FR-012**: System MUST handle each qualifying user turn (e.g., "resolve this site," "score Recreation," "now score Social") by starting a new, independently-scoped `AgentPlanner`/`AgentExecutionOrchestrator` run producing its own short upfront plan limited to only the tool(s) that turn needs. Prior conversation state (the resolved Site Boundary, earlier Category Score Results) MUST be supplied as context to each new run; no single long-lived plan MUST be required to span the whole conversation, and no second, parallel tool-execution framework MUST be introduced.

**Presenting results**

- **FR-013**: Category analysis results (score, findings, recommendations, citations) MUST be presented as an AI-invoked Floating Panel rather than as chat text alone.
- **FR-014**: A new Floating Panel type MUST be registered with the existing Floating Panel framework to render site-analysis category results, without requiring changes to the framework's core panel-management or viewer logic.

**Guardrails**

- **FR-015**: When a data-collection or scoring tool cannot obtain a value it needs, it MUST return an explicit, structured data-gap indication rather than omitting the field or substituting a fabricated or default-looking value.
- **FR-016**: When a data-gap indication is returned, the assistant MUST surface it to the user in the conversation and in the presented results, rather than completing the analysis as if the data had been present.
- **FR-017**: Every AI-generated finding or recommendation produced by this feature MUST display a citation identifying both the metric/score that triggered it and the supporting evidence (standard, source dataset, or knowledge-base passage) behind it.
- **FR-018**: When a data-collection or scoring tool call returns conflicting or materially ambiguous results, the assistant MUST pause and request human input using the existing risk-based approval mechanism rather than resolving the conflict silently.

**Knowledge grounding**

- **FR-019**: Narrative or citation content used to ground findings and recommendations (methodology excerpts, standards thresholds, case studies) MUST be sourced from an Ask Lucy Knowledge Base ingested through the existing RAG pipeline, not from a bespoke citation table built for this feature.
- **FR-020**: Citations attached to AI-generated findings MUST identify the specific source document/passage they came from, using the existing RAG citation mechanism.

**Scope limits**

- **FR-021**: This feature MUST support only the Recreation and Social scoring categories; scoring for Environmental, Sustainability, Accessibility, Safety, and Smart City categories, AI-generated design concepts, and report generation MUST NOT be implemented as part of this feature.

**Isolation, audit & access**

- **FR-022**: Site analyses, their resolved boundaries, and their results MUST be scoped to the conversation and user that requested them, using Ask Lucy's existing per-user conversation and knowledge-base isolation; no new authentication or multi-tenant model MUST be introduced.
- **FR-023**: Every MCP tool call made in service of a site analysis MUST be recorded in the existing per-execution tool-call audit trail, attributable to the initiating user and conversation.

**Project linkage & persistence (TheDigitalCore integration)**

- **FR-024**: Users MUST be able to enter this feature either (a) through a Project-linked deep link originating from TheDigitalCore, which lands them in Ask Lucy's own SPA already associated with that Project, or (b) by starting a fresh conversation and having the assistant establish the Project link itself per FR-001a–FR-001g (resolve location and built-asset status, search TheDigitalCore, and create or link a Project on confirmation, whether the site is confirmed-built or planned/proposed).
- **FR-025**: TheDigitalCore MUST remain the sole system of record for Project, Company, and Attachment data, and for the resulting SiteAnalysis, DesignConcept, and DesignRecommendation records; Ask Lucy MUST NOT persist its own competing copies of these entities.
- **FR-026**: All communication between Ask Lucy and TheDigitalCore for this feature (searching for a matching Project, creating a new Project, and relaying a completed Category Score Result) MUST be made by Ask Lucy's backend calling TheDigitalCore's API under a single, dedicated Ask Lucy service account — never using, or requiring, an individual end user's own TheDigitalCore credentials.
- **FR-027**: The Ask Lucy service-account credential used to call TheDigitalCore MUST be held server-side within Ask Lucy's backend and MUST never be exposed to, or usable from, a browser context or any individual end user; this is a single, non-personal service identity distinct from Ask Lucy's own end-user JWT authentication and from any TheDigitalCore per-user role.
- **FR-027a**: No individual Ask Lucy user MUST be required to have, or be assumed to have, their own TheDigitalCore user account; TheDigitalCore's per-user roles (SuperUser/Administrator/Editor/Viewer) are not evaluated per Ask Lucy end user for this feature's actions. Any authorization over which Ask Lucy users may trigger Project creation through this conversational flow is Ask Lucy's own responsibility, enforced via its existing per-user authentication and conversation isolation (FR-022), not via TheDigitalCore RBAC.

### Key Entities *(include if feature involves data)*

- **Site Boundary**: The resolved geographic representation (outline or point-based marker) of a user-specified site, including the input method used to resolve it (place name or coordinates), and the conversation it belongs to.
- **Category Analysis Run**: A single, scoped unit of work triggered by one user request for one category (Recreation or Social) against an already-resolved Site Boundary; distinct from — and independently invocable relative to — any other category's analysis in the same conversation.
- **Category Score Result**: The score, findings, recommendations, and citations produced for one category (Recreation or Social) for a given Site Boundary, including any Data Gap Indications affecting it.
- **Data Gap Indication**: An explicit record that a specific data input required by a Category Analysis Run could not be obtained, including which input and why, surfaced to the user rather than silently omitted.
- **Site Analysis MCP Server & Tools**: The new external MCP server and its individually invocable tools (site-boundary resolution, per-category data-layer collection, per-category scoring), registered and lifecycle-managed through the existing MCP Tool Engine.
- **Site Analysis Knowledge Base**: The Ask Lucy Knowledge Base holding the methodology, standards, and case-study content used to ground Category Score Result findings and their citations.
- **Project Link**: The association between an Ask Lucy conversation and a TheDigitalCore Project, established either by an inbound deep link from an already-existing Project, or by the assistant's own bootstrap flow (location/built-asset status check, TheDigitalCore search, and confirmed creation or linking — regardless of whether the site is confirmed-built or planned/proposed, per FR-001g); used to relay Category Score Results back to the correct Project. TheDigitalCore's own Project, Company, Attachment, SiteAnalysis, DesignConcept, and DesignRecommendation records are out of this feature's data ownership — Ask Lucy only produces the results that get relayed into them.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can obtain a resolved, visually rendered site boundary from a plain-language chat description (place name or coordinates) within a single conversational exchange, without leaving the chat interface.
- **SC-002**: Users can request and receive a cited Recreation or Social score, with findings and recommendations, for a resolved site within a single conversation turn, without triggering analysis of a category they did not ask for.
- **SC-003**: 100% of AI-generated findings and recommendations produced by this feature display at least one citation identifying both the metric/score that triggered it and its supporting evidence.
- **SC-004**: 100% of data-collection or scoring attempts that cannot obtain a required input result in a user-visible data-gap notice rather than a completed-looking result with the gap silently omitted.
- **SC-005**: Users can request a second, different category analysis later in the same conversation without needing to restart the conversation, re-describe the site, or repeat the first category's request.
- **SC-006**: 0 conflicting or materially ambiguous scoring results are resolved by the system without pausing for human input.
- **SC-007**: 100% of completed Category Analysis Runs successfully relay their Category Score Result back to the originating Project in TheDigitalCore, with zero silent relay failures (a failed relay MUST be visibly surfaced, not dropped).
- **SC-008**: 100% of new-site conversations that describe a real, resolvable site with no matching TheDigitalCore Project receive an explicit location/built-asset-status result and a project-creation offer — whether the site is confirmed-built or planned/proposed (FR-001g) — and a Project is created in TheDigitalCore only after the user's explicit confirmation — never automatically.

## Assumptions

- **TheDigitalCore remains the system of record for Project, Company, Attachments, and analysis-result data.** TheDigitalCore already has production Project/Company/Attachment CRUD and its own Forge/APS integration; this feature does not duplicate that persistence inside Ask Lucy. Ask Lucy owns only the conversational agent reasoning, the MCP-tool-driven analysis, and the Immersive Viewer/Floating Panel presentation; TheDigitalCore owns Project/Attachment/SiteAnalysis/DesignConcept/DesignRecommendation storage. Ask Lucy's backend calls TheDigitalCore's API server-to-server, under a single dedicated Ask Lucy service account, to search for a matching Project, create one, and relay Category Score Results back into it.
- **No individual Ask Lucy user needs a TheDigitalCore user account.** All TheDigitalCore API calls this feature makes are performed by Ask Lucy's backend under one shared service account; TheDigitalCore's own per-user roles are not evaluated per Ask Lucy end user. Any authorization over who may trigger Project creation is enforced on Ask Lucy's side using its existing per-user authentication (FR-022), not TheDigitalCore RBAC.
- **The user works in Ask Lucy's own SPA, not an embedded/iframed view inside TheDigitalCore.** A Project-linked deep link from TheDigitalCore is one way the user arrives at the right conversation; starting a fresh conversation and letting the assistant bootstrap the Project link itself (per FR-001a–FR-001g) is the other. No chat or viewer UI is built or embedded inside TheDigitalCore itself.
- **Project creation is always user-confirmed, never automatic.** The assistant may search TheDigitalCore and report what it finds (or doesn't find), but it MUST NOT create a Project without an explicit, interactive user confirmation — consistent with the platform's no-silent-actions guardrail.
- **Vertical-slice category scope is Recreation and Social only.** Of the full pipeline's eight scoring categories (Site Context, Environmental, Sustainability, Accessibility, Recreation, Social, Safety, Smart City), only Recreation and Social are implemented in this feature, matching the known-good reference output already available for those two categories. The remaining categories, AI-generated design concepts, and report generation are explicitly deferred to follow-on features.
- **This feature's category taxonomy is the notebook's own 8-category scoring rollup, not `docs/AI Urban Design Copilot — Ask Lucy User Stories & Agent Workflow.md`'s presentation-layer categories.** That document groups site *findings* as Accessibility/Mobility/Environment/Urban Context/Services/Smart City, and design-option *KPIs* as a still-different 10-item set (Accessibility/Sustainability/Smart City Readiness/Walkability/Safety/Visitor Experience/Biodiversity/Maintenance/Cost/Mobility) — both are later-stage, presentation/evaluation groupings layered on top of the same underlying pipeline data, not a replacement for the notebook's scoring categories this feature wraps as tools. Follow-on specs covering findings presentation or design-option KPI scoring should map onto this feature's Recreation/Social (and the deferred Environmental/Sustainability/Accessibility/Safety/Smart City) categories rather than introducing a competing site-scoring taxonomy.
- **`docs/AI Urban Design Copilot — Ask Lucy User Stories & Agent Workflow.md` describes a materially larger end-to-end journey** (conversational requirement-gathering, full-category site analysis, design-alternative generation, weighted KPI scoring/comparison, iterative modification, versioning, final optimization, approval, and deliverables) than this feature implements. That document is treated as source material for follow-on specs (051+), not as an expansion of this feature's scope — with one exception already incorporated: its User Story 07/08 rule on planned/proposed sites (see Clarifications).
- **The Agent Engine's turn-by-turn interaction model is resolved (see Clarifications, 2026-08-22).** Each qualifying user turn starts its own new, independently-scoped `AgentPlanner`/`AgentExecutionOrchestrator` run with a short upfront plan, rather than one long-lived plan spanning the whole conversation; this reuses the existing plan-then-execute machinery unmodified and requires no Agent Engine changes.
- **Boundary-input modalities are limited to text in this feature.** The source pipeline supports resolving a site from a place name, a URL, coordinates, or an uploaded boundary/image (with vision-assisted resolution). This feature supports place-name and coordinate input via plain chat text only; URL-based, uploaded-boundary-file, and vision-assisted resolution are deferred, since they would require new file/image-upload interaction beyond conversational text.
- **In-scope data-layer connectors are limited to what Recreation and Social scoring need**, plus whatever base/context imagery the Immersive Viewer needs to render a boundary meaningfully. Connectors specific to deferred categories (e.g., tree canopy/land cover, terrain, hydrology, climate) are out of scope for this feature and deferred alongside those categories.
- **Recreation and Social scoring thresholds/weights are seeded as static configuration** from the existing reference pipeline output for this feature; making them administrator-configurable is deferred to a follow-on feature.
- **Conflicting or ambiguous tool results are surfaced through the existing risk-based approval mechanism** (treating the affected step as high-risk to trigger the existing pause-for-approval flow), rather than through a new, separate escalation mechanism — consistent with the constraint that no second, parallel tool-execution or interruption framework is introduced.
- **The new external MCP server's hosting and its external data-provider credentials are provisioned and registered by an administrator** using the existing MCP Tool Engine registration flow; this feature does not introduce new registration UX.
- **Existing per-user isolation, authentication, and audit/approval mechanisms apply as-is.** No new authentication method, tenancy model, or approval mechanism is introduced by this feature.
