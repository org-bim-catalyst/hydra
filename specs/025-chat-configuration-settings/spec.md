# Feature Specification: Chat Configuration in User Settings

**Feature Branch**: `025-chat-configuration-settings`

**Created**: 2026-08-17

**Status**: Draft

**Input**: User description: "Move advanced Ask Lucy configuration options out of the primary chat interface and into User Settings. Create a new settings section (if needed): Chat Configuration. The Chat Configuration section should contain: current LLM engine/model, chat/agent configuration, chat history, voice configuration, speech-to-text settings, text-to-speech settings, other AI-agent preferences. The primary workspace and chat interface should no longer expose advanced configuration unnecessarily. The goal is to keep the Flumeria workspace visually clean while preserving access to advanced configuration. Integrate Chat Configuration into the existing user settings/dropdown architecture. Do not duplicate configuration controls unnecessarily between the chat window and settings. Preserve all existing configuration functionality."

## Clarifications

### Session 2026-08-17

- Q: Should the existing "AI Providers" and "Voice" Settings tabs be removed/merged into the new "Chat Configuration" section, or should Chat Configuration be an additional umbrella/landing tab that links to them? → A: Keep "AI Providers" and "Voice" as separate, unchanged tabs; "Chat Configuration" is a new hub/landing tab that links to them rather than duplicating their controls inline.
- Q: Where should the relocated chat-history browsing live, and how does model availability in Chat Configuration relate to admin-side model curation? → A: Chat history has nothing to do with Chat Configuration — it is a separate, standalone Settings section, not nested in or linked from the Chat Configuration hub. Separately: a super user/admin decides which AI models from which vendors are active via the existing model catalog; Chat Configuration only lets a user select among that admin-curated active set — it does not manage which models exist.
- Q: The existing "AI Providers" tab only sets the default model for *new* conversations and explicitly never affects a conversation already in progress; the in-chat live switcher being removed from the toolbar is what let a user change the model of their *currently open* conversation. Should that live, mid-conversation capability be preserved (relocated into the Chat Configuration hub) or dropped as an accepted simplification? → A: Preserved — Chat Configuration hosts a dedicated "current conversation" model control, distinct from the AI Providers tab's new-conversation-only default, so mid-conversation switching keeps working, just from Settings instead of the chat toolbar.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A single hub for AI model and voice configuration (Priority: P1)

A user who wants to change the model powering their current conversation, set their default model for future conversations, or adjust how Lucy sounds (voice, speed, style, STT/TTS, devices) opens User Settings and finds a "Chat Configuration" hub that either hosts the control directly or takes them straight to the right existing settings tab — instead of hunting through the chat window.

**Why this priority**: This is the core value of the feature — consolidating scattered, easy-to-miss controls into one discoverable, predictable starting point is the entire reason the feature was requested.

**Independent Test**: Can be fully tested by opening User Settings, navigating to "Chat Configuration," and confirming it offers a current-conversation model control plus working entry points into the existing AI Providers and Voice tabs — without opening the chat workspace at all.

**Acceptance Scenarios**:

1. **Given** a signed-in user opens User Settings, **When** they look at the available sections, **Then** they see a "Chat Configuration" section alongside the existing Security, Account, AI Providers, Voice, Data, and Cookies sections (Chat Configuration does not replace or remove any of these).
2. **Given** a user opens Chat Configuration, **When** they view its contents, **Then** they see a control to change the AI model of their currently open conversation (if any) and clear entry points into the unchanged AI Providers tab (default model for new conversations) and the unchanged Voice tab (voice conversation, speed, style, STT/TTS behavior, microphone/speaker devices).
3. **Given** a user has a conversation open, **When** they change its model from the Chat Configuration hub, **Then** the change applies immediately to that open conversation, exactly as the previous in-chat switcher behaved.
4. **Given** a user follows a Chat Configuration entry point into AI Providers or Voice, **When** they change a setting there, **Then** it saves using that tab's existing, unmodified save behavior.
5. **Given** a user opens Chat Configuration, **When** the list of selectable models is populated, **Then** it only ever shows models a super user/administrator has marked active in the platform's AI provider/model catalog — Chat Configuration has no separate way to add or enable a model.

