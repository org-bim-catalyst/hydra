import { RiChat3Line } from '@remixicon/react'
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Grow,
  Snackbar,
  Toolbar,
} from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useVirtualizer } from '@tanstack/react-virtual'
import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useActiveConversationStore } from '../activeConversationStore'
import { useChatPanelSizeStore } from '../chatPanelSizeStore'
import { ChatAssistantWidget } from '../components/ChatAssistantWidget'
import { CollapsedChatControl } from '../components/CollapsedChatControl'
import { ExpandedChatPanel } from '../components/ExpandedChatPanel'
import type { VoiceAnalyzerState } from '../components/VoiceAnalyzer'
import type { VoiceControlsProps } from '../components/CollapsedVoiceControls'
import { ChatComposer } from '../components/ChatComposer'
import { InsertPromptPicker } from '../components/InsertPromptPicker'
import { MessageBubble } from '../components/MessageBubble'
import { AiPresenceCard } from '../components/AiPresenceCard'
import { HomeProjectCard } from '../components/HomeProjectCard'
import { ViewerSurface } from '../../viewer/components/ViewerSurface'
import { RotationToggleButton } from '../../viewer/components/RotationToggleButton'
import { LocationWeatherWidget } from '../../viewer/components/LocationWeatherWidget'
import { useGeolocation } from '../../viewer/hooks/useGeolocation'
import { ProjectPicker } from '../../memory/components/ProjectPicker'
import { ThinkingIndicator } from '../components/ThinkingIndicator'
import { useAiPreferences } from '../../settings/hooks/useAiPreferences'
import { useChatDetail, useChatMessages } from '../hooks/useChats'
import { useChatStream } from '../hooks/useChatStream'
import { useSpeechRecognition } from '../voice/useSpeechRecognition'
import { useVoicePreferencesQuery } from '../voice/useVoicePreferencesQuery'
import { useVoiceRecorder } from '../voice/useVoiceRecorder'
import { useVoiceOutput } from '../voice/useVoiceOutput'
import { useVoicePreferencesStore } from '../voice/voicePreferencesStore'
import { useWorkspaceOverlayStore } from '../../../store/workspaceOverlayStore'
import { WorkspaceOverlay } from '../../../components/workspace-shell/WorkspaceOverlay'
import { ThemeToggleButton } from '../../../components/workspace-shell/ThemeToggleButton'
import { ComingSoonDialog } from '../../../components/workspace-shell/ComingSoonDialog'
import {
  analysisControl,
  layersControl,
  navigationControl,
  selectionControl,
  useAccountControl,
  useViewModeControl,
} from '../workspaceControls'
import { EmptyState } from '../../../components/EmptyState'
import { ErrorState } from '../../../components/ErrorState'

const CHAT_CONTENT_ID = 'ask-lucy-assistant-content'

/**
 * Owns which chat is selected (2026-07-28 ChatGPT-style history decision). `ConversationView`
 * below is remounted (via `key`) only on an *explicit* navigation — picking a different
 * sidebar chat, or starting a new one — never when a chat is auto-created mid-send (see
 * `handleChatCreated`), so an in-flight stream is never interrupted by its own id arriving.
 */
