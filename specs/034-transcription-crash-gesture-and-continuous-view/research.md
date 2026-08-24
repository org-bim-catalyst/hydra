# Research: Transcription Crash Fix, Review-Gesture Restoration & Continuous Voice View

## Decision 1 — Guard `IFormFile` binding against null/malformed uploads

**Finding** (background investigation, verified directly): `AiController.cs`'s `Transcribe`
(`:205-213`) and `TranscribeMicrophone` (`:218-225`) both call `file.OpenReadStream()` on an
`IFormFile` parameter with no null-check. ASP.NET Core's `FormFileModelBinder` does not fail
`ModelState`/return 400 when a multipart request's file part is missing or malformed — it silently
binds `file` to `null`. Calling `.OpenReadStream()` on a null `IFormFile` throws a bare
`NullReferenceException`, which has no case in `ProblemDetailsMiddleware.Map()`'s switch (`:113-
270`) and falls to the generic default (`:265-269`) — whose `Detail` text
("An unexpected error occurred. Please try again.") is a byte-for-byte match with the reported UI
toast. Two prior rounds' fixes were both inside `OpenAIProvider.cs`, entirely downstream of this
gap, so neither could have touched it — this explains the identical symptom recurring after both.

**Decision**: Add an explicit guard at the top of both actions:
```csharp
if (file is null || file.Length == 0)
{
    return BadRequest(new ProblemDetails { Title = "No audio file was provided", Status = 400 });
}
```
returning a 400 directly from the controller (not routed through `ProblemDetailsMiddleware`'s
exception-based classification, since this isn't an exception at all — it's a normal, expected
"the request didn't include what we needed" case, handled the same way ASP.NET Core model
validation failures already are elsewhere in this codebase).

**Rationale**: Minimal, targeted fix at the exact point of failure; doesn't touch
`OpenAIProvider.cs` (already correctly classifying everything within its own scope) or introduce
a new exception type for what is fundamentally a missing-input case, not a provider failure.

**Correction found during implementation**: empirically (via `TranscriptionUploadGuardTests.cs`),
a request with the `file` multipart part missing entirely is *already* rejected with 400 by
`[ApiController]`'s automatic model-validation for a required reference-type parameter — the
originally-hypothesized null-`IFormFile`-crash for that specific case doesn't reproduce. The real,
confirmed gap is narrower and different: a file part that **is present but has zero bytes**
(`file.Length == 0`) binds successfully to a non-null `IFormFile`, so `.OpenReadStream()` doesn't
throw — the request instead proceeds all the way through to a real outbound call to OpenAI with
an empty audio payload. The `file is null || file.Length == 0` guard closes both cases uniformly
(the null check is now defensive/redundant for the "missing entirely" case, but harmless to keep),
and the `Length == 0` check is what actually prevents the previously-unguarded network call.

**Also found, not fixed (noted for awareness only, out of scope)**: the identical
`IFormFile param; ...OpenReadStream()` pattern with no null-check exists in three other
controllers (`KnowledgeBasesController.cs:191-196`, `DocumentsController.cs:95-99`,
`UsersController.cs:50-53`). Not touched by this feature — flagged as a systemic pattern worth a
follow-up sweep, but out of scope for a voice/transcription-focused fix.

## Decision 2 — Fix production logging so this can never again be a three-strikes-blind bug

**Finding**: Serilog is configured with only `.WriteTo.Console(...)` (`Program.cs:36-39`); no
`Serilog:WriteTo` array exists in `appsettings.Production.json`. The IIS/ANCM deployment
(`site4now.net`, out-of-process hosting per `AskLucy.Web.csproj:8`) generates a `web.config` at
publish time with `stdoutLogEnabled="false"` by default (no `<StdoutLogEnabled>` MSBuild property
overrides it) — meaning the Console sink's output has nowhere to go in production. The exception
behind all three occurrences of this bug (across two prior "fixed" rounds) has been logged into a
void every single time — this is why the same symptom kept "surviving" fixes that, in each
individual case, were both real and insufficient: there was never a way to see what was actually
still failing.

**Decision**: Add `Serilog.Sinks.File` (new NuGet package) and a rolling-file sink to
`appsettings.Production.json`'s `Serilog:WriteTo` array, writing to a writable folder within the
deployed site (e.g. `App_Data/logs/asklucy-.log`, daily rolling). This is independent of IIS/
ANCM's stdout redirection entirely — it works regardless of hosting model, gives structured,
timestamped, retrievable log files, and matches constitution §14's existing Serilog-based
observability principle rather than depending on a legacy IIS-specific mechanism. (Also setting
`<StdoutLogEnabled>true</StdoutLogEnabled>` in the `.csproj` as a cheap secondary measure, since it
catches startup failures that occur before Serilog itself finishes initializing — but the file
sink is the primary, load-bearing fix.)

**Rationale**: A code fix without a way to verify/diagnose future failures repeats the exact
process gap this whole investigation exists to close. This is the single highest-leverage change
in this feature — every other fix in this round (and the two before it) was effectively
undiagnosable without it.

## Decision 3 — Restore the dual tap/hold gesture on Push-to-Talk, using a release-time threshold

**Finding**: specs/033 removed the entire tap-vs-hold distinction in `ChatComposer.tsx` in favor
of pure hold-only, based on an earlier (correct, but incomplete) reading of the user's request.
The user has now clarified the actual desired model — which is, functionally, `CollapsedVoiceControls.tsx`'s
already-working click-to-record-with-review-controls flow (`RecordingReviewControls`' Finish ✓ /
Cancel ✗, already used there, untouched by specs/033) **unified onto the same control** as
`ChatComposer`'s hold-to-auto-finish flow. Since a tap and a hold are physically identical at the
moment of press (only distinguishable by what happens next), the UI cannot know which gesture is
occurring until release.

**Decision**: Reintroduce a hold-duration threshold (`HOLD_THRESHOLD_MS`, the same constant
specs/033 removed) purely as a release-time classifier, plus new local component state
(`isAwaitingTapReview`) driving which of three visual states renders:
1. **Any active press, gesture not yet resolved** — same mic `IconButton` element as today (kept
   mounted, `setPointerCapture`-protected per specs/033's real bug fix, which is preserved
   unchanged) + live waveform. No confirm/discard controls, regardless of how the press will
   eventually resolve.
2. **Resolved as a tap** (`pointerup`/`keyup` fires with elapsed time `< HOLD_THRESHOLD_MS`):
   recording continues (does NOT auto-stop); `isAwaitingTapReview` becomes `true`; the mic button
   is replaced by `RecordingReviewControls` (Finish ✓ / Cancel ✗) alongside the waveform — the
   same component `CollapsedVoiceControls` already uses, reimported into `ChatComposer.tsx`.
   Finish calls the same finish function as a hold-release; Cancel calls `recording.onCancelRecording`
   (`recorder.cancel()`) and discards. Both reset `isAwaitingTapReview` to `false`.
3. **Resolved as a hold** (elapsed `>= HOLD_THRESHOLD_MS` at release): identical to specs/033's
   existing behavior — release directly stops, transcribes, and populates the message field, no
   controls ever shown.

**Note found during implementation**: `recording.phase` only becomes `'recording'` once the
parent's recorder state catches up asynchronously *after* `onStartCapture()` fires — there's
always a real gap between `pointerdown` and that prop update landing, exactly the async-timing
reality specs/033's `isCapturingRef` design (Decision 3) already accounts for on the way down. Its
defensive fallback (`recording?.phase === 'recording'` also counting as "already capturing," for a
hypothetical remount-while-recording case) means a test — or, in principle, a genuinely fast
prop update — that has `recording.phase` already `'recording'` *before* `pointerdown` fires would
cause `handleMicPointerDown`'s own `isCapturing()` guard to no-op the press entirely. Test coverage
for the tap path models the real two-step timing explicitly (start, then a rerender simulating the
parent's async catch-up, then release) rather than seeding `recording.phase: 'recording'` from the
first render.

**Rationale**: This is not a reversion to specs/031's original dual-gesture design (which left a
tap-started recording running silently, requiring a second, separate, undiscoverable tap to
stop) — it reuses the ALREADY-CORRECT, already-tested `RecordingReviewControls` pattern
`CollapsedVoiceControls` has used successfully all along, and is explicit/discoverable (visible
✓/✗ controls) rather than the old "you have to know to tap again" convention. specs/033's real bug
fix (`setPointerCapture`, keeping the same element mounted through a press) is preserved exactly —
only the release-time behavior changes.

## Decision 4 — Continuous mode's mic-mute fix was built into a hook that's never rendered;
## the new dedicated view is where it should actually live

**Finding** (significant correction to specs/033's own work): specs/033's mic-mute-during-
`AiSpeaking` fix was implemented entirely inside `useConversationAudio.ts` — but a repo-wide
search (`= useConversationAudio(`) confirms that hook is invoked nowhere in the application except
its own test file. `ChatPage.tsx`'s actual, live Continuous mode is built from a *different*,
independent implementation: a directly-owned `useSpeechRecognition` instance (`ChatPage.tsx:396-
401`) plus its own effect (`:432-443`) that already does something conceptually similar —
`recognition.cancel()` (a full audio-graph teardown, not a mute) when `tts.isSpeaking` becomes
true, and `recognition.start()` (full reconnect) once it becomes false. specs/033's fix, however
correct in isolation, never took effect on the real user-facing Continuous mode — this is the
most likely explanation for the self-listening complaint persisting past that "fix."

**Decision**: Rather than porting the mute fix a second time into `ChatPage.tsx`'s parallel inline
implementation (leaving two independent, duplicate Continuous-mode orchestrations in the codebase
long-term), use User Story 3's own scope — restructuring Continuous mode into a dedicated view —
as the point where `useConversationAudio` (already correct, already tested, simply never wired up)
becomes the *actual* orchestrator: the new dedicated voice view component owns one
`useConversationAudio` instance and uses its `startTurn`/`stop`/`voiceState`/`errorMessage`/
`getReactiveIntensity` surface directly. Once Continuous mode is fully delegated to this view
(FR-008: activating Continuous mode always opens it), `ChatPage.tsx`'s own Continuous-mode-only
plumbing — the `recognition` instance, `handleFinalTranscriptRef`/`handleFinalTranscript`, and the
two effects at `:396-443` that exist solely to drive it — becomes dead code and is removed, not
left disabled alongside the new path.

**Rationale**: Avoids permanently maintaining two parallel, subtly-different implementations of
"listen, mute while Lucy speaks, resume" — a real duplication risk given this is exactly how
specs/033's fix silently missed the real code path in the first place. Consolidating onto the
already-built, already-tested `useConversationAudio` (constitution §3 DRY/no duplicate business
logic) is both the correct fix for the mute bug and the natural foundation for the new view
FR-008/FR-009 require.

**Scope carried forward from specs/033, unchanged**: `useConversationAudio.ts`'s own mute
mechanism (`setInputMuted` toggling the input `MediaStreamTrack`, added in specs/033) and its
removal of the dead ducking/interruption code are both still correct and are now, for the first
time, actually exercised by a real UI surface.

**Correction found during implementation**: actually wiring `useConversationAudio` up for real
surfaced one genuine, previously-latent bug in the hook itself: `startTurn()` had no `try/catch`
around `await recognition.start()` — its only error handling was the already-resolved (not
thrown) getUserMedia-denial case. Any other failure inside `recognition.start()` (e.g. a missing
browser API) rejected the promise `startTurn()` returns with nothing to catch it; `ChatPage.tsx`
calling it as `void conversationAudio.startTurn()` (matching this codebase's established
fire-and-forget convention for actions the caller doesn't need to await) meant that rejection had
no handler anywhere — an unhandled promise rejection, violating constitution §2.VIII, that this
feature's own test suite caught directly (a test exercising the untouched-until-now `startTurn()`
path crashed the render tree). Fixed by wrapping `startTurn()`'s body in `try/catch`, routing any
exception through the same `handleUnrecoverableFailure`/`errorMessage` path the denied-permission
case already used — every caller now gets a visible error through the existing mechanism, not a
silent/uncaught one, without needing to change any caller.

## Decision 5 — Dedicated Continuous view: reuse `AiPresenceCard`'s existing visualization

**Finding**: `AiPresenceCard.tsx` already renders Lucy's reactive particle-sphere visualization
(`SceneBackground`/`ReactiveSphere`) driven by a `getReactiveIntensity` callback — currently as a
small, always-present floating card, independent of conversation mode. `useConversationAudio`
already exposes its own `getReactiveIntensity` from the same underlying analyzer mechanism.

**Decision**: The new dedicated voice view renders a larger/full presentation of the same
`SceneBackground` component (not a new visualization), driven by the new `useConversationAudio`
instance's own `getReactiveIntensity` — reusing the component, not just the visual style.
`AiPresenceCard` itself (the small persistent card) is unaffected and continues to render as
today outside the dedicated view.

**Rationale**: Constitution §7 (design-system/component reuse) — avoids building a second 3D
visualization for what is conceptually the same "Lucy is present/speaking/listening" indicator at
a different size and context.

## Decision 6 — Entry/exit contract for the dedicated view

**Finding**: Per the resolved clarification, the view must never open automatically on chat load
even when Continuous is the saved preference — only an explicit user action (the mode-switch
button, or an equivalent deliberate action) opens it.

**Decision**: `handleToggleMode` in `ChatPage.tsx`, when switching *into* Continuous mode, sets a
new local "voice view active" flag (not the persisted `conversationMode` preference alone) that
gates rendering the dedicated view. Loading a chat with `conversationMode === 'Continuous'` as a
saved preference leaves this flag `false` until the user explicitly acts. Exit (FR-011) calls the
new `useConversationAudio` instance's `stop()`/`cancelListening()` and clears the flag, returning
to the normal chat view — it does not change the persisted mode preference (per spec.md's
Assumptions), so reactivating Continuous mode later re-opens the same dedicated view directly.

**Rationale**: Directly implements the resolved clarification with the smallest possible state
addition — a transient, non-persisted UI flag, not a new preference or backend concept.

**Unrelated pre-existing bug found while testing this decision**: exercising the mode-switch
click end-to-end in `ChatPage.test.tsx` (needed to test entry into the dedicated view) exposed a
latent bug in `voicePreferencesStore.ts`'s `update()`: it does `const saved = await
saveVoicePreferences({...}); set(saved)`, trusting the awaited result unconditionally. This
file's `saveVoicePreferences` mock was a bare `vi.fn()` with no implementation, resolving to
`undefined` — and zustand's `set(undefined)` (a non-object, non-function argument) *replaces* the
entire store state with `undefined` rather than merging, so any component subscribed to the store
crashes on its next render (`Cannot read properties of ... undefined`), tearing down the whole
React tree. Confirmed as a test-mock gap, not a production bug — the real `saveVoicePreferences`
API call always resolves to a real object; only this file's incomplete mock produced `undefined`.
Not previously caught because no existing test in this file happened to `await` far enough past a
mode-switch click for the corruption to surface before the test's own assertions had already
finished. Fixed by giving the mock a real implementation (echoing the patch back, simulating a
successful save) rather than touching the store's own code, since the store's behavior is correct
given a well-formed API response — only the test's simulation of that response was incomplete.
