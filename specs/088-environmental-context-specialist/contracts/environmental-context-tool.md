# Contract: Environmental Context Tool

**Spec**: [../spec.md](../spec.md) | **Plan**: [../plan.md](../plan.md) | **Requirements**: FR-001–FR-015,
FR-021–FR-033, FR-039–FR-042

The specialist's interface, its calls, and the rules its composed content must satisfy. It mirrors
[SPEC-057's `result-relay.md`](../../057-site-analysis-agent/contracts/result-relay.md): the relay is the only
delivery channel, and the panel document is the only content channel.

## Identity

| Property | Value |
|---|---|
| Class | `SiteEnvironmentalContextTool` (`src/AskLucy.Application/SiteAnalysis/Tools/`) |
| `Name` | `site_analysis_environmental_context` |
| Workflow node key | `environmental_context` |
| `RiskLevel` | `Low` |
| `RequiredPermissions` | none — no external network, file, or AI call in this release |

## Input

```json
{"type":"object","required":["siteAnalysisId","siteName","siteLocation"],
 "properties":{"siteAnalysisId":{"type":"string"},"siteName":{"type":"string"},"siteLocation":{"type":"string"}}}
```

- `siteAnalysisId` must parse as a `Guid`; otherwise the tool returns `AgentToolResult.Failure` without
  consulting the relay.
- `siteName` is display text. `siteLocation` is display text and is **never** parsed for coordinates.
- Coordinates are read from the persisted `SiteAnalysis` named by `siteAnalysisId`, in a fresh DI scope. The
  tool never re-resolves the site and never falls back to the conversation's active location.

## Output

```json
{"type":"object","required":["reported"],"properties":{"reported":{"type":"boolean"}}}
```

`{"reported": true}` on a delivered finding. On failure the tool returns `AgentToolResult.Failure(reason)` after
reporting the failure to the relay.

## Calls (exactly two per run)

1. `IFloodDataProvider.ResolveAsync(analysis.Latitude, analysis.Longitude, ct)`
2. `IClimateDataProvider.ResolveAsync(analysis.Latitude, analysis.Longitude, ct)`

No other provider, repository, or network call is made. The call count does not vary with the site, the
analysis, or the user.

## Rendering rules

Per provider outcome, for every figure that provider owns:

| Outcome | Rendered value |
|---|---|
| `Confirmed`, field non-null and in range | the value (`%`, `°`, `m/s` as applicable) |
| `Confirmed`, field null | `Not reported by the source` |
| `Confirmed`, value out of range | `The source returned an unusable value` |
| `NoCandidates` | `No data available for this location` + reason |
| `Ambiguous` | `Conflicting data for this location` + reason |
| `Unavailable` | `No data source available` + reason |

Additional rules:

- Every figure is rendered in every finding; none is omitted.
- No value is derived, interpolated, or inferred from another figure, from regional knowledge, or from imagery.
- The "Not assessed" statement lists the dimensions this release does not model and is textually distinct from
  any figure state (FR-041/FR-042).
- The solar block contains a reference only: it names the viewer's Solar Analysis, states that it describes the
  site currently shown in the viewer, and contains no sun position, sunrise/sunset time, day length, or shadow
  measurement.
- Block kinds are limited to `heading`, `text`, `keyValue`, `divider`; the provenance `keyValue` block is last.

## Provenance (`SiteAnalysisResultMetadata`)

| Field | Value |
|---|---|
| `AnalysisType` | `SiteAnalysisType.EnvironmentalContext` |
| `SiteName` | the analysis record's `SiteName` |
| `DataSource` | `"no data source configured"` while no figure is grounded; otherwise a truthful identifier (FR-030) |
| `ConfidenceLevel` | all figures grounded → `High`; some → `Medium`; none → `Low` |
| `GeneratedAtUtc` | `DateTime.UtcNow` at report time |
| `Notes` | per-figure reasons; advisory only — the relay persists content and provenance, not notes |

## Failure

| Situation | Behaviour |
|---|---|
| Analysis record not found | `relay.ReportFailureAsync(siteAnalysisId, EnvironmentalContext, reason, cause: null)` then `AgentToolResult.Failure` |
| Provider throws | `ReportFailureAsync` with the originating exception, structured log carries analysis id, site, user, type; then `AgentToolResult.Failure` — **no** success report, no partial finding |
| Provider reports unavailable / no candidates / ambiguous | **not** a failure — the finding is delivered with those states |
| Relay validation rejects the result | recorded by the relay as `Rejected`; nothing delivered; the tool still returns success from its own execution |
| `OperationCanceledException` when the token is cancelled | rethrown, never converted into a failure |

`ReportFailureAsync` delivers nothing to the user (057 FR-024). The analysis still settles through the
`AnyCompleted` merge strategy.

## Delivery

The tool calls `ISiteAnalysisResultRelay.ReportSuccessAsync(siteAnalysisId, EnvironmentalContext, metadata,
content, documentId: null, ct)` **inline, as the last step of `ExecuteAsync`** — never via workflow node
completion events, which the parallel executor batches (057 research D3). It never posts a message, opens a
panel, or notifies a hub itself.
