# Contract: `SiteBoundaryResolverTool` (native `IAgentTool`) — SECONDARY surface

**This is not how User Stories 1-3 work in a normal Lucy conversation** — that's `chat-pipeline-integration.md`, a deterministic hook in `SendChatMessageCommandHandler`/`AiController`, mirroring `ILocationResolutionService`'s existing wiring (research.md #11). This `IAgentTool` is an *additional*, secondary invocation surface for a user-authored custom AI Agent (spec 020's Agent Builder — e.g., a future "Urban Planning Agent") that wants to call boundary resolution explicitly as one of its tools, outside a normal chat turn. Both surfaces are thin callers of the same `IBoundaryResolutionService` — no duplicated scoring/candidate-search logic between them. It follows the exact idiom already established by `DocumentSearchTool`/`KnowledgeSearchTool`.

## Identity

| Property | Value |
|---|---|
| `Name` | `SiteBoundaryResolverTool` |
| `Description` | "Resolves a named or addressed site's geographic boundary as a polygon, with a confidence level and data source." |
| `RiskLevel` | `Low` (read-only, no writes, no side effects) |
| `RequiredPermissions` | `[AgentToolPermission.ExternalNetwork]` (the OSM Overpass call) |

## Input schema

```json
{
  "type": "object",
  "required": ["locationQuery"],
  "properties": {
    "locationQuery": { "type": "string", "description": "Site name, address, or 'lat,lon' — same forms ILocationResolutionService already accepts." },
    "radiusMeters": { "type": "integer", "minimum": 50, "maximum": 5000, "description": "Search radius around the resolved point. Defaults to BoundaryScoringOptions.SearchRadiusMeters (500m) when omitted." }
  }
}
```

## Output schema

```json
{
  "type": "object",
  "required": ["outcome"],
  "properties": {
    "outcome": { "type": "string", "enum": ["confirmed", "no_candidates", "ambiguous", "unavailable"] },
    "message": { "type": "string", "description": "Always present — the user-facing explanation for this outcome, verbatim or near-verbatim narratable by the agent (FR-005/FR-007/FR-012)." },
    "boundary": {
      "type": "object",
      "description": "Present when outcome is 'confirmed' or 'no_candidates' (the latter carries a low-confidence manual-fallback approximation); absent for 'ambiguous'/'unavailable'.",
      "properties": {
        "siteName": { "type": "string" },
        "centroid": { "type": "object", "properties": { "latitude": {"type": "number"}, "longitude": {"type": "number"} } },
        "polygon": { "type": "array", "items": { "type": "object", "properties": { "latitude": {"type": "number"}, "longitude": {"type": "number"} } } },
        "areaSquareMeters": { "type": "number" },
        "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
        "confidenceLevel": { "type": "string", "enum": ["low", "medium", "high"] },
        "source": { "type": "string", "enum": ["osm-boundary", "government-cadastral", "ai-interpretation", "uploaded-boundary", "manual-fallback"] },
        "sourceDetail": { "type": "string" },
        "notes": { "type": "array", "items": { "type": "string" } },
        "alternativeCandidateNames": { "type": "array", "items": { "type": "string" } }
      }
    }
  }
}
```

## Behavior contract (maps directly to spec FRs)

| Outcome | When | What the agent MUST do with it |
|---|---|---|
| `confirmed` | A boundary was resolved (any confidence level) | Narrate `boundary.confidenceLevel` and `boundary.sourceDetail` to the user (FR-004/FR-005) every time — the tool result is not a "just show it silently" payload. If `alternativeCandidateNames` is non-empty, mention them (FR-008). If `confidenceLevel` is `medium`/`low`, explicitly flag it as provisional (FR-006). |
| `no_candidates` | Point resolved, but no boundary candidate met minimal evidence | `boundary` is still present — a Low-confidence, `manual-fallback`-sourced approximate area around the point — state plainly that no reliable boundary was found and that this is only an approximation (FR-007). |
| `ambiguous` | Reserved — the underlying point itself was ambiguous (handled upstream by `ILocationResolutionService`, surfaced here only if the tool is invoked with a query that resolves ambiguously) | Ask the user to disambiguate, same as existing location-ambiguity handling. |
| `unavailable` | The boundary data source could not be reached | State that boundary resolution isn't available right now — never silently retry-and-hide or return a stale/empty result (FR-012). |

`ExecuteAsync` never throws for an expected failure mode — every case above is a `AgentToolResult.Success` carrying a typed `outcome`, matching constitution §VIII (a hard `AgentToolResult.Failure` is reserved for a genuinely unexpected/programmer-error condition, e.g. malformed input that fails schema validation before the tool body even runs).

## Non-goals (explicitly not this tool's contract)

- No mutation, no write permission, no persistence — a second call with the same input may return a different result (OSM data can change) and that's expected; there is no "get the previously saved boundary" operation in this feature.
- No manual polygon editing input — accepting user-supplied corrected vertices is the documented future feature (see project memory), not this contract.
