# Feature Specification: Honest Turn Outcomes, Real Retry, and a Readable Offer Card

**Feature Branch**: `068-honest-retry-offer-card`

**Created**: 2026-09-23

**Status**: Draft

**Input**: User description: "Two defects observed in production (hydra.bimcatalyst.com/studio): 1. Lucy falsely claims success after a failed tool call. Sequence: user asked 'Show me Al Safa Park 2'; an AI provider API-key failure produced 'Something went wrong partway through and I couldn't finish. Please try again.'; user replied 'try again'; Lucy answered 'I've shown you Al Safa Park 2.' — but nothing was shown. The assistant treated the previous (failed) turn as if it had completed, producing a false/misleading success claim. Retry must re-execute the action, and the assistant must never assert an action succeeded unless the tool actually reported success. 2. Suggestion/offer card styling in the chat panel: the 'Suggested' choice card renders in a narrow column inside the assistant bubble, so its text wraps awkwardly on nearly every word and the Choose button is cramped. The card should expand to occupy the full width of the chat window when its text needs more space."

## Context

Two independent defects were observed in one production session and are specified together because both make the chat panel misrepresent what happened in a turn — one in words, one in layout.

**Defect 1 — a fabricated success claim.** After a turn failed mid-stream (provider credential failure), the user asked Lucy to try again. Lucy replied "I've shown you Al Safa Park 2." while the viewer remained on an entirely unrelated location. Nothing was retried, nothing was shown, and the user was told otherwise. This is the most damaging class of failure a workspace assistant can have: a user who cannot trust Lucy's account of her own actions cannot trust any of her output. It is also the specific harm the "no silent failures" principle exists to prevent — a failure that is not merely hidden but actively overwritten with a false success.

**Defect 2 — the offer card is squeezed.** The suggestion card that closes an agentic turn is nested inside the assistant bubble, which is itself width-limited, and the card applies a second limit on top of it. Inside the docked chat panel the card ends up roughly half the panel width, so its question, option labels, option descriptions, and the Choose button each wrap onto their own lines — one or two words per line. The card is legible but visibly broken, and the wasted horizontal space is sitting right next to it.

## Clarifications

### Session 2026-09-23

- Q: How strong must the "never claim an unperformed action" guarantee be? → A: Verified before send — each turn's real outcome is recorded and supplied to whatever composes a later reply, and the composed reply is additionally checked against that recorded outcome and corrected before the user sees it.
- Q: Where does the recorded turn outcome live? → A: Both — persisted with the assistant message as the authority used at reply time, retry time and verification time, and additionally recorded in the existing inspection/audit trail, which is extended to cover every turn (including turns that fail before completing) and stays off the critical path.
- Q: What context does the turn router receive, so a follow-up that cannot stand alone routes correctly? → A: The latest user message plus a compact, bounded structured summary of the last few turns' recorded outcomes (what was attempted, against what target, whether it succeeded) — not raw conversation history.
- Q: How does the offer card get its width? → A: Both widenings together — when an assistant message carries an offer, the message bubble itself spans the chat panel's full usable width, and the card's own width cap is removed so it fills that bubble. Accepted trade-off: the reply prose in an offer-carrying bubble also renders at full width.
- Q: What does the retry affordance record in the transcript? → A: A new assistant turn carrying its own recorded outcome. The original failed turn stays visible, and no synthetic user message is inserted in the user's voice.
- Q: What should voice output speak when a turn ends with an offer card? → A: Not the card's full text. Voice reads Lucy's reply and a brief cue that choices are available; the question, every option label, every option description and the confirm action are not read aloud in full.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Lucy never claims an action she did not perform (Priority: P1)

A user asks Lucy to do something in the workspace — show a location, outline a boundary, run an analysis. Whatever the outcome, Lucy's account of that turn matches what actually happened: success is claimed only when the underlying action reported success, failure is stated plainly, and a turn in which no action ran never reads as though one did.

