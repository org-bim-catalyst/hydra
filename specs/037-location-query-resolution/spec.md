# Feature Specification: Location Query Resolution

**Feature Branch**: `037-location-query-resolution`

**Created**: 2026-08-23

**Status**: Draft

**Input**: User description: "When the user asks about a specific location in chat (e.g. \"show me Al Safa Park 2\"), Lucy should recognize the location intent, resolve the place to a confident lat/lon coordinate and place name, and confirm it to the viewer by emitting a ConfirmedLocationData payload on the AI stream. The frontend infrastructure for receiving this is already in place (activeLocationStore.setFromAgent, __LOCATION__ SSE event, ViewerSurface re-centering). What is missing is the backend agent logic: a tool or system-prompt mechanism that makes Lucy detect location queries, geocode the place, and populate ConfirmedLocationData on the ChatStreamChunk when confidence is sufficient. The viewer should update in real time as the agent streams its response."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Naming a Known Place Recenters the Viewer (Priority: P1)

A user asks Lucy about a specific, well-known real-world place in the middle of a normal chat message (e.g. "show me Al Safa Park 2" or "what's the site context around Zabeel Park?"). Lucy recognizes this as a request to view a place, resolves it to a single confident location, confirms the place name and coordinates back to the user in her reply, and the viewer recenters on that location before the user has to ask again or take any extra action.

**Why this priority**: This is the core value of the feature and the exact gap called out in the request — the frontend already knows how to react to a confirmed location, but nothing on the backend currently produces one. Without this story, the entire location-aware viewer experience described in specs 035/036 stays inert.

**Independent Test**: Can be fully tested by sending a chat message naming a single, well-known, unambiguous real-world place and verifying that (a) Lucy's reply text confirms the resolved place name, and (b) the viewer recenters on the correct coordinates within the same conversation turn, with no follow-up prompt required.

**Acceptance Scenarios**:

1. **Given** an active chat session, **When** the user sends a message naming a specific real-world place with no reasonable ambiguity, **Then** Lucy's streamed reply confirms the resolved place name and the viewer recenters on the matching coordinates before the response finishes.
2. **Given** the same session, **When** the user asks about a second, different well-known place in a later message, **Then** the viewer updates to the new location and the previously active location is replaced.
3. **Given** a location has just been confirmed, **When** the user's next message does not mention a place, **Then** the viewer stays on the previously confirmed location (no unrelated reset).
4. **Given** a location has just been confirmed, **When** the user sends a simple back-reference message (e.g. "zoom in on it", "center on that place") without naming the place again, **Then** Lucy re-emits the already-active location's confirmed data without performing a new geocoding lookup.

---

### User Story 2 - Passing Mention Does Not Move the Viewer (Priority: P2)

A user mentions a place name in chat without actually asking to see, visit, or navigate to it (e.g. "I read that Al Safa Park was renovated last year" or "compare the parking ratio to what Zabeel Park has"). Lucy should not treat every mention of a place as a navigation request — the viewer must only move when the user's message actually expresses intent to view or locate that place.

**Why this priority**: Without this distinction, the feature would be noisy and untrustworthy — the viewer would jump around any time a place name appeared anywhere in the conversation, undermining the "confident" part of confident location resolution.

**Independent Test**: Can be tested by sending a message that references a real, resolvable place name in a non-navigational way (fact, comparison, past-tense recollection) and verifying that no confirmed location payload is emitted and the viewer does not move.

**Acceptance Scenarios**:

1. **Given** an active chat session, **When** the user's message references a place name only incidentally (not as a request to view/navigate/locate it), **Then** no location confirmation is emitted and the viewer's current state is unchanged.
2. **Given** a message contains a place name used only for comparison or analysis purposes ("how does X compare to Y"), **When** Lucy responds, **Then** the response may discuss the place in text without triggering a viewer recenter.

---

### User Story 3 - Uncertain or Unresolvable Place Is Never Silently Guessed (Priority: P2)

A user asks about a place that Lucy cannot resolve with confidence — because the name is ambiguous (matches multiple unrelated real places), misspelled, too vague, or simply not found by the geocoding source. Lucy must never guess and silently recenter the viewer on a low-confidence match. Instead, she tells the user in her reply that she couldn't confidently resolve the place.

**Why this priority**: Incorrectly recentering the viewer on the wrong site would actively mislead a user doing site/BIM analysis, which is worse than doing nothing. This is a trust and correctness boundary, not just a UX nicety.

**Independent Test**: Can be tested by sending a message naming an ambiguous place (matches multiple distinct real places) and a message naming a nonsense/unresolvable place, and verifying that in both cases no confirmed location payload is emitted and Lucy's reply clearly says the place could not be confidently resolved.

**Acceptance Scenarios**:

1. **Given** a place name that resolves to two or more materially different real-world candidates, **When** Lucy responds, **Then** no confirmed location payload is emitted and Lucy's reply tells the user the request was ambiguous.
2. **Given** a place name that the geocoding source cannot find at all, **When** Lucy responds, **Then** no confirmed location payload is emitted and Lucy's reply tells the user the place could not be found.
3. **Given** the geocoding lookup times out or the geospatial source is unavailable, **When** Lucy responds, **Then** no confirmed location payload is emitted and Lucy's reply tells the user the lookup failed rather than leaving the request unanswered.

---

### Edge Cases

- What happens when a single message names more than one specific place (e.g. "compare Al Safa Park 2 and Zabeel Park")? No confirmed location payload should be auto-selected between them; treated as an ambiguous case per User Story 3.
- What happens when the user repeats the exact same location request they just made? The viewer re-confirms the same location without erroring.
- What happens when the resolved place name contains non-Latin characters or diacritics? Resolution and confirmation must still work; the place name must be echoed back as returned by the geocoding source, not transliterated or altered.
- What happens when the place name is spelled slightly incorrectly (e.g. "Al Safa Prak")? A confident single match may still be returned by the geocoding source's own fuzzy matching; if no confident match is found, this falls under User Story 3.
- What happens if the geocoding source returns coordinates outside valid latitude/longitude ranges? Treated as a failed resolution (User Story 3 path), never emitted to the viewer.
- What happens when the location request arrives in a knowledge-base or document-analysis conversation rather than a general chat? Behavior is unaffected — location intent detection applies to any chat message regardless of what else is active in that conversation.
- What happens when the user sends a back-reference (e.g. "zoom in on it") but no location has been confirmed yet in the session? Treated as unresolvable per FR-014/FR-005 — Lucy asks the user to name the place rather than guessing.
- What happens when the user sends a back-reference after the active location was set by device geolocation rather than by the agent (per `activeLocationStore`'s `geolocation` source)? Out of scope for this feature — FR-014 back-references resolve only against a location this feature itself confirmed (source `agent`), not a geolocation-sourced one.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST detect, from the natural-language content of a user's chat message, when the user is expressing intent to view, locate, navigate to, or be shown a specific real-world place — as distinct from merely mentioning a place name in passing (see User Story 2).
- **FR-002**: When location intent is detected, system MUST extract the place name/query the user is asking about and resolve it against a geospatial/geocoding data source, in the manner already assumed by spec 035-location-discovery-viewer (an openly available geocoding source, no per-user authentication).
- **FR-003**: System MUST evaluate resolution results against the same fixed, system-defined confidence threshold model established in spec 035 (single confident match vs. ambiguous/low-confidence) — this feature does not introduce a second, competing threshold or confidence model.
- **FR-004**: When resolution produces exactly one candidate at or above the confidence threshold, system MUST populate the existing `ConfirmedLocationData` payload (place name, latitude, longitude, confidence, source) on the chat response for that turn, reusing the `ChatStreamChunk.ConfirmedLocation` field and `__LOCATION__` streaming contract already defined in specs 036/035 without modifying their shape.
- **FR-005**: System MUST NOT populate `ConfirmedLocationData` when resolution is ambiguous (multiple materially different candidates), below the confidence threshold, not found, or fails/times out. In every one of these cases, Lucy's natural-language reply MUST clearly tell the user the outcome (ambiguous, not found, or lookup failed) — no silent failure and no silent no-op.
- **FR-006**: The confirmed location payload, when emitted, MUST be delivered within the same streamed chat response in which the location intent was detected — the user must not need to send another message or take a separate action to see the viewer update.
- **FR-007**: Confirmed coordinates MUST be validated as being within WGS-84 range (latitude −90 to 90, longitude −180 to 180) before being emitted; a result outside this range MUST be treated as a failed resolution (FR-005 path), not emitted to the viewer.
- **FR-008**: Location intent detection and resolution MUST NOT block or measurably delay the start of Lucy's streamed text reply; the confirmed location (when present) arrives as part of the same response stream, not by making the user wait for geocoding before any text appears.
- **FR-009**: A message naming more than one distinct place MUST NOT result in the system auto-selecting one of them to confirm; this case is treated as ambiguous per FR-005.
- **FR-010**: System MUST NOT introduce a separate, duplicate caching mechanism for geocoding results. Caching is deferred to the future spec 035 (FR-017) implementation, which will wrap `IGeocodingProvider` with a caching decorator without changing this feature's contract. This feature's `NominatimGeocodingProvider` MUST remain cache-free so spec 035 can decorate it later without modifying it.
- **FR-011**: Repeated identical location requests within the same session MUST continue to work (re-confirm the same location) rather than being rejected as duplicates.
- **FR-012**: Every location resolution attempt (success, ambiguous, not found, timeout, or error) MUST be logged server-side with enough detail (query text, chosen source, confidence, outcome) to diagnose incorrect or missed detections after the fact, consistent with the project's no-silent-failure and structured-logging standards.
- **FR-013**: When Lucy's streamed text reply finishes before geocoding completes, the response MUST wait no longer than spec 035's existing 15-second geocoding timeout (FR-015) for a confirmed location before treating that turn's resolution as failed (FR-005 path); this feature MUST NOT introduce a second, separate timeout value for this wait.
- **FR-014**: System MUST also detect simple back-references to the session's already-active confirmed location (e.g. "zoom in on it", "center on that place") when a location has already been confirmed earlier in the session. A recognized back-reference MUST re-emit the already-active location's `ConfirmedLocationData` (name, coordinates, confidence, source) without issuing a new geocoding lookup. If no location is yet active in the session, a back-reference with nothing to resolve against MUST follow the FR-005 no-confirmation path, with Lucy's reply asking the user to name the place.

### Key Entities

- **LocationIntent**: The detected signal that a user's chat message is asking to view/locate/navigate to a specific real-world place, along with either an extracted place name/query text or, for a back-reference (FR-014), a pointer to the session's already-active confirmed location instead of a new query. Transient — exists only for the duration of processing a single message.
- **ConfirmedLocationData**: The existing, already-defined payload (place name, latitude, longitude, confidence, source) carried on the final `ChatStreamChunk` of a response and mirrored onto the `__LOCATION__` SSE event. This feature is responsible for populating it, not for defining or changing its shape.
- **LocationResolutionOutcome**: The result of attempting to resolve a detected `LocationIntent` — either a single confident match (becomes `ConfirmedLocationData`), an ambiguous/low-confidence result, a not-found result, or a failure/timeout. Used for logging and for shaping Lucy's natural-language reply when no confirmed location is emitted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When a user names a single, well-known, unambiguous real-world place in a chat message, the viewer recenters on the correct location within the same conversation turn, without the user providing coordinates or taking any follow-up action.
- **SC-002**: Across a representative benchmark set of place-name requests, no ambiguous or low-confidence match ever causes the viewer to move — zero incorrect auto-confirmations.
- **SC-003**: Every time the viewer recenters from a chat request, Lucy's own reply text confirms the resolved place name in words the user can read, not just a silent visual change.
- **SC-004**: Every time a location request cannot be confidently resolved (ambiguous, not found, or lookup failure), the user receives a clear, readable explanation in the same response — no request is ever left hanging or silently dropped.
- **SC-005**: A passing mention of a place name that is not a navigation request never causes an unwanted viewer change, across a representative benchmark set of non-navigational messages.
- **SC-006**: The confirmed location (when resolvable) is visible to the user within the normal time it takes Lucy's reply to finish streaming — no separate, additional wait beyond the response itself.
- **SC-007**: A simple back-reference to the currently active location (e.g. "zoom in on it") re-confirms that location at least as fast as a fresh named request, since no new geocoding lookup is required.

## Clarifications

### Session 2026-08-23

- Q: When Lucy's text reply finishes streaming before geocoding completes, how long should the system wait for a confirmed location before giving up for that turn? → A: Reuse spec 035's existing 15-second geocoding timeout (FR-015) as the ceiling — no new/second timeout value.
- Q: Should location intent detection consider only the user's latest message, or also resolve back-references to a place named earlier in the conversation (e.g. "zoom in on it")? → A: Also resolve simple back-references to the session's already-active confirmed location, reusing the stored location rather than issuing a new geocode call.

## Assumptions

- This feature covers only the "single confident match" path of spec 035-location-discovery-viewer (its User Story 1) from the backend agent's side. The structured multi-candidate disambiguation list (035 User Story 2) and the coordinate-input fallback flow (035 User Story 3) are not part of this feature; when a request is ambiguous or unresolvable, Lucy communicates that in plain reply text only, per FR-005 / User Story 3 above.
- The geocoding/geospatial data source, its confidence scoring, and the fixed system confidence threshold are as already assumed in spec 035 (an openly available service such as OpenStreetMap Nominatim or equivalent, no per-user authentication); this feature does not choose a different source or invent a second threshold model.
- `ConfirmedLocationData`, `ChatStreamChunk.ConfirmedLocation`, `activeLocationStore.setFromAgent`, the `__LOCATION__` SSE event, and the viewer's re-centering behavior already exist in the codebase (specs 035/036) and are treated as a fixed contract this feature must populate correctly, not redesign.
- "Location intent" detection operates on the content of the user's own chat message for the current turn, plus simple back-references to the session's already-active confirmed location (FR-014); it does not need to infer intent from documents/knowledge-base content, or resolve references to any place other than the currently active one.
- The mechanism by which Lucy detects intent and performs geocoding (e.g. an agent tool the model can invoke, versus a system-prompt-driven behavior) is an implementation decision left to the planning phase; this specification only constrains the observable behavior.
- Geocoding results are treated as authoritative for place name text (echoed as returned by the source); no separate translation or transliteration step is in scope.
- The "already-active confirmed location" a back-reference (FR-014) resolves against is tracked server-side as part of the session (aligning with spec 035's `ActiveSiteContext` entity), since the backend has no direct visibility into the frontend's `activeLocationStore`. A back-reference only ever resolves against a location this feature itself confirmed during the session — not one set purely by device geolocation on the frontend, which the backend never observes.
