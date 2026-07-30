# Feature Specification: Chat Loading & Reply Feedback Fixes

**Feature Branch**: `003-chat-loading-ux-fixes`

**Created**: 2026-07-30

**Status**: Draft

**Input**: User description: "Fix the following chat-experience issues in the conversation/history UI: (1) clicking a conversation in the history list sometimes shows the 'Start a conversation with Ask Lucy.' empty state instead of that conversation's messages; (2) the assistant reply bubble should show an animated three-dot thinking indicator while a response is being generated; (3) clicking a conversation name should show a loading spinner in the chat area while its messages are being fetched; (4) the trailing provider/model attribution line (e.g. 'OpenAI · gpt-3.5-turbo') must be removed from the user-facing reply bubble."

## Clarifications

### Session 2026-07-30

- Q: How fast must the loading spinner (conversation switch) / thinking indicator (message sent) appear after the triggering action, to count as "immediate" per SC-002/SC-003? → A: ≤100ms
- Q: When a conversation's messages (or a reply) fail to load, how should retry work? → A: Manual retry button
- Q: Should the three-dot thinking animation and loading spinner respect the user's OS/browser "reduce motion" accessibility preference? → A: No, always animate
- Q: For very fast AI responses, should the three-dot thinking indicator have a minimum visible display duration to avoid an imperceptible flash? → A: No minimum — show only as long as actually needed

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Always land on the conversation I clicked (Priority: P1)

A user with several past conversations in the history panel clicks on one of them. They expect to see that conversation's messages. Today, they sometimes instead see the "Start a conversation with Ask Lucy." empty-state message, as if nothing were selected — even though a conversation clearly is selected in the history panel. While the messages for the newly selected conversation are being retrieved, the user should see a clear loading indication instead of either the empty state or a blank flash.

**Why this priority**: This is a core-functionality correctness bug. Users cannot trust the chat history feature if clicking an existing conversation can silently produce "no conversation" — this is the most severe and confusing failure mode in scope, and it undermines trust in the whole history feature.

**Independent Test**: Can be fully tested by creating several conversations with existing messages, clicking between them repeatedly (including rapid switching), and confirming that the chat area always ends up showing either (a) the correct conversation's messages, (b) a visible loading indicator while fetching, or (c) a visible error state — and never the "no conversation selected" empty state while a conversation is in fact selected.

**Acceptance Scenarios**:

1. **Given** a user has multiple past conversations with messages, **When** they click a conversation in the history list, **Then** the chat area shows that conversation's messages once loaded, never the "Start a conversation with Ask Lucy." placeholder.
2. **Given** a user clicks a conversation in the history list, **When** its messages are still being retrieved, **Then** the chat area shows a visible loading state (per User Story 2) rather than the empty-state placeholder or a blank/stale view.
3. **Given** a user rapidly clicks between two or more conversations before either finishes loading, **When** loading settles, **Then** the chat area shows the messages for whichever conversation is currently selected (the last one clicked), not a mix of the two or the wrong one.
4. **Given** the fetch for a selected conversation's messages fails (e.g. network error), **When** the failure occurs, **Then** the chat area shows a visible error state with a manual "Retry" action instead of the empty-state placeholder or an indefinite loading spinner — the system does not retry automatically.
5. **Given** a user has no conversation selected (e.g. on first load with no history, or after starting a brand-new chat), **When** the chat area renders, **Then** the "Start a conversation with Ask Lucy." empty state is shown, since this is the one case it is meant for.

---

### User Story 2 - See a loading indicator while a conversation opens (Priority: P1)

When a user clicks a conversation name in the history panel, the chat area should immediately show a loading indicator (e.g. a spinner) confirming that their click registered and content is on its way, rather than appearing to do nothing or showing incorrect content until the messages arrive.

**Why this priority**: This is the mechanism that resolves User Story 1's empty-state bug and gives users confidence their click was registered — it is part of the same core interaction and ships together with it.

**Independent Test**: Can be fully tested by throttling/delaying the conversation-messages fetch and confirming a spinner appears in the chat area immediately on click and is replaced by the loaded messages (or an error state) once the fetch settles.

**Acceptance Scenarios**:

1. **Given** a user clicks a conversation name in the history panel, **When** the click is registered, **Then** a loading spinner appears in the chat area within 100ms.
2. **Given** the loading spinner is shown, **When** the conversation's messages finish loading successfully, **Then** the spinner is replaced by the conversation's messages.
3. **Given** the loading spinner is shown, **When** the fetch fails, **Then** the spinner is replaced by a visible error state with a manual "Retry" action, not left spinning indefinitely and not retried automatically.

---

### User Story 3 - Know the assistant is working on a reply (Priority: P2)

After a user sends a message, they expect immediate feedback that Ask Lucy received it and is composing a response. Today there is a gap between sending a message and seeing any content appear. The reply bubble should show an animated three-dot "thinking" indicator during this gap.

**Why this priority**: This is a valuable perceived-responsiveness improvement independent of the history-loading bug — it can ship and be tested entirely on its own, and is a smaller UX polish item relative to the P1 correctness fix.

**Independent Test**: Can be fully tested by sending a message and observing the reply bubble area — confirming the three-dot indicator appears immediately after send and is replaced by streamed content as soon as the first part of the response arrives.

**Acceptance Scenarios**:

1. **Given** a user sends a message, **When** the assistant has not yet produced any response content, **Then** an animated three-dot indicator is shown in the reply bubble area within 100ms of send.
2. **Given** the three-dot indicator is showing, **When** the first part of the assistant's response begins streaming in, **Then** the indicator is replaced immediately by the incoming response content, with no enforced minimum display duration for the indicator.
3. **Given** the assistant's response fails to start (e.g. provider error), **When** the failure occurs, **Then** the three-dot indicator is replaced by a visible error state with a manual "Retry" action, not left animating indefinitely and not retried automatically.