**Why this priority**: A false success claim is worse than an error message. The user acts on it — they look at the viewer, see the wrong thing, and are left unable to tell whether Lucy is broken, the data is wrong, or they misunderstood their own request. Every other item in this feature is cosmetic or convenience by comparison; this one is about whether Lucy's output can be believed at all.

**Independent Test**: Force an action to fail (revoke the provider credential used by the turn router, or make the capability itself fail), then continue the conversation across several turns. Confirm no reply in the conversation asserts the action succeeded, and that every turn which was asked to act either reports it acted, reports it failed, or says it did not act.

**Acceptance Scenarios**:

1. **Given** a turn that failed mid-stream with a visible failure notice, **When** the user sends any follow-up in the same conversation, **Then** Lucy's reply never states or implies that the failed action was completed.
2. **Given** a turn in which no workspace action ran at all, **When** Lucy composes a plain-words reply, **Then** that reply does not claim to have moved the viewer, outlined a boundary, opened a panel, or performed any other workspace action.
3. **Given** an action that ran and reported failure, **When** Lucy reports the turn's outcome, **Then** the reply states that it did not work, and does not pair the failure with a success sentence for the same action.
4. **Given** an action that ran and reported success, **When** Lucy reports the turn's outcome, **Then** the reply confirms it, and the confirmation is attributable to the action's own reported result rather than to the conversation's narrative.
5. **Given** a conversation whose history contains a failed turn, **When** that history informs any later reply, **Then** the failed turn is distinguishable as a failure and is not read as a completed action.
6. **Given** a composed reply that claims an action which the recorded outcome says never succeeded, **When** the turn is delivered, **Then** the claim is corrected before the user reads it and the user is left with an accurate account of what happened.
7. **Given** a composed reply whose claims match the recorded outcome, **When** the turn is delivered, **Then** the reply reaches the user unchanged and without added delay.

---

### User Story 2 - "Try again" actually tries again (Priority: P2)

After a turn fails, the user asks Lucy to retry. The action the user originally asked for is re-attempted, and the user is told the outcome of that fresh attempt.

**Why this priority**: This is the defect's trigger. Without it, the user's only recovery is to retype the original request verbatim, and a bare "try again" is answered by whatever the conversation model invents — which is exactly how the false claim in Defect 1 was produced. It ranks below US1 because US1 alone already stops the user from being misled; US2 restores the recovery path.

**Independent Test**: Cause a turn requesting a workspace action to fail. Ask Lucy to retry using ordinary language ("try again", "retry that", "can you try once more"). Confirm the original action is re-attempted and its real outcome is reported.

**Acceptance Scenarios**:

1. **Given** a turn requesting a workspace action that failed, **When** the user asks Lucy to try again, **Then** the originally requested action is re-attempted with the original request's parameters.
2. **Given** the retried action succeeds, **When** the turn completes, **Then** the workspace reflects the action and Lucy confirms it.
3. **Given** the retried action fails again, **When** the turn completes, **Then** Lucy states that the retry also failed, and does not claim success.
4. **Given** a turn that was answered in words with no action, **When** the user asks Lucy to try again, **Then** Lucy re-answers or asks what to retry, and does not invent an action to claim.
5. **Given** a failed turn, **When** the user looks at that turn in the transcript, **Then** a retry affordance is available on it so that recovery does not depend on phrasing a request the router can interpret.
6. **Given** the user activates the retry affordance, **When** the retry runs, **Then** a new assistant turn is appended with its own recorded outcome, the original failed turn remains visible above it, and no message is added in the user's voice that they did not type.
7. **Given** the user has moved on to an unrelated topic since the failure, **When** they ask Lucy to try again, **Then** Lucy retries the most recent failed action or asks which action they mean, rather than guessing at an unrelated one.

---

### User Story 3 - The offer card is readable, and not read aloud in full (Priority: P3)