---

### User Story 2 - Browse and reopen chat history from Settings (Priority: P2)

A user who wants to find a past conversation opens a standalone "Chat History" section in Settings — independent of Chat Configuration — to search, filter, and reopen it.

**Why this priority**: Distinct value from Priority 1: this relocates conversation browsing/management out of the chat workspace, but it is a separate capability from AI model/voice configuration and must work even if a user never touches Chat Configuration.

**Independent Test**: Can be fully tested by opening User Settings, navigating to the standalone "Chat History" section (not nested under Chat Configuration), and confirming search, filtering, sorting, pin/favorite/archive/duplicate/export/delete, and reopening a conversation all work exactly as they did in the previous in-workspace panel.

**Acceptance Scenarios**:

1. **Given** a signed-in user opens User Settings, **When** they look at the available sections, **Then** they see a "Chat History" section that is separate from, and not nested inside, "Chat Configuration."
2. **Given** a user opens Chat History, **When** they search, filter (All/Favorites/Pinned/Archived/Recently Deleted), sort, rename, pin/favorite/archive/duplicate/export, or delete a conversation, **Then** each action behaves exactly as it did in the previous in-workspace conversation list.
3. **Given** a user selects a conversation from Chat History, **When** the selection is made, **Then** the system navigates them into the Flumeria Studio workspace with that conversation active.
4. **Given** a user has zero saved conversations, **When** they open Chat History, **Then** they see the same "no conversations yet" empty state that exists today.

---

### User Story 3 - A visually clean chat workspace (Priority: P2)

A user working inside the Flumeria Studio chat workspace sees a focused, uncluttered interface for having a conversation, without configuration or history-browsing controls that don't need to be visible during everyday chatting.

**Why this priority**: This is the stated motivation for the reorganization ("keep the Flumeria workspace visually clean") but is only achievable once User Stories 1 and 2 give users an equivalent way to reach the same capability elsewhere — a clean workspace that loses functionality is a regression, not an improvement.

**Independent Test**: Can be fully tested by opening the Flumeria Studio workspace and confirming that the live provider/model switcher and the conversation history panel are no longer present in the toolbar, while everyday per-message chat actions (sending a message, starting a new conversation) remain exactly where they are today.

**Acceptance Scenarios**:

1. **Given** a user opens the Flumeria Studio workspace, **When** they view the conversation toolbar, **Then** they no longer see the live provider/model switcher or the conversation history browse/search panel — both now reached through Settings.
2. **Given** a user is in an active conversation, **When** they want to reach Chat Configuration or Chat History, **Then** they can do so in two clicks or fewer via the existing account/settings entry point already present in the workspace.
3. **Given** a user is in an active conversation, **When** they perform an everyday chat action (send a message, mute/unmute voice output, toggle push-to-talk vs. continuous voice mode, start a new conversation), **Then** that action remains available directly in the chat workspace exactly as before.

---

### User Story 4 - Reach both destinations without leaving the flow (Priority: P3)

A user in the middle of a chat session realizes they want to change the model, tweak voice behavior, or find an old conversation, and expects to reach either destination through the same account/settings entry point they already use for everything else.

**Why this priority**: Lower priority than the relocations themselves — this is about the quality of the entry point, not whether the destinations exist. A user can still succeed via the standalone Settings page even if this specific in-workspace shortcut were imperfect.

**Independent Test**: Can be fully tested by opening the Flumeria Studio workspace, opening the existing account/settings menu from within the workspace, and confirming entries for both "Chat Configuration" and "Chat History" are present and navigate correctly.

**Acceptance Scenarios**:

1. **Given** a user is in the Flumeria Studio workspace, **When** they open the existing account/settings menu, **Then** they see entries leading to both Chat Configuration and Chat History.
2. **Given** a user reaches User Settings through any other existing path (not from the workspace), **When** they look for chat-related configuration or history, **Then** they find the same two sections with the same behavior — not a second, differently-scoped copy.

### Edge Cases

