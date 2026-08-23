# Feature Specification: Hold-to-Talk Simplification & Self-Listening Fix

**Feature Branch**: `033-hold-to-talk-and-echo-fix`

**Created**: 2026-08-23

**Status**: Draft

**Input**: User description: "Fix three issues in the Ask Lucy chat widget's voice flow, found via live production testing after publishing SPEC-032: (1) transcription still fails with a generic 500 in production, and this round's fix must actually be committed and deployed this time, not left as local uncommitted changes; (2) Push-to-Talk's mic button must become pure hold-to-talk (press = record, release = stop and transcribe into the message field, WhatsApp voice-message style) — the current dual tap-toggle/hold-gesture design is rejected; (3) in Continuous conversation mode, Lucy can hear and react to her own voice output (self-listening/audio feedback) — the mic must be muted while she is speaking."

## Clarifications

### Session 2026-08-23

- Q: Should preventing Lucy's self-listening (FR-009) fully mute the mic during her speech (guaranteed fix, but interruption stops working), or keep the mic live and harden false-positive detection (interruption preserved, but self-listening not fully guaranteed on weak hardware)? → A: Fully mute the mic while Lucy speaks. This deliberately supersedes the prior "natural interruption" design (specs/031 research.md Decision 10, User Story 3) — mid-response barge-in interruption is intentionally removed by this feature so self-listening is fully eliminated, not just mitigated.
- Q: Releasing the Push-to-Talk button now always finishes and transcribes (FR-005) — but the existing mid-recording Cancel button only appears while the button stays held, and tapping it would require releasing first, which would itself trigger finish+transcribe. How should discarding an unwanted recording work now? → A: Drop the dedicated pre-send Cancel affordance entirely. Release always transcribes into the message field as editable draft text; to discard, the user deletes the draft text or simply doesn't press Send. No separate mid-recording cancel gesture is introduced.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Voice recordings transcribe reliably, and this fix actually reaches production (Priority: P1)

A user records a message via Push-to-Talk and expects it to transcribe successfully every time real speech was captured — including after a fix has supposedly already shipped for this exact problem. Today, the same "Transcription failed with 500" error the previous fix targeted is still occurring live in production, undermining confidence that the fix exists at all.

**Why this priority**: This is the same production-blocking failure from the prior round, now confirmed to still be occurring after a fix was implemented — the highest-priority item, since trust in voice input depends on this actually working, and trust in the fix process depends on what's implemented actually reaching users.

