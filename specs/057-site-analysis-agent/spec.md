# Feature Specification: Site Analysis Agent

**Feature Branch**: `057-site-analysis-agent`

**Created**: 2026-09-17

**Status**: Draft

**Input**: User description: "Site Analysis Agent — a hierarchical agent that Lucy dispatches a site name or coordinates to. It resolves/validates the site (reusing spec 042 boundary resolution), then fans out to specialized sub-agents via the spec 022 workflow engine's Parallel/Merge nodes. Each sub-agent reports its result to the parent Site Analysis Agent the moment it finishes (first-done is first-delivered, never batched); the parent validates the result and only then relays it to Lucy, which posts a 'Receiving {analysis} ...' notice in chat and opens a floating panel over the viewer with the result. Sub-agents never talk to Lucy directly and never push UI themselves. On sub-agent failure the parent records the failure but Lucy shows nothing to the user. Results persist server-side so panels can be rehydrated after navigation or reload. Scope of THIS spec: the orchestration skeleton plus one sub-agent — the top-down schematic site-analysis image generator (OpenAI image generation, persisted as a platform Document). Five further analysis sub-agents (Site Geometry, Urban Context, Connectivity & Access, Environmental Context, Character Analysis) are deferred to follow-up specs and must be addable as a provisioner entry plus one IAgentTool class with no structural change. Also deferred: exposing these agents on the Agents page, exposing the fan-out graph on the Workflows page, and making prompts or graphs editable. Metrics with no real data source (FAR, setbacks, flood, wind, climate) go behind stub provider interfaces that return 'unavailable' rather than being invented."

## Overview

Today a user who wants to understand a site must ask Lucy a series of separate questions and read the answers
as prose in the chat transcript. There is no way to ask for a structured, multi-disciplinary read of a site and
get back a set of comparable, side-by-side findings.

This feature introduces a **Site Analysis Agent**: a coordinating agent that accepts a site, breaks the analysis
into independent specialist tasks, and delivers each finding to the user as soon as it is ready — rather than
making the user wait for the slowest one. Findings arrive as floating panels over the site viewer, each carrying
its own provenance (what produced it, from which data source, and how confident it is).

The coordination chain is deliberate and fixed:

```
Lucy → Site Analysis Agent → specialist sub-agents
            ↑ report as each finishes
            ↓ validated findings only
Lucy → chat notice + floating panel
```

Specialist sub-agents report only to the coordinating agent. They never address the user directly and never
place anything on screen themselves. The coordinating agent is the only component that decides what reaches
the user.

**This release delivers the coordination chain plus one specialist: a top-down schematic site map.** Five further
specialists are planned as follow-up features and must be addable without restructuring anything built here.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask for a site analysis and watch findings arrive (Priority: P1)

A user working with a site in the viewer asks Lucy to analyze it. Lucy confirms immediately that the analysis
has started and roughly what to expect, rather than leaving the user watching a spinner. As each specialist
finishes, a short notice appears in the conversation naming the finding that just arrived, and a panel opens
over the viewer showing it. The user can read, move, resize, minimize, and close each panel independently.

**Why this priority**: This is the feature. Without progressive arrival, a multi-specialist analysis is
indistinguishable from one long wait, and the user cannot start reading the first finding while the rest are
still working.

**Independent Test**: Can be fully tested by asking for an analysis of an already-resolved site and confirming
that (a) the acknowledgement returns promptly, well before any finding is ready, and (b) the first finding's
notice and panel appear on their own, without waiting for the overall analysis to be declared finished.

**Acceptance Scenarios**:

1. **Given** a user has a site active in the viewer, **When** they ask Lucy to analyze it, **Then** Lucy
   acknowledges that the analysis has started and names what is being analyzed, without waiting for any
   specialist to finish.
2. **Given** an analysis is running, **When** a specialist finishes successfully, **Then** a notice naming that
   finding appears in the conversation and a panel showing the finding opens over the viewer.
3. **Given** an analysis with more than one specialist is running, **When** the specialists finish at different
   times, **Then** each finding appears at its own time in completion order — no finding waits for a slower one.
