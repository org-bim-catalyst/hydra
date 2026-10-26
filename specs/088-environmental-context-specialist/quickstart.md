# Quickstart: Environmental Context Specialist (SPEC-088)

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | Contracts: [tool](./contracts/environmental-context-tool.md),
[provider seam](./contracts/provider-seam.md)

Runnable validation scenarios that prove the feature end to end. Details of shape and rules live in the
contracts and [data-model.md](./data-model.md); this file only says how to check them.

## Prerequisites

- .NET 10 SDK, the repository restored (`dotnet restore`).
- SQL Server reachable with the configured connection string.
- For persistence-backed tests: `PERSISTENCE_TESTS_CONNECTION_STRING` set (they are destructive against a dev
  database; set `PERSISTENCE_TESTS_DEDICATED_DATABASE=1` only deliberately).
- No frontend build is required for the feature itself; run the client checks only as part of the repo-wide gate.

## Build and automated checks

```bash
dotnet build
dotnet test tests/AskLucy.Application.Tests --filter FullyQualifiedName~SiteAnalysis
```

Expected:
- `EnvironmentalContextToolTests` green, including both providers unavailable, partial grounding, all figures
  grounded, each distinct outcome, a provider exception, and coordinate sourcing from the analysis record.
- `SiteAnalysisProgressiveDeliveryTests` green, including the real two-specialist case that could not be
  constructed before this feature.

Repo-wide gate (when preparing to merge):

```bash
dotnet format --verify-no-changes
dotnet test        # with PERSISTENCE_TESTS_CONNECTION_STRING set
```

Known local noise: `dotnet format` reports `ENDOFLINE` false positives on this repository; verify against CI
rather than chasing them. Ensure `using System.*` directives come first.

## Scenario 1 — Two findings from one analysis

1. Start the application (`dotnet run --project src/AskLucy.Web`) and sign in.
2. Put a site in the viewer, then ask Lucy to analyse it ("analyze this site").
3. Expect an acknowledgement first, then **two** findings arriving independently: the schematic map and the
   environmental context. Neither waits for the other.

Expected environmental panel: heading "Environmental Context"; a short framing line; "Flood risk" with flood-zone
classification and annual exceedance probability; "Climate and wind" with prevailing wind direction and average
wind speed; each of the four stating **"No data source available"** with a reason; a "Sun and shadow" block
pointing at the viewer's Solar Analysis; a "Not assessed" block; provenance last with data source
"no data source configured" and confidence **Low**.

## Scenario 2 — The analysis counts both specialists

After one analysis completes, inspect the record (SQL Server):

```sql
SELECT a.Id, a.ExpectedResultCount, a.Status, r.AnalysisType, r.Status, r.ConfidenceLevel, r.DataSource
FROM SiteAnalyses a
JOIN SiteAnalysisResults r ON r.SiteAnalysisId = a.Id
WHERE a.Id = '<analysis id>';
```

Expected: `ExpectedResultCount = 2`; exactly two result rows, `SchematicImage` and `EnvironmentalContext`; the
analysis `Status = Completed` only after both rows exist. A second finding must never be `Rejected` with a
reason mentioning an analysis that is no longer running.

## Scenario 3 — Nothing invented, nothing omitted

From the stored `ContentJson` of the environmental result, confirm:

- All four figures are present as `keyValue` items; none is missing.
- No figure carries a number that did not come from a provider.
- No block kind outside `heading`, `text`, `keyValue`, `divider` appears.
- No occurrence of sun position, sunrise, sunset, day length, or shadow data anywhere in the content.
- The "Not assessed" statement is present and does not read as an unavailable figure.

## Scenario 4 — Failure is captured, the analysis survives

Temporarily make one provider throw (substitute it in a test, or break its registration in a dev build) and run
an analysis:

1. No environmental finding is delivered; the schematic finding still is.
2. The analysis still reaches a completed state with a partial-completion closing outcome.
3. `SiteAnalysisResults` contains an `EnvironmentalContext` row with `Status = Failed` and a populated
   `FailureReason`.
4. The structured log carries the analysis id, site, specialist, and user id for that failure.

Revert the temporary change.

An *unavailable* provider is not this scenario: it must still deliver a finding (Scenario 1).

## Scenario 5 — Sunrise is referenced, not computed

1. With the environmental panel open, open the viewer's Solar Analysis from its toolbar.
2. Confirm the environmental panel's "Sun and shadow" text points at that analysis and does not restate any of
   the figures it shows.
3. Confirm the finding does not claim those figures belong to the analysed site when the viewer shows a
   different one.

## Scenario 6 — Findings survive a reload

Navigate away from the conversation and return, or reload the page. Both panels must reopen with content and
provenance identical to first delivery. The environmental panel's rehydration title uses the stored analysis
type; content is replayed, never recomposed.

## Scenario 7 — No new configuration, no new migration

- `appsettings*.json` gains no setting for this feature.
- `dotnet ef migrations has-pending-model-changes` (or the repo's equivalent check) reports none: the enum member
  is stored in an existing string column.
- The map-capture endpoint is not called by this specialist; nothing in the finding references an external URL.
