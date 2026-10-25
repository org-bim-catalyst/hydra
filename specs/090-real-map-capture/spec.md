# Feature Specification: Real Map Capture for the Site Map Specialist

**Feature Branch**: `090-real-map-capture`

**Created**: 2026-10-25

**Status**: Draft

**Input**: User description: "Implement the real-map capture for SPEC-057's schematic site-map specialist, per the 'Follow-ups' paragraph in specs/057-site-analysis-agent/plan.md that designates map capture as the imagery source for imagery-based specialists. Replace the AI-generated image with the platform's real map of the analysed site, using the existing server-to-server capture built in SPEC-083. Reuse SPEC-083 — no new provider, credential, or capture path; in-process through CaptureMapQuery, never the HTTP endpoint. Use the coordinates frozen on the persisted SiteAnalysis record, never the site name. Persist the returned bytes through DocumentUploadFinalizer and pass a fileId; never expose a provider URL. One image per finding, current content shape. Reuse the configured source, crop, zoom and view size. Carry attribution if the configured source requires it. Do not imply the imagery is live or dated. Report every capture failure through ISiteAnalysisResultRelay.ReportFailureAsync using MapCaptureFailureReason. Assign confidence through SiteAnalysisConfidence.ResolveConfidence from grounding facts. Decide explicitly between capture-replaces-generation and capture-with-generation-fallback, decide the fate of the versioned image prompt, and record both decisions. Wire-up additive: the analysis type stays SchematicImage; the provisioner branch and tool registration stay. Tests and docs are part of the change."

## Context

Ask Lucy can already run a **Site Analysis** ([specs/057](../057-site-analysis-agent/spec.md)): the user asks for an analysis of a site, a coordinating agent fans the site out to independent specialists, and each specialist reports its finding the moment it finishes. One of the two shipped specialists is the **schematic site map** — the one this feature changes.

**What ships today, and why it is labelled the way it is.** That specialist asks an AI image model to *draw* a top-down map of the site from a versioned prompt and persists the result as a Document. The drawing is an interpretation: it looks like a map, but no pixel of it was observed. That is exactly why its provenance reports **Medium** confidence — the site and its coordinates are exact, but the picture is generative. SPEC-057 recorded this as the finding's defining limitation and left the fix to a follow-up.

**The fix already exists in the platform.** SPEC-057's Follow-ups paragraph designates the map capture built in [specs/083](../083-viewer-map-capture/spec.md) as *the* imagery source for imagery-based specialists — this one, and the Urban Context and Character Analysis follow-ups — and says explicitly that replacing the generated image with a real capture is a follow-up change to that feature. SPEC-083 shipped: a server-to-server capture that takes coordinates, renders imagery around them through the administrator-configured source, crops and caps the image, and returns it **without persisting anything**. The platform therefore already has a way to obtain a real picture of the exact site. What it does not have is a finding that uses it.

**This feature closes that gap, and only that gap.** The specialist keeps its place in the fan-out, its name, its analysis type, its document persistence and its delivery route. What changes is inside the specialist: instead of asking a model to draw the site, it asks the platform's own capture for the site's real imagery and reports that.

Three consequences follow, and they organise the requirements below.

