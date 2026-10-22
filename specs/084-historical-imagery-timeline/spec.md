# Feature Specification: Historical Imagery Timeline

**Feature Branch**: `084-historical-imagery-timeline`

**Created**: 2026-10-22

**Status**: Draft

**Input**: User description: "Extend the map snapshot capability (shipped in specs/083-viewer-map-capture) so Lucy can return the aerial imagery of a place for a chosen time, not only the most recent imagery. A user gives no date, one date, or a start and end date, and gets the current snapshot, a single dated vintage, or an ordered set of vintages with gaps visible. A timeline / carousel UI steps through a site's vintages. The time dimension is a property of the imagery source, not of the platform: a time-enabled source (Esri World Imagery Wayback, with its paired acquisition-date metadata layer as the only authority for a capture date) is the leading candidate, and it must sit behind the existing provider router rather than alongside it. A Wayback release date is a publication date, not an acquisition date. Tiles are stitched into one image and a missing tile fails the whole image. Every historical image carries the source's attribution. Images are persisted as platform documents so a chat message survives a reload. Vintages per request and tiles per image are bounded, and the existing per-caller rate limit applies. Out of scope: video/animation export, ground-level historical imagery, historical imagery for locations other than the requested point, and writing historical images into knowledge bases."

## Context

Ask Lucy can already photograph a place from the air and put the picture in the chat ([specs/083](../083-viewer-map-capture/spec.md)) — a "map snapshot". That picture is always the provider's most recent available capture. It is a photograph of now, and it carries no date: the imagery providers return pixels, not a record of when the aircraft flew.

This feature adds the time dimension. "Show me Al Safa Park 2 as it was in June 2023" should return the June 2023 photograph, labelled with the date that photograph was actually taken. "What did Al Safa Park 2 look like between 2020 and 2024" should return the ordered set of available photographs across those years, with the uncovered stretches visible as gaps rather than quietly closed up.

Three facts shape the whole specification:

1. **The live providers have no history.** Google Static Maps and Esri World Imagery serve the current basemap only. A date in the request cannot be honoured by them, and neither reports an acquisition date. So the date-less path stays exactly as it is, and the dated path needs a source that is time-enabled by nature.
2. **A release date is not an acquisition date.** The leading time-enabled source publishes its archive as a series of *releases*. Each release republishes the whole world, but only part of the world is actually re-flown in any one release, so the release's publication date is systematically wrong as a capture date. The real acquisition date is published separately, in a metadata layer that reports it for a point. That layer is the only authority this feature will accept.
3. **A historical image is assembled from tiles, not fetched as one picture.** The current providers export a single framed image; the historical source serves tiles. Fetching and joining them is the feature's real work, and a tile that is missing must fail the whole image rather than leave a hole in the sky.

This feature **extends** the existing capture pipeline. The time dimension becomes an input to a capture request, routed to a time-enabled renderer behind the same provider router that already serves every other capture. A second, parallel "historical renderer" that bypasses the router would be a second drifting implementation of something the platform already has.

## Clarifications

### Session 2026-10-22

- Q: Which source(s) serve historical imagery? → A: Esri World Imagery Wayback is the only time-enabled source in this release. Google Static Maps and the current Esri World Imagery basemap remain the date-less sources and are unchanged. The Wayback tile service and its paired acquisition-metadata layer are consumed through the existing provider router.
- Q: Which date is authoritative? → A: Only the acquisition-metadata layer. A Wayback release date may be shown, but only ever explicitly as the date the archive release was published, never as the date the photograph was taken.
- Q: How are the tiles of a vintage combined? → A: Fetched as a grid for the requested extent, joined into a single image using the platform's existing image-composition library. If any tile in the grid cannot be fetched, the whole image fails with an explicit error — no partial image is ever delivered.
- Q: Are the images persisted or streamed? → A: Persisted as platform documents, the same way the current snapshot is persisted, so a chat message survives a reload. A timeline is one durable reference to its set of images.
- Q: What bounds the cost of a request? → A: A bounded number of vintages per request, a bounded number of tiles per image, and a per-caller rate limit no looser than the one that already applies to captures. Each of these is a configured value, not a hardcoded literal.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A picture of a place today (Priority: P1)