- What happens when a user has no AI providers enabled yet and opens Chat Configuration? The section MUST show the same "no providers configured" empty state that exists today, not an error.
- What happens when a user opens Chat Configuration with no conversation currently open? The current-conversation model control MUST reflect that there is nothing to change (e.g., hidden or disabled with explanatory text) rather than erroring or showing a misleading value.
- What happens when a user changes the current-conversation model while a response is still streaming? It MUST behave exactly as the previous in-chat switcher did today — no new behavior is introduced by relocating the control.
- What happens when a user denies microphone/speaker permission and opens the Voice tab (via a Chat Configuration entry point or directly)? The device pickers MUST degrade the same way they do today (falling back to "System default" / empty device lists), not crash or block the rest of the tab.
- What happens when a user is mid-conversation with unsent voice input (actively recording) and navigates away to Settings? In-progress voice capture MUST be safely stopped/cancelled, not silently lost mid-transcription with no feedback.
- What happens when a user without administrator rights opens Chat Configuration and no AI providers are enabled at the platform level? The existing "an administrator needs to configure one first" messaging MUST still appear.
- What happens when a user opens Chat Configuration or Chat History on a narrow/mobile viewport? All controls MUST remain usable and readable at mobile breakpoints, consistent with the rest of the Settings experience.
- What happens when a user in an active conversation wants to start a fresh conversation? They MUST be able to do so directly from the workspace without navigating to Settings, since starting a new conversation is an everyday action, not history browsing.
- What happens when a user has zero saved conversations and opens Chat History? It MUST show the same "no conversations yet" empty state that exists today (inviting them to start a new chat back in the workspace), not an error.
- What happens when a user selects a conversation from Chat History? The system MUST navigate them back into the Flumeria Studio workspace with that conversation now active, not display the conversation read-only inside Settings.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a "Chat Configuration" section within User Settings, reachable the same way every other Settings section is reached today, positioned alongside — not replacing — the existing "AI Providers" and "Voice" sections.
- **FR-002**: Chat Configuration MUST provide a clear entry point into the existing, unchanged "AI Providers" section for managing the default AI provider/model used by new conversations, without duplicating that section's controls inline.
- **FR-003**: Chat Configuration MUST provide a clear entry point into the existing, unchanged "Voice" section for voice conversation preferences (mode, mute, voice, speed, style), microphone/speaker device selection, and any STT/TTS-related behavior, without duplicating that section's controls inline.
- **FR-004**: Chat Configuration MUST host directly (not merely link to) a dedicated control for changing the AI provider/model of the conversation the user currently has open — distinct from the AI Providers section's new-conversation-only default — preserving the live, mid-conversation model-switching capability previously available inline in the chat toolbar. A change made here MUST apply immediately to the open conversation.
- **FR-005**: Every AI model presented anywhere in Chat Configuration (the current-conversation control and, transitively, the AI Providers default picker) MUST be limited to the set of providers/models a super user/administrator has marked active via the platform's existing AI provider/model catalog. Chat Configuration MUST NOT provide its own mechanism for enabling, disabling, or otherwise curating which models exist.
- **FR-006**: The system MUST provide a standalone "Chat History" section within User Settings, separate from and not nested inside Chat Configuration, hosting the existing conversation list capability in full: search, filter (All/Favorites/Pinned/Archived/Recently Deleted), sort, inline rename, pin/favorite/archive/duplicate/export/delete.
- **FR-007**: Selecting a conversation from the Chat History Settings section MUST navigate the user back into the Flumeria Studio workspace with that conversation active, preserving the existing behavior of opening a conversation exactly as it works today from the in-workspace switcher.
- **FR-008**: The primary chat workspace (Flumeria Studio) MUST NOT expose, inline in its toolbar/composer, the live per-conversation provider/model switcher (relocated to Chat Configuration per FR-004) or the conversation history browse/search panel (relocated to the standalone Chat History section per FR-006).
- **FR-009**: The chat workspace MUST continue to expose the following as everyday (non-relocated) controls, unchanged: sending messages, muting/unmuting voice output, toggling conversation mode, activating the microphone, starting a new conversation, inserting saved prompts, selecting a translation target language, and assigning a conversation to a memory project.
- **FR-010**: Every control removed from the chat workspace under FR-008 MUST remain fully functional after relocation — no capability may be dropped, only relocated (per FR-004's preservation of mid-conversation model switching and FR-006/FR-007's preservation of history browsing/management).
- **FR-011**: The existing account/settings entry point already present in the chat workspace MUST include a way to reach both Chat Configuration and Chat History, without introducing a second, separately-maintained menu structure beyond what already exists for reaching Settings.
- **FR-012**: The system MUST NOT duplicate any configuration control or piece of history-management functionality in more than one place: the default-for-new-conversations model picker lives only in AI Providers; voice/device/STT/TTS preferences live only in Voice; the current-conversation model control lives only in Chat Configuration; conversation browsing/management lives only in Chat History.
- **FR-013**: Chat Configuration MUST present its contents as clearly labeled, distinct items (the current-conversation model control, an entry point to AI Providers, an entry point to Voice) rather than a single undifferentiated list, so each destination is independently reachable and verifiable.
- **FR-014**: The system MUST preserve every currently-working save/persist path for every relocated or newly-hosted control: the current-conversation model control auto-persists per-conversation exactly as the prior in-chat switcher did; the AI Providers and Voice tabs' existing save behavior is unmodified since their controls are not being altered, only linked to.

