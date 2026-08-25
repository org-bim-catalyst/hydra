# Research: Composer Interaction Bug Fixes

All decisions below are grounded in direct reading of the current implementation
(`ChatComposer.tsx`, `RecordingReviewControls.tsx`, `CollapsedVoiceControls.tsx`, `ChatPage.tsx`,
`OpenAIProvider.cs`, `ProblemDetailsMiddleware.cs`) during the investigation that produced
spec.md, not assumptions.

## Decision 1 (US1/US2): Leading-group / spacer / trailing-group layout pattern

**Decision**: Restructure `ChatComposer`'s control `Stack` so each state's controls are split into
a leading group and a trailing group with a single `flex: 1` spacer between them, rather than the
current single spacer placed *after* every conditional block.

**Rationale**: Reading the JSX confirms the root cause of both US1 and US2: the `<Box sx={{flex:1}}/>`
spacer currently sits after the empty/recording/continuous conditional blocks and before the
typing-only Send button — so in every state *except* typing, whatever renders lands entirely on
the left with the spacer pushing nothing. Figure 1 (empty) and Figure 4 (continuous) both show a
genuine leading-group/trailing-group split; the spacer needs to move inside each state's branch,
between its own leading and trailing elements, not stay fixed at one place in the JSX regardless
of state.

**Alternatives considered**:
- Absolute-positioning the trailing group — rejected, fights the existing `Stack`/flex layout and
  reintroduces the kind of manual positioning MUI's layout primitives already handle correctly.
- A single shared `justify-content: space-between` on the outer `Stack` instead of an explicit
  spacer `Box` — rejected because US3's three-part layout (cancel / waveform / finish, not two
  groups) doesn't fit a simple two-group `space-between`; keeping an explicit spacer element
  that's positioned per-state is more consistent across all four states than mixing two different
  flex strategies.

## Decision 2 (US2): Extend the single-persistent-mic-element invariant to include 'typing'

**Decision**: The mic `IconButton` must remain the exact same DOM element across `'empty'`,
`'typing'`, and `'recording'` — not just `'empty'`/`'recording'` as today.

**Rationale**: specs/033's pointer-capture fix depends on `pointerup` landing on the same element
`pointerdown` fired on. `composerVisualState` computes `'recording'` only once
`recording?.phase !== 'idle'` actually flips (asynchronously, after `onStartCapture()`'s effects
propagate back down) — so a press that begins while `value !== ''` starts in `'typing'`, not yet
`'recording'`, and only transitions a moment later. If the mic button is a different element in
the `'typing'` branch than in the `'empty'`/`'recording'` branch (as it would be with a naive
"just also render it under `typing`" fix), React unmounts/remounts it mid-gesture the instant
`composerVisualState` flips to `'recording'`, silently breaking release handling — the exact bug
class specs/033 fixed for `'empty'`→`'recording'`. This is not a hypothetical: it reproduces with
the same mechanism already documented in this codebase's own comments.

**Alternatives considered**: Keeping mic as a separate element per state and relying on
`setPointerCapture` alone — rejected; the existing code comment already establishes
`setPointerCapture` is necessary but the same-element invariant is what actually keeps `pointerup`
routed correctly once the visual branch changes mid-press.

## Decision 3 (US3): Give `RecordingReviewControls` an optional `middle` slot instead of splitting it

**Decision**: Add an optional `middle?: React.ReactNode` prop to `RecordingReviewControls`,
rendered between the (now-reordered) cancel and finish controls. `ChatComposer` passes the live
waveform as `middle`; `CollapsedVoiceControls` (which already renders its own waveform separately
above this component, per its existing comment) passes nothing, so it renders cancel-then-finish
adjacent with no gap, unchanged in spirit from today aside from the cancel/finish order swap.

