# Quickstart: Site Geometry Specialist

Every scenario below says **what to type**, **what to run**, and **what you should see**. Prompts are
quoted exactly; type them into Lucy's chat in the app. Commands are PowerShell from the repository root
unless stated otherwise.

## Before you start

1. **One-time setup** (skip if already done):

   ```powershell
   cd src/AskLucy.Web/ClientApp; npm install; cd ../../..
   dotnet build "Ask Lucy.sln"
   ```

   The API needs its development secrets configured as described in `README.md` → "Run it locally";
   the test-database connection string is already in the untracked
   `src/AskLucy.Web/appsettings.Development.json`.

2. **Run the app** (one process serves the API and the SPA):

   ```powershell
   dotnet run --project src/AskLucy.Web
   ```

   Open **http://localhost:5142** (or https://localhost:7170), sign in, and open a conversation.

3. **Where findings appear**: a short chat notice (`Receiving the site geometry…`) plus a floating
   panel titled `site geometry — <site name>`. The panel's last block is the provenance block.

> **Which prompt starts an analysis?** Lucy exposes the capability as *analyze this site*. All of these
> work, and each maps to a different scenario below:
>
> | You type | What it analyses |
> |---|---|
> | `Analyze this site` | the site currently active in the viewer |
> | `Run a full site analysis of Al Safa Park 2` | a site named in the prompt |
> | `Run a full site analysis at 25.0900, 55.2000` | an explicit point, with **no** resolved boundary |

---

## 1. Run the focused tests (no app required)

```powershell
dotnet test tests/AskLucy.Application.Tests --filter "FullyQualifiedName~SiteAnalysis"
dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~Overpass|FullyQualifiedName~SiteAnalysis|FullyQualifiedName~Boundaries"
```

Expected: both pass. The second includes `OverpassBoundaryCandidateProviderTests` — the regression
guard for the shared transport.

---

## 2. A site whose map data records a parcel outline (data-dependent)

**First check that a parcel exists there.** OSM parcels (`boundary=parcel`) exist mainly in countries
with cadastral imports; the UAE has almost none. Open <https://overpass-turbo.eu>, paste this query, and
press **Run** (change the coordinates to your area of interest):

```
[out:json][timeout:25];
(
  way(around:500,50.0870,14.4210)["boundary"="parcel"];
  relation(around:500,50.0870,14.4210)["boundary"="parcel"];
);
out geom;
```

If outlines come back, pick a **named** park, mall, school or campus inside or next to one, then:

1. Type `Show me <that site name> on the map` — this resolves the site and stores its boundary on the
   conversation.
2. Type `Analyze this site`.

**What you should see**

- Three notices over the next few seconds: `Receiving the site map…`, `Receiving the environmental
  context…`, `Receiving the site geometry…`.
- Open the **site geometry** panel and check:
  - **Measurements** — `Area` (m², plus hectares above 10,000 m²), `Dimensions` (m × m), `Orientation`
    (a compass direction with a bearing, or "not meaningful" for a near-square site).
  - **Frontage** — one row per named street, plus `Total frontage`; or the reason no named street is
    within 25 m.
  - **Boundary** — `Parcel boundary` reads `Recorded in OpenStreetMap (boundary=parcel)`, and
    `Measurement basis` reads `The site boundary resolved for this analysis`.
  - Provenance: `Analysis: SiteGeometry`, `Data source: OpenStreetMap (Overpass)`,
    `Confidence: High`.

If no parcel outlines come back in your area, skip to scenario 3 — the recorded-parcel path is covered
deterministically by `SiteGeometryToolTests` and `SiteGeometryCalculatorTests`.

---

## 3. A site with no parcel outline (any ordinary site)

1. Type `Run a full site analysis of Al Safa Park 2`.
2. Open the **site geometry** panel.

**What you should see**

- `Boundary → Parcel boundary`: `Not recorded in OpenStreetMap`.
- A line under **Boundary**: *"No parcel or plot boundary is recorded in OpenStreetMap for this site, so
  these figures describe the site boundary resolved for this analysis."*
- `Confidence: Medium` — the figures describe the resolved site boundary, not a recorded parcel.
- Measurements and frontage are present (frontage only if a named street is within 25 m).

---

## 4. A point-only site (no resolved boundary)

1. Type `Run a full site analysis at 25.0900, 55.2000`.
2. Open the **site geometry** panel.

**What you should see**

- **Measurements**, **Frontage** and **Boundary** each carry:
  *"No boundary was resolved for this analysis."* — not a zero, and not omitted.
- `Confidence: Low`.
- The site map and environmental findings still arrive, and the analysis completes.

---

## 5. The map source is unavailable

This proves the outage rule: boundary-derived figures are still delivered; only source-dependent ones
go unavailable.

1. **With Overpass working**, resolve a site first so the conversation has a stored boundary:
   type `Show me Al Safa Park 2 on the map`.