1. **The claim changes, so the label must change with it.** A real capture is not an interpretation. The current "Medium because it is generated" reasoning no longer applies, and confidence must be re-derived from the grounding facts the rule helper already defines — not preserved by inertia. The counter-question is whether an undated, non-live basemap capture truly *directly describes* the site; the answer, and its limits, are stated below.
2. **The finding must not overclaim.** The capture is the imagery provider's most recent available photograph of the area. It is not live, and it carries no capture date. A finding that lets a reader assume either is worse than the generated image it replaces, because a real photograph carries far more authority.
3. **Nothing may be invented to fill a failure.** When the capture cannot be produced, there is no second picture to fall back on. The failure is recorded, the user is told nothing about it (SPEC-057 FR-024's deliberate silence), and the finding is simply not delivered.

What this feature must **not** become is a second capture path, a second map provider, a second set of map credentials, a new specialist, or a server-side reading of the image's pixels. The image is **shown, not read**. Urban Context and Character Analysis are separate follow-ups with their own specifications; historical (dated) imagery is [specs/084](../084-historical-imagery-timeline/spec.md).

## Clarifications

### Session 2026-10-25

- Q: Capture replaces generation, or capture with generation as a fallback when no capture is available? → **A: Capture replaces generation. There is no fallback.** The justification is honesty first: a generated map that *looks like* the site, offered when the real capture failed, is the one failure a user cannot detect — and the user would reasonably believe they are looking at the place. A recorded, silent-to-the-user specialist failure is honest; a substituted drawing is not. Determinism reinforces it: a capture of the same coordinates is the provider's same imagery, reproducible and auditable, while a generated image differs on every run, so two analyses of one site would show two different "maps" and neither would be a record of the place. Cost reinforces it: the fallback would keep an image-model dependency, a second paid call, a second failure vocabulary, and a second confidence basis alive in a path that no longer needs any of them, to serve a case where the honest answer is "not available". No finding ever carries two images.
- Q: What happens to the versioned image prompt? → **A: Superseded and removed from the code; the contract file is retained with a Superseded note.** The prompt existed so a generated image would be reviewable. No generated image is produced any more, so the prompt constant and the generation call are deleted rather than left as dead code — an unreferenced prompt invites a future reader to believe generation still happens. The contract document stays as the historical record, marked Superseded, pointing at the new contract; SPEC-057's own superseded-not-deleted precedent ([tasks.md](../057-site-analysis-agent/tasks.md) T001/T002) is followed.
- Q: Which capture mode does the finding use? → **A: Satellite, named explicitly, with no overlays.** Satellite is what SPEC-083 returns when a caller names no mode, and it is the only mode the default configured source (Esri) can serve — naming roadmap, hybrid or buildings-only would be refused as `ModeUnavailable` under the default configuration, which is inventing a mode the platform's own default cannot produce. Satellite is also the imagery that shows the site's actual physical condition, which is this finding's purpose. The mode is named explicitly rather than left to the capture's default so a future change to that default cannot silently turn the finding into a labelled street map. Overlays are not requested: painting building footprints or markers onto the capture would be annotation, which is out of scope.
- Q: Which source, crop, zoom and view size? → **A: None is named; the capture's administrator-configured defaults apply.** SPEC-083 resolves the source, crop and zoom from the admin AI Tools settings, and the view size from the capture's own configured default (1 km neighbourhood scale), falling back to the catalogue's declared defaults when nothing was saved. Hardcoding any of them in the specialist would fork administrator control into a second place. Only the mode is named.
- Q: Where do the coordinates come from? → **A: The persisted SiteAnalysis record — the point frozen at dispatch.** The specialist reads latitude and longitude from the analysis it was dispatched for, in its own scope, and passes them to the capture with no place-name string. This is the same precedent SPEC-088 set. The name path is **not** used: it costs an extra geocoding call, and it can resolve a different place from the one being analysed — the analysis was started against a frozen point, and the picture must show *that* point. The finding's site name also comes from the record, so a stale tool input can neither move nor mislabel the image.
- Q: How is the capture reached? → **A: In-process, through the existing capture query (MediatR), inside the platform.** The HTTP endpoint `GET /api/v1/map-captures` exists for external callers; its service-API-key authentication and its rate-limit policy are controls for callers outside the trust boundary and do not apply, and do not belong, inside the platform. The specialist takes no HTTP hop, presents no service key, and does not consume the external rate-limit bucket.
- Q: The capture persists nothing — what about the image? → **A: The specialist persists the returned bytes as a platform Document, exactly as the generated image is persisted today.** The capture stays ephemeral (SPEC-083 FR-014); the Document is what makes the finding survive a reload. Ownership is the analysis's user, the write goes through the existing document upload finalizer (magic-byte validation, storage quota, checksum de-duplication), and the content references the document by platform file id only — never a provider URL, an external address, or a physical path.
- Q: Does the configured imagery source require attribution on the finding? → **A: Not today, and the finding must be able to carry it if that changes.** Checked rather than assumed: SPEC-083 imposes no attribution requirement on a current capture; SPEC-084's attribution requirement (its FR-015) is scoped to historical, dated images and does not apply here; and the source configuration carries no attribution setting. Neither configured path writes a credit that the current findings omit (the Esri export is unmarked, and the configured crop removes Google's branded overlay — [ADR 0020](../../docs/adr/0020-service-api-key-authentication-for-map-capture.md)). So no attribution line is added today. The finding still names the imagery source actually used in its provenance, and the capture reports that source; if a source whose terms require a credit is configured later, the finding carries it in its body text.
- Q: How is confidence decided now? → **A: Through `SiteAnalysisConfidence.ResolveConfidence` from grounding facts, and the result is High.** The capture is centred on the exact coordinates frozen at dispatch, with no geocoding and no name matching, so there is no location ambiguity; the pixels are a real record of the place rather than a rendering of a prompt; and nothing was interpolated, interpolated-from-nearby or approximated. That is `dataDirectlyDescribesSite: true` and `dataIsSparseOrApproximate: false` — High. An undated, non-live basemap capture **does** count as directly describing the analysed site: "directly describes" is about the data's relationship to *this site* (on-site versus nearby or generic), not about its currency. Undatedness limits the **temporal** claim only, and the finding states that limit explicitly, so the High label is not a claim that the image is current. Medium is not preserved: Medium requires the data to be sparse or approximate, and a direct capture is neither. `GeocodingCandidate.Importance` is never consulted — it is not even available on this path.
- Q: Does confidence vary with which imagery source served the capture? → **A: No.** Both configured sources return real imagery of the coordinates. Which one served the image changes the pixels' rendering, not whether the picture describes the site, so the confidence rule has no source-dependent branch.
- Q: What does the finding say about time? → **A: That the imagery is the provider's most recent available capture, that it is not live, and that it carries no capture date.** The body text and the image's alternative text both carry that limitation. No date is stated or implied. This mirrors the guidance the existing map-snapshot capability already gives Lucy.
- Q: How is a capture failure surfaced? → **A: As a recorded specialist failure, silently.** Every `MapCaptureFailureReason` is reported through `ISiteAnalysisResultRelay.ReportFailureAsync` with a reason string naming it and its diagnostic detail retained; nothing is delivered to the user (SPEC-057 FR-024), no placeholder or broken image is composed, and the tolerant merge keeps this branch's failure from sinking the other findings. No new exception type or failure vocabulary is introduced.
- Q: Does the analysis type, branch or client change? → **A: No.** The type stays `SchematicImage`, the specialist keeps its tool name and node key, its DI registration and its place in the shared fan-out declaration, the expected-result count is untouched, and the client needs no change. The only code touched outside the specialist is a read-only source report on the capture's in-process result and the user-facing display label for this analysis type.
- Q: Why does the user-visible label change? → **A: Because the image is no longer schematic.** The finding heading and the chat notice must not call a real photograph a "schematic" drawing. The heading becomes **Site Map** and the notice's display name becomes **site map**; the analysis type identifier on the wire stays `SchematicImage` for compatibility, so stored findings and clients are unaffected.
- Q: Does the schema of the tool input change? → **A: No.** The tool keeps `{siteAnalysisId, siteName, siteLocation}` so the provisioner's branch declaration is untouched. The name and location inputs are retained for the contract's shape only; the authoritative site name and the coordinates come from the analysis record.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The site-map finding shows the real place (Priority: P1)

