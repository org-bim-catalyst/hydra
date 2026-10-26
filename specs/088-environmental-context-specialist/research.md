# Research: Environmental Context Specialist (SPEC-088)

**Date**: 2026-10-25
**Prerequisites**: specs 052 (solar analysis), 057 (site analysis agent), 083 (map capture, considered and
rejected as a source here).

Every decision below was verified against the current code, not inferred from the specs that introduced it.
Line references are to the repository state on the date above.

---

## D1 — The specialist is an `IAgentTool` with the shipped specialist's exact shape

**Decision**: `SiteEnvironmentalContextTool`, tool name `site_analysis_environmental_context`, node key
`environmental_context`, input schema
`{siteAnalysisId, siteName, siteLocation}` — identical to `SiteSchematicImageGenerationTool`
(`src/AskLucy.Application/SiteAnalysis/Tools/SiteSchematicImageGenerationTool.cs:34-35`).

**Rationale**: `NativeToolNodeExecutor` adapts any registered `IAgentTool` into a workflow node and resolves its
input from the workflow expression snapshot
(`src/AskLucy.Application/Workflows/Runtime/NativeToolNodeExecutor.cs:30-52`). The provisioner's node-config
template already supplies exactly those three values
(`src/AskLucy.Infrastructure/SiteAnalysis/SystemWorkflowProvisioner.cs`, `BuildNativeToolNodeConfig`), so the
branch entry is one tuple and the template is untouched.

**Coordinates**: the provider seams take `(latitude, longitude)`
(`src/AskLucy.Application/SiteAnalysis/Providers/IFloodDataProvider.cs`), but the tool input does not carry them
and deliberately must not — `siteLocation` is a display string, not a coordinate pair. The site point is read
from the persisted `SiteAnalysis` aggregate (`Latitude`/`Longitude`, frozen at dispatch — `SiteAnalysis.cs`),
in a **fresh DI scope**, mirroring the shipped tool's own scope discipline: a concurrent workflow branch shares
the job's scoped `DbContext`, which is not safe to reuse even for reads
(`SiteSchematicImageGenerationTool.cs:102-109`; `ScopeIsolatedSiteAnalysisResultRelay`).

**Alternatives considered**:
- *Add latitude/longitude to the shared node-config template.* Rejected: it changes the shipped specialist's
  input, and the expression evaluator substitutes string properties only, so numbers would arrive as strings.
- *Re-resolve the site by name.* Rejected by the specification (FR-007): a second identification path can
  produce figures for a different place than the one being analysed.

---

## D2 — The provider seams gain their first, deliberately empty implementation in Infrastructure

**Decision**: `UnavailableFloodDataProvider` and `UnavailableClimateDataProvider` live in
`src/AskLucy.Infrastructure/SiteAnalysis/Providers/` and are registered in
`src/AskLucy.Infrastructure/DependencyInjection.cs`, next to `IWeatherProvider`
(`services.AddScoped<IWeatherProvider, WeatherProvider>()`, line 416). Each returns exactly
`DataResolutionOutcome.Unavailable<T>("No <flood|climate> data source is configured.")` and nothing else.

**Rationale**: the interfaces are declared in Application (`SiteAnalysis/Providers/`) and were shipped with **no
implementation and no registration** by 057 (`plan.md` Complexity Tracking; `research.md` D11). A consumer now
exists, and an unresolvable dependency in an `IAgentTool` breaks `AgentToolCatalog`'s
`IEnumerable<IAgentTool>` resolution — every request, not just this feature. Infrastructure is where a licensed
implementation will also live, so the swap is one registration line and the specialist's calling code never
changes (FR-017/FR-018).

**Verified gap — source identity**: `DataResolutionOutcome<T>(Type, Value, Reason)` carries no source name, so a
*confirmed* value could not be attributed to a vendor if one existed. Unreachable today (no implementation
confirms), and the data source recorded for the finding is therefore the literal "no data source configured"
(FR-030). A licensed implementation will have to supply an identifier — noted as a follow-up, not solved here
(§17: not worth an expensive-to-reverse decision before the vendor is known).

**Credential rule**: a licensed implementation must resolve its credential from stored configuration first and
read configuration only as a fallback (`docs/ARCHITECTURE.md`, "Credential resolution order: database first").
This feature introduces no credential.

**Alternatives considered**:
- *Hard-code "unavailable" in the tool.* Rejected: the seam becomes dead code and the future source forces a
  tool change — the extensibility 057 committed to (FR-031) would be nominal.
- *Register the empty providers in `Application/DependencyInjection.cs`.* Rejected: implementations belong in
  Infrastructure, where the licensed one will live; splitting the swap across two files invites a missed
  registration.

---

## D3 — Expected-result count and branch list come from one Application-owned declaration

**Decision**: new `src/AskLucy.Application/SiteAnalysis/SiteAnalysisSpecialists.cs` holds the fan-out
declaration — `(ToolName, NodeKey)` pairs plus `Count`. `SystemWorkflowProvisioner.SpecialistBranches` is
populated from it, and `RequestSiteAnalysisCapability` creates the analysis with
`ExpectedResultCount = SiteAnalysisSpecialists.Count` (today `2`).