When Lucy closes a turn by offering the user a choice, the choice card uses the width available in the chat panel, so its question and options read as sentences rather than as a column of single words. Voice output speaks her reply and a short cue rather than reciting the whole card.

**Why this priority**: Purely presentational — nothing is lost or misreported, and the user can still make their choice. It is fixed alongside the other two because it appears in the same panel and was reported from the same session.

**Independent Test**: Trigger an offer whose question and option descriptions are long enough to wrap, in both the docked chat panel and the expanded chat view. Confirm the card uses the available width, that neither the option rows nor the confirm action are cramped, and that voice output does not recite the card.

**Acceptance Scenarios**:

1. **Given** an offer card whose content needs more horizontal space than the default message width, **When** it is rendered in the chat panel, **Then** the card expands to the full usable width of the panel.
2. **Given** an offer card whose content is short, **When** it is rendered, **Then** the card does not stretch to full width for no reason.
3. **Given** an offer card at full width, **When** the user reads an option row, **Then** the radio control, the option label and the option description sit on the same horizontal band rather than each wrapping to its own line.
4. **Given** an offer card at full width, **When** the user looks for the Choose button, **Then** the button is fully visible with its label on one line.
5. **Given** the chat panel is resized or the view switches between docked and expanded, **When** the card re-renders, **Then** it adapts to the new width without clipping or horizontal scrolling.
6. **Given** an answered offer rendered in history, **When** it is displayed, **Then** it follows the same width behaviour and remains non-interactive.
7. **Given** a turn that ends with an offer card, **When** voice output speaks that turn, **Then** it speaks Lucy's reply and a brief cue that choices are available, and does not read the question, option labels, option descriptions and confirm action aloud in sequence.
8. **Given** a user relying on a screen reader, **When** an offer card is rendered, **Then** the full question, options and descriptions remain reachable regardless of what voice output spoke.

---

### Edge Cases

- **The failure notice itself is the only content of a turn.** A turn that fails before producing any text leaves a bubble containing nothing but the failure notice. Later turns must read that bubble as a failure, not as Lucy's answer to the request.
- **The failure happens after partial success.** A turn resolves a location successfully and then fails while outlining its boundary. The account must credit only the part that succeeded and report the part that failed — not collapse both into one verdict in either direction.
- **The retry target is ambiguous.** Two different actions failed earlier in the conversation and the user says "try again". Lucy must not silently pick one; she asks, or retries the most recent failure and names which one she retried.
- **Retry of an action whose preconditions have changed.** The user asks to retry an action that depended on a workspace state (an active location, an open panel) that no longer exists. The retry must fail visibly with the reason rather than appearing to succeed against a different target.
- **The provider is still unavailable at retry time.** The retry fails for the same reason as the original. The user must get a distinguishable message rather than the same sentence a third time with no indication that anything was attempted.
- **A workspace action changes state but reports failure.** The reported outcome governs what Lucy says; a partially applied change is described as a failure, and the user is told the workspace may be in an intermediate state.
- **An offer card with an unusually long single option label.** The label must wrap within the card at full width rather than forcing the card wider than the panel or introducing horizontal scrolling.
- **An offer card arrives while voice output is mid-sentence.** The cue is appended to the reply being spoken rather than interrupting it, and the card's contents are still not read out.
- **A voice-only user receives an offer.** The spoken cue must be enough for them to know a choice is waiting and ask for the options; suppressing the card's full text must not leave them unaware an offer exists.
- **A retry whose new turn also ends with an offer.** The transcript then holds two failed turns and one offer; only the newest offer is live, and the width treatment applies to the message carrying it.
- **An offer card that arrives while the panel is at its narrowest supported width.** Full width of a very narrow panel is still narrow; the card must degrade to a readable stacked layout rather than clipping the Choose button.
- **A retry issued while a turn is still streaming.** The in-flight turn's outcome, not the retry, governs what the user is told about the original action.
- **Verification versus streaming.** Replies are streamed as they are generated, so "checked before the user sees it" cannot mean holding every reply until it is complete — that would cost the responsiveness the platform streams for, and voice output reads the text as it arrives. The check must not let an unverified action claim reach the user, and must not stall replies that make no action claim at all.
- **The verification step disagrees with a correct reply.** A false positive must not turn an accurate confirmation into a denial; the recorded outcome is the authority, and a reply consistent with it passes unchanged.