A user asks Lucy to analyse a site. In the site-map finding, they see the platform's real satellite imagery of the site, centred on the point analysed, with the site's name — not a drawing of what a map of the site might look like. The provenance says where the imagery came from and how confident the finding is.

**Why this priority**: This is the feature. Everything else governs how the change behaves at its edges.

**Independent Test**: Run an analysis for a resolved site with imagery coverage. Open the site-map finding and confirm it shows a capture of that site, that the image resolves through the platform's document route, and that its provenance names the imagery source and reports High confidence. Compare against the previous build: no generated image is requested, and no image model is involved.

**Acceptance Scenarios**:

1. **Given** a running analysis of a site with coordinates, **When** the site-map specialist runs, **Then** it captures imagery for the analysis's frozen coordinates and the finding shows that image.
2. **Given** a successful capture, **When** the finding is delivered, **Then** its content is a heading, one body text, exactly one image block, and a provenance block last.
3. **Given** a successful capture, **When** the image is rendered, **Then** it resolves from a platform Document owned by the analysis's user, and no provider URL or external address appears anywhere in the finding.
4. **Given** a successful capture, **When** the provenance is read, **Then** it names this analysis type, the imagery source actually used, High confidence, and the generation time.
5. **Given** a successful capture, **When** the request to the capture is inspected, **Then** it carries the analysis's coordinates and no place-name string, so no geocoding occurs.