**Rationale — a real defect, verified**: `RequestSiteAnalysisCapability` currently creates the aggregate with
`private const int ExpectedResultCount = 1`. `SiteAnalysisResultRelay.SettleIfCompleteAsync` settles as soon as
`Results.Count >= ExpectedResultCount`, calling `Complete()` and setting `Status = Completed`. A second
specialist reporting afterwards fails the relay's validation gate (`analysis.Status != Running` →
"Analysis is no longer running") and is persisted as **Rejected** — never delivered. This is exactly the
situation the second branch creates.

The declaration cannot live in Infrastructure: Application cannot reference it (§2 I). It cannot stay in the
provisioner either, because the capability cannot see it. One list in Application satisfies both readers and
keeps "add a specialist = one entry + one class" true (057 FR-031).

**Alternatives considered**:
- *Bump the constant to 2.* Rejected: two places to edit per future specialist, and the same silent-failure
  defect survives.
- *Count `NativeTool` nodes in the published workflow version at dispatch time.* Rejected: adds a repository
  read and a `ThenInclude` on versions→nodes, and couples the capability to the graph's shape for a value it can
  know statically.

---

## D4 — Content composition: one additive composer method, existing block kinds only

**Decision**: add `SiteAnalysisContentComposer.ComposeSections(heading, bodyText, sections, metadata)` where a
section is `(Title, Items[], BodyText?)`; it emits `heading` (level 2) + `text`, then per section a `divider` +
`heading` + optional `text` + `keyValue`, and always appends `BuildProvenanceBlock(metadata)` **last** (FR-011).

**Rationale**: `ComposeFinding` has a fixed heading + text + optional image + provenance shape
(`SiteAnalysisContentComposer.cs`) and cannot express flood and climate as separate labelled groups. The
composer owns the vocabulary and already exposes `BuildProvenanceBlock` "so a future specialist composing its
own multi-block layout can still append the exact same provenance block" — its own doc comment anticipates this
extension. Building `JsonObject` blocks in the tool would duplicate the vocabulary (kind names, `level`,
envelope version) outside the one class that owns it (DRY, §2 III). The method is shared, not specialist-shaped,
so the Site Geometry follow-up can use it too.

**Verified vocabulary**: the client renders `heading`, `text`, `keyValue`, `divider`, `image`, `table`, `chart`,
`metric` (`viewer/panels/content/blocks.ts`); the specialist uses only the first four, the same set the
specification permits. `keyValue` items accept `string | number | null`; the client renders `null` as
"Not available" — but this specialist never emits `null`, because "no data source available" must be an explicit
statement, not a client-defaulted absence.

**Alternatives considered**:
- *Hand-build blocks in the tool.* Rejected: duplicates the vocabulary and the `{version:1, blocks:[...]}`
  envelope.
- *Tables or charts.* Rejected: no expressive gain for four labelled figures, and a second vocabulary is a
  larger surface than the feature needs (§2 III).

---

## D5 — Outcome rendering: four distinct states, plus the null-field case

**Decision**: `DataResolutionOutcomeType` maps to distinct panel text:

| Outcome | Rendered for each figure it owns |
|---|---|
| `Confirmed` + value | the value |
| `Confirmed` + `null` field | "Not reported by the source" |
| `NoCandidates` | "No data available for this location" + reason |
| `Ambiguous` | "Conflicting data for this location" + reason |
| `Unavailable` | "No data source available" + reason |