export function ChatPage() {
  // specs/025-chat-configuration-settings FR-007: seeds from the shared store so a
  // conversation selected in the Chat History Settings tab (which sets this, then
  // navigates here) actually opens — a fresh mount otherwise has no other way to know
  // which conversation was just picked.
  const [selectedChatId, setSelectedChatId] = useState<string | null>(
    () => useActiveConversationStore.getState().activeChatId,
  )
  const [viewKey, setViewKey] = useState(0)
  const [language, setLanguage] = useState('en')
  const queryClient = useQueryClient()
  // Lifted above ConversationView so the same isSpeaking/intensity state drives both the
  // actual voice playback (triggered from ConversationView) and the sphere reacting to it
  // (AiPresenceCard, a sibling) — a separate hook instance per component wouldn't share state.
  const tts = useVoiceOutput()
  // SPEC-024 FR-024: preserves account/session access (and theme toggle) that removing
  // MinimalTopBar would otherwise have dropped — see workspaceControls.tsx.
  const accountControl = useAccountControl()
  // SPEC-024 FR-010/FR-011/FR-012: the five viewer-tool controls — only view-mode is
  // functional in this feature; layers/navigation/selection/analysis are established,
  // reachable "coming soon" placeholders (FR-021).
  const viewModeControl = useViewModeControl()
  // specs/027-immersive-viewer-platform FR-006: a single geolocation subscription, shared by
  // ViewerSurface (the map/GIS content mode) and LocationWeatherWidget, rather than each
  // opening its own redundant navigator.geolocation.watchPosition.
  const geolocation = useGeolocation()

  // FR-011/SC-004: restores a returning user's mute/input-mode preference without requiring
  // a detour through Settings first (research.md Decision 9 — VoiceTab already hydrates on
  // its own mount, but a user who never opens Settings needs this too). specs/029-fix-chat-
  // widget-bugs research.md Decision 4: previously a manual useEffect mounted once here (not
  // in ConversationView, since ConversationView remounts on every chat switch) — now
  // `useVoicePreferencesQuery` (TanStack Query, called inside ConversationView near the other
  // voice-preference reads) handles this instead; its cache makes a separate single-mount
  // point unnecessary, and its own error state is what drives ChatComposer's small indicator.

  // specs/026-floating-chat-assistant FR-016/FR-017, data-model.md "Client-side:
  // ChatAssistantWidgetState": seeds the response language from the persisted preference
  // once hydration resolves it, mirroring ConversationView's own aiPreference-seeding
  // pattern — seeded once (not re-applied on every store change), so a language the user
  // actively changes mid-session is never silently overwritten by a later hydration.
  const defaultLanguagePreference = useVoicePreferencesStore((s) => s.defaultLanguage)
  const hasSeededLanguageRef = useRef(false)
  useEffect(() => {
    if (hasSeededLanguageRef.current || defaultLanguagePreference === null) return
    hasSeededLanguageRef.current = true
    setLanguage(defaultLanguagePreference)
  }, [defaultLanguagePreference])

  // specs/025-chat-configuration-settings, research.md Decision 1: mirrored into a
  // session-scoped store alongside the local `selectedChatId` state, so Chat Configuration
  // (rendered on a separate page) can know which conversation is "currently open."
  const setActiveChatId = useActiveConversationStore((s) => s.setActiveChatId)

  const handleChatCreated = (id: string) => {
    setSelectedChatId(id)
    setActiveChatId(id)
    void queryClient.invalidateQueries({ queryKey: ['chats'] })
  }

  // specs/026-floating-chat-assistant FR-014: starts a fresh conversation on demand, from
  // the minimal icon in ExpandedChatPanel's header — the same reset `AssistantPanel`'s old
  // "+ New chat" button performed (FR-012's removed control), now reached differently.
  const handleNewChat = () => {
    setSelectedChatId(null)
    setActiveChatId(null)
    setViewKey((k) => k + 1)
  }

  const workspaceControls = [
    viewModeControl,
    layersControl,
    navigationControl,
    selectionControl,
    analysisControl,
    accountControl,
  ]

  // specs/026-floating-chat-assistant research.md #1: the one piece of workspaceOverlayStore
  // state ChatPage itself needs directly — which visual state to render — read here (not only
  // inside ConversationView) so it can be threaded down as an explicit prop.
  const isChatExpanded = useWorkspaceOverlayStore((s) => s.expandedControlId === 'chat')

  return (
    <Box sx={{ position: 'relative', height: '100dvh', width: '100%', overflow: 'hidden' }}>
      {/* SPEC-024 FR-001: renamed from "Chat" — React 19 hoists this into <head> wherever
          it renders, matching LandingPage's existing convention. */}
      <title>Flumeria Studio</title>
      {/* specs/027-immersive-viewer-platform FR-001: the extensible viewer platform, occupying
          the majority of the viewport as the primary workspace surface — replaces the old
          `WorkspaceSurface` gradient placeholder (research.md Decision 1). `AiPresenceCard`
          (rendered below via `WorkspaceOverlay`'s children slot) is unaffected by this change
          (FR-004). */}
      <ViewerSurface geolocation={geolocation} />
      {/* FR-009: appears once location resolves; renders nothing otherwise (FR-008). */}
      <LocationWeatherWidget latitude={geolocation.latitude} longitude={geolocation.longitude} />
      {/* SPEC-024 FR-005/FR-016: every workspace control is reached only through this
          coordinating overlay, never a permanent toolbar. specs/026-floating-chat-assistant
          FR-001: the chat entry point is no longer one of `controls` — it's the bespoke
          `ChatAssistantWidget`, rendered as a `children` sibling alongside `AiPresenceCard`,
          still reading/writing the same `workspaceOverlayStore` for mutual exclusivity
          (research.md #1). */}
      <WorkspaceOverlay
        controls={workspaceControls}
        topClusterLeading={
          <>
            <ThemeToggleButton />
            <RotationToggleButton />
          </>
        }
      >
        <HomeProjectCard />
        <AiPresenceCard getReactiveIntensity={tts.getIntensity} />
        <ChatAssistantWidget>
          <ConversationView
            key={viewKey}
            chatId={selectedChatId}
            language={language}
            onChatCreated={handleChatCreated}
            onNewChat={handleNewChat}
            tts={tts}
            expanded={isChatExpanded}
          />
        </ChatAssistantWidget>
      </WorkspaceOverlay>
      <ComingSoonDialog />
    </Box>
  )
}

