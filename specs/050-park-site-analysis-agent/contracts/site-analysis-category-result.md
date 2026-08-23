# Contract: Category Score Result Shape

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decision 6, 10)

Per research.md Decision 6, there is no dedicated database table for a Category Analysis Run's
result — this JSON shape is what `AgentExecution.FinalOutputJson` holds (the Site Analysis Agent's
`OutputFormat = Json`), and it is also the `data` payload of the `PanelRequested` SignalR event
(`specs/028` contracts/panel-hub-events.md) that renders it as a Floating Panel
(`site-analysis-category-result` type key, research.md Decision 10).

## Shape

```json
{
  "category": "recreation | social",
  "siteName": "string",
  "score": "number (0-100)",
  "findings": [
    {
      "title": "string",
      "type": "opportunity | weakness",
      "triggeringMetric": "string",
      "citation": {
        "documentTitle": "string",
        "passage": "string",
        "sourceRef": "string (existing RAG citation identifier, FR-020)"
      }
    }
  ],
  "dataGaps": [
    { "field": "string", "reason": "string" }
  ],
  "requiresReview": "boolean",
  "reviewReason": "string | null",
  "agentExecutionId": "guid (this AgentExecution's own id, for cross-referencing execution history)"
}
```

## Field-level rules (traced to functional requirements)

- **`findings[].citation` is never absent** for a finding produced by this feature (FR-017/FR-003 in
  the SC list — SC-003 requires 100% of findings to carry a citation). A finding the pipeline could
  not ground in the Site Analysis Knowledge Base MUST NOT be included as a normal finding — it
  becomes a `dataGaps` entry instead, per FR-015/FR-016.
- **`dataGaps` is always present as an array** (possibly empty), never omitted — its presence, not
  just its content, is what makes a data gap visibly distinct from a merely short findings list
  (FR-016, SC-004).
- **`requiresReview`/`reviewReason`** mirror the MCP tool contract's `score_recreation`/`score_social`
  output (contracts/site-analysis-mcp-tools.md) — when `true`, the Floating Panel renderer MUST
  display the pending-review state rather than presenting the score as final (FR-018).
- **`agentExecutionId`** lets the frontend (or a support engineer) trace a displayed panel back to
  its full tool-call audit trail (`AgentExecutionStep`/`AgentToolCall`, FR-023) without this feature
  needing its own separate audit table.

## Consumers

1. **`SiteAnalysisAgentExecutionCompletionHandler`** (Application) — reads
   `AgentExecution.FinalOutputJson` on completion, calls `ITheDigitalCoreClient.RelayCategoryScoreResultAsync`
   (FR-026, this exact shape minus `agentExecutionId`'s internal-only relevance — TheDigitalCore
   receives it too, per contracts/thedigitalcore-integration-api.md Operation 4, for cross-system
   traceability) and `IPanelNotifier.PanelRequestedAsync` (research.md Decision 10).
2. **`SiteAnalysisCategoryResultPanel.tsx`** (frontend renderer) — the sole consumer of this shape as
   `PanelRequested.data`; renders score, findings-with-citations, data gaps, and the review-pending
   state.

## Validation

Per `specs/028`'s own contract convention, the frontend validates this payload with a zod schema
before rendering (matching `panel-hub-events.md`'s "zod validation, registry lookup" step) — an
invalid/malformed payload is a visible fallback/error state (spec 028 FR, "no changes required to the
core viewer or panel-management logic" extends to: a bad payload for *this* type key does not crash
the panel framework itself), never a silently blank panel.