---

### User Story 2 - The finding is honest about what the image is and is not (Priority: P1)

The user reads the finding and knows the picture is the provider's most recent available imagery of the area — not a live view, and not dated. Nothing in the heading, body, alternative text or provenance suggests otherwise.

**Why this priority**: A real photograph is trusted more than a drawing. Allowing a reader to believe it is live or dated would make this feature less honest than the one it replaces.

**Independent Test**: Read the delivered finding's heading, body, alternative text and provenance. Confirm each states or supports the "most recent available, not live, no capture date" limitation, and that no date, freshness or liveness claim appears anywhere.

**Acceptance Scenarios**:

1. **Given** a delivered site-map finding, **When** the body text is read, **Then** it says the image is the platform's map capture of the site, centred on the site's coordinates, that it is the most recent available imagery, that it is not live, and that it carries no capture date.
2. **Given** a delivered site-map finding, **When** the image's alternative text is read, **Then** it meaningfully describes the map and the site, stays within the vocabulary's length limit, and makes no liveness or date claim.
3. **Given** a delivered site-map finding, **When** its heading and the chat notice are read, **Then** neither calls the real image "schematic"; the heading is **Site Map** and the notice names the finding **site map**, while the analysis type identifier remains `SchematicImage`.

---

### User Story 3 - A failed capture is recorded, never shown (Priority: P2)

When the imagery provider is down, the configured configuration cannot serve the request, the location has no coverage, or the image cannot be stored, the user is not shown a broken or empty finding. The failure is recorded with its reason and diagnostic detail so an operator can diagnose it, and the analysis still settles.

**Why this priority**: A missing finding is a known gap; a broken image or a fabricated substitute is not. This is the failure contract the specialist already owes SPEC-057.

**Independent Test**: Make the capture fail in each of its documented ways, run an analysis, and confirm each produces a recorded specialist failure naming that reason, no user-visible notice for the finding, no image block, and an analysis that still reaches its terminal state.

**Acceptance Scenarios**:

1. **Given** any of the eight documented capture failure reasons, **When** the specialist runs, **Then** it records a specialist failure naming that reason and delivers nothing.
2. **Given** a capture failure, **When** the analysis settles, **Then** the other specialists' findings are unaffected and the closing outcome reports the partial result as it does today.
3. **Given** a capture failure, **When** the user looks at the conversation, **Then** they see no failure notice for this finding, no placeholder image, and no "image unavailable" block.
4. **Given** a Document write that fails (storage allowance, validation, storage fault), **When** the specialist runs, **Then** it records a specialist failure rather than delivering a finding whose image cannot be loaded.
5. **Given** the analysis record cannot be read, **When** the specialist runs, **Then** it records a specialist failure and delivers nothing.

---

### User Story 4 - The image is a platform document like any other (Priority: P2)

The captured image is stored and served exactly like the generated one was: owned by the user, validated as an image, subject to their storage allowance, de-duplicated, and retrievable after a reload.

**Why this priority**: The finding must survive navigation and reload (SPEC-057 FR-018). Reusing the existing persistence path is what keeps that guarantee without new machinery.

**Independent Test**: Deliver a finding, navigate away, reload, and reopen the conversation. The same image is present and served from the platform's document route. Inspect the stored document: it is owned by the analysis's user and has the capture's bytes.

**Acceptance Scenarios**:

1. **Given** a successful capture, **When** the bytes are persisted, **Then** they go through the existing document upload finalizer, producing a Document owned by the analysis's user.
2. **Given** a capture whose bytes already exist as a document, **When** it is persisted, **Then** the existing de-duplication result is used and the finding still carries a valid file id.
3. **Given** a delivered finding, **When** the conversation is reopened after a reload, **Then** the image renders from the platform document route with no external request.
4. **Given** a capture, **When** the platform's persistence is inspected, **Then** no capture record, capture table or provider artefact exists: the Document is the only trace.

---

### User Story 5 - The change did not disturb the fan-out (Priority: P3)

Nothing outside the specialist changed shape. The analysis still dispatches the same two specialists, the same expected-result count, the same delivery route and the same client rendering.

**Why this priority**: SPEC-057 FR-031's extensibility claim is only meaningful if a change of imagery source stays inside one class.

**Independent Test**: Diff the change. Confirm no new analysis type, no provisioner branch change, no DI registration change, no expected-result-count change and no client change; confirm the specialist's tool name and node key are unchanged.

**Acceptance Scenarios**:

1. **Given** the change, **When** the fan-out declaration and provisioner are inspected, **Then** they are unchanged.
2. **Given** the change, **When** the specialist's DI registration and tool name are inspected, **Then** they are unchanged.
3. **Given** the change, **When** the client is inspected, **Then** it needs no change to render the finding.
4. **Given** the change, **When** a stored finding from before the change is rehydrated, **Then** it renders as it did before.

### Edge Cases

- **The analysis's coordinates are at the edge of imagery coverage.** The capture reports its documented failure reason; the specialist records it and delivers nothing.
- **The configured source cannot serve satellite mode.** Impossible for either currently supported source; if a future source cannot, the documented mode failure is recorded, never worked around by switching mode silently.
- **The administrator lowers the zoom below the source's range.** The capture reports the zoom failure; it is recorded, never replaced with a different zoom.
- **The configured crop is larger than the rendered frame.** The capture reports the crop failure; it is recorded.
- **The captured PNG exceeds the size cap.** The capture reports the size failure; it is recorded. Nothing partial is stored.
- **The site name in the tool input differs from the analysis record.** The record wins, for both the name and the coordinates; the image and the caption describe the analysed site.
- **Two analyses of the same site run concurrently.** Each captures and persists independently; neither finding references the other's document, and neither image is contaminated.
- **The same imagery bytes are produced twice.** The document finalizer's de-duplication result is reused; the finding still gets a valid file id.
- **The relaying of the finding fails after persistence.** The existing relay behaviour applies — the finding survives and rehydrates; the delivery failure is logged, never silently dropped.
- **Cancellation while capturing.** Cancellation propagates; it is not recorded as a specialist failure.
- **The imagery has no visible change since the last capture.** Nothing is claimed to be newer; the finding still reports no capture date.

## Requirements *(mandatory)*

### Imagery source

- **FR-001**: The site-map finding MUST show the platform's **real map capture** of the analysed site. No AI-generated image is produced, requested, or shown in this finding, and there is no generated fallback.
- **FR-002**: The capture MUST be obtained **in-process through the platform's existing capture service** (SPEC-083's `CaptureMapQuery`, via MediatR). The specialist MUST NOT call the external HTTP capture endpoint, and MUST NOT add a map provider, a provider credential, a second capture path, or a new capture configuration.
- **FR-003**: The capture MUST be centred on the **latitude and longitude frozen onto the persisted `SiteAnalysis` record at dispatch**, read in the specialist's own scope. The specialist MUST NOT obtain imagery through the site name or any place-name string: a name-based capture costs an extra geocoding call and can resolve a different place from the one being analysed. The finding's site name MUST also come from the record.
- **FR-004**: The specialist MUST name **no** imagery source, crop, zoom, or view size, so the administrator-configured capture defaults (SPEC-083) apply. None of those values may be hardcoded in the specialist, and none may be resolved a second time outside the capture.
- **FR-005**: The capture MUST request **satellite mode** and **no overlays**. The mode MUST be named explicitly. Rationale: satellite is the capture's own default for an unnamed request and the only mode the default source (Esri) can serve; naming another mode would be refused under the default configuration. Overlays or markers would annotate the image, which is out of scope.
- **FR-006**: No overlays, markers, pins, site outlines, or annotations of any kind may be added to the capture by the specialist.

