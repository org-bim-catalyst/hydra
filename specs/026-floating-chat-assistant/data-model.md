# Phase 1 Data Model: Floating Chat Assistant Redesign

This feature introduces exactly **one** new piece of persisted state (a single nullable column) and otherwise reshapes existing client-side UI state. It does not touch the `Conversation`/message data model at all (FR-026).

## Persisted: `UserVoicePreference.DefaultLanguage` (backend)

Extends the existing per-user entity (`src/AskLucy.Domain/Ai/UserVoicePreference.cs`) — see research.md #4.

| Field | Type | Notes |
|---|---|---|
| `DefaultLanguage` | `string?` | BCP-47-style language code, one of the product's currently supported set (`en`, `ar`, `es`, `fr`, `de` — `LanguageSelector.tsx`'s existing list). `null` means "no explicit preference yet" — the system falls back to the assistant's current default (Edge Cases: "a user has never set a default language"). |

**Validation rules**: `DefaultLanguage`, when non-null, MUST be one of the supported language codes — enforced in `SaveUserVoicePreferenceCommandValidator` (FluentValidation), not as a Domain invariant (matches the existing convention: cross-field/range validation for `VoiceSpeed`/`VoiceStyle` already lives at the Application layer per that entity's own doc comment).

**State transitions**: None beyond a plain field update — `SetPreferences` (or a small dedicated setter) sets it and stamps `ModifiedAtUtc`/`ModifiedBy`, same as every other field on this entity.

**Migration**: One additive EF Core migration (nullable column, no backfill required — existing rows simply read as `null`, which is a valid, handled state).

**Flows through**: `UserVoicePreferenceDto` → `GetUserVoicePreferenceQuery`/`SaveUserVoicePreferenceCommand` → `voiceApi.ts`'s `UserVoicePreference` TS interface → `useVoicePreferencesStore` (gains `defaultLanguage` alongside its existing `conversationMode`/`isMuted`/etc. fields, synced and localStorage-cached exactly like those).

## Client-side: `ChatAssistantWidgetState`

Not a new Zustand store — the widget's open/closed state continues to live in the existing `workspaceOverlayStore` (`expandedControlId === 'chat'`, unchanged from spec 024), reused as-is per research.md #1. What's new is purely local component state:

| State | Owner | Notes |
|---|---|---|
| `language` | `ConversationView` (existing `useState`, unchanged mechanism) | Per-conversation response language. Today defaults to a hardcoded `'en'`; this feature changes its initial seed to `voicePreferences.defaultLanguage ?? 'en'` (read once via `useVoicePreferencesStore`, mirroring how `providerId`/`modelId` already seed from `aiPreference` on mount). No longer user-editable via a dropdown in this component (FR-015) — editable only via Chat Configuration. |
| Recording phase | New `useVoiceRecorder` hook (Push-to-Talk only) | See below. |

## `useVoiceRecorder` state machine (new hook, Push-to-Talk only — research.md #2)

```text
idle ──start()──> recording ──finish()──> reviewing ──accept()──> transcribing ──(success)──> idle
                       │                       │
                       └──cancel()─────────────┴──cancel()──> idle   (discards buffered audio, no transmission)
```

| Field | Type | Notes |
|---|---|---|
| `phase` | `'idle' \| 'recording' \| 'reviewing' \| 'transcribing'` | Drives which of the three review controls (finish / cancel / send) is shown, and the Collapsed analyzer's Speaking/Listening state. |
| `isSupported` | `boolean` | `MediaRecorder`/`getUserMedia`/`AudioContext` availability check, mirroring `useSpeechRecognition.isSupported`'s existing pattern. |
| `permissionState` | `'unknown' \| 'granted' \| 'denied'` | Reuses the exact type already defined in `useSpeechRecognition.ts` (`MicrophonePermissionState`) so both hooks' permission-denied UI (`ChatComposer`'s existing `Alert`) stays consistent without duplicating the type. |
| `getIntensity()` | `() => number` | Ref-based, read every animation frame by the waveform — same contract shape as `useVoiceAnalyzer.getReactiveIntensity()`/`useVoiceOutput.getIntensity()` (research.md #3). |
| `error` | `string \| null` | Surfaced via the same `Snackbar`/`Alert` pattern already used for `recognition.error`/`captureError` (constitution §2.VIII — no silent failures). |

**Actions**: `start()` (idle → recording: requests mic permission, begins `MediaRecorder` + analyser), `finish()` (recording → reviewing: stops `MediaRecorder`, keeps the buffered `Blob` in memory, transmits nothing), `cancel()` (recording or reviewing → idle: discards the `Blob`, stops any active stream, never calls the transcription endpoint), `accept()` (reviewing → transcribing → idle: the **only** path that calls `transcribeAudio(blob)`; on success, the returned transcript is used exactly like `ChatComposer`'s existing file-attach transcript today — appended into the composer text).

**Validation rules**: `accept()` is a no-op unless `phase === 'reviewing'` — mirrors the existing guard style in `useSpeechRecognition` (`start()`/`stop()`/`cancel()` already no-op on invalid states). Collapsing the widget while `phase` is `'recording'` or `'reviewing'` calls `cancel()` (FR-024) rather than leaving the recorder running detached from any visible UI.

**Relationships**: Entirely independent of `useSpeechRecognition` — the two hooks are never active for the same utterance; `ConversationView` selects which one is live based on `conversationMode` exactly as it already does today (`recognition` for Continuous, the new recorder for Push-to-Talk), so this is an additive branch, not a rewrite of existing mode-selection logic.

## Component tree (new, replacing today's `AssistantPanel` + chat `ControlDefinition`)

```text
ChatPage.tsx
└── WorkspaceOverlay (unchanged)
    ├── ...existing controls (view-mode/layers/navigation/selection/analysis/account) — unchanged
    ├── HomeProjectCard, AiPresenceCard — unchanged, still WorkspaceOverlay children
    └── ChatAssistantWidget (new — replaces the old `chatControl: ControlDefinition` + `FloatingPanel`)
        ├── CollapsedChatControl (visible when !expanded)
        │   ├── handle (expand trigger)
        │   ├── VoiceAnalyzer (Idle/Processing/Speaking — research.md #3)
        │   └── CollapsedVoiceControls (Push-to-Talk, Continuous toggle, Mute — same props VoiceControlBar already takes)
        └── ExpandedChatPanel (visible when expanded — absorbs AssistantPanel's old role)
            ├── header: back/collapse control, LucyPortrait + name + online status, ActiveLanguageFlag, minimal new-chat icon
            ├── ConversationView (unchanged internals: message list, ProjectPicker+Translate toolbar, composer, VoiceControlBar footer)
            └── recording-review overlay (finish/cancel/send — shown only while useVoiceRecorder.phase !== 'idle')
```

**Removed**: `AssistantPanel.tsx` and its test, the chat `ControlDefinition` object and its `FloatingPanel`/`CircularAction` wiring in `ChatPage.tsx`, `LanguageSelector.tsx` (no longer referenced anywhere), the "Generate image" `IconButton` in `ConversationView`'s toolbar.

**Unchanged**: `ConversationView`'s message-fetching/streaming/sending logic, `MessageBubble`, `InsertPromptPicker`, `ThinkingIndicator`, `useChatStream`, `useChatDetail`/`useChatMessages`, `workspaceOverlayStore`, `useVoiceOutput`, `useVoicePreferencesStore` (beyond the new field), `useSpeechRecognition` (beyond being scoped to Continuous only).