A user asks Lucy for a picture of a place from the air and gets one image, exactly as they do today. The image is the provider's most recent available capture of the area, and Lucy's words say so. Lucy does not state a date for it, because the provider does not report one and Lucy will not invent one.

**Why this priority**: This is the path almost every user takes, and it already works. The feature's first duty is to leave it alone — including its honesty, which is easy to break by habit once dates are in the product.

**Independent Test**: Ask for a snapshot of a place with no date. Confirm exactly one image comes back, and confirm the reply contains no capture date and does not claim the image shows the site "as of today".

**Acceptance Scenarios**:

1. **Given** a user asks for an aerial picture of a place and names no date, **When** Lucy answers, **Then** exactly one image is returned and the reply states that the imagery is the provider's most recent available capture of the area.
2. **Given** the same request, **When** the reply is read, **Then** it contains no capture date, no "as of today", and no other wording that promises a date the provider did not report.
3. **Given** the existing date-less snapshot behaviour, **When** this feature ships, **Then** that behaviour is unchanged — the same single image, the same capture settings, the same failure wording.

---

### User Story 2 - A picture of a place as it was on a date (Priority: P1)

A user asks for a place as it looked on a given date — "show me Al Safa Park 2 in June 2023". Lucy returns the closest available photograph of that period, labelled with the date that photograph was actually taken, and says so plainly when the closest photograph is not in the month the user asked for.

**Why this priority**: It is the feature in one sentence, and it is the case that depends on the honesty rules being right — a wrong date here is worse than no image at all.

**Independent Test**: Ask for a place and a specific month, and confirm the returned image is labelled with a real acquisition date and that the reply states where that date sits relative to the requested month.

**Acceptance Scenarios**:

1. **Given** a user asks for a place on a specific date, **When** imagery for that period is available, **Then** one image is returned, labelled with the real acquisition date of the photograph used.
2. **Given** the closest available photograph falls before the requested month, **When** Lucy answers, **Then** the reply says the nearest available imagery is from that earlier date rather than presenting it as the requested month.
3. **Given** the closest available photograph falls after the requested month, **When** Lucy answers, **Then** the reply says the nearest available imagery is from that later date rather than presenting it as the requested month.
4. **Given** no photograph is available anywhere near the requested date, **When** Lucy answers, **Then** she says plainly that there is no imagery for that period and offers the nearest available photograph, clearly labelled with its real date, rather than quietly returning a different date.
5. **Given** a user asks for a date range that is only partly covered, **When** the range is returned, **Then** every returned image carries a real date and the uncovered stretches are visible as gaps.

---

### User Story 3 - Every photograph of a place across a range (Priority: P2)

A user asks for the history of a place over several years — "what did Al Safa Park 2 look like between 2020 and 2024". Lucy returns every available photograph of that place in that period, oldest first, each labelled with its real date, with the periods that no photograph covers visible as gaps.

**Why this priority**: A timeline is the shape of the data, and the one a site-development conversation actually needs. It depends on the single-date path, so it comes after it.

**Independent Test**: Ask for a multi-year range and confirm the images arrive in chronological order, each carries a real date, and the stretches between photographs are visible as gaps rather than closed up.

**Acceptance Scenarios**:

1. **Given** a user asks for every photograph of a place between two dates, **When** the answer arrives, **Then** the images are ordered oldest first and each is labelled with its real acquisition date.
2. **Given** the range spans a period with no coverage, **When** the answer arrives, **Then** that period appears as a visible gap, not as a silently skipped stretch.
3. **Given** the range would return more photographs than the platform will return at once, **When** the answer arrives, **Then** the result is truncated to the most recent photographs in the range and the reply states plainly that the range was truncated.
4. **Given** a range request for a place with no historical coverage at all, **When** Lucy answers, **Then** she says plainly that there is no imagery for that period.

