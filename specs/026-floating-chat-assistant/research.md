# Phase 0 Research: Floating Chat Assistant Redesign

No `[NEEDS CLARIFICATION]` markers remained after `/speckit-clarify`, so every item below is a concrete decision grounded in the existing codebase (`src/AskLucy.Web/ClientApp`, `src/AskLucy.Domain`, `src/AskLucy.Application`) rather than an open technology choice.

## 1. Where does the redesigned chat widget live in the workspace-shell architecture?

**Decision**: A new, bespoke `ChatAssistantWidget` (with `CollapsedChatControl` and `ExpandedChatPanel` sub-components), rendered directly in `ChatPage.tsx` as one of `WorkspaceOverlay`'s `children` — the same slot `AiPresenceCard`/`HomeProjectCard` already use — **not** as a `ControlDefinition` routed through `WorkspaceOverlay`'s `renderControl`/`CircularAction`. It still reads and writes `workspaceOverlayStore.expandedControlId`/`toggle('chat')`/`markUnread('chat')` directly (the same store spec 024 already established), so it stays mutually exclusive with the other six controls (FR-015, already shipped) without a second, competing "what's open" source of truth.

**Rationale**: FR-001 is explicit that only the chat entry point's presentation changes — "other Studio contextual controls (view mode, layers, navigation, selection, analysis, account) are unaffected by this feature." `CircularAction` (`src/components/workspace-shell/CircularAction.tsx`) bakes in a single fixed collapsed shape: one `Fab` icon inside a `radius.pill` circle, animating to a `radius.lg` rounded rectangle on expand (`CIRCULAR_ACTION_CHROME`). The spec's required Collapsed shape — a narrow vertical strip containing a handle, a vertical voice analyzer, three separate voice controls, and a status label (FR-003) — cannot be expressed as "one icon in a circle" without changing `CircularAction` itself, which the other six live controls (and their own passing a11y/unit tests, `CircularAction.test.tsx`/`CircularAction.a11y.test.tsx`) depend on unchanged.

**Alternatives considered**:
- Extending `CircularAction` with an optional `collapsedContent` slot instead of a fixed `icon` prop — rejected: `CircularAction`'s `Badge`+`Fab`+pill-radius-transition assumptions are structural, not cosmetic; branching them for the one caller that needs a different collapsed shape adds permanent conditional complexity to a shared primitive with zero other consumers needing it (constitution §2.III, YAGNI).
- Leaving chat on `CircularAction` and cramming the analyzer/voice controls into its expanded pill only, with a plain icon while collapsed — rejected: directly contradicts FR-003, which requires the analyzer and voice controls to be visible *while collapsed*, not only after expanding.

## 2. Push-to-Talk's recording mechanism — reuse `useSpeechRecognition`, or build new?

**Decision**: A new `useVoiceRecorder` hook, used by Push-to-Talk only, built on the browser's `MediaRecorder` API (buffers audio locally into a `Blob`) plus a `Web Audio AnalyserNode` tapped directly off the same `getUserMedia` `MediaStream` for the live waveform. On accept, the buffered `Blob` is sent through the **existing** `transcribeAudio(file: File)` REST call (`src/features/chat/api/aiApi.ts` → `POST /ai/transcriptions`) — already used today by `ChatComposer`'s file-attach path — with no backend changes. Continuous Listening keeps using `useSpeechRecognition` (`src/features/chat/voice/useSpeechRecognition.ts`) exactly as it does today, per FR-025.

**Rationale**: `useSpeechRecognition` is a realtime-streaming engine — the instant `start()` is called it opens a WebSocket directly to ElevenLabs and streams PCM audio chunks continuously (`workletNode.port.onmessage` → `socket.send(...)` on every audio frame). That is a direct, structural conflict with FR-019/FR-021 (Clarification Q2): "no audio is transmitted to any transcription service at this point" during recording, and nothing is sent until the user's explicit accept. There is no way to satisfy that requirement while `useSpeechRecognition`'s socket is open and streaming — the audio would already be off the device. `useSpeechRecognition` also has no capability to produce a finished audio file to submit later; it only ever emits streamed partial/final *text*, not a `Blob`. The existing `/ai/transcriptions` endpoint, by contrast, is already exactly the "send a finished audio file, get a transcript back" discrete call FR-022 needs — reusing it means zero backend work for this decision.