**Rationale**: `RecordingReviewControls` is shared between `ChatComposer`'s row layout (needs
cancel—waveform—finish, three parts) and `CollapsedVoiceControls`'s vertical stack (needs only
cancel+finish, no waveform interleaved — it already renders its own analyzer above this
component). Splitting the component into two separately-exported buttons would require both call
sites to duplicate the shared tooltip/aria-label/sizing logic; a `middle` slot keeps one component,
one place to fix the cancel-before-finish order (matches standard "destructive/secondary action on
the left, confirm/primary action on the right" convention, per Figure 3), and composes cleanly for
both call sites.

**Alternatives considered**: Reordering only in `ChatComposer` by not using `RecordingReviewControls`
there at all (inlining cancel/waveform/finish directly) — rejected; would duplicate the
tooltip/aria-label markup that already exists in one place, and would leave `CollapsedVoiceControls`
alone in a still-wrong finish-before-cancel order.

## Decision 4 (US4): Reuse `ChatPage`'s existing `conversationAudio.getReactiveIntensity`/analyzer state for the composer's waveform

**Decision**: Add a `continuousAnalyzer?: { state: VoiceAnalyzerState; getIntensity: () => number }`
prop to `ChatComposer`, populated from the same `analyzerState`/`analyzerIntensity` values `ChatPage`
already computes for the Ai presence card/sphere (`conversationAudio.getReactiveIntensity` when
`isContinuousEngaged`).

**Rationale**: `ChatPage.tsx` already derives exactly this reactive intensity for the sphere widget
(lines computing `analyzerState`/`analyzerIntensity`); reusing it for the composer's waveform avoids
a second, divergent source of "is Lucy listening" truth and matches Figure 4's single continuous
live waveform.

**Alternatives considered**: Giving the composer its own independent `AnalyserNode` subscription —
rejected as duplicate work and a second potential source of drift from the sphere's reactivity.

## Decision 5 (US5): Retry the continuous-mode capture-start via effect once prerequisites are ready

**Decision**: Add a `useEffect` in `ChatPage.tsx` watching
`[conversationMode, chatId, providerId, modelId, conversationAudio.voiceState]` that calls
`conversationAudio.startTurn()` once `conversationMode === 'Continuous' && chatId && providerId &&
modelId && conversationAudio.voiceState === 'Idle'`.

**Rationale**: `handleStartCapture`'s Continuous branch (`else if (chatId && providerId &&
modelId) { void conversationAudio.startTurn() }`) is the confirmed root cause — on the very first
activation, before any chat exists, this condition is false and the call is silently skipped with
no retry and no visible feedback, exactly matching the reported repro (works only after sending a
first message, which is what actually populates `chatId`). The effect-based retry closes the gap
without changing what the prerequisites are — it only ensures the trigger isn't dropped once they
become true.

**Alternatives considered**:
- Disabling the continuous-conversation entry button until a chat exists — rejected; Figure 1 shows
  it always available from the empty state, and disabling it would leave the user with no path
  forward at all, contradicting FR-007 ("without any additional user action beyond entering the
  mode").
- Proactively creating a placeholder chat the moment continuous mode is entered — rejected as
  out of scope; chat-creation semantics belong to the existing send-flow and changing them is a
  larger surface area than this bug fix warrants (see spec.md Assumptions).

FR-008's "visible error if it genuinely cannot start" is already satisfied by
`conversationAudio.startTurn()`'s own existing `try`/`catch` → `handleUnrecoverableFailure`
path (real mic-permission/engine failures already surface via `errorMessage`); no new error-surfacing
code is needed beyond the effect-based retry itself.

## Decision 6 (US6): Validate the API key before use, and classify `HttpRequestException` in the middleware as defense-in-depth

**Decision**: Two changes:
1. `OpenAIProvider.CreateClient()` throws the existing `AiProviderAuthenticationException`
   (already mapped to 502 with the message "An administrator needs to check the provider's API
   key") when `_options.ApiKey` is null/empty, instead of letting `AuthenticationHeaderValue`'s
   constructor throw an unclassified exception.
2. `ProblemDetailsMiddleware` maps a bare `HttpRequestException` (one that reaches the middleware
   without already having been classified into an `AiProvider*Exception`) to the same 502
   "provider unavailable" Problem Details shape, as a defense-in-depth catch-all.

**Rationale**: Confirmed by reading `EnsureSuccessAsync`/`IsTransient`/`WithRetryAsync`: every
*reachable* HTTP-status-driven failure (401/403, 429, other 4xx, 5xx) is already correctly
classified and retried where appropriate — that logic is sound. The gap is upstream of any HTTP
call: `CreateClient()` builds the `Authorization` header unconditionally, so a missing/blank
credential throws before a request is even sent, bypassing every classification path and landing
on `ProblemDetailsMiddleware`'s generic `_ => 500 "An unexpected error occurred"` fallback — which
is exactly the message the live error banner showed. The middleware-level `HttpRequestException`
mapping is a secondary hardening measure: it's the specific exception type `EnsureSuccessAsync`'s
own final fallback throws for a 5xx from OpenAI, and while that particular path is already caught
and reclassified by `WithRetryAsync` on the *provider* side today, mapping it in the middleware too
means any future code that throws a raw `HttpRequestException` without going through that wrapper
still gets a classified response instead of silently regressing to the generic 500 (constitution
§2.VIII).

**Alternatives considered**: Mapping `HttpRequestException` to a brand-new, transcription-specific
problem type — rejected; the existing `ai-provider-unavailable` type already communicates the
correct semantics ("the AI provider could not be reached/failed"), and no other subsystem in this
codebase throws a raw, unclassified `HttpRequestException` today (the weather integration already
has its own dedicated `WeatherProviderUnavailableException`), so reusing the existing type doesn't
risk misclassifying an unrelated failure.

## Decision 7 (US7): Explicit `placement="bottom"` on every composer/voice-control tooltip

**Decision**: Set `placement="bottom"` explicitly on every `Tooltip` in `ChatComposer.tsx`,
`RecordingReviewControls.tsx`, and `CollapsedVoiceControls.tsx` — including ones that currently
rely on MUI's default rather than an explicit prop — rather than relying on the default staying
`'bottom'` implicitly.

**Rationale**: `RecordingReviewControls` and `CollapsedVoiceControls` both hardcode `left`/`right`
placement today via an explicit `placement` prop, which is the confirmed, direct cause of the
inconsistency. Making every tooltip's placement explicit (rather than depending on some being
explicit and others relying on an unstated default) removes any ambiguity about what happens if a
future change alters a default, and makes the "every tooltip is bottom" requirement independently
verifiable by reading each `Tooltip` element rather than needing to know MUI's current default.

**Alternatives considered**: Leaving tooltips that already render bottom (by relying on default)
untouched and fixing only the explicit `left`/`right` ones — rejected; less robust against future
drift and harder to verify at a glance that the requirement is actually met everywhere.
