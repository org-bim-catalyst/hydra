# Contract: Floating Chat Assistant Widget Components

This feature's primary interface is a client-side component contract (mirroring spec 024's `contracts/workspace-shell-components.md` convention), plus one small backend API delta (see `voice-preference-api.md`). Types below are illustrative TypeScript signatures, not final implementation code (bodies are a `/speckit-tasks` + implementation concern).

## ChatAssistantWidget

The top-level widget, replacing today's chat `ControlDefinition` + `FloatingPanel` pairing. Rendered directly inside `ChatPage.tsx`, not through `WorkspaceOverlay`'s `controls` list (research.md #1).

```ts
interface ChatAssistantWidgetProps {
  chatId: string | null
  onNewChat: () => void            // existing handleNewChat, unchanged (research.md #5)
  onChatCreated: (id: string) => void
  language: string
  onLanguageChange: (language: string) => void
  tts: ReturnType<typeof useVoiceOutput>
}
```

**Contract guarantees**:
- Reads/writes `workspaceOverlayStore.expandedControlId`/`toggle('chat')`/`markUnread('chat')` directly (FR-006) — stays mutually exclusive with every other Studio control (spec 024 FR-015) without a second "what's open" source of truth.
- Renders exactly one of `CollapsedChatControl` or `ExpandedChatPanel` at a time, keyed off `expandedControlId === 'chat'` — both `expand()`/`collapse()` transitions are animated per research.md #7, honoring reduced-motion.
- Independently implements the WAI-ARIA disclosure contract `CircularAction` establishes elsewhere in the shell (research.md #9): `aria-expanded`/`aria-controls` on the handle, `Enter`/`Space` to expand, `Escape` to collapse (returning focus to the handle), initial focus moved inside `ExpandedChatPanel` on open without trapping it.
- Never unmounts `ConversationView` while collapsing — same "don't lose in-progress conversation" guarantee `FloatingPanel`/`CircularAction`'s `Collapse` + `inert` already provide today (visibility is CSS-driven, not a remount).

## CollapsedChatControl

The Collapsed-state visual (FR-002/FR-003/FR-005) — a narrow, lightweight vertical strip that never overlaps the workspace viewer.

```ts
interface CollapsedChatControlProps {
  onExpand: () => void
  analyzerState: 'idle' | 'processing' | 'speaking' | 'listening'
  getIntensity: () => number        // ref-based, read per animation frame (research.md #3)
  voiceControls: CollapsedVoiceControlsProps  // see below — same data VoiceControlBar already takes
}
```

**Contract guarantees**:
- Displays, in order: an expand handle, `VoiceAnalyzer`, `CollapsedVoiceControls` (Push-to-Talk / Continuous toggle / Mute), and a minimal status indicator + short text label reflecting `analyzerState` (FR-003).
- Does not render a live partial transcript, message history, or text input — those exist only in `ExpandedChatPanel` (FR-007).
- Sized to remain visually narrow and never intercept pointer events over the workspace viewer outside its own bounds (FR-005), matching `WorkspaceOverlay`'s existing `pointer-events: none`-outside-controls convention.

## VoiceAnalyzer

```ts
interface VoiceAnalyzerProps {
  state: 'idle' | 'processing' | 'speaking' | 'listening'
  getIntensity: () => number
}
```

**Contract guarantees**: Purely presentational — renders a vertical bar/waveform visualization driven by polling `getIntensity()` via `requestAnimationFrame` (never via React state per frame, matching `AiPresenceCard`'s existing consumption of `tts.getIntensity()`). Visually distinguishes all four `state` values (FR-004); honors reduced-motion for any of its own decorative motion beyond the amplitude-driven bars themselves.

## CollapsedVoiceControls / ExpandedChatPanel's voice-controls footer

Both consume the **same** props `ConversationView` already threads to today's `VoiceControlBar` — this is a shared data contract with two presentational layouts (research.md #10), not two separate logical components.

```ts
interface VoiceControlsProps {
  isAvailable: boolean
  isListening: boolean
  isSpeaking: boolean
  isMuted: boolean
  conversationMode: 'PushToTalk' | 'Continuous'
  errorMessage: string | null
  permissionState: MicrophonePermissionState
  onStart: () => void
  onStop: () => void
  onCancel: () => void
  onStopSpeaking: () => void
  onToggleMode: () => void
  onToggleMute: () => void
  onClearError: () => void
  // New for Push-to-Talk's recording-review flow (FR-019–FR-023):
  recording: {
    phase: 'idle' | 'recording' | 'reviewing' | 'transcribing'
    getIntensity: () => number
    onFinish: () => void
    onCancelRecording: () => void
    onAccept: () => void
  }
}
```

**Contract guarantees**:
- `CollapsedVoiceControls` renders these as a compact vertical icon stack; the existing `VoiceControlBar` continues rendering them as a horizontal row inside `ExpandedChatPanel` — both read from the identical prop shape, so a behavior change (e.g. a new guard on `onToggleMode`) only needs to be made once.
- While `recording.phase !== 'idle'`, both layouts show the same three controls — finish speaking, cancel, accept/send — using identical semantics regardless of which one triggered recording (FR-023).
- `recording.phase === 'reviewing'` MUST NOT have triggered any network transmission yet; only `onAccept()` does (FR-019/FR-021/FR-022 — this is the load-bearing privacy guarantee behind Clarification Q2).
- This contract applies to Push-to-Talk only; when `conversationMode === 'Continuous'`, `recording.phase` stays `'idle'` and none of the review controls render (FR-025).

## ExpandedChatPanel

Replaces `AssistantPanel` + the old `FloatingPanel`'s direct use for chat (research.md #10).

```ts
interface ExpandedChatPanelProps {
  onCollapse: () => void
  onNewChat: () => void
  language: string
  children: ReactNode   // ConversationView, unchanged
}
```

**Contract guarantees**:
- Header renders, left-to-right: a back/collapse control (`onCollapse`), the assistant's identity (`LucyPortrait` + name + online/connection status), `ActiveLanguageFlag` (read-only, driven by `language`), and a minimal icon-only new-chat control (`onNewChat`) — no text-labeled "+ New chat" button anywhere (FR-012).
- Does not render its own language dropdown (FR-015) — `ActiveLanguageFlag` is display-only; changing the value happens exclusively in Chat Configuration (FR-017).
- `children` (`ConversationView`) is rendered unchanged internally; only `LanguageSelector` and the Generate-image button are removed from its own toolbar (FR-015/FR-018) — `ProjectPicker` and the Translate action stay (FR-027).

## ActiveLanguageFlag

```ts
interface ActiveLanguageFlagProps {
  language: string   // one of 'en' | 'ar' | 'es' | 'fr' | 'de'
}
```

**Contract guarantees**: Purely presentational, read-only — renders a small circular flag glyph mapped from `language` (research.md #6). No `onChange`/interactive affordance; the only way its displayed value changes is a re-render triggered by `language` changing (i.e., after a save in Chat Configuration).