---

### User Story 4 - Stepping through a site's history (Priority: P2)

A user who has been given a site's history can walk through it: move to the previous or next photograph, drag a scrubber along the time axis, or pick from a strip of thumbnails, and always read the date of the photograph currently on screen.

**Why this priority**: It is what turns a pile of images into a history the user can read. It is worthless without User Stories 2 and 3, and it is where the accessibility obligations bite.

**Independent Test**: Open a history in the chat on a phone-width viewport and on a desktop, and step through it with the keyboard alone, checking that the date of the shown photograph is readable and that every control announces what it does.

**Acceptance Scenarios**:

1. **Given** a history of several photographs, **When** the user moves to the next or previous photograph, **Then** the displayed image changes and the date of the newly displayed photograph is shown.
2. **Given** the user is on the first photograph, **When** they ask for the previous one, **Then** nothing moves and the control is unavailable rather than wrapping around silently.
3. **Given** the user drags the time scrubber, **When** they release it, **Then** the photograph nearest that point in time is shown, together with its real date.
4. **Given** the user operates the history with the keyboard only, **When** they move through it, **Then** every control receives focus in a visible order, every control announces what it does, and the date of the current photograph is available to a screen reader.
5. **Given** the history is open on a phone-width screen, **When** the user uses it, **Then** the controls remain reachable and the current date remains readable without horizontal scrolling of the page.
6. **Given** the history is shown, **When** a period with no coverage sits between two photographs, **Then** the gap is visible in the history itself, labelled as a gap and not conveyed by colour alone.
7. **Given** the platform is running in English or in Arabic, **When** the history is shown, **Then** every label, control name and date format is presented in that language.

---

### User Story 5 - A place with no history (Priority: P3)

A user asks for the history of a place that was never photographed, or not photographed in the period they asked about. Lucy says so plainly and does not offer a picture that is not from that period.

**Why this priority**: It is the failure path, and it is the one where the feature could most easily mislead — by showing the current aerial photograph as though it were the historical one. It is P3 only because it is rare; it is not optional.

**Independent Test**: Ask for the history of a place with no coverage and confirm the reply is a plain statement of no coverage, with no image presented as historical.

**Acceptance Scenarios**:

1. **Given** a place with no historical coverage, **When** the user asks for its history, **Then** Lucy says plainly that there is no imagery for that period.
2. **Given** the same request, **When** the answer arrives, **Then** no image is shown, and in particular the current aerial photograph is not offered as though it were the requested period.
3. **Given** a place with no historical coverage and the user asks for a single dated photograph, **When** the answer arrives, **Then** no photograph of another period is substituted for the one that was asked for.

---

### Edge Cases

