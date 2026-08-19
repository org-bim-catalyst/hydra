---

description: "Task list for Floating Chat Assistant Redesign (026-floating-chat-assistant)"

---

# Tasks: Floating Chat Assistant Redesign

**Input**: Design documents from `specs/026-floating-chat-assistant/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/chat-widget-components.md, contracts/voice-preference-api.md, quickstart.md

**Tests**: Included. Constitution §10 requires tests for new/changed behavior in the same PR, and this codebase's existing convention pairs every component with a `*.test.tsx` (and, where interactive, a `*.a11y.test.tsx` via jest-axe) and every Application handler with an xUnit test — this feature follows that convention throughout, with extra emphasis on `useVoiceRecorder` (data-flow-critical: research.md #2/#9) and the widget's own a11y coverage (it does not inherit `CircularAction`'s, per research.md #9).

**Organization**: Tasks are grouped by user story (spec.md) to enable independent implementation and testing of each story.

## Path Conventions

Full-stack feature. Frontend paths are relative to `src/AskLucy.Web/ClientApp/` unless stated otherwise. Backend paths are relative to the repository root (`src/AskLucy.Domain`, `src/AskLucy.Application`, `src/AskLucy.Persistence`, `src/AskLucy.Web`, `tests/AskLucy.*.Tests`).

---

## Phase 1: Setup

**Purpose**: One shared data source — language codes, display labels, and flag glyphs — needed starting with User Story 2 (the Expanded header's language indicator) and reused again by User Story 4 (Chat Configuration's new control), so it's extracted once up front rather than duplicated or retrofitted later.

- [X] T001 [P] Create `src/features/chat/languageOptions.ts` exporting `SUPPORTED_LANGUAGES: { code, label }[]` (extracted unchanged from `src/features/chat/components/LanguageSelector.tsx`'s existing `LANGUAGES` array: `en`/`ar`/`es`/`fr`/`de`) and a `LANGUAGE_FLAGS: Record<string, string>` code→emoji map (research.md #6) — `LanguageSelector.tsx` temporarily re-imports `SUPPORTED_LANGUAGES` from here; no behavior change yet

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Retire the old chat entry point and stand up the new widget's container/shell so User Stories 1 and 2 have something real to fill in.

**⚠️ CRITICAL**: No user story work in Phase 3 onward can begin until this phase is complete.

- [X] T002 Remove the old chat `ControlDefinition` (`chatControl`) object and its `FloatingPanel`/`CircularAction` wiring from `src/features/chat/pages/ChatPage.tsx`; delete `src/features/chat/components/AssistantPanel.tsx` and `AssistantPanel.test.tsx` (data-model.md "Removed" — its role is absorbed into the new `ExpandedChatPanel` header, built in Phase 4)
- [X] T003 [P] Create `src/features/chat/components/ChatAssistantWidget.tsx` — the new top-level container per contracts/chat-widget-components.md's `ChatAssistantWidgetProps`, reading/writing `workspaceOverlayStore.expandedControlId`/`toggle('chat')`/`markUnread('chat')` directly (research.md #1); both `CollapsedChatControl` and `ExpandedChatPanel` (stubbed as empty `Box`es in this task — real content lands in Phase 3/4) stay **mounted simultaneously**, toggling visibility via `theme.transitions`-driven `Collapse`/`sx` rules (research.md #7) — never a conditional/ternary render that would unmount `ExpandedChatPanel` (and therefore `ConversationView`) on every collapse, which would silently break the "don't lose an in-progress conversation" guarantee (contracts/chat-widget-components.md). Also owns the container-level `Escape`-collapses-and-returns-focus-to-handle keydown handler, mirroring `CircularAction.tsx`'s existing pattern (research.md #9)
- [X] T004 Mount `ChatAssistantWidget` inside `WorkspaceOverlay`'s `children` slot in `src/features/chat/pages/ChatPage.tsx`, alongside `HomeProjectCard`/`AiPresenceCard`, passing `chatId`, `onNewChat` (existing `handleNewChat`, unchanged), `onChatCreated`, `language`/`onLanguageChange`, and `tts` (depends on T002, T003)
- [X] T005 [P] Unit tests for `ChatAssistantWidget`'s expand/collapse wiring in `src/features/chat/components/ChatAssistantWidget.test.tsx` — activating the handle calls `workspaceOverlayStore.toggle('chat')`; expanding a different control (e.g. `layers`) collapses chat, preserving spec 024's FR-015 (depends on T003)

**Checkpoint**: Old chat control is gone; the new shell is wired into `ChatPage.tsx` and mutually exclusive with the rest of the shell, but renders empty placeholders. User Story 1 and 2 work can now begin (in parallel).

---

## Phase 3: User Story 1 - Arriving to an unobstructed, collapsed assistant (Priority: P1) 🎯 MVP

**Goal**: The Collapsed state — a narrow vertical strip with a handle, real-time voice analyzer, Push-to-Talk, Continuous Listening toggle, Mute Agent, and status indicator — is the default on load and never obstructs the Studio viewer.

**Independent Test**: Load `/studio` fresh; confirm the widget renders only as the narrow Collapsed control with the viewer fully visible/unobstructed behind it, and that the analyzer visibly shifts between Idle/Processing/Speaking states.

### Implementation for User Story 1

- [X] T006 [P] [US1] Create `VoiceAnalyzer.tsx` in `src/features/chat/components/VoiceAnalyzer.tsx` — presentational vertical bar/waveform visualization per contracts/chat-widget-components.md's `VoiceAnalyzerProps`, polling `getIntensity()` via `requestAnimationFrame` (never React state per frame), visually distinguishing `idle`/`processing`/`speaking`/`listening` (FR-004)
- [X] T007 [P] [US1] Create `CollapsedVoiceControls.tsx` in `src/features/chat/components/CollapsedVoiceControls.tsx` — vertical icon-stack layout consuming the same `VoiceControlsProps` `VoiceControlBar` already takes (Push-to-Talk, Continuous toggle, Mute only for now — the contract's `recording` field is accepted but renders nothing until User Story 5 wires it; callers pass an idle-only stub until then) (research.md #10, contracts)
- [X] T008 [P] [US1] Tests for `VoiceAnalyzer` in `VoiceAnalyzer.test.tsx` and `VoiceAnalyzer.a11y.test.tsx` — all four states render distinguishably; zero jest-axe violations
- [X] T009 [US1] Implement `CollapsedChatControl.tsx` in `src/features/chat/components/CollapsedChatControl.tsx` per contracts/chat-widget-components.md's `CollapsedChatControlProps` — expand handle, `VoiceAnalyzer`, `CollapsedVoiceControls`, and a minimal status indicator + short text label (Idle/Processing/Speaking/Listening) — nothing else (FR-003). The expand handle is a native `<button>` with `aria-expanded={expanded}`/`aria-controls`/`aria-label` (Enter/Space activation is native, no extra handling needed), matching `CircularAction`'s existing disclosure contract (research.md #9). Size via MUI breakpoint `sx` rules (`xs`/`sm`/...), not fixed pixels, so it never overlaps the `top-cluster` at mobile widths (research.md #11) (depends on T006, T007)
- [X] T010 [US1] Replace `ChatAssistantWidget`'s Collapsed placeholder (T003) with real `CollapsedChatControl`, sourcing `analyzerState`/`getIntensity` from `isStreaming` (Processing), `tts.isSpeaking` + `tts.getIntensity()` (Speaking), and `recognition.isListening` (Listening — Continuous only for now; Push-to-Talk's recorder-driven listening lands in User Story 5) per research.md #3 (depends on T009)
- [X] T011 [P] [US1] Tests for `CollapsedChatControl` in `CollapsedChatControl.test.tsx` — renders exactly handle/analyzer/three voice controls/status, nothing more (FR-003 AC2); layout assertions confirming it stays narrow/does not claim full-viewport space (FR-005)
- [X] T012 [P] [US1] Accessibility test `CollapsedChatControl.a11y.test.tsx` (jest-axe, zero violations) — independent coverage since this widget is not built on `CircularAction` (research.md #9)
- [X] T013 [P] [US1] Update `ChatPage.test.tsx`/`ChatPage.a11y.test.tsx`: on fresh mount, the chat widget renders Collapsed by default and the design viewer remains unobstructed (FR-002/FR-005)

**Checkpoint**: Collapsed state is fully functional and independently testable. Activating its handle toggles `expandedControlId`, but the Expanded panel is still a placeholder until User Story 2 lands (both are P1 and together form this feature's MVP).

---

## Phase 4: User Story 2 - Expanding into the full conversation (Priority: P1) 🎯 MVP

**Goal**: Activating the handle smoothly reveals the full conversation panel — header with identity/status/language flag, message history, composer, and voice controls — with the analyzer hidden.

**Independent Test**: From a Collapsed widget, activate the handle; confirm the panel expands to show conversation history, input, and voice controls with the analyzer gone; collapse it again and confirm the workspace beneath was never disturbed.

### Implementation for User Story 2

- [X] T014 [P] [US2] Create `ActiveLanguageFlag.tsx` in `src/features/chat/components/ActiveLanguageFlag.tsx` per contracts — read-only circular flag glyph looked up from `languageOptions.ts`'s `LANGUAGE_FLAGS` (T001); an unrecognized/`null` code falls back to a sensible default glyph (Edge Cases)
- [X] T015 [P] [US2] Unit test `ActiveLanguageFlag.test.tsx` — correct flag per supported code; fallback behavior for an unknown code
- [X] T016 [US2] Implement `ExpandedChatPanel.tsx` in `src/features/chat/components/ExpandedChatPanel.tsx` per contracts/chat-widget-components.md's `ExpandedChatPanelProps` — header row: back/collapse control (`onCollapse`, a native `<button>` with `aria-label="Collapse"`, matching the expand handle's ARIA-disclosure symmetry — research.md #9), `LucyPortrait` + "Ask Lucy" + "Online" status text, `ActiveLanguageFlag` (fed by the existing `language` prop — real persisted-preference seeding lands in User Story 4); no new-chat control yet (added in User Story 3). Size the panel via MUI breakpoint `sx` rules, mirroring `FloatingPanel`'s existing `width: { xs: 'min(92vw, 380px)', sm: 400 }` pattern (research.md #11) (depends on T014)
- [X] T017 [US2] Relocate `ConversationView`'s existing rendering (its own internal toolbar, message list, composer, `VoiceControlBar` footer) from the old `FloatingPanel`+`AssistantPanel` wrapper into `ExpandedChatPanel`'s body as `children`, unchanged internally (FR-026/FR-027) — depends on T016
- [X] T018 [US2] Replace `ChatAssistantWidget`'s Expanded placeholder (T003) with real `ExpandedChatPanel`, hiding `VoiceAnalyzer` while expanded (FR-007). Confirm `ConversationView` stays mounted (not remounted) across the toggle, per T003's `Collapse`-based visibility approach — the actual test for this is T019 (depends on T016, T017)
- [X] T019 [P] [US2] Tests for `ExpandedChatPanel` in `ExpandedChatPanel.test.tsx` — header shows identity/status/flag; `VoiceAnalyzer` is not present; collapsing keeps the conversation mounted-but-hidden rather than remounting it (same guarantee `FloatingPanel`'s `Collapse`+`inert` already provided); sending a message and receiving a streamed reply still work end-to-end through the relocated `ConversationView`
- [X] T020 [P] [US2] Accessibility test `ExpandedChatPanel.a11y.test.tsx` (jest-axe) — initial focus moves inside on open without trapping it, mirroring `FloatingPanel`'s existing focus-management effect (research.md #9)
- [X] T021 [P] [US2] Update `ChatPage.test.tsx`/`ChatPage.a11y.test.tsx`: full expand/collapse round-trip works; the Studio viewer and other contextual controls remain interactive throughout (FR-011); transition is animated, not instant, except under reduced motion (FR-009)

**Checkpoint**: User Stories 1 and 2 together deliver the full MVP — the widget's two-state shell is complete, and every pre-existing chat capability (send/stream/attach/etc.) is provably preserved through the relocation.

---

## Phase 5: User Story 3 - Starting fresh by default, with a minimal manual option (Priority: P2)

**Goal**: No prominent "+ New chat" button anywhere (already true structurally since `AssistantPanel` is gone); a minimal icon-only control in the Expanded header lets a user deliberately start fresh mid-session.

**Independent Test**: Load `/studio`, confirm a new empty conversation is already active with no prominent "+ New chat" control anywhere; confirm the minimal icon starts a fresh conversation on demand; confirm prior conversations remain reachable via Chat History in Settings.

### Implementation for User Story 3

- [X] T022 [US3] Add a minimal icon-only new-chat `IconButton` to `ExpandedChatPanel`'s header (`src/features/chat/components/ExpandedChatPanel.tsx`), positioned alongside the flag/back control, wired to the existing `onNewChat` prop (`handleNewChat`, unchanged) (FR-014) (depends on T016)
- [X] T023 [P] [US3] Test in `ChatPage.test.tsx` asserting a fresh mount shows an empty/greeting conversation with no manual action required (FR-013) — `activeChatId` starts `null` (existing spec-025 behavior), now asserted explicitly as part of this feature's contract
- [X] T024 [P] [US3] Tests for the new icon in `ExpandedChatPanel.test.tsx` — activating it makes a new empty conversation active without a page reload, and the conversation it replaced remains reachable via Chat History (FR-014 AC4)
- [X] T025 [P] [US3] Assertion in `ChatPage.test.tsx`/`ExpandedChatPanel.test.tsx` confirming no text-labeled "+ New chat" control exists anywhere, in either widget state (FR-012)

**Checkpoint**: Manual new-chat is still possible without the old prominent button; auto-new-session behavior is explicitly verified.

---

## Phase 6: User Story 4 - Seeing the active language as a flag, changed only from Settings (Priority: P2)

**Goal**: The inline language dropdown is gone; a persisted `defaultLanguage` preference (new) drives the header flag and can only be changed from Chat Configuration.

**Independent Test**: Confirm no language dropdown exists in either widget state; confirm the flag reflects the current language; change the default language in Chat Configuration and confirm the flag updates on revisit.

### Backend for User Story 4

- [X] T026 [P] [US4] Add a nullable `DefaultLanguage` field + setter to `UserVoicePreference` in `src/AskLucy.Domain/Ai/UserVoicePreference.cs` per data-model.md
- [X] T027 [P] [US4] Add `DefaultLanguage` to `SaveUserVoicePreferenceCommand`/`SaveUserVoicePreferenceCommandHandler`, `GetUserVoicePreferenceQueryHandler`, and `UserVoicePreferenceDto` in `src/AskLucy.Application/Ai/` per contracts/voice-preference-api.md
- [X] T028 [US4] Add a FluentValidation rule in `SaveUserVoicePreferenceCommandValidator` rejecting a `DefaultLanguage` outside `en`/`ar`/`es`/`fr`/`de` with a specific message, never silently coerced (data-model.md validation rule) (depends on T027)
- [X] T029 [US4] Add an EF Core migration for the new nullable column and update `src/AskLucy.Persistence/Configurations/UserVoicePreferenceConfiguration.cs` (depends on T026)
- [X] T030 [P] [US4] Thread `DefaultLanguage` through `SaveVoicePreferenceRequest` and the `GetVoicePreferences`/`SaveVoicePreferences` actions in `src/AskLucy.Web/Controllers/v1/AiController.cs` per contracts/voice-preference-api.md (depends on T027)
- [X] T031 [P] [US4] Backend unit tests in `tests/AskLucy.Application.Tests/Ai/`: `SaveUserVoicePreferenceCommandValidator` rejects an unsupported language code; `SaveUserVoicePreferenceCommandHandler`/`GetUserVoicePreferenceQueryHandler` correctly round-trip `DefaultLanguage` (NSubstitute-faked repository) (depends on T028)
- [X] T032 [P] [US4] Backend integration test in `tests/AskLucy.Web.Tests/Ai/` round-tripping `PUT`/`GET /api/v1/ai/voice/preferences` with `defaultLanguage` set against a real test database (depends on T029, T030)

### Frontend for User Story 4

- [X] T033 [P] [US4] Add `defaultLanguage` to the `UserVoicePreference` TS interface and `getPreferences`/`savePreferences` calls in `src/features/chat/api/voiceApi.ts` per contracts/voice-preference-api.md (depends on T030)
- [X] T034 [US4] Add `defaultLanguage` to `useVoicePreferencesStore`'s `DEFAULTS`, state shape, and `persist` `partialize` in `src/features/chat/voice/voicePreferencesStore.ts` (depends on T033)
- [X] T035 [US4] Seed `ConversationView`'s local `language` state from `voicePreferences.defaultLanguage ?? 'en'` on mount in `src/features/chat/pages/ChatPage.tsx`, mirroring the existing `aiPreference`-seeding `useEffect` pattern (data-model.md) (depends on T034)
- [X] T036 [US4] Delete `src/features/chat/components/LanguageSelector.tsx` and its test, and remove its import/usage from `ConversationView`'s toolbar in `ChatPage.tsx` (FR-015) (depends on T001, T035)
- [X] T037 [US4] Add a "Default language" `<Select>` control (built from `languageOptions.ts`'s `SUPPORTED_LANGUAGES`) to `src/features/settings/pages/ChatConfigurationTab.tsx`, calling `useVoicePreferencesStore`'s `update({ defaultLanguage })` on change, with visible save-error feedback per constitution §2.VIII (depends on T034)
- [X] T038 [P] [US4] Tests for the new control in `src/features/settings/pages/ChatConfigurationTab.test.tsx` — saves successfully, reflects the persisted value on load, surfaces a visible error on a failed save (depends on T037)
- [X] T039 [P] [US4] Update `src/features/settings/pages/ChatConfigurationTab.a11y.test.tsx` for the new control (jest-axe) (depends on T037)
- [X] T040 [P] [US4] Update `ActiveLanguageFlag`/`ExpandedChatPanel`/`ChatPage` tests confirming the header flag reflects a `defaultLanguage` changed in Chat Configuration on the next view (FR-017 AC4) (depends on T035, T037)

**Checkpoint**: Language is flag-only in the widget, backed by a real persisted preference; the old dropdown is fully removed, not just hidden.

---

## Phase 7: User Story 5 - Reviewing a voice message before it's transcribed (Priority: P2)

**Goal**: Push-to-Talk switches to a discrete record → review (waveform, no live transcript) → cancel/accept-to-transcribe flow; nothing is transmitted before explicit accept; Continuous Listening is untouched.

**Independent Test**: Start a Push-to-Talk recording, confirm a live waveform with no live transcript, confirm cancel discards with zero network activity, confirm accept is the only action that calls `transcribeAudio`; confirm Continuous Listening behaves exactly as before.

### Implementation for User Story 5

- [X] T041 [P] [US5] Implement `useVoiceRecorder.ts` in `src/features/chat/voice/useVoiceRecorder.ts` — `MediaRecorder` + `AnalyserNode` on the same `getUserMedia` `MediaStream`, `phase` state machine (`idle`/`recording`/`reviewing`/`transcribing`), `start()`/`finish()`/`cancel()`/`accept()`, ref-based `getIntensity()`, `error`/`permissionState` (reusing `MicrophonePermissionState` from `useSpeechRecognition.ts`) per data-model.md and research.md #2
- [X] T042 [P] [US5] Unit tests for `useVoiceRecorder` in `useVoiceRecorder.test.ts` — `accept()` is the only path that ever calls the mocked `transcribeAudio`; `cancel()` from `recording` or `reviewing` never calls it; `finish()` alone never transmits anything; an external collapse-triggered `cancel()` discards mid-recording/review state (FR-024) (depends on T041)
- [X] T043 [US5] Make the `recording` field live in `CollapsedVoiceControls.tsx` and `VoiceControlBar.tsx` per contracts/chat-widget-components.md — render the finish/cancel/send controls and live waveform when `recording.phase !== 'idle'`, for Push-to-Talk only, using identical semantics in both layouts (FR-023) (depends on T007, T041)
- [X] T044 [US5] Wire `useVoiceRecorder` into `ConversationView` (`src/features/chat/pages/ChatPage.tsx`) for `conversationMode === 'PushToTalk'`, replacing `useSpeechRecognition`'s role for this mode only (Continuous keeps using `useSpeechRecognition` unchanged); on `accept()` success, append the returned transcript into the composer exactly as `ChatComposer`'s existing file-attach transcript flow does today. **Also wire `ChatAssistantWidget`'s collapse action (T003) to call `useVoiceRecorder.cancel()` whenever `phase !== 'idle'`** at the moment of collapse, so a mid-recording/review collapse discards the buffer rather than leaving it running invisibly (FR-024) (depends on T043)
- [X] T045 [US5] Feed `useVoiceRecorder`'s recording state/`getIntensity` into `VoiceAnalyzer`'s Listening state for Push-to-Talk, extending T010's state-mapping (which today only covers Continuous) (depends on T044)
- [X] T046 [P] [US5] Update `CollapsedVoiceControls.test.tsx`/`VoiceControlBar.test.tsx` confirming the finish/cancel/send UI appears/disappears correctly and behaves identically between both layouts (FR-023) (depends on T043)
- [X] T047 [P] [US5] Integration-style test in `ChatPage.test.tsx` covering the full Push-to-Talk flow end-to-end: start → waveform, no transcript → finish → reviewing (assert `transcribeAudio` not yet called) → accept (assert called exactly once) → transcript used in the composer; the cancel path (assert never called at all); and **collapsing `ChatAssistantWidget` mid-recording and mid-review (assert `transcribeAudio` never called and the widget returns to idle Collapsed, per FR-024 and T044's collapse-wiring)** (FR-019–FR-022, FR-024) (depends on T044)
- [X] T048 [P] [US5] Confirm-via-test that Continuous Listening is unaffected: existing Continuous-mode tests in `ChatPage.test.tsx`/`VoiceControlBar.test.tsx` still pass unmodified, plus an explicit assertion that no recording-review UI ever renders while `conversationMode === 'Continuous'` (FR-025) (depends on T043)

**Checkpoint**: Push-to-Talk's new review flow is live and provably privacy-safe (no transmission before accept) in both widget states; Continuous Listening is provably unchanged.

---

## Phase 8: User Story 6 - No standalone image-generation button (Priority: P3)

**Goal**: The "Generate image" button is gone from the composer; nothing replaces it in this feature.

**Independent Test**: Inspect the Expanded state's composer actions; confirm no "Generate image" control exists anywhere.

### Implementation for User Story 6

- [X] T049 [US6] Remove the "Generate image" `IconButton`/`handleGenerateImage` trigger from `ConversationView`'s toolbar in `src/features/chat/pages/ChatPage.tsx` (FR-018) — the underlying `sendImage`/backend generation capability itself is left untouched, per spec.md Assumptions
- [X] T050 [P] [US6] Update `ChatPage.test.tsx`/`ExpandedChatPanel.test.tsx` confirming no "Generate image" control renders anywhere (FR-018 AC1)

**Checkpoint**: All six user stories are independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final validation across all six stories together.

- [ ] T051 Run all `quickstart.md` scenarios end-to-end against a locally running instance with real microphone access, including the network-panel verification in Scenario 5 (no transcription request before accept) — **requires a live browser + microphone session; not executable from this headless environment, see Completion Report**
- [X] T052 [P] Full-page jest-axe sweep of `/studio` covering both Collapsed and Expanded states plus the recording-review flow, in `ChatPage.a11y.test.tsx`
- [X] T053 `git grep` the codebase for stale references to `AssistantPanel`/`LanguageSelector`/the old chat `FloatingPanel`/`ControlDefinition` wiring, updating or removing any leftover doc comments that describe the pre-redesign shape
- [ ] T054 [P] Manually verify reduced-motion behavior for the widget's expand/collapse transition (FR-009) using the existing `usePrefersReducedMotion` convention, per research.md #7 — code-level wiring confirmed (`App.tsx` → `createAppTheme(mode, prefersReducedMotion)` → `theme/tokens/motion.ts` collapses durations to 0, consumed by the `Grow` wrapper's default duration); **visual confirmation in a real browser with OS reduced-motion enabled still outstanding, see Completion Report**
- [X] T055 [P] Behavioral keyboard/screen-reader test (not jest-axe — an interaction test) in `ChatPage.test.tsx` (co-located with the rest of the widget's behavioral coverage per `ChatAssistantWidget.test.tsx`'s own documented scope, since the widget itself is a thin positioning shell — see that file's docstring) covering the full ARIA-disclosure contract from T003/T009/T016: handle is reachable via Tab (not `tabindex="-1"`); `Enter` and `Space` both expand it; `Tab` continues moving focus forward into the revealed panel's content; `Escape` collapses it and returns focus to the handle; `aria-expanded`/mount-state is asserted correct at every transition (FR-010/SC-007, research.md #9) — added per `/speckit-analyze` finding D1
- [ ] T056 [P] Responsive verification across mobile/tablet/desktop widths for `CollapsedChatControl` and `ExpandedChatPanel` (device-emulation or real-viewport resize) — confirm the Collapsed strip never overlaps the `top-cluster` account/theme controls and the Expanded panel stays fully within the viewport at every width, mirroring spec 024's quickstart Scenario 5 (SC-001, research.md #11) — added per `/speckit-analyze` finding E2. Structural review confirms this by construction: `ChatAssistantWidget` anchors bottom-right while `top-cluster`/`right-stack` anchor top-end (`WorkspaceOverlay.tsx`), so they cannot occupy the same screen region; `ExpandedChatPanel` uses `width: { xs: 'min(92vw, 380px)', sm: 400 }` / `height: { xs: 'min(70vh, 600px)', sm: 560 }`, mirroring `FloatingPanel`'s already-shipped, already-verified (spec 024) viewport-relative sizing pattern. **Visual/device-emulation confirmation in a real browser still outstanding, see Completion Report**

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational (Phase 2). No dependency on other stories.
- **User Story 2 (Phase 4)**: Depends on Foundational (Phase 2) and on T001 (Setup, for `ActiveLanguageFlag`). Can proceed in parallel with User Story 1 — they touch different new files (`CollapsedChatControl.tsx` vs. `ExpandedChatPanel.tsx`).
- **User Story 3 (Phase 5)**: Depends on User Story 2 (T016 — the header it adds its icon to must exist).
- **User Story 4 (Phase 6)**: Depends on Foundational (Phase 2), Setup (T001), and User Story 2 (T016 — the header/flag it wires real data into must exist). Its backend tasks (T026–T032) have no frontend dependency and can start as soon as Phase 2 is done, in parallel with US1–US3.
- **User Story 5 (Phase 7)**: Depends on User Story 1 (T007 — `CollapsedVoiceControls` it extends) and User Story 2 (T017 — `VoiceControlBar`'s relocated usage).
- **User Story 6 (Phase 8)**: Depends on User Story 2 (T017 — the toolbar it removes a button from).
- **Polish (Phase 9)**: Depends on all six user stories being complete.

### Within Each User Story

- Presentational sub-components before the story's composing component (e.g., US1: T006/T007 before T009).
- Backend before the frontend code that calls it (US4: T026–T032 before T033–T040).
- Tests for a component are written alongside/after that component's implementation task, per this codebase's existing convention (not strict TDD).
- Story complete and checkpointed before a dependent story (US3, US5, US6 on US2; US4 partially) begins its own work.

### Parallel Opportunities

- T001 (Setup) has no dependencies and can start immediately.
- T003 and T005 (widget container + its test) can proceed in parallel once T002 lands.
- Once Phase 2 is complete, User Story 1 (Phase 3) and User Story 2 (Phase 4) can be implemented in parallel — different new files, no shared dependency beyond the Phase 2 shell.
- User Story 4's backend tasks (T026–T032) can proceed in parallel with User Story 1/2/3's frontend work — different codebases (`AskLucy.Domain`/`Application`/`Persistence`/`Web` vs. `ClientApp`).
- User Story 6 (Phase 8) is small and independent of User Story 4/5's internals — only needs User Story 2's relocated toolbar to exist.

---

## Parallel Example: User Story 1 + User Story 2 (once Foundational is done)

```bash
# User Story 1:
Task: "Create VoiceAnalyzer.tsx in src/features/chat/components/VoiceAnalyzer.tsx"
Task: "Create CollapsedVoiceControls.tsx in src/features/chat/components/CollapsedVoiceControls.tsx"