4. **Given** a finding panel is open, **When** the user moves, resizes, minimizes, or closes it, **Then** it
   behaves exactly like every other panel in the product, and closing it does not affect other findings.
5. **Given** the user asks to analyze a site that is not currently active, **When** the site name can be
   resolved to a real place, **Then** the analysis runs against that resolved site without the user having to
   locate it manually first.

---

### User Story 2 - Every finding carries its provenance (Priority: P1)

A user reading a finding needs to know whether they are looking at something measured from real data or
something inferred. Each finding therefore states what produced it, what data source it drew on, and how
confident that makes it. Where a figure has no real data source behind it, the finding says so plainly instead
of presenting an invented number.

**Why this priority**: An architectural or planning decision made on a confidently-presented guess is worse
than no analysis at all. Provenance is what makes the output safe to act on, so it ships with the first
finding, not after.

**Independent Test**: Can be fully tested by opening any delivered finding and confirming it displays its
analysis type, data source, and confidence level, and that any unavailable figure is labelled as unavailable
rather than estimated.

**Acceptance Scenarios**:

1. **Given** a finding has been delivered, **When** the user reads its panel, **Then** it shows which analysis
   produced it, which data source it used, and a confidence level.
2. **Given** an analysis needs a figure for which no data source is configured, **When** the finding is
   produced, **Then** that figure is reported as unavailable with the reason, and no estimated value is
   presented in its place.
3. **Given** a finding was produced primarily by AI interpretation rather than measured data, **When** the user
   reads its panel, **Then** its confidence level and data source make that distinction visible.

---

### User Story 3 - Findings survive leaving the page (Priority: P1)

An analysis can take minutes. A user who switches to another part of the product, or reloads the page, and then
returns to the conversation still finds the completed findings waiting for them, rather than discovering that
everything which finished while they were away is gone.

**Why this priority**: This is the first feature in the product where results arrive over an extended period at
unpredictable times, so the existing behavior — where on-screen panels live only for the current session — would
lose real work with no way to recover it. Fixing it is part of delivering the feature, not a later enhancement.

**Independent Test**: Can be fully tested by starting an analysis, navigating away before it completes,
returning after it has finished, and confirming the completed findings are present and readable.

**Acceptance Scenarios**:

1. **Given** an analysis is running, **When** the user navigates away and returns to the same conversation,
   **Then** all findings that completed in the meantime are available to view.
2. **Given** an analysis has fully completed, **When** the user reloads the page and reopens the conversation,
   **Then** the findings are still retrievable.
3. **Given** a user reopens a conversation with completed findings, **When** they view a finding, **Then** its
   content and provenance are identical to when it was first delivered.

---

### User Story 4 - A failing specialist does not sink the analysis (Priority: P2)

When one specialist cannot complete — an unavailable external service, a missing configuration, a site with no
usable data — the remaining specialists still deliver their findings, and the analysis as a whole still reaches
a definite end state. The failure itself is recorded in full, with its reason, so an operator can determine
afterwards what went wrong.

**Why this priority**: Independent specialists mean independent failure modes. Without this, the least reliable
data source dictates whether the user gets anything at all. It is P2 only because it presupposes the delivery
chain from Story 1.

**Independent Test**: Can be fully tested by deliberately misconfiguring one specialist, running an analysis,
and confirming the other findings still arrive and the failure is recorded with its reason.

**Acceptance Scenarios**:

1. **Given** one specialist fails, **When** the other specialists complete, **Then** their findings are still
   delivered to the user.
2. **Given** one specialist fails, **When** the analysis finishes, **Then** the analysis reaches a definite
   completed state rather than remaining perpetually in progress.
3. **Given** a specialist has failed, **When** an operator inspects the analysis record, **Then** the failure
   and its reason are recorded and attributable to that specialist.
4. **Given** a specialist returns a result that is malformed or incomplete, **When** the coordinating agent
   validates it, **Then** the result is rejected and recorded as rejected, and nothing malformed is shown to
   the user.
5. **Given** a specialist fails, **When** it fails, **Then** no notice or panel appears for that failure on its
   own — the user's reading of the successful findings is not interrupted.
