# Contract: Open Solar Analysis Capability

How Lucy opens solar analysis (FR-033…FR-036). Mirrors the trailing-SSE tag pattern already proven
twice — `AdjustViewerFocusCapability`/`__ZOOM__` and `LoadViewerContentCapability`/
`__VIEWER_CONTENT__` (research D3). Nothing new is invented.

## Capability

```csharp
public const string CapabilityKey = "open_solar_analysis";
IsAvailable => true;
IsOfferable => false;   // Lucy invokes it; it is not offered as a menu choice
```

### Input schema

| Field | Type | Required | Notes |
|---|---|---|---|
| `date` | `string` | no | `YYYY-MM-DD`, site-local. Defaults to today at the site. |
| `timeOfDay` | `string` | no | `HH:mm`, site-local. Defaults to the current time. |

The capability deliberately takes **no coordinates**: it acts on the *active* site, which the
viewer already owns. A capability that could be pointed at arbitrary coordinates would be a way to
move the viewer as a side effect of a chat turn.

### It performs no solar computation

Per research D3 and FR-034, the capability opens the analysis and returns a status. The figures are
computed once, in the browser, and Lucy describes what is shown rather than reciting numbers the
user can already see. This is why no astronomy dependency is added to the backend.

### The capability's description is load-bearing (FR-033, FR-034)

FR-034 requires Lucy's reply to "refer to what is displayed rather than restating figures the user
can already see" — and since this capability deliberately returns no figures, the *only* thing that
produces that behaviour is the instruction text the capability carries. It must direct Lucy to
describe what the analysis shows qualitatively — how the site sits against the sun, where shadows
fall and how they move — and explicitly not to recite azimuth, altitude, sunrise or sunset, which
the panel already displays.

This wording is asserted in `OpenSolarAnalysisCapabilityTests`. Without that assertion the
requirement has no mechanism at all: a later edit could turn the capability into a figure-reciter
and every other test would still pass.

## Result

Success (`AgentToolResult.Success`):

```json
{ "opened": true, "date": "2026-09-13", "timeOfDay": "14:00" }
```

Refusal — **no active site** (FR-035):

```json
{ "opened": false, "reason": "no-active-site" }
```

Lucy must then say a site is needed. She must **not** open an empty analysis.

Refusal — **cannot be produced** (FR-036):

```json
{ "opened": false, "reason": "viewer-unavailable" }
```

Lucy must say so and why, and must not describe results that do not exist.

## Transport

1. Capability returns `AgentToolResult.Success(json)`.
2. `StructuredPayloadExtractor.TryExtract` matches on `CapabilityKey` and shapes a
   `ChatStreamChunk.SolarAnalysis` field.
3. `AiController` emits a trailing SSE event: `data: __SOLAR_ANALYSIS__{json}\n\n`.
4. `aiApi.ts` parses it into a `ChatStreamEvent` of type `solarAnalysis`.
5. `useChatStream.ts` activates `viewer.solar-analysis` with the supplied date/time.

Every step mirrors an existing, working equivalent. The only additions are one record, one
`ChatStreamChunk` field, one `case`, one emit block, one parse block and one handler branch.

## Failure (FR-045, FR-046, §2.VIII)

| Failure | Surfaced as |
|---|---|
| No active site | Lucy's own words (FR-035) — a refusal, not an error |
| Viewer unavailable / WebGL unsupported | Lucy's words **and** the viewer's existing unsupported-3D notice |
| Extension fails to activate | `ExtensionFailureNotice` — the framework's existing surface |
| Building data unavailable after opening | The analysis's `partial` state and its buildings notice (FR-014) |

No path leaves a user with nothing to read, and none is observable only in logs.