- A date is given that no photograph matches at all, and no nearby photograph exists: the request is refused with a plain "no imagery for that period", never with an empty timeline.
- The requested date falls inside a coverage gap: the nearest photograph on either side is returned, and which side it came from is stated.
- A photograph is available but the source reports no acquisition date for it: it is not presented as though it were dated. See FR-004.
- The archive contains releases for a place that do not change the imagery there: those releases do not become separate entries in a history, so a history is a list of genuine changes, not a list of publications.
- A range is so wide it would return more images than the platform will return at once: the result is truncated to the most recent images in the range and the truncation is stated.
- A range is entirely in the future: there is no imagery, and the reply says so rather than returning the most recent photograph.
- The tile grid for the requested extent cannot be fetched in full: the whole image fails with an explicit error; no partial image is delivered and no hole is left in the picture.
- The imagery source is unavailable, times out, or refuses a request: the user is told the source is temporarily unavailable, and no image is delivered.
- The composition of a single image exceeds the platform's per-image size cap: the request is refused with an explicit error and nothing partial is delivered.
- Storing a whole history would exceed the requesting user's storage allowance: the request fails with an explicit message about the allowance, and the images already written are not left behind as orphans.
- The user reloads the page or reopens the conversation later: the history is still there, in order, with its dates.
- The user asks for a range in a language other than English or Arabic: the dates and labels are still produced and the history is still readable.
- A place name resolves to a different place than the user meant: the history is of the place that was matched, and the reply names it, so the user can see which place they got.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support returning the aerial imagery of a place for a requested time, as an extension of the existing map snapshot capability. A request that names no date MUST behave exactly as it does today: one image, no timeline, and no change to the capture settings, the persistence, or the failure wording.
- **FR-002**: The time dimension MUST be an input to the existing capture request and MUST be served through the existing provider router. The system MUST NOT introduce a second capture pipeline, a second router, or a second image-composition path alongside the one that already serves every other capture.
- **FR-003**: The date-less sources (the current Esri World Imagery basemap and Google Static Maps) MUST continue to serve every date-less capture unchanged, and MUST continue to be the fallback for the current snapshot. The dated path MUST be additive.
- **FR-004**: The acquisition date of a historical image MUST be read from the imagery source's acquisition-date metadata for the requested point. The system MUST NOT derive a capture date from a release or publication date, from a provider's "most recent" label, from the request date, or from the current date. Where the source reports no acquisition date for a vintage, the system MUST NOT present that vintage as dated: the vintage MUST still appear in the history, positioned by its release date, and MUST be labelled explicitly as undated — hiding available imagery would be a silent omission.
- **FR-005**: Where the source also reports a release or publication date, the system MAY show it, but MUST label it as the date the archive release was published, MUST NOT label it as a capture date, and MUST NOT present it as the date of the photograph.
- **FR-006**: A request for a specific date MUST return the vintage whose acquisition date is closest to the requested date, and MUST state that vintage's real acquisition date.
- **FR-007**: A request for a specific date MUST state which side of the requested date the returned vintage falls on whenever the returned vintage is not in the same month as the request.
- **FR-008**: When no vintage is within the configured closeness threshold of the requested date, the system MUST say plainly that no imagery is available for the requested period and MUST offer the nearest available vintage, clearly labelled with its real date. The system MUST NOT silently substitute a different date.
- **FR-009**: A request for a date range MUST return every available vintage in that range, ordered oldest first, each labelled with its real acquisition date.
- **FR-010**: A request for a date range MUST make every uncovered period visible as a gap. A gap MUST NOT be closed up, and MUST NOT be represented only by a visual cue that depends on colour.
- **FR-011**: All vintages in one history MUST be captured at the same centre point, the same view size, and the same framing level, so that they are directly comparable with one another.
- **FR-012**: The tiles of a vintage MUST be joined into a single image. If any tile of the requested grid cannot be fetched, the whole image MUST fail with an explicit error. The system MUST NOT deliver a partial image, and MUST NOT leave a hole or a placeholder in a delivered image.
- **FR-013**: The number of tiles joined into one image MUST NOT exceed a configured maximum (default 64). The framing level MUST be reduced to the highest level whose grid fits within that maximum, and a request whose extent cannot be framed within the maximum at any usable level MUST be refused with an explicit error.
- **FR-014**: The number of vintages returned by one request MUST NOT exceed a configured maximum (default 10). A range that exceeds it MUST be truncated to the most recent vintages in the range, and the reply MUST state plainly that the range was truncated.
- **FR-015**: Every historical image MUST carry the imagery source's attribution, including the provider and licence attribution the source requires, and that attribution MUST be rendered in the UI that surrounds the image. The attribution MUST also be stored with the image so that it travels with the image when the image is opened, downloaded, or shared.
- **FR-016**: Every historical image MUST be persisted as a platform document owned by the requesting user, following the same lifecycle, ownership, access control, and storage-allowance rules as any other user document.
- **FR-017**: A history MUST remain complete and readable after the page is reloaded or the conversation is reopened later. The conversation message MUST carry a durable reference to the history and to every image in it.
- **FR-018**: The history UI MUST be operable with the keyboard alone. Every control MUST have an accessible name that describes what it does, the current photograph's date MUST be available to assistive technology, focus MUST be visible, and the interaction MUST NOT be drag-only.
- **FR-019**: The history UI MUST work at phone width without the page scrolling horizontally, and MUST remain usable with both the light and the dark theme.
- **FR-020**: Every new user-facing string MUST exist in both the English and the Arabic catalogs, and the history's dates and labels MUST be presented in the user's language.
- **FR-021**: The reply for a dated request MUST NOT state a capture date that the system has not read from the acquisition metadata.
- **FR-022**: A place with no coverage for the requested period MUST produce a plain statement of no coverage. The system MUST NOT return an empty history and MUST NOT present a current image as though it were a historical one.
- **FR-023**: Historical capture requests MUST be subject to a per-caller rate limit that is no looser than the limit that already applies to captures, and the limit MUST be enforced on the request as a whole, not on each internal provider call separately.
- **FR-024**: Every failure MUST reach the user as a visible message the user can act on — whether the failure is a location that cannot be found, a source that is unavailable, a missing tile, an image that is too large, or a storage allowance that has been reached. No failure may be swallowed, and no failure may be reported as a successful capture.
- **FR-025**: The system MUST NOT introduce a new third-party package unless the platform genuinely cannot deliver the capability without it, and any such addition MUST be justified in the plan.
- **FR-026**: The system MUST NOT write historical images into knowledge bases, MUST NOT produce video or animation, and MUST NOT request ground-level or street-level historical imagery. A history covers the requested point only.