interface ConversationViewProps {
  chatId: string | null
  /** specs/026-floating-chat-assistant FR-015: no longer changeable via an in-toolbar
   * control — this is the seeded-from-preference value, fixed for the conversation
   * (FR-017 changes it only via Chat Configuration, reflected on the next mount/seed). */
  language: string
  onChatCreated: (id: string) => void
  /** specs/026-floating-chat-assistant FR-014: the existing `handleNewChat` handler, now
   * triggered from `ExpandedChatPanel`'s minimal icon instead of the removed
   * `AssistantPanel` button. */
  onNewChat: () => void
  tts: ReturnType<typeof useVoiceOutput>
  /** specs/026-floating-chat-assistant: which visual state to render. Defaults to `true`
   * so every existing standalone `<ConversationView>` test render (many of which predate
   * this feature and never set `workspaceOverlayStore`) continues to see the full
   * conversation content unchanged — only `ChatPage`'s real usage passes this explicitly,
   * driven by `workspaceOverlayStore.expandedControlId === 'chat'`. */
  expanded?: boolean
}

export function ConversationView({
  chatId,
  language,
  onChatCreated,
  onNewChat,
  tts,
  expanded = true,
}: ConversationViewProps) {
  const {
    data,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
    isPending: isMessagesPending,
    isError: isMessagesError,
    refetch: refetchMessages,
  } = useChatMessages(chatId)

  // FR-024: a long conversation's full history is loaded incrementally (background-fetched
  // page by page here) rather than all at once, then rendered with only the visible portion
  // mounted (the virtualizer below) — the two together keep scrolling smooth regardless of
  // conversation length.
  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage) {
      void fetchNextPage()
    }
  }, [hasNextPage, isFetchingNextPage, fetchNextPage])

  const persistedMessages = useMemo(() => data?.pages.flatMap((page) => page.items), [data])

  const {
    messages,
    isStreaming,
    error,
    clearError,
    send,
    retry,
    providerId,
    modelId,
    setSelection,
  } = useChatStream(chatId, persistedMessages, onChatCreated)

  // specs/025-chat-configuration-settings, T021 — replaces the auto-select-on-mount behavior
  // the removed in-toolbar `ProviderModelSelector` used to provide (changing the model is now
  // done from Chat Configuration in Settings, FR-004): a freshly mounted conversation still
  // needs *some* model selected before the composer can send. Reopening an existing
  // conversation seeds from its own last-used model (an improvement over the old blanket
  // provider[0]/model[0] auto-pick); a brand-new conversation seeds from the user's configured
  // default (AiProvidersTab).
  const { data: aiPreference } = useAiPreferences()
  const { data: chatDetail } = useChatDetail(chatId)
  useEffect(() => {
    if (providerId || modelId) return
    if (chatId) {
      if (chatDetail?.providerId && chatDetail.modelId) {
        setSelection(chatDetail.providerId, chatDetail.modelId)
      } else if (chatDetail && aiPreference) {
        setSelection(aiPreference.defaultProviderId, aiPreference.defaultModelId)
      }
    } else if (aiPreference) {
      setSelection(aiPreference.defaultProviderId, aiPreference.defaultModelId)
    }
  }, [chatId, chatDetail, aiPreference, providerId, modelId, setSelection])

  // spec.md FR-002a, User Story 5 — this view remounts (via `key`) on an explicit chat switch, so
  // a plain useState reset is correct; not yet seeded from persisted history (UserChatDto doesn't
  // carry ProjectId).
  const [projectId, setProjectId] = useState<string | null>(null)
  const scrollRef = useRef<HTMLDivElement>(null)
  const listParentRef = useRef<HTMLDivElement>(null)

  const virtualizer = useVirtualizer({
    count: messages.length,
    getScrollElement: () => listParentRef.current,
    estimateSize: () => 96,
    overscan: 8,
  })

  useEffect(() => {
    scrollRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  // Restores the legacy app's behavior of speaking every AI reply aloud as soon as it
  // finishes streaming (FR-006) — the React migration had only kept the Translate button's
  // on-demand read-aloud, dropping the automatic one entirely. `tts.speak()` itself no-ops
  // while muted (useVoiceOutput.ts, SPEC-013 Decision 3), so this effect needs no isMuted
  // check of its own.
  const toggleWorkspaceControl = useWorkspaceOverlayStore((s) => s.toggle)
  const markUnread = useWorkspaceOverlayStore((s) => s.markUnread)
  const wasStreamingRef = useRef(false)
  useEffect(() => {
    if (wasStreamingRef.current && !isStreaming) {
      const last = messages[messages.length - 1]
      if (last?.role === 'assistant' && last.content) {
        tts.speak(last.content, language)
        // FR-016: the toggle needs to indicate new activity when the panel is collapsed.
        // specs/026-floating-chat-assistant: `expanded` (a prop, defaulting to `true`) is now
        // this component's single source of truth for "is the panel open," replacing the
        // separate `isPanelOpen` store read this effect used to compute independently.
        if (!expanded) markUnread('chat')
      }
    }
    wasStreamingRef.current = isStreaming
  }, [isStreaming, messages, language, tts, expanded, markUnread])

  // SPEC-013 US1 (FR-001/FR-003): keeps the extended useVoiceOutput's real-time mute gate
  // in sync with the persisted preference — store is the source of truth (ChatComposer's
  // consolidated mute control and Settings' VoiceTab both write through it), tts.isMuted is
  // only its live effect.
  const isMutedPreference = useVoicePreferencesStore((s) => s.isMuted)
  const updateVoicePreference = useVoicePreferencesStore((s) => s.update)
  // specs/029-fix-chat-widget-bugs research.md Decision 4 — replaces the old manual
  // hydrate-on-mount effect; syncs fetched preferences into the store on success, and its own
  // (not the store's) isError drives ChatComposer's small, non-blocking indicator below,
  // completely separate from the store's `error` field (which stays scoped to save failures).
  const voicePreferencesQuery = useVoicePreferencesQuery()
  // FR-012/constitution §2.VIII: a rejected `update({ isMuted })` (e.g. offline, 500) rolls
  // back to the last-known-good state inside the store itself (voicePreferencesStore.ts) —
  // this just makes that failure visible instead of leaving it store-internal only.
  const voicePreferenceError = useVoicePreferencesStore((s) => s.error)
  const clearVoicePreferenceError = useVoicePreferencesStore((s) => s.clearError)
  // specs/030-composer-panel-refinements FR-008a — the panel's last-chosen half/full height
  // state, persisted to localStorage so it survives a reload (chatPanelSizeStore.ts).
  const isPanelFullHeight = useChatPanelSizeStore((s) => s.isFullHeight)
  const togglePanelHeight = useChatPanelSizeStore((s) => s.toggle)
  useEffect(() => {
    tts.setMuted(isMutedPreference)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isMutedPreference])

  const conversationMode = useVoicePreferencesStore((s) => s.conversationMode)
  const updateConversationMode = useVoicePreferencesStore((s) => s.update)

  // SPEC-013 US2: the composer's text field is lifted here (rather than owned internally by
  // ChatComposer) so a Push-to-Talk transcript can fill it directly (research.md Decision 4),
  // the same way a Continuous-mode transcript calls `send()` directly below.
  const [composerText, setComposerText] = useState('')
  const [isInsertPromptOpen, setInsertPromptOpen] = useState(false)
  const handleSend = () => {
    if (!composerText.trim()) return
    send(composerText.trim())
    setComposerText('')
  }

  // `useSpeechRecognition` attaches its WebSocket 'message' listener exactly once per
  // connection (inside `start()`), closing over whatever `onFinalTranscript` instance was
  // current in *that* render — a plain inline arrow function here would freeze `providerId`/
  // `modelId`/`isStreaming`/`conversationMode` at their values from the render Continuous
  // mode happened to auto-start listening in (often before the provider/model catalog has
  // finished loading), and never see later updates for the rest of that connection's
  // lifetime. Routing through a ref that's refreshed every render (and calling through a
  // stable wrapper) keeps the handler reading current values on every call instead.
  const handleFinalTranscriptRef = useRef<(transcript: string) => void>(() => {})
  useEffect(() => {
    handleFinalTranscriptRef.current = (transcript: string) => {
      if (!transcript.trim()) return
      // Continuous mode can start listening (e.g. on mount, if the mode was already
      // Continuous from a prior session) before the provider/model catalog has finished
      // loading/auto-selecting — auto-sending in that window would hit the same
      // "Choose an AI provider and model" guard useChatStream's send() already enforces for
      // the manual Send button, but silently discard the user's spoken words instead of just
      // rejecting a typed one. Fall back to filling the composer instead of sending, so a
      // transcript is never lost — the user can send manually once ready, same as
      // Push-to-Talk's normal behavior.
      if (conversationMode === 'Continuous' && providerId && modelId && !isStreaming) {
        send(transcript.trim())
      } else {
        setComposerText((prev) => `${prev} ${transcript}`.trim())
      }
    }
  })
  const handleFinalTranscript = useCallback(
    (transcript: string) => handleFinalTranscriptRef.current(transcript),
    [],
  )

  // SPEC-013 US2 (research.md Decision 1/4): a single `useSpeechRecognition` instance, owned
  // here (mirroring `tts`'s existing lifted-hook convention), shared by `ChatComposer` (mic
  // control, status display, and transcript target — specs/029-fix-chat-widget-bugs
  // research.md Decision 5 consolidated what used to be split with the retired
  // `VoiceControlBar`) — not `useConversationAudio`, which would coincidentally also speak
  // the reply and regress the "every reply is spoken, typed or voice" behavior the mute
  // effect above preserves.
  const recognition = useSpeechRecognition({
    language,
    mode: conversationMode === 'Continuous' ? 'continuous' : 'push-to-talk',
    onPartialTranscript: () => {},
    onFinalTranscript: handleFinalTranscript,
  })

  // specs/026-floating-chat-assistant FR-019–FR-025, research.md #2: Push-to-Talk no
  // longer uses `recognition` at all (that engine streams audio live the instant start()
  // is called, which would violate "no audio transmitted before explicit accept") — this
  // hook's discrete record/review/cancel/accept flow replaces it for this mode only.
  // Continuous keeps using `recognition` above, completely unchanged (FR-025).
  const recorder = useVoiceRecorder()
  // specs/031-voice-controls-redesign FR-001/FR-002, research.md Decision 1 — finish() now
  // stops and transcribes in one step; this just appends the result into the draft text
  // field, replacing the old two-step finish-then-manually-accept flow.
  const handleFinishAndTranscribe = async () => {
    const transcript = await recorder.finish()
    if (transcript.trim()) {
      setComposerText((prev) => `${prev} ${transcript}`.trim())
    }
  }

  // FR-006: Continuous mode has no per-utterance activation — selecting it starts listening
  // immediately (and keeps listening across utterances); switching away stops it. Push-to-Talk
  // capture itself is started/stopped by ChatComposer's mic control, not this effect.
  //
  // Also pauses Continuous listening while Lucy is speaking (`tts.isSpeaking`) and resumes it
  // once she finishes — without this, the always-on mic hears her own TTS audio through the
  // speakers and transcribes it as if the user said it, producing replies to nothing anyone
  // actually said. `cancel()` (discard), not `stop()` (commit), since by the time a reply is
  // audible the user's real utterance was already finalized well before generation/TTS synthesis
  // finished (the silence-commit window is 800ms; a full reply takes far longer) — anything
  // still accumulating when playback starts is not a genuine new utterance to process. This
  // only applies to Continuous mode: a Push-to-Talk hold is an explicit user gesture (e.g. a
  // deliberate barge-in) and is never force-stopped just because Lucy is talking.
  useEffect(() => {
    if (conversationMode === 'Continuous') {
      if (tts.isSpeaking) {
        if (recognition.isListening) recognition.cancel()
      } else if (!recognition.isListening) {
        void recognition.start()
      }
    } else if (recognition.isListening) {
      recognition.stop()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [conversationMode, tts.isSpeaking])

  // FR-007/Clarification Q4 (research.md Decision 6): blocks switching away from Push-to-Talk
  // while a capture (hold or toggle) is actively in progress, until it's released/stopped.
  // specs/026-floating-chat-assistant: now guards on the recorder's phase (recording OR
  // still awaiting review), not `recognition.isListening`, since Push-to-Talk no longer
  // uses `recognition` at all.
  const isModeSwitchBlocked = conversationMode === 'PushToTalk' && recorder.phase !== 'idle'
  const handleToggleMode = () => {
    if (isModeSwitchBlocked) return
    void updateConversationMode({
      conversationMode: conversationMode === 'PushToTalk' ? 'Continuous' : 'PushToTalk',
    })
  }

  // specs/026-floating-chat-assistant FR-006/research.md #9: the expand handle lives inside
  // `CollapsedChatControl`; this ref lets the Escape handler below move focus back to it once
  // the collapse re-render has happened (the handle doesn't exist in the DOM yet at the moment
  // Escape fires, since we're still on the Expanded branch mid-event).
  const handleRef = useRef<HTMLButtonElement>(null)
  const wasExpandedRef = useRef(expanded)
  useEffect(() => {
    if (wasExpandedRef.current && !expanded) {
      handleRef.current?.focus()
      // FR-024: collapsing mid-recording/review discards it rather than leaving it
      // running invisibly — safe to call unconditionally, cancel() itself no-ops from idle.
      recorder.cancel()
    }
    wasExpandedRef.current = expanded
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [expanded])

  const handleToggleExpanded = () => toggleWorkspaceControl('chat')

  const handleContainerKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape' && expanded) {
      event.stopPropagation()
      handleToggleExpanded()
    }
  }

  // specs/026-floating-chat-assistant FR-004/research.md #3: Processing reflects the
  // assistant generating a reply; Speaking reflects TTS playback; Listening reflects live
  // mic capture — Continuous Listening's `recognition.isListening`, or Push-to-Talk's
  // recorder actively `recording` (not yet `reviewing`/`transcribing`, which have their
  // own finish/cancel/send UI instead); anything else is Idle.
  const isPushToTalkRecording = conversationMode === 'PushToTalk' && recorder.phase === 'recording'
  const isContinuousListening = conversationMode === 'Continuous' && recognition.isListening
  const analyzerState: VoiceAnalyzerState = isStreaming
    ? 'processing'
    : tts.isSpeaking
      ? 'speaking'
      : isPushToTalkRecording || isContinuousListening
        ? 'listening'
        : 'idle'
  const analyzerIntensity = tts.isSpeaking
    ? tts.getIntensity
    : isPushToTalkRecording
      ? recorder.getIntensity
      : () => 0

  // specs/029-fix-chat-widget-bugs research.md Decision 5a — merges the former separate
  // "mute Lucy's speaker output" and "stop the reply she's currently speaking" actions into
  // one toggle (FR-006a/FR-006b): muting while she's actively speaking also stops that
  // playback immediately; muting at any other time (or unmuting) is just the preference
  // toggle. Shared by both the Collapsed widget's mute control and the Expanded panel's
  // ChatComposer, since both read/write the same `voiceControlsProps`-shaped contract.
  const handleToggleMute = () => {
    if (tts.isSpeaking) tts.stop()
    void updateVoicePreference({ isMuted: !isMutedPreference })
  }

  // specs/026-floating-chat-assistant research.md #10: the single data contract shared by
  // `CollapsedVoiceControls` (Collapsed) and the Expanded panel's `ChatComposer`
  // (specs/029-fix-chat-widget-bugs research.md Decision 5 — no longer `VoiceControlBar`,
  // retired) — only the layout differs between the two. Push-to-Talk is driven by
  // `recorder`; Continuous keeps using `recognition`, completely unchanged (FR-025).
  const voiceControlsProps: VoiceControlsProps =
    conversationMode === 'PushToTalk'
      ? {
          isAvailable: tts.isSupported,
          isListening: recorder.phase !== 'idle',
          isSpeaking: tts.isSpeaking,
          isMuted: tts.isMuted,
          conversationMode,
          errorMessage: recorder.error,
          permissionState: recorder.permissionState,
          onStart: () => void recorder.start(),
          onStop: () => void handleFinishAndTranscribe(),
          onCancel: recorder.cancel,
          onStopSpeaking: tts.stop,
          onToggleMode: handleToggleMode,
          onToggleMute: handleToggleMute,
          onClearError: recorder.clearError,
          recording: {
            phase: recorder.phase,
            getIntensity: recorder.getIntensity,
            onFinish: () => void handleFinishAndTranscribe(),
            onCancelRecording: recorder.cancel,
          },
        }
      : {
          isAvailable: tts.isSupported,
          isListening: recognition.isListening,
          isSpeaking: tts.isSpeaking,
          isMuted: tts.isMuted,
          conversationMode,
          errorMessage: recognition.error,
          permissionState: recognition.permissionState,
          onStart: () => void recognition.start(),
          onStop: recognition.stop,
          onCancel: recognition.cancel,
          onStopSpeaking: tts.stop,
          onToggleMode: handleToggleMode,
          onToggleMute: handleToggleMute,
          onClearError: recognition.clearError,
        }

  // specs/026-floating-chat-assistant FR-009: `key={expanded}` forces React to treat each
  // toggle as a fresh element, retriggering Grow's `appear` transition every time — timed by
  // `theme.transitions` (createMotionTokens), which already collapses to 0 under a reduced-
  // motion preference (spec 024 research.md #2), so no separate reduced-motion branch is
  // needed here either.
  if (!expanded) {
    return (
      <Grow in appear key="collapsed">
        <Box onKeyDown={handleContainerKeyDown}>
          <CollapsedChatControl
            onExpand={handleToggleExpanded}
            analyzerState={analyzerState}
            getIntensity={analyzerIntensity}
            voiceControls={voiceControlsProps}
            triggerRef={handleRef}
            contentId={CHAT_CONTENT_ID}
          />
        </Box>
      </Grow>
    )
  }

  return (
    <Grow in appear key="expanded">
      <Box onKeyDown={handleContainerKeyDown}>
        <ExpandedChatPanel
          open={expanded}
          onCollapse={handleToggleExpanded}
          onNewChat={onNewChat}
          language={language}
          contentId={CHAT_CONTENT_ID}
          isFullHeight={isPanelFullHeight}
          onToggleHeight={togglePanelHeight}
          isMuted={voiceControlsProps.isMuted}
          onToggleMute={voiceControlsProps.onToggleMute}
        >
          {/* FR-007/FR-015: chat-specific controls only — brand, theme, and account access
            live behind the Studio workspace's account circular control (SPEC-024 FR-024).
            specs/029-fix-chat-widget-bugs research.md Decision 6: the translate control
            moved into ChatComposer's row below (FR-007) — ProjectPicker stays here
            (spec 026's contracts/chat-widget-components.md:108 deliberately anchored it in
            this toolbar, not ExpandedChatPanel's identity header, a boundary this feature
            doesn't revisit). This toolbar's height is now explicit rather than the MUI
            `dense` variant default, so removing one icon actually shrinks the row instead of
            leaving a fixed-height row with one fewer icon in it (FR-008/SC-004). */}
          <Toolbar
            variant="dense"
            sx={{
              justifyContent: 'flex-end',
              gap: 0.5,
              borderBottom: '1px solid',
              borderColor: 'divider',
              minHeight: 40,
              '&.MuiToolbar-root': { minHeight: 40 },
            }}
          >
            <ProjectPicker chatId={chatId} projectId={projectId} onAssigned={setProjectId} />
          </Toolbar>

          <Box
            ref={listParentRef}
            sx={{ flex: 1, overflow: 'auto', p: 2, bgcolor: 'background.default' }}
          >
            <Box sx={{ maxWidth: 800, mx: 'auto' }}>
              {chatId === null ? (
                // FR-001: this placeholder is reserved for "no conversation selected" only — it
                // must never be the fallback for a selected conversation that's loading/erroring.
                <Box sx={{ mt: 6 }}>
                  <EmptyState
                    icon={<RiChat3Line size="1em" />}
                    title="Start a conversation with Ask Lucy."
                    description="Ask a question, brainstorm, or attach a file to get started."
                  />
                </Box>
              ) : isMessagesPending ? (
                <Box sx={{ display: 'flex', justifyContent: 'center', mt: 8 }}>
                  <CircularProgress
                    role="status"
                    aria-live="polite"
                    aria-label="Loading conversation…"
                  />
                </Box>
              ) : isMessagesError ? (
                <Box sx={{ mt: 6 }}>
                  <ErrorState
                    title="Failed to load this conversation"
                    description="Please try again."
                    onRetry={() => void refetchMessages()}
                  />
                </Box>
              ) : (
                <Box sx={{ position: 'relative', height: virtualizer.getTotalSize() }}>
                  {virtualizer.getVirtualItems().map((virtualItem) => {
                    const message = messages[virtualItem.index]
                    // FR-006/FR-007: the in-flight assistant placeholder (empty content while
                    // streaming) renders as the thinking indicator instead of an empty bubble.
                    const isThinking =
                      isStreaming && message.role === 'assistant' && message.content === ''
                    return (
                      <Box
                        key={virtualItem.key}
                        data-index={virtualItem.index}
                        ref={virtualizer.measureElement}
                        sx={{
                          position: 'absolute',
                          top: 0,
                          left: 0,
                          width: '100%',
                          transform: `translateY(${virtualItem.start}px)`,
                        }}
                      >
                        {isThinking ? (
                          <ThinkingIndicator />
                        ) : (
                          <MessageBubble message={message} chatId={chatId} />
                        )}
                      </Box>
                    )
                  })}
                </Box>
              )}
              <div ref={scrollRef} />
            </Box>
          </Box>

          {/* specs/029-fix-chat-widget-bugs research.md Decision 5 — `VoiceControlBar` no
              longer renders here; ChatComposer below is the single consolidated voice
              control for the Expanded panel. Every field it needs for whichever mode is
              active is already correctly branched in `voiceControlsProps` above (the same
              contract `CollapsedVoiceControls` consumes) — reused here rather than
              re-deriving a second, potentially-divergent copy of the same PushToTalk/
              Continuous branching (the exact kind of drift that caused the original bug). */}
          <ChatComposer
            value={composerText}
            onChange={setComposerText}
            onSend={handleSend}
            disabled={isStreaming || !providerId || !modelId}
            conversationMode={conversationMode}
            isListening={voiceControlsProps.isListening}
            permissionState={voiceControlsProps.permissionState}
            captureError={voiceControlsProps.errorMessage}
            onStartCapture={voiceControlsProps.onStart}
            onStopCapture={voiceControlsProps.onStop}
            onClearCaptureError={voiceControlsProps.onClearError}
            onInsertPromptClick={chatId ? () => setInsertPromptOpen(true) : undefined}
            onToggleMode={voiceControlsProps.onToggleMode}
            recording={voiceControlsProps.recording}
            voicePreferencesUnavailable={voicePreferencesQuery.isError}
          />
          {chatId && (
            <InsertPromptPicker
              open={isInsertPromptOpen}
              onClose={() => setInsertPromptOpen(false)}
              chatId={chatId}
              providerId={providerId}
              modelId={modelId}
              onInserted={() => void refetchMessages()}
            />
          )}
          <Snackbar open={Boolean(error)} autoHideDuration={5000} onClose={clearError}>
            <Alert
              severity="error"
              variant="filled"
              onClose={clearError}
              action={
                <Button color="inherit" size="small" onClick={retry}>
                  Retry
                </Button>
              }
            >
              {error}
            </Alert>
          </Snackbar>
          <Snackbar open={Boolean(tts.error)} autoHideDuration={5000} onClose={tts.clearError}>
            <Alert severity="error" variant="filled" onClose={tts.clearError}>
              {tts.error}
            </Alert>
          </Snackbar>
          <Snackbar
            open={Boolean(voicePreferenceError)}
            autoHideDuration={5000}
            onClose={clearVoicePreferenceError}
          >
            <Alert severity="error" variant="filled" onClose={clearVoicePreferenceError}>
              {voicePreferenceError}
            </Alert>
          </Snackbar>
        </ExpandedChatPanel>
      </Box>
    </Grow>
  )
}