# User Story 2, in parallel:
Task: "Create ActiveLanguageFlag.tsx in src/features/chat/components/ActiveLanguageFlag.tsx"
Task: "Unit test ActiveLanguageFlag.test.tsx"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
3. Complete Phase 3 + Phase 4: User Stories 1 and 2 — the full Collapsed/Expanded shell, all existing chat behavior preserved.
4. **STOP and VALIDATE**: Run quickstart.md Scenarios 1–2 independently in a real browser.
5. Deploy/demo if ready — this is the redesign's visual core, even before the behavioral changes (US3–US6) land.

### Incremental Delivery

1. Setup + Foundational → shell ready, not yet demoable.
2. Add User Story 1 + 2 → full two-state widget live → test independently → demo (MVP!).
3. Add User Story 3 → minimal new-chat option → test independently → demo.
4. Add User Story 4 → language flag + Chat Configuration control (backend can proceed in parallel with steps 2–3) → test independently → demo.
5. Add User Story 5 → Push-to-Talk review flow → test independently (including the network-panel privacy check) → demo.
6. Add User Story 6 → Generate-image button removed → test independently → demo.
7. Polish → full quickstart.md pass, full-page a11y sweep, stale-reference cleanup.

### Parallel Team Strategy

With multiple developers, once Phase 2 (Foundational) is done:

- Developer A: User Story 1 (Phase 3).
- Developer B: User Story 2 (Phase 4).
- Developer C: User Story 4's backend (T026–T032), independent of frontend progress.
- User Story 3, 5, and 6 are picked up once User Story 2 is checkpointed complete (all three depend on its header/toolbar existing).

---

## Notes

- `[P]` tasks touch different files with no blocking dependency on another incomplete task in the same batch.
- `[Story]` label maps each task to its user story for traceability.
- User Stories 3, 5, and 6 are the deliberate exceptions to "stories are independent" — each needs User Story 2's `ExpandedChatPanel` to exist first, matching how the spec itself frames US1/US2 as the foundational P1 pair the rest build on.
- The single highest-risk task in this feature is T041–T042 (`useVoiceRecorder`) — get its tests (accept-is-the-only-transmission-path) green before wiring it into the real UI in T043–T044, since this is the feature's core privacy guarantee (FR-019/FR-021/FR-022).
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently before continuing.
- T003/T009/T016/T018/T044/T047 were strengthened, and T055/T056 added, via `/speckit-analyze` remediation (findings D1, E1, C1, E2, F1) — T055/T056 are numbered after T054 to avoid renumbering existing tasks, but both are pure verification passes over behavior already required by the (now-amended) implementation tasks in Phases 2–7, not new scope.
