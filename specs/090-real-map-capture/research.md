# Research: Real Map Capture for the Site Map Specialist

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

Phase 0 decisions, each verified against shipped code rather than assumed. The pattern is the
same as SPEC-057's and SPEC-088's: a decision, its rationale, and what was rejected.

## D1 — Dispatch the capture in-process through `CaptureMapQuery`, from an isolated scope

**Decision**: The specialist resolves `ISender` from a fresh `IServiceScopeFactory`-created scope
and sends `CaptureMapQuery` with `CallerName: "site-analysis:{siteAnalysisId}"`. It never calls
`GET /api/v1/map-captures`.

**Rationale**: SPEC-083 already ships the capture as a CQRS query; the HTTP controller is a thin
adapter over it. In-process dispatch reuses the exact pipeline — geocoding bypass, default
resolution, renderer routing, crop, size cap, failure taxonomy — without an HTTP hop, an API key,
or a rate-limit bucket, none of which apply inside the trust boundary. The isolated scope is not
optional: the capture's handler depends on `IToolSettingsReader` → `IToolSettingRepository` →
`DbContext`, and parallel workflow branches share the job's scoped `DbContext`
(`WorkflowExecutionOrchestrator`), which is not safe for concurrent use — the same hazard
`SiteEnvironmentalContextTool` and `ScopeIsolatedSiteAnalysisResultRelay` already document. The
analysis read, the capture, and the document write all use that one scope.

**Alternatives considered**:
- *HTTP call to the endpoint* — rejected: it would require a service API key inside the platform,
  would consume the external caller's rate-limit bucket, and would add a network hop and a
  serialization boundary to an in-process call.
- *Injecting `ISender` into the tool constructor* — rejected: the tool is resolved from the
  branch's scope, so its sender would execute the handler against the shared `DbContext`.
- *Calling the handler class directly* — rejected: it bypasses MediatR's validation pipeline and
  hard-codes the concrete handler, the same reason every other caller uses the query.

## D2 — Coordinates from the persisted record; the name path is not used

**Decision**: Read `Latitude`/`Longitude` (and `SiteName`) from the `SiteAnalysis` record obtained
by id, in the isolated scope, and pass `Location: null` to the query.

**Rationale**: The analysis was dispatched against a point frozen at dispatch. `CaptureMapQuery`
gives coordinates precedence, so this path performs no geocoding and resolves no candidate. A
name-based capture would cost an extra geocoding call — the very call the analysis already made —
and could resolve a different place (a different `Al Safa Park`, a street instead of a park), so
the picture would not be of the analysed site. The record's name is used for the heading, body
and alt text for the same reason: a stale tool input must neither move nor mislabel the image.
`SiteEnvironmentalContextTool.LoadSitePointAsync` is the precedent.

**Alternatives considered**:
- *Read `latitude`/`longitude` from the tool input* — rejected: the input schema is fixed at
  `{siteAnalysisId, siteName, siteLocation}` and the provisioner passes names; adding coordinates
  would change the workflow input contract and create a second source of truth.
- *Pass both the name and the coordinates and let the capture choose* — rejected: it works today
  only because coordinates win; it stores the trap that a future precedence change reverses.

## D3 — Name only the mode; reuse the administrator's source, crop, zoom and view size

**Decision**: `Mode: "satellite"`, `Overlays: null`, `Source: null`, `CropPixels: null`,
`Zoom: null`, `ViewSizeMetres: null`.

**Rationale**: `CaptureMapQueryHandler` resolves absent source, crop and zoom through
`IToolSettingsReader.GetMapCaptureDefaultsAsync` (admin AI Tools settings, with catalogue defaults
for anything never saved), and resolves an absent view size from `MapCaptureOptions`
(default 1000 m). Naming any of them would fork administrator control into a second place and
make the specialist drift from the snapshot capability. The mode is named because it is **not**
administrator-controlled and because the default source (Esri) serves satellite only —
`EsriSatelliteImageRenderer` refuses any other mode with `ModeUnavailable` — so naming roadmap,
hybrid or buildings-only would guarantee failure under the default configuration. Naming
satellite explicitly also prevents a future change of the capture's own default from silently
turning the finding into a labelled street map. No overlays: painting building footprints or
markers would annotate the capture, which the feature excludes.