---

### User Story 4 - Reply bubbles show only the answer (Priority: P3)

A user reading a past or current reply currently sees a trailing line naming the AI provider and model (e.g. "OpenAI · gpt-3.5-turbo") at the end of the answer. This internal detail should not be part of what the user sees — the reply bubble should end with the answer content itself.

**Why this priority**: This is a self-contained visual cleanup with no dependency on the other fixes and the lowest risk/impact of the four items.

**Independent Test**: Can be fully tested by viewing any assistant reply (new or historical) and confirming no provider/model text is rendered anywhere in the bubble.

**Acceptance Scenarios**:

1. **Given** an assistant reply is rendered (new or from history), **When** the user views the reply bubble, **Then** no provider or model name/label is displayed anywhere in the bubble.
2. **Given** provider/model information is still needed for internal usage tracking or analytics, **When** a reply is generated, **Then** that information continues to be recorded, just not displayed to the user.

---

### Edge Cases

- What happens when a user clicks the conversation that is already open/selected? The system should not show a loading state or re-fetch unnecessarily if the content is already loaded and current.
- What happens when a brand-new, empty conversation (no messages yet) is selected from history? It should show an appropriate empty conversation view, distinct from the "no conversation selected" placeholder.
- How does the system handle a user navigating away from a conversation (e.g. clicking a different one, or leaving the chat view) while its messages are still loading? The in-flight load must not overwrite the state of whatever conversation is selected when it later resolves.
- How does the system handle the thinking indicator if the user cancels/stops generation mid-response?
- How does the system handle extremely fast responses? There is no enforced minimum display duration for the thinking indicator or the loading spinner — if a response or fetch completes in a few milliseconds, the indicator is shown only as long as it is actually needed and is not artificially held on screen.
- The three-dot thinking animation and the loading spinner animate the same way for all users; there is no reduced-motion/static fallback for users with an OS/browser "reduce motion" preference enabled.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The chat area MUST display the "Start a conversation with Ask Lucy." empty state only when no conversation is currently selected; it MUST NOT be shown when a conversation is selected but its messages have not yet loaded, or have failed to load.
- **FR-002**: When a user selects a conversation from the history panel, the system MUST show a loading indicator in the chat area within 100ms while that conversation's messages are being fetched.
- **FR-003**: When a conversation's messages finish loading successfully, the system MUST replace the loading indicator with that conversation's messages, with no enforced minimum display duration for the loading indicator.
- **FR-004**: When a conversation's messages fail to load, the system MUST replace the loading indicator with a visible, user-facing error state that includes a manual "Retry" action (not a silent failure, blank view, indefinite spinner, or automatic retry), consistent with the platform's no-silent-failures requirement.
- **FR-005**: If a user selects a different conversation before a prior selection's message fetch has completed, the system MUST ensure only the messages for the conversation currently selected are ever shown — results from an abandoned/stale fetch MUST NOT be applied to the UI.
- **FR-006**: The system MUST show an animated three-dot "thinking" indicator in the reply bubble area within 100ms of a user's message being sent, continuing until the assistant's response begins producing visible content.
- **FR-007**: The system MUST replace the thinking indicator with the assistant's streamed response content as soon as that content begins arriving, with no enforced minimum display duration for the thinking indicator.
- **FR-008**: If the assistant's response fails to start or errors out before producing content, the system MUST replace the thinking indicator with a visible, user-facing error state that includes a manual "Retry" action, rather than leaving it animating indefinitely or retrying automatically.
- **FR-009**: The system MUST NOT render any provider or model name/label (e.g. "OpenAI · gpt-3.5-turbo") within an assistant reply bubble, for both newly generated and previously stored replies.
- **FR-010**: The system MAY continue to retain provider/model metadata internally (e.g., for usage tracking, billing, or analytics) associated with each reply; only its display within the reply bubble is removed.
- **FR-011**: The three-dot thinking indicator and the conversation loading spinner MUST animate identically for all users; no static/reduced-motion fallback variant is required.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of clicks on an existing conversation in the history panel result in either that conversation's messages, a visible loading indicator, or a visible error state — never the "no conversation selected" empty-state message.
- **SC-002**: Users see a visible loading indicator in the chat area within 100ms of clicking a conversation in history, every time, until that conversation's messages or an error state are shown.
- **SC-003**: Users see a visible "thinking" indicator in the reply bubble within 100ms of sending a message, every time, until response content begins streaming in.
- **SC-004**: 0% of rendered assistant reply bubbles (new or historical) display provider/model attribution text to the user.
- **SC-005**: User-reported issues describing "my chat disappeared," "wrong conversation showed up," or "nothing happens when I click a chat" drop to zero after release.

## Assumptions

- The existing conversation-messages retrieval mechanism (via the frontend's data-fetching layer) is being corrected for loading/error/state-scoping behavior, not replaced with a new fetching mechanism.
- The existing streaming response mechanism already distinguishes "no content received yet" from "content has begun arriving," which the thinking indicator can key off of.
- Provider/model metadata is already being captured and stored for each reply for usage tracking purposes; this feature only changes what is rendered to the user, not what is recorded.
- No new backend endpoints or data are required; this is a frontend state-management and rendering correctness fix within the existing Chat Engine UI.
- "Start a conversation with Ask Lucy." remains the correct message for the true empty case (no conversation selected / brand-new session with no history).