### Key Entities *(include if data involved)*

- **Historical Vint**: One available photograph of a place at a point in time — a single dated image, its real acquisition date, the release it was published in, the provider and sensor that captured it, and its resolution and accuracy. The acquisition date is the vintage's defining fact.
- **Imagery Coverage**: The span of time a place is covered by at least one vintage. A period between two consecutive vintages that no vintage covers is a **gap**.
- **History**: An ordered set of vintages for one place over a range, sharing one centre, one view size and one framing level, together with the gaps between them and the source attribution. It is the unit a user sees, stores and re-opens.
- **Acquisition Metadata**: The source's own record of when, by whom and at what accuracy a piece of imagery was captured, for a given point. It is the only authority the platform accepts for a capture date.
- **Attribution**: The provider and licence credit the imagery source requires, carried by every historical image and rendered wherever the image is shown.
- **Historical Image Document**: The stored form of one vintage's image, owned by the requesting user, subject to the platform's normal document lifecycle and storage allowance.
- **Source Attribution Line**: The date, provider and credit shown beside a historical image, distinguishing the date the photograph was taken from the date the archive release was published.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of date-less snapshot requests return exactly one image, and 100% of their replies contain no capture date and no claim that the image shows the site as of a particular day.
- **SC-002**: 100% of historical images shown to a user are labelled with a date read from the source's acquisition metadata; 0 historical images are labelled with a release or publication date as though it were a capture date.
- **SC-003**: 95% of single-date historical requests return an image or an explicit refusal within 15 seconds of the request.
- **SC-004**: 95% of range requests covering up to the maximum number of vintages return within 45 seconds of the request.
- **SC-005**: 100% of range requests return their vintages in chronological order, and 100% of uncovered periods in the returned range are visible as gaps.
- **SC-006**: 0 partial images are delivered: every historical image that reaches a user is complete, and every failure to assemble one produces a visible message.
- **SC-007**: 100% of requests for a period with no coverage return a plain statement of no coverage, and 0 current images are presented as historical.
- **SC-008**: 100% of truncated range requests state the truncation in the reply.
- **SC-009**: 100% of historical images carry the source's attribution, both in the surrounding interface and in the stored form of the image.
- **SC-010**: A history opened from a reloaded page or a reopened conversation shows the same vintages, in the same order, with the same dates, as it did before the reload.
- **SC-011**: The history is fully operable with the keyboard alone, every control has an accessible name, and the current photograph's date is exposed to assistive technology — verified by automated accessibility checks in the continuous-integration pipeline.
- **SC-012**: 100% of new user-facing strings for this feature exist in both the English and the Arabic catalogs, verified by the catalog-completeness check already in the pipeline.
- **SC-013**: 100% of historical capture requests respect the per-caller rate limit, measured as 0 requests served beyond the limit.
- **SC-014**: 100% of failures in the historical path reach the user as a visible message; 0 failures are swallowed or reported as success.
- **SC-015**: The existing date-less snapshot behaviour is unchanged by this feature: the current snapshot's own test suite passes without modification.
- **SC-016**: 0 new third-party packages are introduced unless the plan records a justification that the platform cannot deliver the capability without one.