### Persistence

- **FR-007**: The capture MUST remain ephemeral, as SPEC-083 requires. The returned PNG bytes MUST be persisted as a **platform Document owned by the analysis's user**, through the existing document upload finalizer — the same path the generated image used — so magic-byte validation, storage-allowance enforcement, checksum de-duplication and ownership apply unchanged.
- **FR-008**: The finding's image block MUST reference the persisted document by **platform file id**. A provider URL, an external address, a data URL, or a physical file path MUST NOT appear in the finding, the result metadata, or any client-facing payload.
- **FR-009**: The Document MUST be the only persistence this change adds. No capture record, table, migration, or stored provider artefact may be introduced.
- **FR-010**: A document-write failure (storage allowance, type validation, storage fault) MUST be handled as a specialist failure, never delivered as a finding whose image cannot load.

### Content

- **FR-011**: The finding's content MUST keep its current shape: one heading, one body text, **exactly one image block**, and the provenance block **last**. A finding MUST NOT carry two images.
- **FR-012**: The body text MUST identify the image as the platform's map capture of the site, centred on the site's coordinates, and MUST state that the imagery is the imagery provider's **most recent available capture, that it is not live, and that it carries no capture date**. It MUST NOT state or imply a capture date, a live view, or currency "as of today".
- **FR-013**: The image's alternative text MUST meaningfully describe the map and the site for assistive technology, MUST stay within the content vocabulary's length limit, and MUST make no liveness or date claim.
- **FR-014**: The heading MUST be **Site Map**, and the chat notice's user-facing display name for this finding MUST be **site map**. Neither may describe the real image as "schematic". The analysis type identifier on the wire MUST remain `SchematicImage`.
- **FR-015**: The provenance block MUST be complete: the analysis type, the **imagery source actually used**, the confidence level, and the generation time. It MUST resolve those values from the capture rather than from a second read of administrator configuration.

### Attribution

- **FR-016**: The finding MUST carry the attribution the configured imagery source requires. For the current configuration it requires none: SPEC-083 imposes no attribution requirement on a current capture, SPEC-084's attribution requirement is scoped to historical dated imagery and does not apply, and the source configuration carries no attribution setting. The finding MUST therefore carry no attribution line today, but MUST name the imagery source in its provenance so the credit is identifiable, and MUST carry a source's required credit in its body text if a source that requires one is configured later.
- **FR-017**: The capture's in-process result MUST report **which imagery source served the image**, so provenance is correct by construction. This is the one additive change inside SPEC-083's code: a read-only field on the query result, set on success. It changes no provider, no endpoint behaviour, no wire contract, no credential and no capture path. Resolving the administrator's settings a second time from the specialist is rejected — it would resolve the same value twice and could disagree with the capture if the configuration changed in between.

### Failure

- **FR-018**: Every capture failure MUST be reported as a **specialist failure** through `ISiteAnalysisResultRelay.ReportFailureAsync`, using the existing `MapCaptureFailureReason` vocabulary — `LocationNotFound`, `GeocodingUnavailable`, `ModeUnavailable`, `OverlayUnavailable`, `ZoomUnavailable`, `ProviderUnavailable`, `CropTooSmall`, `ImageTooLarge`. The recorded reason MUST name the failure reason and retain its diagnostic detail. No new exception type or failure vocabulary may be introduced.
- **FR-019**: The failure mapping MUST be **total**: every reason the capture can return has a recorded outcome. No reason is ignored, and none is converted into a successful finding.
- **FR-020**: The specialist MUST NOT deliver an empty, partial, broken, or placeholder finding. On any failure it composes no content, references no document, and delivers nothing to the user.
- **FR-021**: Any other failure — the analysis record cannot be read, the document write fails, or the capture throws unexpectedly — MUST likewise be recorded as a specialist failure. Only cancellation may propagate.
- **FR-022**: A capture failure MUST NOT prevent the analysis from settling, and MUST NOT affect the other specialists' findings.