6. **Given** an analysis finishes with some specialists having failed, **When** it reaches its end state,
   **Then** the user is told once that the analysis finished and how much of it succeeded.
7. **Given** an analysis finishes with no successful findings at all, **When** it reaches its end state,
   **Then** the user is told the analysis could not be completed.

---

### User Story 5 - A new specialist can be added without redesign (Priority: P3)

A platform engineer adding one of the five planned specialists later can do so by registering the new
specialist and declaring it as another parallel task — without changing how coordination, validation, delivery,
persistence, or rehydration work.

**Why this priority**: Five further specialists are already planned, so the cost of adding the sixth is a
deliberate design goal of this release. It is P3 because it is verified by inspection and by the follow-up
features, not by end-user behavior in this release.

**Independent Test**: Can be fully tested by adding a trivial no-op specialist and confirming it is delivered,
validated, persisted, and rehydrated with no changes to shared coordination logic.

**Acceptance Scenarios**:

1. **Given** the coordination chain exists, **When** a new specialist is registered and declared as a parallel
   task, **Then** its findings are delivered, validated, persisted, and rehydrated by the existing machinery.
2. **Given** a new specialist is added, **When** it runs alongside existing specialists, **Then** each still
   delivers independently on completion.

---

### Edge Cases

- **The site cannot be identified.** The user names a place that cannot be resolved. The analysis is not
  started, and Lucy explains in the same conversation turn that the site could not be identified — this is an
  in-turn failure the user must see, distinct from a specialist failing later.
- **The site is ambiguous.** Several places match the name. The existing site-resolution behavior governs;
  analysis begins only once a site is confirmed.
- **Every specialist fails.** The user is told the analysis could not be completed (FR-026), rather than being
  left with a start acknowledgement and permanent silence. Individual failures still pass without their own
  notices (FR-024).
- **The user asks for a second analysis while one is running** for the same site and conversation. The in-flight
  analysis is reused rather than starting a duplicate.
- **The user asks for a fresh analysis of a site already analyzed.** A new analysis is started; earlier findings
  are retained and not overwritten.
- **The user closes a finding panel and wants it back.** The finding remains retrievable from the stored
  analysis; closing a panel discards the view, never the finding.
- **A specialist takes an unreasonably long time.** It is bounded by the platform's existing execution limits
  and ends as a failure rather than running indefinitely.
- **The user is signed in on two devices.** Findings are delivered to the session belonging to the user who
  started the analysis; the other device sees them on reopening the conversation.
- **The conversation is deleted while an analysis is running.** Findings for a conversation that no longer
  exists are not delivered and do not error visibly.

## Requirements *(mandatory)*

### Functional Requirements

**Dispatch and coordination**

- **FR-001**: The system MUST accept a site analysis request naming either a site by name or explicit
  coordinates, initiated through conversation with Lucy.
- **FR-002**: The system MUST resolve and confirm the site before dispatching any specialist, reusing the
  platform's existing site-resolution and boundary behavior rather than introducing a second way to identify a
  place.
- **FR-003**: The system MUST acknowledge the request to the user promptly, without waiting for any specialist
  to produce a finding.
- **FR-004**: The system MUST dispatch its specialists so that they run independently of one another, and one
  specialist's duration MUST NOT delay another's delivery.
- **FR-005**: A specialist MUST report its result to the coordinating agent as soon as it finishes; results
  MUST NOT be held back and delivered together as a batch.
- **FR-006**: The coordinating agent MUST validate each reported result before any part of it reaches the user,
  and MUST reject results that are malformed, empty, or missing required provenance.
- **FR-007**: Specialists MUST NOT communicate with the user directly and MUST NOT place content on screen
  themselves; only the coordinating agent may cause a finding to be delivered.

**Delivery and presentation**

- **FR-008**: On a validated finding, the system MUST post a short notice in the originating conversation
  naming the analysis that has arrived.
- **FR-009**: On a validated finding, the system MUST open a panel over the site viewer presenting that
  finding's content.
- **FR-010**: Delivered panels MUST behave consistently with the product's existing panel behavior — movable,
  resizable, minimizable, closable, and independent of one another.
- **FR-011**: Each delivered finding MUST display the analysis that produced it, the data source used, the
  confidence level, and the time it was generated.