2. Stop the app (Ctrl+C), then restart it with every Overpass endpoint pointed at a dead host:

   ```powershell
   $env:Boundaries__Overpass__SearchBaseUrl       = "http://127.0.0.1:9/api/"
   $env:Boundaries__Overpass__MirrorBaseUrls__0   = "http://127.0.0.1:9/api/"
   $env:Boundaries__Overpass__MirrorBaseUrls__1   = "http://127.0.0.1:9/api/"
   dotnet run --project src/AskLucy.Web
   ```

3. In the conversation, type `Analyze this site` (it reuses the stored boundary, so no Overpass call is
   needed to resolve the site).

**What you should see**

- The geometry finding **still arrives**, with `Area`, `Dimensions` and `Orientation` measured from the
  stored boundary.
- `Frontage` and `Boundary → Parcel boundary` read `Unavailable — …` followed by the source reason.
- No error notice or panel for the geometry specialist; the analysis still completes.
- The app's console shows:
  `Site geometry for analysis …: the map source was unavailable (…)` (Information).

**Restore**: close that PowerShell window (the variables are per-process), then run the app normally.

---

## 6. Findings arrive independently

1. Type `Run a full site analysis of Al Safa Park 2`.
2. Watch the conversation timestamps.

**What you should see**: three notices arriving at different times — the environmental finding is
usually first, the geometry finding follows, the site-map capture varies with imagery latency. Nothing
arrives as one batch at the end. This is SPEC-057 SC-002 in production.

---

## 7. Content vocabulary and provenance

Open the geometry panel and inspect it visually, or assert it:

```powershell
dotnet test tests/AskLucy.Application.Tests --filter "FullyQualifiedName~ExecuteAsync_ShouldComposeOnlyTheExistingBlockKinds"
```

**What you should see**: only headings, text, key-value rows and dividers; **no image block**; the
provenance block is the last block and shows analysis type, data source, confidence and generation time.

---

## 8. Operator view: the record and the logs

Enums are stored as strings, so the queries read directly. These commands read the connection string from
the untracked dev settings and never print it.

```powershell
$c = (Get-Content 'src/AskLucy.Web/appsettings.Development.json' -Raw | ConvertFrom-Json).ConnectionStrings.DefaultConnection
$b = New-Object System.Data.Common.DbConnectionStringBuilder
$b.ConnectionString = $c

# Most recent analyses (grab an Id)
sqlcmd -S $b['Data Source'] -d $b['Initial Catalog'] -U $b['User Id'] -P $b['Password'] -C -Q `
  "SELECT TOP 5 Id, SiteName, Status, ExpectedResultCount, StartedAtUtc, CompletedAtUtc FROM SiteAnalyses ORDER BY StartedAtUtc DESC"

# That analysis's specialists
sqlcmd -S $b['Data Source'] -d $b['Initial Catalog'] -U $b['User Id'] -P $b['Password'] -C -Q `
  "SELECT AnalysisType, Status, DataSource, ConfidenceLevel, FailureReason FROM SiteAnalysisResults WHERE SiteAnalysisId = '<id>'"
```

**What you should see** after a normal run: three rows — `SchematicImage`, `EnvironmentalContext`,
`SiteGeometry` — each `Completed`; the geometry row with `DataSource = OpenStreetMap (Overpass)` and
`ConfidenceLevel = High` (parcel recorded) or `Medium` (site boundary used).

**Genuine failures** (unreadable analysis, unexpected exception) are recorded as `Failed` with a
`FailureReason` and logged by the relay as
`Specialist SiteGeometry failed for analysis …`. Inducing one live requires deleting or corrupting
records mid-run; the deterministic check is the unit suite:

```powershell
dotnet test tests/AskLucy.Application.Tests --filter "FullyQualifiedName~SiteGeometryToolTests"
```

It covers: analysis not found, provider exception, source outage (success with unavailable figures),
and caller cancellation (rethrown, nothing reported).

---

## Troubleshooting

- **The turn ends with "taking longer than expected (90s)" and no analysis is created.** The 90-second
  limit belongs to the `request_site_analysis` capability, and it fires during site resolution, before
  any specialist runs. The observed cause was the deciding model passing `{"site":"this site"}`: the
  capability geocoded the literal words, which started the whole boundary pipeline. It now treats
  "this site", "the current site", "this place" and "here" as the conversation's active site and uses
  the stored boundary, so the prompt `Analyze this site` returns immediately. If it recurs with a real
  place name, that name's boundary resolution is the slow part, not the geometry specialist.
- **Only two findings arrive (site map and environmental context).** The app is running a build from
  before this feature. `dotnet build` does not restart a running process: stop it (Ctrl+C) and run
  `dotnet run --project src/AskLucy.Web` again. On startup the console should show the site-analysis
  system workflow being upgraded to a new version.
- **The geometry finding arrives but the panel is not visible.** Check the panel dock/stack — findings
  open as floating panels and can be behind the viewer chrome or minimised.
- **Parcel scenarios look the same as "no parcel".** That is the data: `boundary=parcel` is largely
  absent outside countries with cadastral imports. Scenario 2 is data-dependent by design.

---

## Cleanup

- If you launched the app for scenario 5, close that shell before running it normally again.
- No configuration file is changed by any scenario; nothing to revert in source control.