### Confidence

- **FR-023**: Confidence MUST be assigned through `SiteAnalysisConfidence.ResolveConfidence` from **grounding facts only**. `GeocodingCandidate.Importance` MUST NOT be consulted — it is not available on this path and is on incompatible scales across providers.
- **FR-024**: A successful capture MUST resolve to **High** confidence: `dataDirectlyDescribesSite: true` and `dataIsSparseOrApproximate: false`. The reasoning is that the frame is centred on the exact coordinates frozen at dispatch with no geocoding or name matching, the pixels are a real record of the place rather than a rendering of a prompt, and nothing was interpolated or approximated. An undated, non-live capture still directly describes the site: undatedness limits the temporal claim, which FR-012 states explicitly, not the spatial one. Medium is not preserved.
- **FR-025**: Confidence MUST NOT depend on which imagery source served the capture.

### Prompt and code disposition

- **FR-026**: The versioned image prompt is **superseded**. The prompt constant and the image-generation call MUST be removed from the specialist and from the code that builds the prompt, leaving no unreferenced prompt in the codebase. The contract document MUST be retained and marked **Superseded**, with a pointer to this feature's contract, as the historical record. It MUST NOT be retained as a fallback and MUST NOT be silently deleted from the specification record.
- **FR-027**: The image-generation service itself MUST NOT be removed, changed, or bypassed elsewhere; it has other consumers. Only the specialist's use of it is removed.

### Wire-up

- **FR-028**: The analysis type MUST remain `SchematicImage`. The specialist's **tool name** (`site_analysis_schematic_image`), **node key** (`schematic_image`), **DI registration**, and **place in the shared fan-out declaration** MUST remain unchanged, so the provisioner's branch and the expected-result count are untouched.
- **FR-029**: The tool's input schema MUST remain `{siteAnalysisId, siteName, siteLocation}`. The inputs keep the contract's shape; the authoritative name and coordinates come from the analysis record (FR-003).
- **FR-030**: The tool's description and documentation MUST describe the real capture, not generation. Its required permissions MUST remain the existing external-network and file-write permissions.
- **FR-031**: The client MUST require no change. An already-stored finding from before this change MUST continue to render unchanged.
- **FR-032**: This change MUST NOT add a dependency, a configuration key, or a database migration.

### Tests

- **FR-033**: Tests MUST accompany the behaviour in this change, mirroring the existing site-analysis tool and relay tests. They MUST cover:
  1. **Success**: the capture's bytes are persisted through the document finalizer, the image block carries the resulting file id, the content is heading/text/one image/provenance in order, and the provenance is complete, including the imagery source actually used.
  2. **Attribution**: the finding carries what the configured source requires — none today — and names the source in provenance.
  3. **Every failure reason**: each `MapCaptureFailureReason` produces a recorded specialist failure naming that value, with no delivery and no user-visible notice.
  4. **Coordinates**: the capture receives the analysis record's coordinates and no place-name string, even when the tool input carries a different name.
  5. **Confidence**: a successful capture resolves to the level the stated rule produces (High), and the rule is exercised from grounding facts rather than from any importance-like value.
  6. **Document failure**: a document-write failure is recorded as a specialist failure and nothing is delivered.
  7. **Storage and reuse**: the persisted document is owned by the analysis's user; a duplicate capture reuses the de-duplication result.
- **FR-034**: Tests MUST NOT require a live imagery provider or a live map service. The capture and its failures are faked at the application boundary.

### Documentation

- **FR-035**: `specs/057-site-analysis-agent/plan.md` MUST be updated: the Follow-ups paragraph that designates map capture as the imagery source and describes the generated image must record that this change implements it and link this specification.
- **FR-036**: `specs/057-site-analysis-agent/tasks.md` MUST be updated so its follow-up notes point at this change and no longer describe the specialist as generating its image.
- **FR-037**: The schematic image contract (`specs/057-site-analysis-agent/contracts/schematic-image-prompt.md`) MUST be marked Superseded and point at this feature's contract; this feature MUST supply that contract, recording the capture's parameters, content shape, provenance, confidence rule, attribution position and failure mapping.
- **FR-038**: The architecture documentation that describes the finding as a generated image MUST be updated to describe the real capture, and the decision and its rationale (capture replaces generation; prompt superseded; High confidence from direct description; no attribution required today) MUST be recorded there or in this specification.

