# Data Model: Environmental Context Specialist (SPEC-088)

**Date**: 2026-10-25 | **Plan**: [plan.md](./plan.md)

No new aggregate, table, or column. A finding is a `SiteAnalysisResult` owned by the existing `SiteAnalysis`
aggregate ([specs/057 data-model.md](../057-site-analysis-agent/data-model.md)); this feature adds an analysis
type and the values that flow into that result.

---

## Enum addition — `SiteAnalysisType`

`src/AskLucy.Domain/SiteAnalysis/SiteAnalysisEnums.cs`

| Member | Notes |
|---|---|
| `SchematicImage` | existing |
| `EnvironmentalContext` | **new** — this specialist |

Stored as a string (`SiteAnalysisResultConfiguration`: `HasConversion<string>().HasMaxLength(40)`); the new member
is 20 characters. **No migration.** String serialization API-wide is already guaranteed by the global enum
converter — never compare numerically on the client.

---

## Fan-out declaration — `SiteAnalysisSpecialists` (not persisted)

`src/AskLucy.Application/SiteAnalysis/SiteAnalysisSpecialists.cs`

| Field | Type | Notes |
|---|---|---|
| `ToolName` | `string` | `site_analysis_schematic_image`, `site_analysis_environmental_context` |
| `NodeKey` | `string` | `schematic_image`, `environmental_context` — stable, unique within a version |
| `All` | `IReadOnlyList<…>` | the declaration; read by the provisioner and the capability |
| `Count` | `int` | the number of branches dispatched — the analysis's `ExpectedResultCount` |

Rules:
- `Count` **is** the analysis's expected-result count; no other value may be used.
- The provisioner publishes exactly one branch per entry; the node config template is unchanged.
- Adding a specialist is one entry here plus one `IAgentTool` class — nothing else.

---

## Figure model

The specialist reports four figures, defined entirely by the two existing provider records. No field is added,
derived, or renamed.

| Figure | Provider record field | Unit / form | Presented as |
|---|---|---|---|
| Flood zone | `FloodData.FloodZoneClassification` (`string?`) | classification label | text |
| Annual exceedance probability | `FloodData.AnnualExceedanceProbabilityPercent` (`double?`) | percent | number + `%` |
| Prevailing wind direction | `ClimateData.PrevailingWindDirectionDegrees` (`double?`) | degrees, 0–360 | number + `°` |
| Average wind speed | `ClimateData.AverageWindSpeedMetersPerSecond` (`double?`) | metres/second | number + `m/s` |

### Figure state (per figure, derived)

| State | Condition | Panel value | Grounded? |
|---|---|---|---|
| `Value` | outcome `Confirmed` and field non-null and in range | the value | yes |
| `NotReported` | outcome `Confirmed` and field null | "Not reported by the source" | no |
| `OutOfRange` | value outside the figure's meaningful range (e.g. direction outside 0–360) | "The source returned an unusable value" | no |
| `NoCandidates` | provider outcome `NoCandidates` | "No data available for this location" + reason | no |
| `Ambiguous` | provider outcome `Ambiguous` | "Conflicting data for this location" + reason | no |
| `Unavailable` | provider outcome `Unavailable` | "No data source available" + reason | no |

Invariant: a figure is **never** omitted from the panel, and no state produces an estimated value.

---

## Finding content document

`{"version": 1, "blocks": [...]}` — emitted by `SiteAnalysisContentComposer.ComposeSections`, existing block
kinds only, provenance block **last** (FR-011).

| # | Block | Content |
|---|---|---|
| 1 | `heading` (level 2) | "Environmental Context" |
| 2 | `text` | one-line framing: the site, and that figures with no source say so |
| 3 | `divider` + `heading` | "Flood risk" |
| 4 | `keyValue` | the two flood figures, each in its state from the table above |
| 5 | `divider` + `heading` | "Climate and wind" |
| 6 | `keyValue` | the two climate figures |
| 7 | `divider` + `heading` | "Sun and shadow" |
| 8 | `text` | reference to the viewer's Solar Analysis, for the site currently shown in the viewer; no figures |
| 9 | `divider` + `heading` | "Not assessed" |
| 10 | `text` | the dimensions this release does not model (FR-041), distinct from unavailable figures |
| 11 | `keyValue` | provenance: analysis type, data source, confidence, generated time — **last** |

Prohibitions: no `image`, `table`, `chart`, or `metric` block; no external address; no solar figure; no numeric
value that did not come from a provider.

---

## Confidence

Assigned once per finding by `SiteAnalysisConfidence.ResolveConfidence`, never from a geocoder score.

| Grounded figures (of 4) | `dataDirectlyDescribesSite` | `dataIsSparseOrApproximate` | Level |
|---|---|---|---|
| 4 | `true` | `false` | `High` |
| 1–3 | `false` | `true` | `Medium` |
| 0 | `false` | `false` | `Low` |

The solar reference does not count toward coverage (FR-024).

---

## Provenance and failure

| Field (existing `SiteAnalysisResult`) | Value |
|---|---|
| `AnalysisType` | `EnvironmentalContext` |
| `DataSource` | `"no data source configured"` while no figure is grounded; a truthful source identifier when one exists (FR-030) |
| `ConfidenceLevel` | from the table above |
| `ContentJson` | the document above |
| `DocumentId` | always `null` — this specialist produces no file |
| `FailureReason` | set only when the specialist failed (provider exception, analysis record missing); never for an unavailable figure |

---

## Validation rules (from the specification)

- A figure with no configured source is **rendered**, never omitted (FR-010).
- `Unavailable`, `NoCandidates`, `Ambiguous`, `NotReported`, and `OutOfRange` are five distinct statements and may
  not be conflated (FR-011–FR-015).
- The finding names the unassessed dimensions, and that statement is not an unavailable figure (FR-041/FR-042).
- The solar reference names the site the viewer's analysis applies to and contains no solar figure (FR-021–FR-025).
- No image block, no external address, no new block kind (FR-026/FR-027).
- Provider half of each seam is called once per run (FR-040); no call scales with the site or user.