### Key Entities

- **Current-Conversation Model Selection**: The AI provider/model actively used by the conversation a user has open right now, changeable from the Chat Configuration hub. Distinct from the "default provider/model" preference (already covered by the existing AI Providers tab), which only affects conversations started after the change.
- **Chat History Entry**: A user's existing conversation (with its favorite/pinned/archived/deleted state, title, and timestamps), now browsed and reopened from the standalone Chat History Settings section rather than from an in-workspace panel. Not a new data entity; the existing conversation list relocated to a new, independent Settings surface.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can locate the Chat Configuration hub and reach the model, voice, and STT/TTS settings it points to in under 20 seconds, without needing to open the chat workspace.
- **SC-002**: 100% of configuration and history-browsing controls present in the chat workspace before this change continue to work after the change, whether relocated to Chat Configuration, relocated to Chat History, or kept in the workspace as an everyday control.
- **SC-003**: The live provider/model switcher and the conversation history panel are no longer visible in the default chat workspace view, while the number of clicks required to reach either relocated destination from within the workspace does not exceed two.
- **SC-004**: Zero previously-saved user preferences (default model, voice settings, device selection) and zero existing conversations are lost or become unreachable as a result of this reorganization, verified across all existing users' saved data.

## Assumptions

- The existing "AI Providers" and "Voice" Settings tabs are not modified by this feature beyond being linked to from the new Chat Configuration hub; their internal controls, layout, and save behavior stay exactly as they are today.
- "Chat/agent configuration" and "other AI-agent preferences" from the original request are satisfied by Chat Configuration's entry points into AI Providers and Voice plus the current-conversation model control; the dedicated Agents feature (agent creation/management) is a separate, already-existing part of the product and remains out of scope for this relocation.
- Which AI models are available for selection anywhere in Chat Configuration is governed entirely by the existing, separate AI provider/model catalog management capability used by super users/administrators; this feature only consumes that curated list and does not change how models are curated.
- Chat History is intentionally a standalone Settings section, independent of Chat Configuration, satisfying the original request's "chat history" element as its own item rather than as a sub-part of Chat Configuration.
- The existing account/settings dropdown/menu mechanism (used both from the standalone Settings page and from within the chat workspace) is the correct integration point for the new Chat Configuration and Chat History destinations, per the request's explicit instruction to integrate into "the existing user settings/dropdown architecture."
- No new backend preference storage is required for the AI Providers, Voice, or current-conversation model controls; only the current-conversation model control is a genuinely new UI surface (its underlying per-conversation model data already exists today). Chat History is a relocation of the existing conversation list's UI, not a new data feature.
- Removing the live in-conversation provider/model switcher and the in-workspace conversation-history panel from the chat toolbar are intentional behavior changes confirmed by the requester: reaching full model/voice management or history browsing now requires a trip to Settings (except for the lightweight current-conversation switch, which stays one hop away via Chat Configuration), in exchange for a cleaner default workspace view.
- Mobile/responsive behavior for both new sections follows the same standard already applied to the rest of the Settings experience.