### Key Entities

- **Site map finding**: the analysis result of type `SchematicImage` — heading, body, one image, provenance. Its label is a map of the site; it is the visible output of this change.
- **Map capture**: the platform's ephemeral still image of a location (SPEC-083). Here it is always satellite, centred on a frozen site point, with the administrator's source, crop, zoom and view size.
- **Imagery source**: the configured provider that served the capture (currently Esri or Google). It determines the pixels, the provenance label, and whether an attribution credit is required.
- **Platform document**: the persisted PNG the finding references. Owned by the analysis's user; the only trace of the capture.
- **Site analysis record**: the aggregate whose frozen coordinates and site name the finding must describe. It is the authority for both, never the tool input.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of delivered site-map findings show imagery produced by the platform's map capture; zero findings use an AI-generated image.
- **SC-002**: 100% of captures are centred on the coordinates frozen on the analysis record; zero findings show imagery obtained from a place name or geocoded text.
- **SC-003**: 100% of delivered findings reference their image by platform file id, and zero findings contain a provider URL, external address, or physical path.
- **SC-004**: 100% of capture failures produce a recorded specialist failure naming one of the eight documented reasons, and zero failures produce a user-visible finding, notice, or placeholder for this specialist.
- **SC-005**: 100% of delivered findings state that the imagery is the most recent available capture, is not live, and carries no capture date; zero findings state or imply otherwise.
- **SC-006**: 100% of delivered findings carry complete provenance, including the imagery source actually used; 100% of findings that a configured source requires a credit for carry it.
- **SC-007**: 100% of delivered findings contain exactly one image block, with the provenance block last.
- **SC-008**: 100% of successfully captured findings report High confidence; zero report Medium or Low without a stated grounding failure.
- **SC-009**: Adding the change required no modification to the fan-out declaration, provisioner, expected-result count, specialist registration, analysis-type enum, or client: zero files changed in those areas.

## Assumptions

- SPEC-083 is shipped and its capture query is available for in-process dispatch inside the platform; its external endpoint authentication and rate limiting are outside the trust boundary and are not used.
- The persisted `SiteAnalysis` record always carries a site name and a latitude/longitude pair, frozen at dispatch (SPEC-057's aggregate invariant).
- The capture returns PNG bytes or a typed failure — never both, never neither — and those bytes are already cropped and within the size cap on success.
- The existing document pipeline accepts PNG and enforces ownership, storage allowance, magic-byte validation and de-duplication; SPEC-057's earlier verification of PNG support still holds.
- The capture is read-only: the Document is the only persistence, as SPEC-083 FR-014 requires.
- The specialist runs inside the existing workflow branch and reports inline through the relay, exactly as it does today (SPEC-057 research D3).
- The imagery is the provider's most recent available capture. No date is available from this path, so none is claimed; dated imagery belongs to SPEC-084.
- Both currently supported imagery sources return real imagery of the requested coordinates, so confidence does not vary by source.

## Out of Scope

- **Urban Context** and **Character Analysis** — the other imagery-based specialists. Each has its own specification; this change establishes the imagery pattern only for the site map.
- **Changes to the capture endpoint or its providers**, beyond the read-only source report on the in-process result (FR-017). No new provider, credential, mode, routing rule, rate limit or authentication path.
- **Historical imagery** ([specs/084](../084-historical-imagery-timeline/spec.md)) — dated captures, timelines, and their attribution requirements.
- **Overlays, markers, pins, site outlines, or annotations** on the capture.
- **Any server-side analysis of the captured pixels** — no classification, measurement, feature extraction, or derived figure may be read from the image. The image is shown, not read.
- **A generated-image fallback** — explicitly rejected (Clarifications).
- **A second image, a side-by-side comparison, or a gallery.**
- **Renaming the analysis type** away from `SchematicImage`, or any migration of stored findings.
- **Changes to the client** to render the finding.