## Assumptions

- **The only time-enabled source in this release is Esri World Imagery Wayback**, consumed together with its paired acquisition-metadata layer. Google Static Maps and the current Esri World Imagery basemap have no history and remain the date-less sources. A second time-enabled source would be an infrastructure addition, not a change to this feature's shape.
- **The archive publishes a release roughly monthly**, and only part of the world is re-flown in any one release, so the number of releases that matter for a given point is far smaller than the number of releases published. A history lists genuine changes at the requested point, not publications.
- **The nearest-vintage threshold is six months.** A requested date whose nearest vintage falls within six months is served with that vintage and a note of which side it came from; beyond six months the reply says plainly that no imagery is available for that period and offers the nearest available vintage instead.
- **A vintage whose acquisition date the source does not report is kept, not dropped.** It appears in the history positioned by its release date and labelled explicitly as undated, because the alternative — quietly removing imagery that exists — is the same silent omission the constitution forbids. It never contributes a date to the history's date axis.
- **A single dated request returns one image**, in the same bubble form the current snapshot uses, with the date and attribution shown beside it. The stepping-through history is what a range request produces.
- **A history is at most ten vintages by default**, and each vintage is one stored image. The limit is a configured value so a deployment can raise or lower it, and the storage allowance still applies on top of it.
- **An image is at most 64 tiles** by default, and the framing level is reduced automatically to fit that budget, so the tile cost of a history grows with the number of vintages and not with the requested view size.
- **Historical images are stored using a photographic encoding** so that a whole history stays within the requesting user's storage allowance, while the per-image size cap that already applies to a capture continues to apply to each image.
- **Every historical image is a separate document owned by the requesting user**, subject to the platform's normal document lifecycle: the same access control, the same soft delete, the same storage allowance, and the same ability to be downloaded or opened.
- **A history's reference is durable**: the conversation message carries a reference that resolves to the history and to each of its images, so the client re-resolves fresh access URLs on every render, exactly as it does for a single snapshot today.
- **The existing geocoding, framing and image-composition facilities are reused**; no new provider, no new geocoder, and no new composition path is introduced.
- **The existing per-caller capture rate limit continues to apply** to the historical path, with a tighter per-request ceiling for historical work because one request costs many provider calls.
- **The reply wording rules are already carried by the snapshot capability's own usage guidance** — the imagery is the provider's most recent capture, and it does not carry a capture date. This feature extends that guidance rather than restating it in the model prompt.
- **Out of scope**: video or animation export of a history; ground-level or street-level historical imagery; historical imagery for any location other than the requested point; writing historical images into knowledge bases; and any per-vintage export, print or share-sheet workflow beyond the existing image download.