**Alternatives considered**:
- *Name `satellite` implicitly by passing `Mode: null`* — rejected: it produces the same image
  today but makes the finding's mode depend on a default elsewhere.
- *Name the source as Esri to match the default* — rejected: it defeats the administrator setting.

## D4 — Capture replaces generation; there is no generated fallback

**Decision**: The specialist makes one capture call. It does not call `IImageGenerationService`
on success or failure.

**Rationale**:
- **Honesty (decisive).** A generated map is not of the site; it is a plausible picture of what a
  map of the site might look like. Delivered as a fallback, it is the one failure a user cannot
  detect: they asked to see the place and would reasonably believe they are seeing it. A
  specialist failure that is recorded and silent is honest; a substituted drawing is not.
- **Determinism.** A capture of the same coordinates is the provider's same imagery —
  reproducible, auditable, and comparable between two analyses of one site. A generated image
  differs on every run, so the two findings would show two different "maps" and neither would be
  a record of the place.
- **Cost and complexity.** The fallback would keep an image-model assignment, a paid generation
  call, a second failure vocabulary and a second confidence basis alive for a case whose honest
  answer is "not available". It would also make the finding's confidence depend on which path ran,
  so one analysis type would carry two different grounding claims.

**Alternatives considered**:
- *Capture with generation as fallback* — rejected for the reasons above.
- *Capture plus a clearly labelled generated illustration alongside it* — rejected: two images in
  one finding, and a drawing next to a photograph invites the reader to treat it as comparable.

## D5 — Persist the bytes as a platform `Document`, exactly as the generated image was

**Decision**: Persist the capture PNG through `DocumentUploadFinalizer.FinalizeAsync` with
`ownerId = analysis.UserId`, actor `system:site-analysis-map-capture`, filename
`site-analysis-site-map-{guid}.png`; commit with `IUnitOfWork`; reference the document id in the
image block.

**Rationale**: SPEC-083 FR-014 makes a capture ephemeral and deliberately stateless — it has no
storage port. The finding, by contrast, must survive navigation and reload (SPEC-057 FR-018).
`DocumentUploadFinalizer` is the platform's one path for turning bytes into an owned, validated,
quota-counted, de-duplicated document, and it is what the generated image already used. The
extension must be `.png` because the validator sniffs magic bytes and rejects a misnamed PNG
(`MapSnapshotCapability` records this). The relay already requires a non-null document for
`SchematicImage` (`AnalysisTypesRequiringDocument`), so the write must commit before reporting
success. Ownership is taken from the analysis record, the aggregate's own owner.

**Alternatives considered**:
- *Stream the bytes to the client* — rejected: the capture is ephemeral by design, the finding
  must rehydrate, and the content vocabulary's image block accepts a platform file id only.
- *Persist a capture record* — rejected: out of scope and contrary to SPEC-083's read-only rule;
  the Document is the only needed trace.
- *Keep the current owner (`context.UserId`)* — rejected: the record is authoritative and the
  context is workflow bookkeeping.

## D6 — Confidence is High, by the rule helper, from grounding facts

**Decision**: `SiteAnalysisConfidence.ResolveConfidence(dataDirectlyDescribesSite: true,
dataIsSparseOrApproximate: false)` → `High`.

**Rationale**: The rule's first parameter asks whether the data specifically and directly
describes *this* site, as opposed to a nearby or generic value. A capture centred on the exact
coordinates frozen at dispatch, with no geocoding and no candidate selection, is on-site by
construction; the pixels are a real record of the place; and nothing was interpolated,
substituted from a nearby point, or generated. The second parameter is therefore false. **An
undated, non-live capture still counts as directly describing the site**: "directly describes"
concerns the data's spatial relationship to the site, not its currency. Undatedness limits the
**temporal** claim, which the finding states explicitly (FR-012), so High is not a claim that the
image is current. Medium would require the data to be sparse or approximate, which a direct
capture is not — preserving Medium would keep a label whose stated reason (generative
interpretation) no longer exists. `GeocodingCandidate.Importance` is never consulted; the path
performs no geocoding and the helper cannot accept an importance-shaped value (SPEC-057 research
D10).

