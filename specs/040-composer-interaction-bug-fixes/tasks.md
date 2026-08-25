# Tasks: Composer Interaction Bug Fixes

**Input**: Design documents from `/specs/040-composer-interaction-bug-fixes/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: Included — this codebase's established convention is thorough test coverage per change
(existing `ChatComposer.test.tsx`/`ChatPage.test.tsx`/backend handler+middleware tests).

**Organization**: One phase per user story, in the delivery order spec.md's Assumptions section
commits to (US1 → US7). Each phase is its own independently mergeable PR — no phase depends on
another phase's code (only Phase 10/Polish runs after all are merged).

**Post-`/speckit-analyze` remediation**: One MEDIUM finding (G1) — `RecordingReviewControls.tsx`
was about to get its first-ever test file (T008) without an accompanying accessibility check,
despite US3 changing its interactive control order and constitution §7/§10 requiring automated
a11y checks as a merge gate for user-facing UI changes. Added T008a to close the gap.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Maps to spec.md's user stories (US1–US7)

## Phase 1: Setup

N/A — existing project, no new dependencies, no scaffolding needed.

## Phase 2: Foundational

N/A — each user story is self-contained within the files it touches; no shared blocking
prerequisite work exists across stories.

---

## Phase 3: User Story 1 - Empty-state button positions (Priority: P1) 🎯 MVP

**Goal**: Attachment pinned left, mic + continuous-entry pinned right, in the empty composer state.

**Independent Test**: Open the composer with no text typed; verify DOM/visual order and the flex
spacer's position per quickstart.md's US1 section.

- [X] T001 [US1] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`, move the `<Box sx={{ flex: 1 }} />` spacer so it sits between the attachment button and the mic/continuous-entry group within the `composerVisualState === 'empty'` rendering, instead of after the entire empty/recording/continuous block.
- [X] T002 [US1] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx`, add/extend a test asserting that in the empty state the attachment button precedes the spacer and the mic + continuous-conversation buttons follow it (query by role/order; assert the spacer element sits between the leading and trailing groups).

**Checkpoint**: Empty-state layout matches Figure 1; ship as its own PR before starting US2.

---

## Phase 4: User Story 2 - Typing-state keeps attach + mic (Priority: P1)

**Goal**: Attachment + mic remain visible and functional while typing; only continuous-entry is
replaced by Send.

**Independent Test**: Type text into the composer; verify attach/mic/Send are all present and mic
still starts a recording whose transcript appends after the existing text (quickstart.md US2).

- [X] T003 [US2] In `ChatComposer.tsx`, place the `flex:1` spacer so it sits between the attachment button and the trailing group for BOTH empty and typing states (shared spacer). For PushToTalk typing the trailing group is mic+Send (Figure 2: attach → spacer → mic → send); for Continuous typing the trailing group is Send-only and the mic is NOT rendered (Figure 5: attach → spacer → send). The mic button must remain the same persistent DOM element across `empty`/`recording`/`typing`-PushToTalk (specs/033 invariant). Also fix the hold-to-talk waveform to use `sx={{ flex: 1 }}` instead of the fixed `width: 64` (Figure 9 shows it filling the row).
- [X] T004 [US2] In `ChatComposer.tsx`, ensure the Send button continues to render for `'typing'` (both PushToTalk and Continuous sub-variants) at the trailing edge.
- [X] T005 [US2] In `ChatComposer.test.tsx`, update/add tests: (a) PushToTalk typing DOM order is attach → [spacer] → mic → send (mic in trailing group, FR-002a); (b) Continuous typing shows attach + send only — no mic (FR-002b); (c) tapping mic while text is present starts a recording and transcript appends after existing text; (d) the mic `IconButton` is the same DOM node across a press starting in `'typing'` and transitioning into `'recording'` (specs/033 invariant).

**Checkpoint**: Typing state matches Figure 2 and mic stays fully functional; ship as its own PR.

---

## Phase 5: User Story 3 - Recording/tap-review button order (Priority: P1)

**Goal**: Cancel renders left of the waveform, finish renders right of it, in the tap-review state.

**Independent Test**: Tap-release the mic to enter tap-review; verify X → waveform → check order
and that both controls still work (quickstart.md US3).

- [X] T006 [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/components/RecordingReviewControls.tsx`, add the optional `middle?: React.ReactNode` prop, widen `placement` to include `'bottom'`, and change the render order to cancel → `middle` → finish (swapped from the current finish-then-cancel, per contracts/composer-layout-contract.md).
- [X] T007 [US3] In `ChatComposer.tsx`, pass the existing live-waveform `<Box>`/`<VoiceAnalyzer>` element as `RecordingReviewControls`'s new `middle` prop instead of rendering it as a separate sibling before `RecordingReviewControls`.
- [X] T008 [P] [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/components/RecordingReviewControls.test.tsx` (confirmed not to exist yet — this is the component's first test file), add tests asserting: cancel renders before `middle` before finish when `middle` is provided; cancel renders immediately before finish (adjacent, swapped order from today) when `middle` is omitted (the `CollapsedVoiceControls` usage).
- [X] T008a [P] [US3] In the same new `RecordingReviewControls.test.tsx`, add a `jest-axe` accessibility check (matching this codebase's established `.a11y.test.tsx` pattern, e.g. `MessageBubble.a11y.test.tsx`) covering both the `middle`-provided and `middle`-omitted render variants — required by constitution §7/§10's automated-a11y-check merge gate, and this is the component's first-ever test coverage of any kind, so there's no pre-existing a11y suite to rely on instead.
- [X] T009 [US3] In `ChatComposer.test.tsx`, add/update a test asserting the tap-review row's DOM order is cancel → waveform → finish, and that both controls still call `onCancelRecording`/`onFinish` correctly (existing behavioral tests must continue passing, only order assertions are new).
- [X] T009a [US3] In `ChatComposer.tsx`, change the tap-review `middle` waveform's `<Box>` from `sx={{ width: 64 }}` to `sx={{ flex: 1 }}` so it fills the available space between cancel and finish in the expanded composer (Figure 3 — waveform spans the gap). Also fix the hold-to-talk waveform to `flex: 1` (Figure 9).

**Checkpoint**: Tap-review order matches Figure 3 with full-width waveform; ship as its own PR.

---

## Phase 6: User Story 4 - Continuous-mode waveform + right-anchored controls (Priority: P2)

**Goal**: A live waveform fills the leading space of the continuous-listening composer row; mute +
exit anchor to the trailing edge.

**Independent Test**: Enter continuous mode and reach idle-listening; verify the waveform renders
and reacts, with mute/exit on the right (quickstart.md US4).

- [X] T010 [US4] In `ChatComposer.tsx`, add the optional `continuousAnalyzer?: { state: VoiceAnalyzerState; getIntensity: () => number }` prop (contracts/composer-layout-contract.md) and, in the `composerVisualState === 'continuous'` branch, render a `<VoiceAnalyzer>` using it at the leading edge (flex-grow) followed by a spacer, then the existing mute and exit `IconButton`s at the trailing edge.
- [X] T011 [US4] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, pass `continuousAnalyzer={{ state: analyzerState, getIntensity: analyzerIntensity }}` (research.md Decision 4 — reusing the values already computed for the Ai presence sphere) into the `ChatComposer` element whenever `conversationMode === 'Continuous'`.
- [X] T012 [P] [US4] In `ChatComposer.test.tsx`, add tests asserting the continuous-listening state renders a waveform element preceding the mute/exit buttons in DOM order, and that omitting `continuousAnalyzer` doesn't crash (renders an idle waveform).
- [X] T013 [US4] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx`, add/update a test asserting `ChatComposer` receives a `continuousAnalyzer` prop matching the existing `analyzerState`/`analyzerIntensity` values once continuous mode is active.

**Checkpoint**: Continuous-mode layout matches Figure 4; ship as its own PR.

---

## Phase 7: User Story 5 - Continuous mode starts listening reliably (Priority: P1)

**Goal**: Continuous mode reliably starts listening once its prerequisites are met, even if they
weren't ready at the moment the mode was activated.

**Independent Test**: Activate continuous mode in a brand-new session before any chat exists;
verify listening starts once a chat/provider/model become available, with no manual workaround
(quickstart.md US5).

- [X] T014 [US5] In `ChatPage.tsx`, add a `useEffect` (research.md Decision 5) watching `[conversationMode, chatId, providerId, modelId, conversationAudio.voiceState]` that calls `conversationAudio.startTurn()` when `conversationMode === 'Continuous' && chatId && providerId && modelId && conversationAudio.voiceState === 'Idle'`.
- [X] T015 [US5] In `ChatPage.test.tsx`, add tests: (a) entering continuous mode before `chatId` exists, then having `chatId`/`providerId`/`modelId` become available, results in `conversationAudio.startTurn()` being called once they're all set; (b) the effect does not call `startTurn()` again if `voiceState` is already not `'Idle'` (no duplicate/overlapping starts); (c) the effect does not fire at all while `conversationMode === 'PushToTalk'`.

**Checkpoint**: Continuous mode never silently fails to start; ship as its own PR.

---

## Phase 8: User Story 6 - Transcription failures surface a classified error (Priority: P1)

**Goal**: A missing/invalid OpenAI credential (or any bare `HttpRequestException`) surfaces the
existing classified, actionable Problem Details response instead of the generic 500.

**Independent Test**: Backend tests confirming both new mappings; optional live check with a
blanked API key in a lower environment (quickstart.md US6).

- [ ] T016 [US6] In `src/AskLucy.Infrastructure/Ai/OpenAIProvider.cs`, update `CreateClient()` to throw `AiProviderAuthenticationException("The OpenAI provider is not configured with an API key.")` when `_options.ApiKey` is null/whitespace, before constructing the `AuthenticationHeaderValue` (contracts/transcription-error-contract.md).
- [ ] T017 [US6] In `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs`, add a `HttpRequestException` case mapping to the same 502 `ai-provider-unavailable` Problem Details shape as the existing `AiProviderUnavailableException` case, placed before the generic `_ => 500` fallback.
- [ ] T018 [P] [US6] In `tests/AskLucy.Infrastructure.Tests/Ai/OpenAIProviderTests.cs` (extend, or create if this exact provider isn't yet under test there), add a test asserting a call that reaches `CreateClient()` (e.g. via `TranscribeAudioAsync`) throws `AiProviderAuthenticationException` when `OpenAIOptions.ApiKey` is null/empty.
- [ ] T019 [P] [US6] In `tests/AskLucy.Web.Tests/Middleware/ProblemDetailsMiddlewareTests.cs`, add a test asserting a bare `HttpRequestException` thrown from an endpoint maps to a 502 response with the `ai-provider-unavailable` problem type.

**Checkpoint**: Transcription (and every other OpenAI-backed call) fails loudly and actionably, never silently; ship as its own PR.

---

## Phase 9: User Story 7 - Consistent bottom-positioned tooltips (Priority: P3)

**Goal**: Every composer/voice-control tooltip appears below its button.

**Independent Test**: Hover every button across all composer states, tap-review, and the Collapsed
widget; verify bottom placement everywhere (quickstart.md US7).

- [ ] T020 [US7] In `ChatComposer.tsx`, add explicit `placement="bottom"` to every `Tooltip` (attach, mic, continuous-entry, mute, exit, Send, and the voice-preferences-unavailable indicator's tooltip).
- [ ] T021 [US7] In `RecordingReviewControls.tsx`, change the default value of the `placement` prop from `'right'` to `'bottom'`.
- [ ] T022 [US7] In `ChatComposer.tsx` and `src/AskLucy.Web/ClientApp/src/features/chat/components/CollapsedVoiceControls.tsx`, remove/update the explicit `placement="right"`/`placement="left"` arguments passed to `RecordingReviewControls` and to `CollapsedVoiceControls`'s own three `Tooltip`s so all resolve to `'bottom'`.
- [ ] T023 [P] [US7] Add/update tests in `ChatComposer.test.tsx`, `RecordingReviewControls.test.tsx`, and `CollapsedVoiceControls.test.tsx` asserting every rendered `Tooltip` resolves a bottom placement (e.g. via the `placement` prop reaching MUI, or the rendered popper's placement data attribute, matching this test suite's existing convention for asserting MUI props).

**Checkpoint**: All tooltips consistent; ship as its own PR (last in sequence).

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Final validation after all seven stories have merged.

- [ ] T024 Run the full frontend suite (`npm run lint` + `npx vitest run`) and the full backend suite (`dotnet format --verify-no-changes` + `dotnet build` + `dotnet test`) once all stories are merged to `main`, confirming no regression across the whole composer/voice feature area.
- [ ] T025 Re-run quickstart.md's seven manual scenarios end-to-end against the integrated app and record results in this file.

---

## Dependencies & Execution Order

- **Setup/Foundational**: N/A.
- **User Stories**: All seven are mutually independent at the code level (different, non-overlapping regions of the same few files, except T006/T007 both touch `RecordingReviewControls.tsx`/`ChatComposer.tsx` within US3 itself). Delivery order is priority + narrative order per spec.md's Assumptions: **US1 → US2 → US3 → US4 → US5 → US6 → US7**, each merged to `main` before the next story's branch is created (so later stories always start from a `main` that already contains earlier fixes, minimizing merge conflicts in the shared `ChatComposer.tsx` file).
- **Polish (Phase 10)**: After all seven stories are merged.

## Parallel Execution Examples

- Within US3: T008/T008a (`RecordingReviewControls.test.tsx`) can be written in parallel with T009 (`ChatComposer.test.tsx`) once T006/T007 land, since they're different files.
- Within US4: T012 and T013 touch different test files and can be written in parallel once T010/T011 land.
- Within US6: T018 and T019 touch different test files/projects and can run in parallel once T016/T017 land.
- Within US7: T023 spans three test files and its sub-parts can be split across those files in parallel.
- **Across stories**: because each story is merged before the next begins (per the Dependencies note above), cross-story parallelism is not used — this is a deliberate choice to satisfy "push and merge them one by one," not a technical limitation.

## Implementation Strategy

**MVP**: US1 alone is a complete, shippable, independently valuable fix (Figure 1 compliance) —
suggested first PR.

**Incremental delivery**: Ship US1 → US2 → US3 (three P1 layout/functional fixes covering the most
frequently-seen composer states) → US4 (P2 continuous-mode layout) → US5 → US6 (the two remaining
P1 functional/backend fixes) → US7 (P3 polish), merging each to `main` before starting the next.