**Rationale**: the vocabulary already exists (`Providers/DataResolutionOutcome.cs`, mirroring
`SiteBoundaryResolverTool`'s `confirmed|no_candidates|ambiguous|unavailable`). Collapsing the four into one
message would tell the user a source does not exist when it does and merely had no entry — the exact confusion
the specification forbids (FR-010–FR-013). `Confirmed` with a null field is a fifth, distinct state: the source
answered and did not carry that figure.

**Verified**: only `Confirmed` and `Unavailable` have factories (`DataResolutionOutcome.Confirmed/Unavailable`);
`NoCandidates` and `Ambiguous` are constructed through the record. Only `Unavailable` is reachable today, but the
rendering exists so a licensed source needs no specialist change.

---

## D6 — Confidence: rule-based, coverage-driven, never `Importance`

**Decision**: derive per-figure grounding (confirmed + non-null), then call
`SiteAnalysisConfidence.ResolveConfidence` once for the finding:

| Coverage | Call | Level |
|---|---|---|
| every reported figure grounded | `(dataDirectlyDescribesSite: true, dataIsSparseOrApproximate: false)` | High |
| some grounded, some not | `(false, true)` | Medium |
| none grounded | `(false, false)` | Low |

**Rationale**: `ResolveConfidence` returns High whenever `dataDirectlyDescribesSite` is true and otherwise
Medium/Low from the sparse flag (`SiteAnalysisConfidence.cs`), so coverage is the only honest way to express
"unavailable figures push confidence down" (FR-033) through the existing helper. Today every figure is
unavailable → **Low**. The finding is still delivered (an unavailable provider is data, not failure), and the
solar reference contributes nothing to confidence because it is a pointer, not a measurement (FR-024).

**Never**: `GeocodingCandidate.Importance` — incompatible scales across providers (057 research D10, rule 6).
The helper takes booleans only, so the field cannot be passed even by accident.

---

## D7 — The solar cross-reference is a typed pointer, not a computation

**Decision**: one `text` block stating that sun path, sunrise/sunset, day length, and real building shadows are
computed live in the viewer's Solar Analysis **for the site currently shown in the viewer**, that this finding
does not repeat them, and how the analysis is opened (viewer toolbar, or by asking Lucy).

**Rationale**: 052 computes all solar figures in the browser and deliberately keeps astronomy out of the backend
(`specs/052-solar-analysis/research.md` D3); `OpenSolarAnalysisCapability` performs no solar computation and
acts on the **active** site, not arbitrary coordinates (`OpenSolarAnalysisCapability.cs`). The server therefore
cannot know which site the viewer shows, so the reference states what the analysis covers rather than asserting
a match (FR-023).

**No action is emitted**: the client's panel action allowlist is a closed set of viewer commands
(`viewer/panels/actions/allowlist.ts`) and contains no "open solar analysis". Adding one would be a client
command and a security-relevant allowlist change — a separate feature (FR-025).

---

## D8 — Outcome versus failure are different states

**Decision**:
- `Unavailable` / `NoCandidates` / `Ambiguous` → **deliver** the finding; these are facts about the data.
- An exception from a provider (or any other unexpected error) → `relay.ReportFailureAsync(...)` **and**
  `AgentToolResult.Failure`, with structured context; nothing is delivered to the user (057 FR-024).

**Rationale**: conflating the two either presents a fault as "no data source" (hiding it) or presents honest
unavailability as a failure (losing the finding and the sun reference). `ReportFailureAsync` persists the
reason and logs analysis id, site, specialist, and user id — the diagnosability §2 VIII requires.

**Also**: the analysis record is read before reporting. If it cannot be found, the tool reports failure with that
reason (the relay would otherwise log "not found — dropping"); it never invents a site point or falls back to
the conversation's active location.

---

## D9 — Data source string, and no migration

**DataSource**: `"no data source configured"` while nothing is confirmed — truthful per FR-030, non-empty for
the relay's validation gate, and it never names a source that was not consulted. The source-identity gap is
recorded in D2.

**No migration**: `SiteAnalysisResult.AnalysisType` is stored as a string
(`SiteAnalysisResultConfiguration.cs: HasConversion<string>().HasMaxLength(40)`); `"EnvironmentalContext"` is
20 characters. Adding the enum member changes no schema. A generated migration would be noise.

**Relay display name**: `SiteAnalysisResultRelay.FormatAnalysisTypeName` maps `SchematicImage` to
"schematic site map" and falls through to `ToString()` otherwise. Without a case, the chat notice would read
"Receiving the EnvironmentalContext…" and the panel title "EnvironmentalContext — <site>". One case is added:
`"environmental context"`.

**Client**: no change. `siteAnalysisApi.ts` types `analysisType` as `string`, and the panel renders the existing
block kinds. Known pre-existing inconsistency, out of scope: live delivery titles use the relay's formatted name
while rehydration uses the raw enum string (`useSiteAnalysisRehydration.ts:41`) — true for `SchematicImage` today.

---

## D10 — Tests

**Decision**: `EnvironmentalContextToolTests` covers:
- both providers `Unavailable` → delivered finding, every figure "No data source available" + reason, confidence
  Low, data source "no data source configured", solar reference present, no solar figure in the content;
- one confirmed + one unavailable → partial rendering, confidence Medium;
- confirmed values → figures rendered, confidence High;
- confirmed with a null field → "Not reported by the source";
- `NoCandidates` / `Ambiguous` → their distinct text, never "no data source available";
- provider throws → `ReportFailureAsync` + `AgentToolResult.Failure`, no success reported;
- coordinates passed to the providers are the analysis record's, not the input name;
- every emitted block kind is one of `heading|text|keyValue|divider`, and the provenance block is last;
- invalid/missing `siteAnalysisId` → failure without touching the relay.

**Progressive delivery**: `SiteAnalysisProgressiveDeliveryTests` currently explains it cannot construct a real
two-specialist analysis because the release ships exactly one `SiteAnalysisType`. With
`EnvironmentalContext` added, that test becomes constructible for the first time; a case proving two distinct
types report concurrently, both deliver, and the analysis settles only after both, is added (SC-011/SC-013).

---

## D11 — The map capture is not a source here

**Decision**: the capture endpoint ([specs/083](../083-viewer-map-capture/spec.md)) is not consumed by this
specialist. It is designated as the imagery source for the schematic site-map specialist and for the
imagery-based follow-ups (Urban Context, Character Analysis) — recorded in 057's `plan.md` Follow-ups.

**Rationale**: the capture renders a real basemap; it carries no flood classification, exceedance probability,
wind direction, wind speed, topography, hydrology, ecology, noise, or air-quality dataset. Deriving any of those
from pixels would be inference, which the specification forbids (FR-014, Out of Scope). Imagery is not data.