**Alternatives considered**:
- *Keep Medium for safety* — rejected: it would be an unexplained downgrade and would misstate
  the data's grounding.
- *Vary confidence by imagery source* — rejected: both sources return real imagery; the source
  changes rendering, not whether the picture describes the site.

## D7 — Failure: total mapping over the existing vocabulary, recorded and silent

**Decision**: On `!IsSuccess`, report through
`ISiteAnalysisResultRelay.ReportFailureAsync(siteAnalysisId, SchematicImage,
$"{result.FailureReason}: {result.FailureDetail}", cause: null, ct)` and return
`AgentToolResult.Failure(...)`. On any exception, report with the exception as the cause. Compose
no content and write no document. Rethrow cancellation only.

**Rationale**: `MapCaptureFailureReason` is the capture's own taxonomy and each value is already
logged by the handler; inventing a second vocabulary would split diagnosis. Every value is mapped
— `LocationNotFound`, `GeocodingUnavailable`, `ModeUnavailable`, `OverlayUnavailable`,
`ZoomUnavailable`, `ProviderUnavailable`, `CropTooSmall`, `ImageTooLarge` — even though the
coordinate/no-overlay path makes some unlikely, because administrator configuration (zoom, crop,
source) and provider health can produce the rest, and a partial mapping would be a latent silent
failure. SPEC-057 FR-024 makes the user silence deliberate: the relay persists the failure and
logs it with structured context but pushes no notice or panel. The tolerant `AnyCompleted` merge
keeps one branch's failure from sinking the analysis.

**Alternatives considered**:
- *Translate the reason into user-facing copy* — rejected: nothing is shown to the user; the
  reason is diagnostic.
- *Return `AgentToolResult.Success` with `reported = false`* — rejected: it would mask the
  failure in the workflow's audit trail and in the merge behaviour.

## D8 — The prompt is superseded, removed from code, retained in the record

**Decision**: Delete `SiteAnalysisPrompts` (its only consumer is this tool). Mark
`specs/057-site-analysis-agent/contracts/schematic-image-prompt.md` Superseded with a pointer to
this feature's contract. Do not retain a fallback prompt.

**Rationale**: A versioned prompt exists so a generated image is reviewable. No generated image
is produced any more, so the prompt constant is dead code and the contract is history. This
follows the repository's own precedent: SPEC-057's `SiteAnalysisOptions` tasks (T001/T002) were
reverted but left in `tasks.md` struck through with the reason, and the prompt contract itself
already carries a "Superseded (2026-09-18)" note for an earlier settings section. Deleting the
document would erase the record of what shipped; leaving the code would invite a reader to think
generation still happens.

**Alternatives considered**:
- *Retain the prompt as a fallback* — rejected with D4.
- *Delete the contract file too* — rejected: git preserves the text, but the specification record
  should say what replaced it and why.

## D9 — The capture reports the source it used

**Decision**: Add `MapCaptureSource? Source { get; init; }` to `MapImageResult`, set by
`CaptureMapQueryHandler` on success (`result with { ResolvedLocation = locationName, Source =
source }`). The specialist's `DataSource` becomes `"map-capture:{wire}"` (for example
`map-capture:esri`) via `MapCaptureSourceWire.WireValueOf`.

**Rationale**: The specialist deliberately names no source, so the capture is the only component
that knows which provider served the image. Naming the provider in provenance is what makes the
provenance complete and the attribution requirement satisfiable (D11). Reading
`IToolSettingsReader.GetMapCaptureDefaultsAsync` a second time from the specialist was rejected:
it would resolve the same configuration twice, could disagree with the capture if an administrator
saved a change between the two reads, and would leak a capture concern into the tool. The field is
additive and read-only; the endpoint does not serialize `MapImageResult` (it writes PNG bytes plus
headers derived from the request), the renderers already receive the source in `MapImageRequest`,
and no credential or path changes.