## Requirements *(mandatory)*

### Functional Requirements

#### Truthful accounts of what happened

- **FR-001**: The system MUST derive every statement that a workspace action was performed from that action's own reported outcome, never from the conversation's narrative or the model's inference.
- **FR-002**: When a turn produces a plain-words reply without running any workspace action, the system MUST prevent that reply from asserting that a workspace action was performed.
- **FR-002a**: The system MUST check every composed reply against the recorded outcomes of the turns it refers to, and MUST correct or withhold any claim that contradicts those outcomes before the user reads it.
- **FR-002b**: When the verification step corrects a reply, the system MUST leave the user with an accurate account of what happened — never a blank, a truncated sentence, or a reply whose meaning was silently inverted without explanation.
- **FR-002c**: When the verification step is itself unavailable or fails, the system MUST fall back to a reply that makes no claim about workspace actions, and MUST NOT emit an unverified claim as though it had been verified.
- **FR-003**: When an action reports failure, the system MUST report that failure to the user and MUST NOT emit a success statement for the same action in the same turn.
- **FR-004**: When a turn fails before completing, the system MUST record that turn's outcome as a failure in a form that later turns can distinguish from a completed turn.
- **FR-004a**: The system MUST persist each turn's outcome together with the assistant message that turn produced, so that the outcome survives a page reload and is unambiguously attributable to the message the user is looking at.
- **FR-004b**: The message-attached outcome MUST be the single authority consulted when composing a later reply, verifying a reply, and resolving a retry.
- **FR-004c**: The system MUST record the outcome even for turns that fail before completing, including a turn whose failure prevents it from reaching its normal end.
- **FR-004d**: When persisting a turn's outcome fails, the system MUST surface that failure rather than proceeding as though the outcome were recorded, since a missing outcome silently removes the guarantee in FR-002a.
- **FR-004e**: The system MUST additionally record every turn — including ordinary turns that run no action and turns that fail before completing — in the inspection/audit trail, so an operator can reconstruct what happened without reading the transcript.
- **FR-004f**: The inspection/audit trail MUST remain off the critical path: a failure to write it MUST be logged and surfaced to operators without failing or delaying the user's turn, and it MUST NOT be the source consulted under FR-004b.
- **FR-005**: The system MUST make each prior turn's real outcome — acted and succeeded, acted and failed, answered in words only, or failed before completing — available to whatever composes a later reply, so that a reply referring to earlier work reflects what actually happened.
- **FR-006**: When a turn partially succeeds, the system MUST report which parts succeeded and which failed, rather than a single verdict covering all of them.
- **FR-007**: The system MUST NOT present a user-visible failure notice as though it were Lucy's substantive answer to the request when that turn's history is reused later.

#### Retry