**Alternatives considered**:
- Keep `useSpeechRecognition` running live during "recording" and merely delay *displaying* its live transcript / *inserting* it until the user accepts — rejected: audio would already have been continuously transmitted to ElevenLabs during "recording," which is the literal thing FR-019 forbids; this isn't a UI-only deferral, it's a data-flow violation.
- A new backend streaming/session endpoint purpose-built for "buffer, then transcribe" — rejected: `/ai/transcriptions` already does exactly this for file uploads; building a parallel endpoint would duplicate existing, working infrastructure (constitution §2.III, DRY).

## 3. Voice Analyzer — what drives its Idle / Processing / Speaking states?

**Decision**: The analyzer (used in the Collapsed state, FR-004) derives its state and intensity entirely from data these hooks/components already expose — no new audio-analysis code beyond the one `AnalyserNode` introduced in Decision 2:

| Analyzer state | Source |
|---|---|
| Speaking (assistant audio playing) | `tts.isSpeaking` + `tts.getIntensity()` (`useVoiceOutput`, already driving `AiPresenceCard`'s particle-sphere reactivity) |
| Listening (user's mic capturing) | Continuous: `recognition.isListening`. Push-to-Talk: the new `useVoiceRecorder`'s `isRecording` + its own `AnalyserNode`-derived intensity (Decision 2) |
| Processing (assistant generating a reply) | `isStreaming` (`useChatStream`, already tracked in `ConversationView`) |
| Idle | None of the above |

**Rationale**: `useVoiceAnalyzer.ts` already establishes the exact pattern needed — a ref-based `getReactiveIntensity()` read every animation frame via `analyser.getByteFrequencyData`, never triggering React re-renders per frame. Mirroring it for the new recorder's input-side analyser (rather than inventing a different technique) keeps the codebase's two live-audio-visualization code paths consistent and keeps `requestAnimationFrame` polling — not React state — as the established way to drive high-frequency visual feedback (constitution §15, avoids unnecessary re-renders).

**Alternatives considered**: A single shared "audio intensity" hook covering both output (TTS) and input (mic) — rejected for this feature: `useVoiceAnalyzer`'s graph is wired specifically to the `<audio>`/`MediaSource` playback element, structurally different from a raw `getUserMedia` `MediaStream`; unifying them would mean reworking an existing, working, unrelated hook for a marginal code-sharing gain (YAGNI) — the two `AnalyserNode` setups already share a common *pattern*, which is what actually matters for consistency.

## 4. Where does the persisted default-language preference live?

**Decision**: Extend the existing `UserVoicePreference` domain entity (`src/AskLucy.Domain/Ai/UserVoicePreference.cs`) with a new nullable `DefaultLanguage` field, threaded through its existing `SaveUserVoicePreferenceCommand`/`GetUserVoicePreferenceQuery`/`UserVoicePreferenceDto`/`voiceApi.ts`/`useVoicePreferencesStore` vertical slice — not `UserAiPreference` (provider/model scoped, unrelated concern) and not a new entity/endpoint.

**Rationale**: `ChatConfigurationTab.tsx` already links its "Voice, speech-to-text & text-to-speech" section to the same Settings > Voice tab that owns conversation mode, mute, and device selection — response language is already conceptually adjacent (it drives both `useSpeechRecognition`'s STT `language` param and `tts.speak(text, language)`'s TTS language, both voice concerns). `useVoicePreferencesStore` already has the exact shape needed: server-synced via `hydrateFromServer`/`update`, `persist`-cached to localStorage for instant restore, explicit-error-surfaced on save failure (constitution §2.VIII) — extending it costs one field end-to-end, versus building an equivalent store from scratch for `UserAiPreference` or a new concept.

**Alternatives considered**:
- Extending `UserAiPreference` instead — rejected: that entity's own doc comment and spec-005 grounding scope it to provider/model/generation-parameters; language is not an AI-provider concern.
- A brand-new `UserChatPreference` entity/endpoint just for `defaultLanguage` — rejected: one nullable string field does not justify a new CQRS vertical slice, new table, and new frontend store when an existing one already matches its actual usage (constitution §2.III, YAGNI).

## 5. Minimal new-chat icon (FR-014) — new logic, or reuse?

**Decision**: Reuse `ChatPage.tsx`'s existing `handleNewChat` handler unchanged (already implemented: resets `selectedChatId`/`activeChatId`/`viewKey`). Only its trigger UI moves — from `AssistantPanel`'s current full-width "New chat" text button to a small icon-only `IconButton` inside `ExpandedChatPanel`'s new header, alongside the collapse/back control.

**Rationale**: FR-014's behavior ("make a new, empty conversation the active one... don't delete or hide the one it replaced") is exactly what `handleNewChat` already does today — this is a presentation-only change, not new logic, consistent with the spec's explicit framing (FR-026/FR-027: preserve existing chat/agent behavior, change only presentation and interaction model).

## 6. Active-language flag icon — how is it rendered?

**Decision**: A small circular flag glyph (Unicode regional-indicator flag emoji, e.g. `🇬🇧`/`🇪🇸`/`🇫🇷`/`🇩🇪`/`🇸🇦`) centered inside a fixed-size circular `Box`/`Avatar`, mapped from the existing five language codes already defined in `LanguageSelector.tsx` (`en`, `ar`, `es`, `fr`, `de`) — no new icon/flag asset library.

**Rationale**: Emoji flags render natively across every target platform/browser with zero bundle cost and zero new dependency; `@remixicon/react` (added for the rest of the Studio page's iconography this session) has no national-flag glyphs, and pulling in a dedicated flag-sprite package (e.g. `flag-icons`) for five circular glyphs would be disproportionate (constitution §2.III, YAGNI) next to a one-line lookup map.

**Alternatives considered**: An SVG flag-icon package — rejected per above; a plain two-letter language-code badge instead of a flag — rejected, the spec explicitly asks for "a flag of the current active language in circle" (not a text code).

## 7. Expand/collapse motion for the new widget

**Decision**: Reuse the same mechanism spec 024 already established for every other control: MUI's own transition primitives (`Collapse`/`Grow`/`sx`-driven `theme.transitions.create([...])`), timed by `theme.transitions`, which is itself built from `createMotionTokens(prefersReducedMotion)` (`src/theme/tokens/motion.ts`) — automatically collapsing to `0`-duration under a reduced-motion preference.

**Rationale**: This is the same reduced-motion-aware, dependency-free mechanism already proven for the other six controls (spec 024 research.md #2); introducing a second animation approach (or a new library) for one widget would fragment the codebase's one existing "how do things animate" answer for no functional gain.

## 8. Visual chrome — matching the rest of the floating-control family

**Decision**: The new widget reuses the same dark-navy-glass token family `CIRCULAR_ACTION_CHROME` already defines (`collapsedBg`/`expandedBg`/`icon`/`border`, sampled from the readdy.ai reference's own computed styles per spec 024) rather than inventing a second, competing color palette for one control.

**Rationale**: All of Studio's other floating chrome is already this fixed dark-glass family, deliberately independent of the app's own light/dark theme toggle (spec 024 research.md, `CircularAction.tsx`'s own doc comment). A visually distinct chat widget would read as a mismatched, bolted-on control rather than part of the same floating-control system — directly undermining the spec's "premium floating aesthetic" requirement (User Story 2) and SC-007 from spec 024 ("a later feature... visibly matches the same... pattern... users never encounter two competing floating-UI styles").

## 9. Accessibility contract for the new (non-`CircularAction`) widget

**Decision**: `ChatAssistantWidget` independently implements the same WAI-ARIA disclosure pattern `CircularAction` already establishes for the rest of the shell — `aria-expanded`/`aria-controls` on the collapse/expand trigger, `Enter`/`Space` activation via a native `<button>`, `Escape` collapses and returns focus to the trigger, initial focus moves inside `ExpandedChatPanel` on open without trapping it (mirroring `FloatingPanel`'s existing, unchanged focus-management effect) — plus its own `CircularAction.a11y.test.tsx`-equivalent automated a11y test, since it does not inherit that coverage for free by no longer routing through `CircularAction`.

**Rationale**: Constitution §7 requires WCAG 2.1 AA for all interactive UI, with automated a11y checks plus manual review specifically called out for "novel interaction patterns" — this widget is exactly that, since Decision 1 deliberately takes it off the already-audited `CircularAction` path. Re-implementing the same proven ARIA contract (rather than a different one) keeps the whole Studio shell behaviorally consistent for keyboard/screen-reader users even though this one control's markup is now bespoke.

## 10. Existing components subsumed by the redesign

**Decision**:
- `AssistantPanel.tsx` (today: just a "+ New chat" button wrapper) is deleted; its role is absorbed into `ExpandedChatPanel`'s new header (identity, online status, language flag, minimal new-chat icon, collapse control).
- `VoiceControlBar.tsx`'s existing props/handlers/logic (`isListening`, `isSpeaking`, `isMuted`, `conversationMode`, `onStart`/`onStop`/`onCancel`/`onToggleMode`/`onToggleMute`, etc. — already threaded from `ConversationView`) are reused **unchanged** as the data contract for both new presentational layouts: the existing horizontal row continues to serve `ExpandedChatPanel`'s footer, and a new vertical variant (`CollapsedVoiceControls`) serves `CollapsedChatControl`, consuming the exact same props. Only layout is duplicated, not logic (constitution §2.III, DRY).
- `ConversationView`'s own internal `<Toolbar>` (today: `ProjectPicker`, the removed `LanguageSelector`, Translate, the removed Generate-image button) keeps `ProjectPicker` and Translate untouched (FR-027) — only `LanguageSelector` (FR-015) and the Generate-image `IconButton` (FR-018) are removed from it.

**Rationale**: Minimizes the actual diff to exactly what the spec requires changed, per FR-026/FR-027's explicit "preserve existing chat/agent behavior... only presentation and interaction model change" — every hook, handler, and piece of business logic already wired through `ConversationView` stays exactly as-is; only which component renders which prop, and in what shape, changes.

## 11. Responsive behavior for the new (non-`FloatingToolbar`) widget

**Decision**: `ChatAssistantWidget` anchors itself to a fixed screen edge (bottom-end, matching the old chat trigger's position and `FloatingToolbar`'s own `anchor="bottom-end"` convention) and sizes itself using MUI's responsive breakpoint system directly (`sx={{ width: { xs: ..., sm: ... } }}`-style rules on `CollapsedChatControl`/`ExpandedChatPanel`), the same technique `FloatingPanel` already uses for its own responsive width/height (`width: { xs: 'min(92vw, 380px)', sm: 400 }`). At narrow (mobile) widths, `ExpandedChatPanel` grows to a bounded viewport-relative size rather than a fixed pixel size, and `CollapsedChatControl` stays narrow enough to never overlap the account/theme-toggle cluster in `WorkspaceOverlay`'s `top-cluster` at any supported width.

**Rationale**: Taking the widget outside `WorkspaceOverlay`'s `controls`/`FloatingToolbar` system (research.md #1) means it no longer inherits that system's existing responsive stacking/repositioning behavior (spec 024 FR-020) for free — it needs its own answer. Reusing `FloatingPanel`'s already-proven breakpoint pattern (rather than inventing a new responsive technique) keeps this consistent with the rest of the shell and satisfies constitution §7 ("Layouts MUST work from mobile breakpoints through desktop... fixed-pixel layouts that break under resize are a review-blocking finding") without a new dependency or a bespoke media-query system.

**Alternatives considered**: Routing `ChatAssistantWidget` back through `FloatingToolbar` just for its responsive behavior while keeping its custom collapsed/expanded content — rejected as reintroducing exactly the shape mismatch research.md #1 already ruled out (`FloatingToolbar` positions `CircularAction` children, not an arbitrary two-state widget). A dedicated `useMediaQuery`-driven JS breakpoint hook instead of `sx` breakpoint objects — rejected as an unnecessary second responsive mechanism next to the one MUI (and the rest of this codebase) already provides declaratively.

**Verification**: tasks.md T009/T016 build sizing in from the start; T056 (Polish) exercises Collapsed and Expanded at mobile/tablet/desktop widths, mirroring spec 024's quickstart Scenario 5.
