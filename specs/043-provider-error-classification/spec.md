# Feature Specification: AI Provider Failure Classification & Accurate Health Reporting

**Feature Branch**: `043-provider-error-classification`

**Created**: 2026-08-29

**Status**: Draft

**Input**: User description: "Investigate the Gemini provider integration — API availability, quota/rate limits, provider health, and error reporting. Distinguish invalid credentials, provider unavailable, quota exhausted, rate limited, billing restriction, temporary failure, and internal errors. Do not hide provider-side failures behind 'An unexpected error occurred.' Provider health must reflect the real state. Gemini Vision failures in the site-boundary workflow must fall back gracefully."

## Investigation Findings *(evidence gathered before writing this spec)*

These are the verified, code-traced facts this specification is built on. No live provider call was made from this environment, so no claim of actual quota exhaustion is asserted — the observed symptoms are fully explained by the classification gaps below.

1. **The generic toast is the API's 500 fallback, shown verbatim.** The web client renders the Problem Details `detail` string as-is. `"An unexpected error occurred. Please try again."` is the unmapped-exception fallback detail. Seeing it means an exception type reached the error boundary that the error map does not recognise.

2. **The catalog-sync read path has no failure translation.** Every provider's "list the vendor's models" call runs outside the retry/translate wrapper that chat calls use. Only two outcomes are translated (credential rejected, rate limited); a request timeout, an unparseable or unexpected response shape, and a failure to decrypt the stored credential all escape unclassified and become that generic 500. A credential-decryption failure is the single most likely cause of the observed state, because it simultaneously explains both symptoms in the screenshots — the generic sync error *and* the red Unhealthy chip on a provider whose credential shows as Configured.

3. **Health is a bare true/false with the reason discarded.** The health status is Unknown/Healthy/Unhealthy only. A reason string *is* recorded in the append-only health-check log, but the admin provider list never returns it, so the page cannot show it. A quota-exhausted provider, a wrong API key, a disabled billing account, and a momentary network blip all render as the identical red "Unhealthy" chip.

4. **The health probe collapses every non-success response to "unhealthy".** It reports success/failure of one real live call and nothing else, so a 429 — which Google returns for *both* per-minute rate limiting and daily project-quota exhaustion — is indistinguishable from a rejected key.

5. **Health is real but can be silently stale.** The probe is a genuine live API call against each enabled provider on a recurring background interval (2 minutes by default). It is therefore not a permanently cached failure. However: if the background cycle stops or repeatedly fails, the last status persists indefinitely with **no staleness indication** — the screenshot shows a health timestamp two days older than the session date, i.e. the displayed status was not current. There is also **no on-demand "check now" action**, so an administrator who has just fixed a credential cannot verify it without waiting for the next tick.

6. **The vendor's own reason codes are never read.** Google returns a machine-readable reason alongside the HTTP status (rate limit vs. project quota exhausted share a 429; invalid key, API-not-enabled, and billing-disabled share a 403). The code branches on HTTP status alone, so a billing-disabled project is reported to the administrator as "check the provider's API key" — actively misleading.

7. **Models with no vendor-supplied token limits can never be added.** The prior fix correctly stopped a null token limit from aborting the whole sync, but such rows are substituted with zero, which the catalog's own validation rejects. Those models are therefore reported as per-row failures on every single sync attempt, forever. This affects every model in one vendor's list (that vendor publishes no token metadata at all) and the non-chat entries in Gemini's list. Compounding it, there is no way to supply the figures afterwards — the only mutators on a catalog model are status and pricing, and the only model endpoint is a status patch — so the "an administrator reviews and corrects it" intent these zero substitutions were written for was never actually built. Both figures are also read by nothing but two display DTOs: no chat, context-assembly, or token-budgeting path consumes them, so they are informational metadata that was gating creation for no functional reason.