**Alternatives considered**:
- *`DataSource = "map-capture"` with no provider* — rejected: it hides which source produced the
  pixels and cannot carry a source-specific credit.
- *Have the tool name the source explicitly so it knows it* — rejected with D3.
- *Change the endpoint's headers to report the source* — rejected: that is an external contract
  change for an internal need.

## D10 — User-visible label changes, the identifier does not

**Decision**: The finding heading becomes **Site Map**; `FormatAnalysisTypeName` returns **site
map** for `SchematicImage`. The enum member, stored rows, API shape and client are unchanged.

**Rationale**: The image is no longer schematic. Calling a real photograph a "schematic" drawing
in the panel heading and the chat notice is the same inaccuracy this change removes. The enum is
a stable identifier used by the API (serialized as a string) and by stored results, so it must not
change; a display label is not the identifier. The relay change is one line in a shared file, and
no test asserts the old string.

**Alternatives considered**:
- *Leave both labels* — rejected: the finding would misdescribe its own image.
- *Rename the enum member* — rejected: it would break stored findings, the API contract and the
  client, for no user benefit.

## D11 — Attribution: none required today, and the finding can carry it if that changes

**Decision**: No attribution line is added. Provenance names the imagery source. If a configured
source's terms require a credit, it goes in the body text.

**Rationale**: Checked, not assumed. SPEC-083 contains **no** attribution requirement — its
success criteria cover crop, size, presence/absence of trace, authentication, the satellite
default, geocoding rank and viewer independence. SPEC-084 FR-015 requires attribution for
*historical images* and is scoped to that feature's dated path, which this is not. The source
configuration (`MapCaptureDefaults`) has no attribution field. Neither current renderer writes a
credit the finding omits: ADR 0020 records that the Esri export is "without a burned-in logo", and
that Google's branded overlay is "what the crop removes". Reporting the source in provenance is
therefore the accurate, sufficient statement. The conditional requirement keeps the door open
without inventing a credit today.

**Alternatives considered**:
- *Add a generic "Imagery: Esri" line* — rejected: not required by any source, and it would
  become wrong the moment an administrator switches the configured source.
- *Assume attribution is never required* — rejected: the source is configurable and a future one
  may require it; the spec states the condition and the reporting field makes it satisfiable.

## D12 — The non-live, undated limitation is stated in the finding and the alt text

**Decision**: The body text says the imagery is the provider's most recent available capture, is
not live, and carries no capture date. The alt text describes the map and the site without a
liveness or date claim.

**Rationale**: `MapSnapshotCapability.UsageGuidance` already states the platform's position: "The
imagery is the provider's most recent capture of the area; it is not live and it does not carry a
capture date." A site-analysis finding is more authoritative than a chat snapshot, so the
limitation must be more explicit there, not less. SPEC-084 FR-004/FR-005 forbid deriving a capture
date from a release date, a "most recent" label or the request date; this path has no acquisition
metadata at all, so it states none. The body is plain text, so the statement is available to
assistive technology without relying on a visual cue.

**Alternatives considered**:
- *Say nothing and rely on provenance* — rejected: provenance says the source and confidence, not
  the imagery's currency.
- *Say "recent imagery" only* — rejected: "recent" is a currency claim the platform cannot
  substantiate.

## D13 — The class name is kept; its documentation is rewritten

**Decision**: `SiteSchematicImageGenerationTool` keeps its name and its `Name` value
(`site_analysis_schematic_image`); its XML documentation and `Description` are rewritten.

**Rationale**: The tool name is the stable identity the published system workflow references
(`{"toolName":"site_analysis_schematic_image"}`), and the class name mirrors its tool name in this
codebase conventionally. Renaming the class would change the DI registration line the feature is
required to leave alone, for a readability gain only. The documentation change is where the
behaviour change is recorded.

**Alternatives considered**:
- *Rename to `SiteMapCaptureTool`* — rejected: it touches the DI registration and the test file
  name without changing any observable behaviour; the tool name would remain the old spelling
  anyway.