- **FR-008**: The system MUST interpret a user's request to retry as a request to re-attempt the most recent failed action, and MUST re-execute that action rather than answering in words about it.
- **FR-009**: The system MUST supply whatever composes a turn's routing decision with the latest user message plus a compact summary of the recent turns' recorded outcomes — what was attempted, against what target, and whether it succeeded — so that a follow-up which cannot stand alone (a retry request, a correction, a bare confirmation) is routed to the action it refers to rather than treated as an isolated message.
- **FR-009a**: The routing context MUST be bounded in size and MUST NOT grow with the length of the conversation.
- **FR-009b**: The routing context MUST carry recorded outcomes rather than raw message prose, so that routing depends on what actually happened rather than on how it was described.
- **FR-009c**: When a conversation has no recent turn outcomes to summarise, routing MUST behave as it does today and MUST NOT incur additional cost.
- **FR-010**: When retrying, the system MUST reuse the original request's target and parameters.
- **FR-011**: The system MUST report the outcome of the retry attempt itself, distinguishable from the outcome of the original attempt.
- **FR-012**: When a retry request cannot be resolved to a specific prior action, the system MUST ask the user which action to retry rather than selecting one silently or inventing an outcome.
- **FR-013**: Users MUST be able to retry a failed turn from a visible affordance on that turn, without relying on the router's interpretation of typed language.
- **FR-013a**: A retry MUST append a new assistant turn carrying its own recorded outcome, and MUST leave the original failed turn and its recorded outcome intact and visible.
- **FR-013b**: A retry MUST NOT insert a message attributed to the user that the user did not type, and MUST NOT overwrite or remove the turn being retried.
- **FR-014**: When a retry fails, the system MUST surface the reason to the user in the chat, in line with the project's error-handling rules — never a silent no-op and never an unexplained repeat of the original notice.
- **FR-015**: The system MUST NOT retry an action that already reported success; a retry request against a successful turn MUST be answered by telling the user it already succeeded, or by asking whether they want it run again.

#### Offer card presentation

- **FR-016**: When an assistant message carries an offer, the system MUST render that message's bubble at the full usable width of the chat panel, rather than at the width used for ordinary reply bubbles.
- **FR-016a**: The system MUST render the offer card at the full width of the bubble containing it, with no width cap of its own.
- **FR-017**: Assistant messages that carry no offer MUST keep their existing reply width; the full-width treatment MUST apply only to offer-carrying messages, and MUST end when the panel next renders a message without an offer.
- **FR-017a**: The system MUST accept that the reply text in an offer-carrying bubble also renders at full width, and MUST keep that text readable at that width.
- **FR-018**: The system MUST keep each option's selection control, label and description on a shared horizontal band at the card's rendered width, rather than wrapping each element onto its own line.
- **FR-019**: The system MUST render the card's confirm action fully visible with its label unwrapped at every supported panel width.
- **FR-020**: The system MUST apply the same width behaviour to answered and historical offer cards as to live ones, without making them interactive.
- **FR-021**: The system MUST avoid horizontal scrolling and clipping of the offer card at the narrowest supported chat panel width, degrading to a stacked layout instead.
- **FR-022**: The system MUST preserve the offer card's existing keyboard operation, focus order and assistive-technology labelling at every rendered width.
- **FR-023**: When a turn ends with an offer card, voice output MUST NOT read the card's full contents aloud — not the question, each option label, each option description and the confirm action in sequence.
- **FR-024**: Voice output MUST still speak Lucy's own reply for that turn, plus a brief cue that choices are available, so a user listening rather than reading knows an offer is waiting.
- **FR-025**: Suppressing spoken card text MUST NOT reduce what assistive technology can reach: the card's full question, options and descriptions remain available to a screen reader under FR-022.

### Key Entities