**Independent Test**: Record a normal spoken message via Push-to-Talk and confirm it transcribes successfully. Separately, confirm (via the repository's version-control history and a live deployment check) that the code implementing this fix is committed, merged, and actually running in production — not merely present in an uncommitted working copy.

**Acceptance Scenarios**:

1. **Given** a user records a normal, audible message via Push-to-Talk, **When** the recording finishes, **Then** it transcribes successfully into the message field — no generic failure.
2. **Given** the transcription provider returns a response that cannot be understood (malformed, empty, or unexpected content) even after a successful connection, **When** this occurs, **Then** the user sees a specific, actionable error — not an unexplained generic failure — and can immediately try again.
3. **Given** this feature's implementation is complete, **When** it is delivered, **Then** the change is committed to version control, merged, and verifiably running in the production environment — not left as uncommitted local changes, so this exact "the fix doesn't seem to be live" situation cannot recur.
4. **Given** the transcription provider is temporarily unavailable, rate-limited, or explicitly rejects a specific recording (already-handled conditions from the prior fix), **When** these occur, **Then** the user still sees their existing distinct messages, unchanged by this fix.

---

### User Story 2 - Push-to-Talk is pure hold-to-talk, like a voice message (Priority: P1)

A user presses and holds the mic button, speaks, and releases it, expecting the release itself to immediately finish the recording, transcribe it, and place the result in the message field ready to send — exactly like recording a voice message in a messaging app. There is no separate mode where a quick tap starts a recording that keeps running until a second, separate tap stops it, and no separate "finished speaking" button to press afterward.

**Why this priority**: Directly, explicitly requested by the user as a correction to the prior round's behavior; the current dual-gesture design is actively confusing users about how to start and stop a recording, which blocks the core Push-to-Talk experience just as much as the transcription failure does.

**Independent Test**: In Push-to-Talk mode, press and hold the mic, speak, and release. Confirm recording is active only while held, and that releasing immediately transcribes the result into the message field with no further action needed to complete the recording. Confirm a quick, accidental tap does not leave a recording running unattended.

**Acceptance Scenarios**:

1. **Given** Push-to-Talk mode with an idle mic, **When** the user presses and holds the mic button while speaking, **Then** recording is active only for as long as the button remains physically held or pressed — regardless of how short or long that duration is.
2. **Given** an in-progress recording, **When** the user releases the button, **Then** recording stops immediately and the captured audio is transcribed into the message field as editable draft text, with no additional tap, click, or button required to complete the transcription.
3. **Given** the user presses and releases the button very quickly (a brief tap), **When** this happens, **Then** the same press-then-release behavior applies — the brief recording is transcribed on release — rather than the recording being left running and waiting for a second tap to stop it.
4. **Given** a transcription in Scenario 2 succeeds, **When** the user reviews the resulting text, **Then** they can edit it and tap Send to send the message, exactly as before.
5. **Given** the user presses the button, records something, and decides not to send it, **When** the recording finishes on release and its transcript appears as draft text, **Then** the user can delete that text or simply not press Send — there is no separate mid-recording cancel affordance; discarding happens after the transcript lands in the draft, not before (Clarification, 2026-08-23).

---

### User Story 3 - Lucy doesn't hear or react to her own voice (Priority: P2)

While Lucy is speaking in Continuous conversation mode, the user's microphone should not pick up and react to Lucy's own voice output as if it were the user talking. Today, on hardware without effective echo cancellation (e.g., built-in laptop or phone speakers instead of headphones), Lucy's own speech can leak into the microphone and be misread as the user interrupting or speaking, confusing the conversation.

**Why this priority**: A real, confirmed conversational-quality defect that undermines trust in Continuous mode, but it affects an optional conversation mode rather than the core send-a-message path, so it follows the two P1 items above.

**Independent Test**: Start a Continuous-mode conversation using device speakers (not headphones) at a normal volume, let Lucy respond with a spoken reply, and confirm her own voice does not get misread as user speech or cause an unwanted reaction during her reply.

**Acceptance Scenarios**:

1. **Given** Continuous mode is active and Lucy is speaking a response, **When** her voice is audible through the device's speakers, **Then** the microphone does not treat her own voice as user speech.
2. **Given** Lucy has finished speaking a response, **When** she stops, **Then** the microphone resumes listening for the user normally, with no noticeable delay beyond what already exists today.
3. **Given** the user genuinely wants to interrupt Lucy while she is speaking, **When** they do so, **Then** the microphone is fully muted for the duration of her speech (Clarification, 2026-08-23) — the user cannot interrupt her mid-response by talking over her; they wait until she finishes speaking (at which point the mic resumes listening per Scenario 2) before the system will pick up what they say.

---

### Edge Cases

- What happens if the user holds the Push-to-Talk button for an extremely long time? The existing maximum-recording-duration/safety behavior (if any already exists) is unaffected by this change — only the start/stop trigger mechanism changes, not any existing duration limit.
- What happens if the transcription provider's response is technically a 2xx success but the body is empty or unparseable? The user must see a clear, specific error (per User Story 1, Scenario 2) rather than a confusing silent/empty result or a generic failure.
- What happens if the user releases the Push-to-Talk button before any audio was actually captured (e.g., an instant tap-and-release)? The same release-triggers-transcription behavior applies; if the resulting clip is too short/silent to transcribe, the existing "specific error, not generic failure" handling (from the prior round) applies.
- What happens if the user talks while Lucy is still speaking, intending to interrupt her? Per the resolved clarification, the mic is muted for the duration of her speech, so this is not picked up as an interruption — the user's speech is not captured until she finishes and the mic resumes listening.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST successfully transcribe a Push-to-Talk recording whenever it contains genuine, transcribable speech, without surfacing a generic/unexplained failure.
- **FR-002**: When the transcription provider's response — whether an error status or a malformed/unparseable success response — cannot be used, the system MUST classify the failure and surface a specific, visible, user-facing error, distinct from a generic internal-error message, and MUST leave the user able to immediately attempt a new recording. This closes the remaining gap left by the prior round's fix (which classified error-status responses but not malformed success responses).
- **FR-003**: The system MUST NOT silently or generically fail a transcription request when the underlying cause is identifiable (constitution §2.VIII, no silent failures).
- **FR-004**: This feature's implementation MUST be committed to version control and merged before it is considered complete, and its presence in the production deployment MUST be explicitly verified — not merely implemented in an uncommitted working copy.
- **FR-005**: The Push-to-Talk mic control MUST use a single gesture: pressing/holding starts recording, and releasing always immediately stops recording and transcribes it into the message field, regardless of how long the button was held.
- **FR-006**: The Push-to-Talk mic control MUST NOT have a mode where a short press starts a recording that continues running until a second, separate interaction stops it.
- **FR-007**: Releasing the Push-to-Talk button MUST NOT require any additional button press (such as a separate "finished speaking" action) to complete the transcription — release alone is sufficient.
- **FR-008**: In the hold-to-talk control this feature changes, the dedicated mid-recording Cancel affordance (a separate button shown only while actively recording) MUST be removed, since it is no longer reachable without releasing the mic button, which itself now always triggers finish+transcribe (Clarification, 2026-08-23). Discarding an unwanted recording happens after transcription, via the normal message-field editing the user already has (delete the text, or don't press Send) — no dedicated pre-send discard mechanism is introduced. This does not apply to any other, separate Push-to-Talk control elsewhere in the product that uses a plain click-to-start/click-to-stop interaction rather than a hold gesture — such a control has no hold/release ambiguity and is unaffected by this feature.
- **FR-009**: While Lucy is speaking in Continuous mode, the system MUST fully mute/disable microphone input capture for the duration of her speech, so her own voice output cannot be misread as user speech under any hardware condition (Clarification, 2026-08-23). Mid-response interruption (talking over her while she is still speaking) is intentionally not supported by this feature — this supersedes the "natural interruption" behavior from specs/031 research.md Decision 10/User Story 3.
- **FR-010**: Once Lucy finishes speaking, the microphone MUST resume normal listening for the user without a noticeable added delay beyond what already exists today.
- **FR-011**: This fix MUST NOT change the existing, already-correct handling of transcription-provider unavailability, rate-limiting, or explicit request rejection (from the prior round) — those cases continue to surface their existing distinct messages.

### Key Entities

*(Not applicable — this feature fixes interaction gestures, error classification, and audio-capture behavior; it introduces no new data entities.)*

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Push-to-Talk recordings containing genuine, audible speech transcribe successfully in production, with zero occurrences of an unexplained generic failure for that case.
- **SC-002**: 100% of the time, starting and completing a Push-to-Talk recording requires exactly two physical interactions total — press and release — with zero additional taps or button presses needed.
- **SC-003**: This feature's fix for the transcription failure is verifiably present in the production deployment, confirmed by matching the deployed behavior/version to what is recorded in version control, not merely assumed from a local implementation.
- **SC-004**: In a Continuous-mode conversation conducted through device speakers (not headphones), Lucy's own spoken responses are not misread as user speech, across repeated test conversations.
- **SC-005**: Zero regressions: the already-correct provider-unavailable/rate-limited/rejected-request handling continues to pass every acceptance scenario already covered by specs/029-032's test suites (excluding the mid-recording Cancel affordance and dual tap-toggle gesture, both deliberately removed by this feature — see Clarifications).

## Assumptions

- User Story 1's "malformed success response" gap (FR-002) is addressed the same way the prior round addressed error-status responses: by classifying the failure into a specific, actionable error rather than a generic one — this feature does not change what "successful transcription" means, only what happens when a nominally-successful response can't actually be used.
- "Verifiably running in production" (FR-004/SC-003) is satisfied by this feature's own delivery process (committing, merging, and deploying through the project's established CI/CD workflow) rather than by adding new in-app version-reporting UI, which is out of scope.
- No new user permissions, external integrations, or database changes are required.