8. **The site-boundary vision fallback is already correct by design.** The vision analyzer never throws: every failure path — including timeout, non-success status, empty response, and any unexpected exception — returns a "not available, here's why" result, and the boundary resolution service proceeds on the deterministic result. This needs regression tests locking the behaviour in, not a fix.

## Clarifications

### Session 2026-08-29

- Q: Does the health classification replace the existing Unknown/Healthy/Unhealthy status, or sit alongside it? → A: Augment — keep the tri-state as the coarse signal, add a nullable classification + reason alongside it on both the provider's current state and each append-only check row (additive schema and DTO changes only).
- Q: How much of the classification reaches a non-administrator in chat? → A: None — end users keep today's generic "the service couldn't process your request, try again" for every provider failure. The full classification is administrator-only, so end-user chat messaging is unchanged by this feature.
- Q: When is a health status considered possibly out of date? → A: When it is older than 3x the configured background-check interval (6 minutes at today's 2-minute default). Expressed as a multiple, not an absolute duration, so widening the interval cannot mark every provider permanently stale.
- Q: What is the time budget for the site-boundary vision call? → A: 30 seconds, configurable — a dedicated budget for the vision call rather than the shared 2-minute HTTP client timeout it inherits today. Chosen to sit above the 15s value that produced false "unavailable" results for Overpass and Geocoding on this host, while capping the interactive wait before fallback.
- Q: How does a model with no vendor-published token limits get into the catalog? → A: Context window and max output MUST NOT be constraints on adding a model at all. They are optional, display-only metadata; the domain's reject-on-zero rule is removed, absence is stored as absence, and absence never blocks adding, enabling, or using a model. No administrator data entry and no new edit action are required to add a model.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An administrator learns *why* a provider action failed (Priority: P1)

An administrator opens the AI Providers page and syncs a provider's model catalog. If the attempt fails, the page tells them which of the failure kinds occurred and what, if anything, they can do about it — instead of a generic "unexpected error".

**Why this priority**: This is the reported defect. Without it, an administrator cannot distinguish a problem they must fix (a bad key, a disabled billing account) from one they must simply wait out (a rate limit, a vendor outage), and cannot tell either apart from a bug in Ask Lucy.

**Independent Test**: Drive the sync action against a stubbed provider that returns, in turn, each failure kind, and confirm the page shows a distinct, accurate, actionable message for each. Delivers value on its own with no other story implemented.

**Acceptance Scenarios**:

1. **Given** the provider rejects the stored credential, **When** an administrator runs the catalog sync, **Then** the page states that the provider rejected the configured credential and that the API key needs to be replaced.
2. **Given** the provider reports the project's quota is exhausted, **When** an administrator runs the catalog sync, **Then** the page states the provider is configured correctly but temporarily unavailable because its usage quota is exhausted, and never suggests the credential is wrong.
3. **Given** the provider reports a short-term rate limit, **When** an administrator runs the catalog sync, **Then** the page identifies it as rate limiting and, when the provider supplied a retry hint, tells the administrator how long to wait.
4. **Given** the provider reports that billing is disabled or the API is not enabled for the project, **When** an administrator runs the catalog sync, **Then** the page names that specific restriction rather than describing it as a credential problem.
5. **Given** the request to the provider times out or the provider returns a server error, **When** an administrator runs the catalog sync, **Then** the page states the provider is temporarily unreachable and invites a retry.
6. **Given** the stored credential cannot be decrypted (for example after a deployment changed the protection keys), **When** an administrator runs the catalog sync, **Then** the page states that the stored credential could not be read and must be re-entered — not "unexpected error".
7. **Given** any of the above, **When** the failure is displayed, **Then** no raw provider response body, credential, or stack trace appears anywhere in the user-visible message.

---

### User Story 2 - Provider health shows the real, current state with its reason (Priority: P1)

The AI Providers page shows, per provider, not just healthy/unhealthy but *why* it is unhealthy and *when* that was last confirmed — and flags the status as stale when it has not been refreshed recently.

**Why this priority**: A red chip with no reason and a two-day-old timestamp is worse than no chip: it looks current and says nothing. Health and error classification are two views of the same underlying facts, so they must ship together.

**Independent Test**: Record health outcomes of each failure kind for a provider, load the page, and confirm each renders a distinct status with its reason and check time; then let the recorded time age past the staleness threshold and confirm the page marks it stale.

**Acceptance Scenarios**:

1. **Given** a provider's last health probe was rejected for quota exhaustion, **When** an administrator views the page, **Then** the provider is shown as configured-but-limited with "quota exhausted" as the reason — visually distinct from a credential failure.
2. **Given** a provider's last health probe succeeded, **When** an administrator views the page, **Then** it shows healthy with the time of that confirmation.
3. **Given** a provider's recorded health is older than the staleness threshold, **When** an administrator views the page, **Then** the status is presented as possibly out of date rather than as a current fact.
4. **Given** a provider is enabled but has never been probed, **When** an administrator views the page, **Then** it shows an explicit "not yet checked" state, not "unhealthy".
5. **Given** a provider is disabled, **When** an administrator views the page, **Then** its health is not presented as a failure.

---

### User Story 3 - An administrator re-checks a provider on demand (Priority: P2)

After fixing a credential, enabling billing, or waiting out a limit, an administrator can trigger an immediate health check for one provider and see the fresh, classified result without waiting for the background cycle.

**Why this priority**: Turns diagnosis (stories 1 and 2) into a closed feedback loop. Valuable, but the page is already accurate without it.

**Independent Test**: Change a stubbed provider's response from failing to succeeding, trigger the on-demand check, and confirm the displayed status and reason update immediately.

**Acceptance Scenarios**:

1. **Given** a provider showing unhealthy, **When** an administrator triggers a re-check and the provider now responds successfully, **Then** the page shows healthy with a just-now timestamp.
2. **Given** a provider showing unhealthy, **When** an administrator triggers a re-check and the provider still fails, **Then** the page shows the current failure kind and reason.
3. **Given** a re-check is in progress, **When** the administrator triggers it again, **Then** repeated triggering does not queue multiple concurrent probes for the same provider.
4. **Given** a re-check itself fails for an internal reason, **When** it completes, **Then** the administrator sees an error rather than a silently unchanged status.

---

### User Story 4 - Token limits never block adding a model (Priority: P2)

An administrator can add any vendor model to the catalog regardless of whether the vendor published context-window or output-limit figures for it, and the page shows plainly which figures the vendor did not publish.

**Why this priority**: Without it, "catalog sync works" is only half true — an entire vendor's models, and Gemini's non-chat entries, are permanently unaddable and are re-reported as failures on every sync, with no way for an administrator to resolve it. It is a distinct defect from error classification, so it ships after the P1 stories.

**Independent Test**: Run a sync against a stub whose model list omits token limits, apply the rows, and confirm every row is added with its limits marked not-published, with no failures reported and no data entry required.

**Acceptance Scenarios**:

1. **Given** the vendor reports a model with no token-limit figures, **When** an administrator applies the sync for that row, **Then** the model is added to the catalog with its limits recorded as not published by the vendor.
2. **Given** a model was added with not-published limits, **When** an administrator views the catalog, **Then** those figures are shown as not published by the vendor rather than as zero.
3. **Given** a model was added with not-published limits, **When** an administrator enables it, **Then** it becomes usable through the existing status control, with no requirement to supply the missing figures first.
4. **Given** a row genuinely cannot be applied for another reason, **When** the sync is applied, **Then** that row is still reported as a per-row failure with its reason, and the other rows still apply.

---

### User Story 5 - The site-boundary workflow survives any Gemini failure (Priority: P3)

A user resolving a site boundary always gets the deterministically-derived result, even when the AI vision enhancement is unavailable for any reason.

**Why this priority**: Investigation confirms this already behaves correctly. The work is regression coverage that locks the guarantee in, so it carries real but lower value.

**Independent Test**: Force the vision analyzer to fail in each way — quota, rate limit, credential rejected, server error, timeout, malformed response, missing credential — and confirm boundary resolution completes with the deterministic result each time.

**Acceptance Scenarios**:

1. **Given** the vision analyzer fails for any provider-side reason, **When** a user resolves a site boundary, **Then** the workflow completes successfully using the deterministic boundary result.
2. **Given** the vision analyzer fails, **When** the result is returned, **Then** it carries a plain-language note that AI verification was unavailable and why, rather than presenting AI-verified confidence it does not have.
3. **Given** the vision request exceeds its time budget, **When** the workflow runs, **Then** boundary resolution still completes rather than hanging on the AI call.
4. **Given** the user cancelled the request, **When** the vision call is in flight, **Then** cancellation propagates as cancellation and is not reported as a provider failure.

---

### Edge Cases

- A provider returns a failure status with an unrecognised or absent vendor reason code — classification falls back to the closest kind implied by the response status, never to "internal error".
- A provider returns a success status with a body the system cannot interpret — classified as a provider-side failure, not an internal error, and never as healthy.
- A provider returns a retry hint that is absent, malformed, or implausibly large — the administrator is told to retry later without a specific duration, and no absurd wait is displayed.
- The background health cycle cannot run at all (for example the store of provider records is unreachable) — no provider's health is falsely downgraded to unhealthy on the strength of a failure that was not the provider's.
- Two failure kinds are plausible at once (a rate limit arriving during an outage) — one kind is chosen deterministically by a documented precedence so the same response always classifies the same way.
- An administrator triggers an on-demand check for a provider with no credential configured — reported as "not configured", distinct from both unhealthy and healthy.
- A provider is enabled but the vendor's model list is empty — reported as an empty diff, not as an error.
- The vendor returns its model list across multiple pages — every page is retrieved before the diff is computed, so no model is silently missing. A continuation that does not terminate is treated as an unusable response rather than followed indefinitely.

## Requirements *(mandatory)*

### Functional Requirements

**Failure classification**

- **FR-001**: The system MUST classify every failure of an interaction with an AI provider into exactly one of: credential rejected, credential unreadable, provider not configured, quota exhausted, rate limited, usage or billing restriction, provider temporarily unavailable, provider response not understood, or internal application error.
- **FR-002**: The system MUST derive that classification from both the provider's response status and any machine-readable reason the provider supplied, preferring the vendor reason when the two disagree.
- **FR-003**: The system MUST apply a single, documented precedence when more than one classification could apply, so identical responses always classify identically.
- **FR-004**: The system MUST classify a failure to read or decrypt a stored provider credential as "credential unreadable" and never as an internal application error.
- **FR-005**: The system MUST classify a request that exceeded its time budget as "provider temporarily unavailable" and never as an internal application error.
- **FR-006**: The system MUST classify a provider response it cannot interpret as "provider response not understood" and never as an internal application error.
- **FR-007**: The system MUST reserve "internal application error" for failures originating inside Ask Lucy, and MUST NOT use it as a catch-all for provider-originated failures.
- **FR-008**: The system MUST apply this classification to the model-catalog listing path, not only to the chat path.
- **FR-009**: The system MUST apply this classification uniformly across every AI provider, not Gemini alone.

**Surfacing failures to people**

- **FR-010**: Every classified failure MUST reach the administrator as a distinct, plain-language message naming the classification, and MUST NOT be presented as "an unexpected error occurred".
- **FR-011**: Each message MUST state whether the administrator can act (replace the credential, enable billing) or must wait (rate limit, quota, outage).
- **FR-012**: When a provider supplies a retry hint, the system MUST convey the wait to the administrator; when it does not, the system MUST say to retry later without inventing a duration.
- **FR-013**: No user-visible message may contain a credential, a raw provider response body, an exception type name, or a stack trace.
- **FR-014**: Every classified failure MUST be recorded server-side with enough detail to diagnose it, including the provider, the classification, and the vendor reason code where one was supplied.
- **FR-015**: Every failing operation on the AI Providers page MUST produce visible feedback; no failure may be logged only or left unhandled.
- **FR-015a**: The classification MUST be disclosed only to administrators. A non-administrator experiencing a provider failure in chat MUST continue to receive the existing generic, cause-free message, and MUST NOT be shown the quota, billing, credential, or provider-restriction state behind it. End-user-facing chat messaging is therefore unchanged by this feature.

**Provider health**

- **FR-016**: Provider health MUST retain its existing three-state signal (not yet checked / healthy / unhealthy) as the coarse status, and MUST record the failure classification and its reason as additional attributes alongside that status — on both the provider's current-state record and each append-only check outcome. The classification is absent when a check succeeded and when no check has ever run.
- **FR-017**: The administrator-facing provider list MUST expose that classification and its reason alongside the status and the time of the check.
- **FR-018**: A provider whose health check failed for quota or rate limiting MUST be presented as configured-and-credentialled but temporarily limited — visually and textually distinct from a credential failure.
- **FR-019**: The system MUST present a health result older than three times the configured background-check interval as possibly out of date rather than as current fact. The window MUST be derived from that interval rather than fixed as an absolute duration, so changing the interval cannot render every provider permanently stale.
- **FR-020**: A provider that has never been checked MUST be presented as "not yet checked", distinct from unhealthy.
- **FR-021**: A provider with no credential configured MUST be presented as "not configured", distinct from both healthy and unhealthy.
- **FR-022**: Health checks MUST continue to be performed by a real call to the provider on a recurring interval, and MUST NOT add latency to any end-user request.
- **FR-023**: A failure of the health-check mechanism itself MUST NOT be recorded as a provider being unhealthy.
- **FR-024**: Administrators MUST be able to trigger an immediate health check for a single provider and see the classified result.
- **FR-025**: An on-demand check MUST NOT allow an administrator to issue unbounded concurrent probes for the same provider.
- **FR-026**: The system MUST retain the history of health-check outcomes as an append-only record.

**Catalog sync**

- **FR-027**: The catalog sync MUST either complete or explain, in the classified terms of FR-001, why it could not.
- **FR-028**: A single vendor model entry the system cannot fully interpret MUST NOT prevent the remaining entries from being listed or applied.
- **FR-028a**: The system MUST retrieve a vendor's complete model list, following pagination wherever the vendor paginates, before computing a sync diff. A continuation sequence that does not terminate within a bounded number of pages MUST be classified as an unusable provider response rather than followed indefinitely.
- **FR-029**: Context window and maximum output MUST NOT be preconditions for adding a model to the catalog. The rule rejecting a model whose figures are absent MUST be removed, and absence MUST be stored as absence rather than substituted with a placeholder value. A supplied figure MUST still be a positive number.
- **FR-029a**: The not-published state MUST be named distinctly from the provider-health states "not yet checked" and "not configured" (FR-020/FR-021) everywhere it is presented, and MUST NOT reuse the word "unknown" already carried by the health status and by absent pricing in the model list.
- **FR-030**: A model whose token limits were not published MUST be shown as not published and MUST NOT be shown as zero. Absent figures MUST NOT block adding, enabling, or using the model, and MUST NOT require any administrator data entry — these figures are display-only metadata that no chat, context-assembly, or token-budgeting behaviour depends on.
- **FR-031**: Per-row apply failures MUST continue to be reported per row, naming the row and the reason, while every other row still applies.

**Site-boundary resilience**

- **FR-032**: A failure of the AI vision enhancement, for any reason including quota, rate limiting, credential rejection, provider outage, timeout, or an uninterpretable response, MUST NOT fail the site-boundary workflow.
- **FR-033**: When the vision enhancement is unavailable, the workflow MUST return the deterministically-derived boundary result together with a plain-language note that AI verification was unavailable and why.
- **FR-034**: The vision enhancement MUST operate under its own bounded time budget, defaulting to 30 seconds and configurable, rather than inheriting the general-purpose provider timeout. Exceeding it MUST fall back to the deterministic result per FR-032/FR-033.
- **FR-035**: A user-initiated cancellation MUST propagate as a cancellation and MUST NOT be recorded or reported as a provider failure.

### Key Entities

- **Provider failure classification**: The named kind of a single failed provider interaction, its human-readable explanation, whether the administrator can act on it, and any retry hint. Derived from the provider's response; never invented.
- **Provider health record**: The outcome of one health check for one provider — when it ran, whether it succeeded, and, when it did not, the failure classification and reason. The classification is an added attribute on the existing record, not a replacement for its success flag. Append-only.
- **Provider health summary**: The current view an administrator sees for a provider — its three-state status, the classification and reason behind that status (absent on success and before the first check), when it was last confirmed, and whether that confirmation is stale.
- **Catalog model entry**: A model offered by a vendor, whose token-limit figures may legitimately have gone unpublished by that vendor. Not-published is a distinct state from zero and from a supplied value, is display-only, and constrains nothing. Because a model row can only be created by a successful catalog sync, which requires a working credential, not-published limits can never indicate an unconfigured provider — it is unrelated to the provider-health states of the same colloquial name.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For each failure classification, an administrator seeing the resulting message can correctly name the cause and the correct next action, in 100% of cases, without consulting server logs.
- **SC-002**: The message "an unexpected error occurred" appears for zero provider-originated failures across the AI Providers page — verified by exercising every classification.
- **SC-003**: An administrator can tell a quota or rate-limit condition apart from an invalid credential at a glance on the provider list, with no drill-down.
- **SC-004**: After fixing a provider's credential, an administrator can confirm the fix within 30 seconds, without waiting for a background cycle.
- **SC-005**: Every health status shown is either confirmed within the freshness window or explicitly marked as possibly out of date — no status is displayed as current when it is not.
- **SC-006**: A catalog sync against a vendor list that publishes no token limits adds every selected row in one action — all ~97 rows of the affected vendor's list — with zero rows rejected and zero figures typed by the administrator.
- **SC-007**: The site-boundary workflow returns a usable boundary result in 100% of runs where the AI vision enhancement fails, across every failure mode, and a hung or unresponsive vision call adds no more than 30 seconds to the workflow before the deterministic result is returned.
- **SC-008**: Zero user-visible messages contain a credential, a raw provider response body, an exception type name, or a stack trace, verified across every classification.
- **SC-009**: Automated regression coverage exists for every classification and for every site-boundary vision failure mode, and the full existing test suite continues to pass.

## Assumptions

- The observed symptoms are explained by the classification gaps documented in Investigation Findings. No live provider call was made from this environment, so actual quota exhaustion is **not** asserted; the work makes the true cause self-evident to an administrator whichever it turns out to be. Confirming the live account state is an operational step, not a code change.
- Distinguishing a short-term rate limit from an exhausted longer-term quota depends on the vendor supplying a reason code that separates them. Where a vendor does not, the system reports the broader "temporarily limited" condition rather than guessing, and says so.
- The freshness window for health status is settled at 3x the configured background-check interval by clarification (FR-019), and the vision time budget at 30 seconds (FR-034).
- The existing recurring background health check, its append-only outcome log, and the existing administrator-only authorization on these actions are reused rather than replaced.
- Ask Lucy's existing structured error contract for API responses is extended with the classification rather than replaced by a new error shape.
- End-user-facing chat error messages are explicitly out of scope and remain exactly as they are today (FR-015a). The shared classifier is used on the chat path too, but only to produce better server-side diagnostics and health data — never a different message for the end user.
- Providers other than Gemini are in scope for the shared classification behaviour, but no vendor reason codes beyond those each vendor already documents are catalogued.
- Retrospectively re-classifying health records written before this change is out of scope; they age out of the freshness window naturally.