- **Turn outcome**: What one conversational turn actually did — which actions were attempted, which succeeded, which failed and why, or that the turn answered in words only or failed before completing. Distinct from the text the user read, persisted alongside the assistant message that turn produced, and the authority for any later statement about that turn — for composing a reply, verifying one, and resolving a retry.
- **Retryable action**: A prior turn's action recorded with enough detail — what was asked for, against what target, with what parameters, and whether it succeeded — to be re-attempted on request.
- **Offer card**: The choice presented at the end of a turn: a question, a set of options each with a label and optional description, a decline option, and a confirm action.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 0% of turns does Lucy state that a workspace action was performed when no action reported success — verified across a scripted set covering provider failure, action failure, partial success, and plain-words turns. Because every reply is checked against the recorded outcome before the user reads it, this is a guarantee of the system rather than a best-effort target of the model.
- **SC-001a**: 100% of replies that contradict a recorded turn outcome are corrected before the user reads them, and every correction leaves an accurate account rather than a blank or truncated reply.
- **SC-001b**: The verification step adds no perceptible delay to replies that make no claim about workspace actions — the common conversational turn streams as promptly as it does today.
- **SC-001c**: 100% of turns — including turns that fail before completing — leave a retrievable outcome attached to their assistant message, confirmed by reloading the conversation and re-running the checks in SC-001 against the reloaded transcript.
- **SC-001d**: 100% of turns appear in the inspection trail, and an operator can determine what a given turn attempted and whether it succeeded without reading the chat transcript.
- **SC-002**: 100% of turns in which an action fails produce a user-visible statement of that failure in the chat.
- **SC-003**: A user who asks Lucy to retry after a failure gets a genuine re-attempt of the original action in at least 95% of attempts across the supported retry phrasings, and an explicit request for clarification in the remainder — never a fabricated outcome.
- **SC-004**: A user can recover from a failed action in one interaction (one retry affordance or one retry message) without retyping their original request.
- **SC-005**: Every retry attempt produces an outcome statement the user can distinguish from the original attempt's outcome statement.
- **SC-006**: At the default docked chat panel width, no offer card renders any option label or the confirm action at fewer than 4 words per line where the underlying text is longer than 4 words.
- **SC-006a**: An offer-carrying message occupies the full usable width of the chat panel, and an adjacent message without an offer occupies the ordinary reply width, in the same transcript.
- **SC-007**: 0% of offer cards clip content or introduce horizontal scrolling at any supported chat panel width.
- **SC-008**: The offer card passes the project's automated accessibility checks at its rendered widths, with no regression against its current results.
- **SC-011**: A turn ending with an offer speaks Lucy's reply plus a short cue, and does not speak the card's question, option labels, option descriptions or confirm action verbatim — measured as spoken duration no longer than the same reply without an offer plus the cue.
- **SC-012**: A retried turn appears as an additional turn in the transcript, with the original failure still visible above it, and both turns carry retrievable outcomes after a reload.
- **SC-009**: Ordinary conversational turns that neither act nor fail add no measurable per-turn cost beyond the bounded routing summary, whose size stays constant as a conversation grows from its first turn to its hundredth.
- **SC-010**: A referring follow-up issued immediately after a failed action ("try again", "retry that", "do it again") routes to the intended action rather than to a plain-words answer in at least 95% of attempts.

## Assumptions

- The two defects ship together because they were reported from one session and both concern the chat panel. They are independently testable and independently deployable; either can be dropped without blocking the other.
- "Workspace action" means any capability whose effect the user can observe outside the chat transcript — moving or setting the viewer location, outlining a site boundary, opening or configuring a panel, running an analysis. Statements about Lucy's own conversational behaviour ("I'll explain that") are out of scope.
- Retry covers the most recent failed action in the current conversation. Retrying an arbitrary earlier turn, or retrying across conversations, is out of scope.
- The existing user-visible failure notice wording is adequate and is not being redesigned here; what changes is that the failure it reports becomes part of the turn's recorded outcome rather than only its visible text.
- "Full width of the chat window" means the full usable width of the chat panel's message area, inside its existing padding — not an overlay that escapes the panel. Applied to the offer-carrying bubble and the card within it.
- The offer card's existing content, option semantics, decline handling and history rendering are unchanged; what changes is its width behaviour, its internal layout at that width, and how much of it voice output speaks.
- Voice output reads whatever the corrected reply text says, and is additionally scoped so an offer card is not read out in full (FR-023–FR-025). The existing young-adult female voice persona requirement is unaffected — this governs *what* is spoken, not which voice speaks it.
- The provider credential failure that triggered the reported session is an operational issue handled separately. This feature governs how Lucy behaves when such a failure occurs, not whether it occurs.