- **FR-012**: The system MUST deliver findings only to the user who initiated the analysis.

**Provenance and honesty**

- **FR-013**: Each finding MUST carry a confidence level drawn from a fixed, documented set of levels, assigned
  by a stated rule rather than an arbitrary score.
- **FR-014**: Where an analysis requires a figure for which no data source is configured, the system MUST report
  that figure as unavailable together with the reason, and MUST NOT present an estimated or inferred value as
  though it were measured.
- **FR-015**: Confidence MUST NOT be derived from any scoring value that is not comparable across the data
  sources it is drawn from.

**Persistence and recovery**

- **FR-016**: The system MUST persist every analysis and each of its results, including failed and rejected
  ones, so they outlive the user's browser session.
- **FR-017**: The system MUST allow a user to retrieve the findings of an analysis they initiated, after
  navigating away or reloading.
- **FR-018**: A retrieved finding MUST present the same content and provenance as when first delivered.
- **FR-019**: Access to an analysis and its findings MUST be restricted to the user who initiated it.

**Failure behavior**

- **FR-020**: A failing specialist MUST NOT prevent other specialists' findings from being delivered.
- **FR-021**: An analysis MUST reach a definite end state even when some or all of its specialists fail.
- **FR-022**: Every specialist failure MUST be captured at the point it occurs — never swallowed, discarded, or
  allowed to pass unobserved — and recorded with its reason, its originating error, and enough context
  (which analysis, which site, which specialist) to diagnose it afterwards without reproducing it.
- **FR-022a**: Every failure MUST additionally be written to the platform's structured logs with that same
  context, so a failure is diagnosable from operational tooling alone, independently of the stored analysis
  record.
- **FR-023**: When a site cannot be resolved, the system MUST tell the user so within the same conversation
  turn and MUST NOT start an analysis.
- **FR-024**: An individual specialist's failure MUST NOT produce its own notice, panel, or any other
  interruption for the user.
- **FR-025**: When an analysis ends with one or more specialists having failed or been rejected, the system
  MUST surface that outcome to the user once, briefly, in the originating conversation — stating that the
  analysis finished and how much of it succeeded.
- **FR-026**: When an analysis ends with no successful findings at all, the system MUST tell the user that the
  analysis could not be completed. The user MUST NOT be left with only a start acknowledgement and no closing
  outcome.
- **FR-027**: The closing outcome MUST be reported exactly once per analysis, regardless of how many
  specialists failed.

*Note on constitution §2 VIII (No Silent Failures)*: that principle governs **capture and diagnosability** — a
failure must never be swallowed by an empty catch or allowed to pass unobserved, because an uncaptured failure
leaves no way to determine later what actually went wrong. It is satisfied here by FR-022/FR-022a, not by
telling the user. It does **not** require exposing failures to the end user, and FR-024's silence is fully
compliant with it.

FR-025 through FR-027 are a **product decision**, not a compliance requirement: without a closing outcome a
user who asked for an analysis would be left indefinitely on a start acknowledgement with no way to know it had
ended.

**The schematic site map specialist (the one specialist in this release)**

- **FR-028**: The system MUST produce a top-down schematic site map for the analyzed site, generated from a
  versioned, reviewable prompt rather than a prompt embedded inline in code.
- **FR-029**: The schematic map MUST be stored as a platform file asset and MUST be served to the user through
  the platform's own file-access mechanism; the system MUST NOT present an externally-hosted generation URL to
  the user.
- **FR-030**: When image generation is unavailable or unsupported by the configured provider, the specialist
  MUST fail with a recorded reason rather than delivering an empty or broken finding.

**Extensibility**

- **FR-031**: Adding a further specialist MUST require only registering that specialist and declaring it as an
  additional parallel task, with no change to coordination, validation, delivery, persistence, or rehydration.

### Key Entities

- **Site Analysis**: One request to analyze one site, initiated by one user within one conversation. Records
  which site, when it started, its overall state (running, completed, failed), and when it ended. Owns its
  results.
