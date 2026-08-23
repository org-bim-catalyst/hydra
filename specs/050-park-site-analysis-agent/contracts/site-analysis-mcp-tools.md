# Contract: Site Analysis MCP Tools

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decisions 4-5)

Five MCP tools, implemented by the new Python server in the sibling `park-redesign` repository
(`mcp_server/`), registered into Ask Lucy through the existing, unmodified `McpServersController`
admin flow (`specs/021`). Each wraps an existing notebook module's logic verbatim (research.md
Decision 4) — this contract defines the tool-call boundary the Site Analysis Agent's plan steps use,
not the internal pipeline implementation.

All five tools share this failure contract (FR-015/FR-016, constitution §2.VIII): any input that
cannot be obtained from any available source/fallback returns an explicit `dataGaps` entry (field
name + reason) in the tool's success response rather than omitting the field, fabricating a value, or
raising an unhandled tool error. A tool call fails outright (surfaced as a normal `AgentToolCall`
failure, per `specs/021`'s existing MCP failure handling) only when the tool itself cannot run at all
(e.g., the MCP server is unreachable) — not when a specific data point is unavailable.

## 1. `resolve_site_boundary`

Serves both FR-001a (physical-existence verification) and FR-002 (boundary resolution) — research.md
Decision 5.

**Input**:
```json
{ "siteDescription": "string (place name) | null", "latitude": "number | null", "longitude": "number | null" }
```
Exactly one of `siteDescription` or the `latitude`/`longitude` pair is supplied, per FR-001.

**Output (success)**:
```json
{
  "resolved": true,
  "builtAssetConfirmed": "boolean",
  "boundary": { "type": "outline | marker", "geoJson": "object" },
  "resolvedName": "string",
  "latitude": "number",
  "longitude": "number",
  "candidateCount": "integer"
}
```
`resolved: true` means the location itself was found (FR-001a) — it does **not** by itself mean a
built park/facility exists there. `builtAssetConfirmed` is a separate signal: `true` for a
confirmed-existing built asset, `false` for a resolvable location with no confirmed built asset
(a planned/proposed/under-construction site, per spec.md FR-001g and Clarifications). Both cases
proceed identically to TheDigitalCore search (FR-001c) — `builtAssetConfirmed` only changes what
the assistant tells the user, never whether it proceeds.
`candidateCount > 1` signals an ambiguous match (FR-004) — the calling Application code (not the tool)
is responsible for turning that into a clarifying chat question, per this codebase's existing
"tools return data, agents/handlers decide user-facing behavior" separation.

**Output (not resolved)**:
```json
{ "resolved": false, "reason": "string" }
```
FR-001b: `resolved: false` means the *location itself* could not be found at all (ambiguous or
unrecognizable) — only this case blocks. The agent MUST report this and ask the user to
clarify/correct — it MUST NOT proceed to TheDigitalCore search or offer Project creation for an
unresolvable location.

## 2. `collect_recreation_data_layers`

**Input**: `{ "boundary": "geoJson (from resolve_site_boundary)" }`

**Output**:
```json
{
  "layers": { "<layer-name>": "value | null" },
  "dataGaps": [ { "layer": "string", "reason": "string" } ]
}
```
Layer set is limited to whatever Recreation scoring (`score_recreation`) needs — the notebook's full
Module 02 layer catalog is not exposed 1:1 here (spec Assumptions: "in-scope data-layer connectors are
limited to what Recreation and Social scoring need").

## 3. `collect_social_data_layers`

Same shape as `collect_recreation_data_layers`, scoped to Social-relevant layers.

## 4. `score_recreation`

**Input**: `{ "boundary": "geoJson", "layers": "object (from collect_recreation_data_layers)" }`

**Output**:
```json
{
  "score": "number (0-100)",
  "findings": [
    {
      "title": "string",
      "type": "opportunity | weakness",
      "triggeringMetric": "string",
      "citationRef": "string (Site Analysis Knowledge Base document/passage id)"
    }
  ],
  "requiresReview": "boolean",
  "reviewReason": "string | null"
}
```
`requiresReview: true` is how a conflicting/materially-ambiguous scoring outcome is signaled
(research.md Decision 9) — the Site Analysis Agent's tool-risk configuration maps this outcome to
High risk so `AgentPolicy` pauses for human approval (FR-018), rather than the tool or the agent
silently picking a result.

`citationRef` is an identifier the Application layer resolves against the Site Analysis Knowledge
Base's existing RAG citation mechanism (FR-019/FR-020) — the tool itself does not perform retrieval;
it only names which methodology/standard its finding is grounded in, matching the notebook's existing
evidence/provenance model (`AI-Assisted_Urban_Park_Analysis_Framework.docx`, per
`FLUMERIA-STUDIO-INTEGRATION-ARCHITECTURE.md` §6).

## 5. `score_social`

Same shape as `score_recreation`, for the Social category.

## What is deliberately not a tool here

Per research.md Decision 3, searching/creating/relaying to TheDigitalCore is **not** one of these
five tools — it is a direct Application-layer call via `ITheDigitalCoreClient`, invoked by the
bootstrap-flow command handlers and the completion handler, never by the Site Analysis Agent's own
plan steps. The Agent's tool set (these 5 + built-in Knowledge Search) never includes a
TheDigitalCore-calling tool.
