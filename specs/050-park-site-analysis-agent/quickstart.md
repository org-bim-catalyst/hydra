# Quickstart: Conversational Park Site Analysis Agent

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

Validation scenarios for this feature's vertical slice, traced to spec.md's user stories and success
criteria. See [data-model.md](./data-model.md) and [contracts/](./contracts/) for the shapes referenced
below.

## Prerequisites

1. The new Python MCP server (`park-redesign/mcp_server`, contracts/site-analysis-mcp-tools.md) is
   running and reachable.
2. An administrator has registered it as an MCP server through the existing, unmodified
   `McpServersController` admin UI, and has activated its five tools
   (`resolve_site_boundary`, `collect_recreation_data_layers`, `collect_social_data_layers`,
   `score_recreation`, `score_social`) — per `specs/021`'s existing activation lifecycle.
3. An administrator has created the "Site Analysis Knowledge Base" (existing KB Engine,
   `specs/016`), ingested the methodology/standards/case-study source content, and created and
   Published the one "Site Analysis Agent" (`specs/020`) with those five MCP tools plus the built-in
   Knowledge Search tool scoped to that knowledge base (research.md Decision 2).
4. TheDigitalCore's service-account credential is configured in Ask Lucy's
   `TheDigitalCoreIntegrationOptions` (contracts/thedigitalcore-integration-api.md).
5. A test user is authenticated in Ask Lucy.

## Scenario 1 — Bootstrap a new digital Project (User Story 1)

1. Start a brand-new conversation with no `SiteAnalysisProjectLink`.
2. Send: "I want to redesign Al Safa Park in Dubai — it exists physically but has no digital project
   here."
3. **Expect**: the assistant reports a location/built-asset-status result (`resolve_site_boundary`,
   contracts/site-analysis-mcp-tools.md), then reports it searched TheDigitalCore and found no
   matching Project (contracts/thedigitalcore-integration-api.md Operations 1-2), then offers to
   create one and waits.
4. Reply with an explicit confirmation (e.g., "yes, create it").
5. **Expect**: a new Project is created in TheDigitalCore (Operation 3), a `SiteAnalysisProjectLink`
   row is created with `LinkSource = BootstrapCreated`, and the conversation is now linked.

## Scenario 1b — Bootstrap a planned/proposed site with no confirmed built asset (User Story 1, FR-001g)

1. Start a brand-new conversation with no `SiteAnalysisProjectLink`.
2. Send a description of a site whose location resolves but has no confirmed built park/facility
   there (e.g., a named future/planned development).
3. **Expect**: `resolve_site_boundary` returns `resolved: true` with `builtAssetConfirmed: false`
   (contracts/site-analysis-mcp-tools.md); the assistant does **not** treat this as a blocking
   failure — it tells the user the site appears planned/proposed and proceeds to search TheDigitalCore
   (FR-001g) exactly as Scenario 1 does for a confirmed-built site.
4. Continue as in Scenario 1, steps 3-5.
6. **Expect (negative check)**: repeating step 2-3 without step 4's confirmation never creates a
   Project — confirm no Project appears in TheDigitalCore until explicit confirmation is given
   (FR-001e/FR-001f).

## Scenario 2 — Existing Project is matched, not duplicated (User Story 1, edge case)

1. Using a site name/location that already has a matching Project in TheDigitalCore.
2. Send the same kind of description as Scenario 1.
3. **Expect**: the assistant reports the existing Project instead of offering to create one; a
   `SiteAnalysisProjectLink` is created with `LinkSource = BootstrapMatched`; no duplicate Project is
   created in TheDigitalCore.

## Scenario 3 — Site boundary appears in the Immersive Viewer (User Story 2)

1. In a conversation with a `SiteAnalysisProjectLink` already established (Scenario 1 or 2, or via a
   deep link — Scenario 6), if no boundary is resolved yet, send a site name or a `latitude,longitude`
   pair.
2. **Expect**: within one short `AgentExecution` (contracts/chat-to-agent-routing.md), the Immersive
   Viewer renders a boundary outline/marker (research.md Decision 11) — no page navigation, no new
   viewer surface.
3. Ask an unrelated follow-up question about the same site.
4. **Expect**: the boundary is not re-resolved (FR-005) — no new `resolve_site_boundary` tool call
   appears in the execution history for this follow-up.

## Scenario 4 — Recreation analysis with citations (User Story 3)

1. With a boundary already resolved (Scenario 3), send: "how good is this park for recreation?"
2. **Expect**: a new short `AgentExecution` runs `collect_recreation_data_layers` then
   `score_recreation` (contracts/site-analysis-mcp-tools.md); on completion, a Floating Panel appears
   (`site-analysis-category-result` type key) showing a score, findings, and at least one citation per
   finding (contracts/site-analysis-category-result.md).
3. Open a finding's citation.
4. **Expect**: the metric/score that triggered it and its supporting evidence are both visible
   (FR-017, SC-003).
5. **Expect**: the Category Score Result was relayed to TheDigitalCore (contracts/
   thedigitalcore-integration-api.md Operation 4) — confirm via TheDigitalCore's own Project view, or
   by inspecting the relay call in Ask Lucy's logs if TheDigitalCore access is unavailable in this
   environment.

## Scenario 5 — Social analysis is independent and additive (User Story 4, User Story 5)

1. In the same conversation as Scenario 4 (Recreation already scored), send: "now analyze social."
2. **Expect**: only Social-scoped tools run (`collect_social_data_layers`, `score_social`) — no
   re-run of Recreation's tools (FR-011); a second, separate Floating Panel appears with the Social
   score/findings; the earlier Recreation panel/result remains available (FR-014, User Story 4 AC2).
3. Ask for Recreation again later in the same conversation.
4. **Expect**: it re-runs and refreshes (does not silently reuse the earlier result) per spec.md's
   edge case on repeated requests.

## Scenario 6 — Deep-link entry from TheDigitalCore (FR-024(a))

1. From TheDigitalCore, follow a Project-linked deep link into Ask Lucy (contracts hand-off:
   `POST /api/v1/site-analysis/project-links`, research.md Decision 12).
2. **Expect**: the user lands in Ask Lucy's own SPA, in a conversation already linked to that Project
   (`SiteAnalysisProjectLink` with `LinkSource = InboundDeepLink`) — no TheDigitalCore-embedded UI, no
   separate login.

## Scenario 7 — Data gap and conflicting-result transparency (User Story 6)

1. Force a scenario where a required data layer is unavailable (e.g., disable/misconfigure one of the
   MCP server's upstream connectors for a test run).
2. Request the affected category's analysis.
3. **Expect**: the resulting panel visibly flags the specific field as a data gap
   (contracts/site-analysis-category-result.md's `dataGaps`) rather than omitting it or showing a
   fabricated value (FR-015/FR-016, SC-004).
4. Force a scenario where the scoring tool reports `requiresReview: true` (contracts/
   site-analysis-mcp-tools.md).
5. **Expect**: the execution pauses in `WaitingForApproval` (existing `specs/020` mechanism,
   research.md Decision 9) rather than completing with a silently-chosen result (FR-018, SC-006).

## Out of scope for this quickstart

Environmental/Sustainability/Accessibility/Safety/Smart City scoring, AI-generated design concepts,
and report generation — asking for any of these MUST produce a clear "not yet supported" reply
(spec.md edge case), which is worth a quick manual check but is not one of this feature's delivered
capabilities to otherwise validate here.