- **Site Analysis Result**: One specialist's outcome within an analysis. Records which analysis type produced
  it, its state (completed, failed, rejected), the finding content to display, its provenance (data source,
  confidence level, generation time), a reference to any generated file, and — when unsuccessful — the failure
  reason.
- **Analysis Type**: The fixed set of specialist analyses the platform can perform. This release defines the
  schematic site map; the five planned analyses extend this set.
- **Confidence Level**: A small fixed ordered set describing how well-grounded a finding is, assigned by rule.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Confirmation that the analysis has started is produced **without waiting on any specialist** —
  the request path never blocks on analysis work. Verified behaviourally (the dispatch path returns before the
  work it scheduled), not by a latency budget; in practice this is observed within about 3 seconds.
- **SC-002**: When multiple specialists run, each finding reaches the user **as soon as that specialist
  finishes**, independent of whether others are still working — deliveries are never accumulated and released
  together. Verified behaviourally by observing two specialists finishing at different times and arriving at
  correspondingly different times, not by a latency budget.
- **SC-003**: 100% of delivered findings display their analysis type, data source, and confidence level.
- **SC-004**: 100% of findings that completed while a user was away from the page are retrievable when they
  return.
- **SC-005**: When one specialist out of several fails, 100% of the remaining successful findings are still
  delivered, and the analysis still reaches a definite end state.
- **SC-006**: 100% of specialist failures are captured and recorded with a reason attributable to the
  specialist, and are diagnosable from operational logs alone — no failure path exists that discards its error.
- **SC-007**: No figure lacking a configured data source is ever presented as a measured value; such figures are
  labelled unavailable in 100% of cases.
- **SC-008**: A platform engineer can add a further specialist and have it delivered, validated, persisted, and
  rehydrated without modifying shared coordination logic — verified by adding one.
- **SC-009**: A user can retrieve the findings of an analysis they initiated, and cannot retrieve one initiated
  by anyone else, in 100% of attempts.
- **SC-010**: 100% of analyses end with exactly one user-visible closing outcome — so no user who started an
  analysis is ever left with only a start acknowledgement, and none receives that outcome more than once.
- **SC-011**: An individual specialist failing produces zero additional notices or panels for the user.

## Assumptions

- **Site resolution is a solved problem.** The platform's existing site-identification and boundary behavior is
  reused as-is; this feature adds no new way to identify a place and inherits that behavior's accuracy and
  ambiguity handling.
- **The user has a site context.** Analyses are requested in a conversation where a site is active or nameable.
  Analyzing an arbitrary region with no identifiable site is out of scope.
- **Findings are per-user and private.** An analysis belongs to the user who started it; sharing findings with
  other users is out of scope for this release.
- **Findings are retained for the life of their conversation.** No separate expiry or retention policy is
  introduced; findings live as long as the conversation they belong to.
- **A duplicate request reuses the in-flight analysis.** Asking again for the same site in the same conversation
  while one is running does not start a second analysis.
- **Panel presentation is reused, not reinvented.** Findings are presented using the product's existing panel
  and content presentation capabilities; no new presentation vocabulary is introduced.
- **Existing execution limits apply.** Specialist duration, concurrency, and cost are bounded by the platform's
  existing agent/workflow execution limits rather than by new limits defined here.
- **Only one specialist ships in this release.** Consequently, the progressive-arrival behavior (SC-002,
  Story 1 Scenario 3) is fully demonstrable only once a second specialist exists; in this release it is
  verified by adding a temporary second specialist during testing.
- **Image generation availability varies by provider.** Not every configured AI provider can generate images;
  the feature depends on at least one that can being configured.

## Out of Scope

Deliberately excluded from this release, each planned as follow-up work:

- The five further specialist analyses: Site Geometry, Urban Context, Connectivity & Access, Environmental
  Context, and Character Analysis.
- Real data sources for zoning (floor area ratio, setbacks, height limits), flood modelling and hydrology, and
  wind and climate conditions. Interfaces for these are defined so a future release can supply them, but no
  release of real data is included here.
- Listing these agents on the Agents page.
- Showing the coordination graph on the Workflows page.
- Editing specialist prompts, instructions, or the coordination graph through the product.
- Sharing analyses or findings between users.
- Exporting an analysis as a document or report.
