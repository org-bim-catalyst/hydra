# Phase 0 Research: Chat Widget Reliability & Voice UI Consolidation

All four bugs were root-caused against the live codebase before this document was
written (not inferred from symptoms alone) — see file:line references throughout.

## Decision 1 — Voice-preferences 500: apply the missing migration, don't add defensive code

**Decision**: The 500 on `GET /api/v1/ai/voice/preferences` is fixed by applying the
already-authored, already-committed migration
`src/AskLucy.Persistence/Migrations/20260817110019_AddUserVoicePreferenceDefaultLanguage.cs`
to the target database. No application code changes are needed for the 500 itself —
`GetUserVoicePreferenceQueryHandler.cs:11-28` already null-checks a missing row and
returns platform defaults; `UserVoicePreferenceRepository.cs` is a correct, simple
`FirstOrDefaultAsync`. The only plausible unhandled-exception path in this chain is EF
projecting a column (`DefaultLanguage`) that doesn't exist yet on the deployed table,
throwing a `SqlException` that `ProblemDetailsMiddleware.Map()` has no specific case for
(falls to the generic `_ =>` arm, `ProblemDetailsMiddleware.cs:253-257`).

**Rationale**: Adding defensive `try/catch` around already-correct application code
would treat a symptom (schema drift) as if it were a code defect, and would mask future
genuine failures in the same handler behind a swallowed exception — directly against
constitution §2.VIII. The actual defect is operational (a migration that exists in
source control was never applied to the deployed database), and the actual fix is
operational (apply it) plus the safeguard in Decision 2 (catch this class of drift
before it manifests as a live-request failure again).

**Alternatives considered**: Wrapping the repository call in a try/catch that falls back
to defaults on any exception — rejected, because it would silently mask *any* future
database failure on this path (connectivity loss, permission error, a genuine future
bug), not just this one-time schema drift, which is a much bigger and less honest scope
than what's needed, and edges toward the "catch and discard" pattern §2.VIII forbids.

## Decision 2 — Recurrence safeguard: EF pending-migrations readiness check, not auto-migrate

**Decision**: Add a `/health/ready` endpoint backed by a new `IHealthCheck` that calls
`AskLucyDbContext.Database.GetPendingMigrationsAsync()` and reports unhealthy if any
migration is pending. This satisfies FR-012 (Clarification Q1: a targeted safeguard, not
a platform-wide schema-drift system) using EF Core's own migration bookkeeping — no
custom schema-diffing.

**Rationale**: `Program.cs` has no `Database.MigrateAsync()` call anywhere today — this
project deliberately does not auto-apply migrations at host startup (consistent with
constitution §5's two-step-deploy requirement for destructive migrations, and with this
team's established manual-migration deployment convention). Auto-migrating at boot would
be a bigger, riskier architectural change than this bug fix calls for, and would bypass
the controlled deploy gate §5 requires for destructive schema changes. A readiness check
is the documented, expected shape for this ("§14 Observability: `/health/ready`...
checking DB/provider connectivity") — it fails loudly and visibly (ops tooling, deploy
gates) the moment drift exists, before any user request hits it, without the app taking
unilateral schema action on its own.

**Scope note**: `GetPendingMigrationsAsync()` is inherently table-agnostic — EF Core has
no API for "pending migrations affecting table X only." Building one would be bespoke
complexity disproportionate to this fix (violates §2.III KISS/YAGNI) for a check whose
only realistic trigger, today, is exactly this table's migration. The check is described
here as scoped to "this data path" in the sense that it exists *because of* and is
*validated by* this feature's failure — not as a claim that it's mechanically restricted
to one table.

**Alternatives considered**: (a) `Database.MigrateAsync()` at startup — rejected, see
Rationale. (b) A custom reflection-based check that only inspects migrations touching
`UserVoicePreferences` — rejected as over-engineered relative to the value; EF's
boolean "any pending migrations" check already catches this exact failure class and is
the standard, supported primitive.

## Decision 3 — Reconciling "no alarming banner" with the No-Silent-Failures principle

**Decision**: Replace `ChatPage.tsx:709-717`'s `Snackbar` + `Alert severity="error"
variant="filled"` (which fires automatically, full-width, on every chat load when the
fetch fails) with a small, dismissible, non-blocking inline indicator placed in the
voice-settings area of the consolidated mic control (Decision 5) — e.g., a low-key
warning glyph with a tooltip/caption reading "Using default voice settings." It appears
only when the user actually opens voice settings or interacts with the mic control, not
as an unsolicited full-width banner the instant the chat window opens.

**Rationale**: FR-001 requires no *alarming* banner; constitution §2.VIII (NON-NEGOTIABLE)
requires that no failure be fully silent — "every async operation that can fail... MUST
have an explicit error path that reaches the user through visible UI feedback." These
are not actually in tension once "visible" is decoupled from "loud and immediate": a
small, contextual, dismissible indicator is still user-facing UI feedback (satisfies
§2.VIII) without being the first thing every user sees on every load (satisfies FR-001).
Server-side traceability (FR-003) is already satisfied without new code —
`ProblemDetailsMiddleware.cs:32-35` already logs unhandled exceptions at Error level
before mapping them to the generic Problem Details response, so operators can already
detect and diagnose this failure from existing logs.

**Alternatives considered**: (a) Remove the error surfacing entirely, log only to
`console.error` — rejected, this is exactly the frontend pattern §2.VIII forbids
("just a console log" is explicitly called out as insufficient). (b) Keep the existing
blocking Snackbar as-is — rejected, this is precisely Bug 1 as reported and fails FR-001
and SC-001/SC-005 outright.

## Decision 4 — `voicePreferencesStore` fetch moves to TanStack Query

**Decision**: `hydrateFromServer`'s manual `fetch`+`try/catch` (`voicePreferencesStore.ts:41-50`)
is replaced with a `useQuery` call (matching the existing sibling pattern in
`src/features/settings/hooks/useAiPreferences.ts`), consumed once near the top of
`ChatPage.tsx`'s `ConversationView`. The Zustand store is narrowed to hold only the
client-derived/synchronously-read preference values and any local UI-only flags (e.g.
"currently showing the fallback notice"); it no longer owns the fetch or its error state.

**Rationale**: Constitution §7 is explicit — "server state... lives in TanStack Query and
MUST NOT be duplicated into Zustand" — and the current file already violates this. It's
also the direct enabler of Bug 1's specific failure shape: a hand-rolled `try/catch`
around a raw `fetch` has none of TanStack Query's built-in retry/staleness/error-state
separation, which is exactly why this one fetch ended up wired straight into a blocking,
always-on Snackbar in the first place. Since this file must be edited anyway to
implement Decision 3, bringing it into line with the established, already-used pattern
in this codebase (§7 "Convention Over Configuration") costs nothing extra and directly
reduces the chance of the same failure shape recurring elsewhere.

**Alternatives considered**: Leave the store's fetch mechanism as-is and only change what
UI renders on error — rejected; it would leave a documented constitutional violation
in place in a file this feature is already touching, for no savings in effort.

## Decision 5 — Voice control consolidation: `ChatComposer` becomes the single surface

**Decision**: `ChatPage.tsx:659` (`<VoiceControlBar {...voiceControlsProps} />`) is
removed from `ExpandedChatPanel`'s children. `ChatComposer.tsx` becomes the sole home for
all voice controls in the Expanded panel: its existing PTT hold/tap gesture handling
(`ChatComposer.tsx:96-148`, already correct and reused as-is) stays; it gains:
- The mic icon rendering in **both** modes (today `showMicButton = conversationMode ===
  'PushToTalk'` hides it entirely in Continuous — `ChatComposer.tsx:150`). In Continuous
  mode the same icon becomes the listening pause/resume toggle (`isListening ? onStop :
  onStart`, already the exact semantic `VoiceControlBar.handleMicClick` used).
- A small menu/popover anchored to the mic icon holding the Continuous/Push-to-Talk mode
  switch (`onToggleMode`, moved from `VoiceControlBar.tsx:148-165`) — no microphone
  hardware device picker is added (see Scope note).
- The existing `RecordingReviewControls` (already shared, already used by both
  `VoiceControlBar` and `CollapsedVoiceControls`) for the PTT record→review→X/✓ flow,
  reused unchanged, invoked from `ChatComposer` instead of `VoiceControlBar`.
- Two distinct controls remain, deliberately not three (revised after user direction —
  see Decision 5a): (1) the mic icon's own contextual state *is* the microphone mute —
  in Continuous mode, tapping it stops the app from picking up the user's voice / stops
  Lucy from listening, without a separate icon (FR-006); (2) a single, persistent,
  always-visible speaker icon (`isMuted`/`onToggleMute`, generalizing
  `VoiceControlBar.tsx:131-135`) now does double duty as both the speaker-output mute
  toggle *and* the former "stop this reply" action (`VoiceControlBar.tsx:123-129`,
  merged in — see Decision 5a). Both remain distinct from each other (one mutes the
  mic/listening, the other mutes what Lucy speaks) and neither is folded into the mic's
  mode-switch menu.
- The single error/permission-denied surfacing already present in `ChatComposer`
  (`captureError`/`permissionState`, `ChatComposer.tsx:263-274`) absorbs
  `VoiceControlBar`'s equivalent (`errorMessage`/`onClearError`) — confirmed in
  `ChatPage.tsx:502-542` that both already read the *same* underlying `recorder.error`/
  `recognition.error` value, so this removes a second, genuinely redundant error
  Snackbar, not just a redundant icon.

`CollapsedVoiceControls.tsx` is unaffected — it already renders exactly one
consolidated stack (PTT mic / mode toggle / mute) for the Collapsed widget and was never
part of the duplication; it stands as this feature's own working reference for what a
correctly single-surfaced voice control looks like in this codebase.

**Rationale**: The bug is structural, not cosmetic — `VoiceControlBar` and
`ChatComposer` are unconnected siblings independently reacting to the same
`recorder`/`recognition` state (`ChatPage.tsx:659-679`), each rendering its own mic
button and its own recording-status UI. The fix has to remove one of the two rendering
trees, not paper over the symptom (e.g. conditionally hiding one based on the other's
state, which would be fragile and leave two implementations of the same feature to keep
in sync forever — a DRY violation the Constitution's §2.III explicitly warns against for
*business logic*, and voice-control state semantics qualify). `ChatComposer` is chosen
as the surviving surface because it already matches the reference product's layout (mic
inside the message-composer pill, not a separate toolbar above it) and already owns the
more sophisticated, already-tested tap-vs-hold gesture disambiguation
(`ChatComposer.tsx:62-148`) that would otherwise need to be rebuilt in `VoiceControlBar`.

**Scope note — no device picker**: `voice-preference-api.md` (spec 026) shows the
backend DTO already has dormant `preferredMicrophoneDeviceId`/`preferredSpeakerDeviceId`
fields, and the reference screenshots the user supplied show a device-selection dropdown.
Spec 029's own Assumptions state "no new voice-recording capability is being
introduced," and no FR in spec 029 requires device selection — only mode-switching is
named. Wiring actual `navigator.mediaDevices.enumerateDevices()` device selection UI is
therefore explicitly out of scope for this feature; the mic's menu here contains only
the mode toggle. Flagged as a natural, separate follow-up feature.

**Alternatives considered**: Keep both components but suppress `ChatComposer`'s inline
mic UI whenever `VoiceControlBar` is present — rejected; this doesn't remove the
duplicated *logic*, only hides its symptom, and leaves two divergent implementations of
recording UI to maintain (violates DRY per §2.III, and reintroduces exactly this bug the
next time either file changes without the other).

### Decision 5a — Speaker-mute and stop-current-reply are merged into one icon (post-plan revision)

**Decision**: `isSpeaking`/`onStopSpeaking` (`VoiceControlBar.tsx:123-129`, the separate
"stop AI reply" button, only visible while Lucy is speaking) is removed as its own
control, and as a prop `ChatComposer` needs at all. `onToggleMute` becomes the single
handler for both concerns, fully owned by the caller (`ChatPage.tsx`): when invoked
while `tts.isSpeaking` is true, it must both call `tts.stop()` (silencing the
in-progress reply immediately) and set `isMuted: true` via the existing
`updateVoicePreference({ isMuted: true })` path (`ChatPage.tsx:517`/`540`, unchanged
call site); when invoked at any other time, it simply toggles `isMuted`. Unmuting never
resumes a previously interrupted reply — it only allows the *next* reply to be spoken.
`ChatComposer` itself only needs `isMuted` (to render the correct icon) and
`onToggleMute` — it does not need to know `isSpeaking` for this control at all.

The "Lucy is speaking…" text label (`VoiceControlBar.tsx:172-174`) is removed with **no
replacement inside the chat panel**, not a pulse as this document originally proposed
one revision ago. Correction, per direct user clarification: the `VoiceAnalyzer`
waveform this document previously pointed to as "already covering this" is wired to
`CollapsedChatControl` (`ChatPage.tsx:555-556`) and represents the *user's own
microphone* activity, not Lucy's — a distinct, separate concern from what the deleted
text label was showing. The actual existing indicator for Lucy speaking is
`AiPresenceCard` (`ChatPage.tsx:176`), a persistent reactive presence visual ("the
sphere") rendered as a sibling of the chat widget inside `WorkspaceOverlay`, driven by
the same `tts.getIntensity` signal, independent of whether the chat panel is expanded or
collapsed. Because that indicator already exists, is already prominent, and already
renders regardless of chat-panel state, no replacement indicator is needed inside the
composer row — the original "just remove the text" instinct was correct, only the
justification was wrong in the prior revision of this document.

**Rationale**: Explicit user direction: "merge the function of Speaker mute and stop in
one button... muted... continue muted either Lucy is speaking or not until it is
pressed again," followed by a direct correction of this document's own reasoning about
which visual already indicates Lucy speaking. Both are the user resolving genuine
ambiguity/errors this plan had, not scope creep — the net result is a simpler contract
(one fewer prop, no new animation) than the immediately preceding revision.

**Alternatives considered**: Keep the pulse-on-icon idea from the prior revision —
rejected now that its premise (no existing Lucy-speaking indicator) is known to be
false; it would have been unnecessary, redundant UI work. Add a waveform to the Expanded
panel — rejected, same reason: `AiPresenceCard` already serves this purpose platform-wide.

### Decision 5b — "Listening…" text label also removed, no replacement

**Decision**: The "Listening…" text (`ChatComposer.tsx:225`, `VoiceControlBar.tsx:169`
— both instances retired along with the rest of the duplicated UI per Decision 5) is not
carried into the consolidated control. No replacement text or animation is added beyond
what already exists: `ChatComposer.tsx:203-213`'s pulse animation on the mic icon while
`isListening` (already present, already `usePrefersReducedMotion`-aware) already
communicates active capture on its own.

**Rationale**: Same principle as Decision 5a's "Lucy is speaking…" removal — a text
label restating a state the control's own visual (pulse) already conveys is redundant,
per explicit user direction. Unlike the speaking-text case, this one needed no
correction: the replacement mechanism (the mic's existing pulse) was already part of
this plan's design (Decision 5) before this text label was raised, so no new prop or
animation is introduced — only a deletion.

**Alternatives considered**: None substantive — this follows directly from the same
reasoning already applied to FR-013, with an already-existing visual (rather than a
newly discovered one) serving as the reason no replacement is needed.

## Decision 6 — Translate control relocates into the composer row; `ProjectPicker` stays put

**Decision**: The `RiTranslate2` `IconButton` (`ChatPage.tsx:588-590`) moves out of the
`Toolbar` at `ChatPage.tsx:578-591` and into `ChatComposer`'s row, next to the
attach/mic/send icons, per Clarification Q2. `ProjectPicker` (`ChatPage.tsx:587`) is
**not** moved — it stays in the existing `Toolbar`, which becomes a single-item row and
has its `sx` height tightened (removing the fixed MUI `Toolbar` dense-variant minimum
that would otherwise persist unchanged regardless of child count) so the vacated space
is genuinely reclaimed for the message list, satisfying FR-008/SC-004 without disturbing
`ProjectPicker`'s position.

**Rationale**: `specs/026-floating-chat-assistant/contracts/chat-widget-components.md:108`
explicitly documents "`ProjectPicker` and the Translate action stay" in
`ConversationView`'s own toolbar as a deliberate boundary against `ExpandedChatPanel`'s
identity/branding header (`ExpandedChatPanel.tsx:63-105`, which spec 026 FR-012/FR-015
kept deliberately minimal — no extra controls beyond collapse/identity/language-flag/
new-chat). Relocating `ProjectPicker` into that header (e.g., via its already-present but
currently-unused `headerTrailing` slot) would reclaim more vertical space, but would
silently reverse a different feature's documented, deliberate design decision that spec
029 was never asked to revisit — exactly the kind of unrequested scope expansion the
constitution's AI Agent Rules warn against ("never invent requirements not present in
the approved specification"). Tightening the now-single-item `Toolbar`'s own height
achieves the required space gain (FR-008) without touching that boundary.

**Alternatives considered**: Move `ProjectPicker` into `ExpandedChatPanel`'s
`headerTrailing` slot alongside/instead of relocating translate — rejected per Rationale;
flagged here for visibility in case the user wants that larger change as a follow-up,
but not applied without it being explicitly requested.

## Decision 7 — SignalR hub interception: `MapFallback`, not a bigger exclusion list

**Decision**: `Program.cs`'s SPA-fallback `app.Use(...)` middleware (lines 496-543) is
split in two. The static-file-serving half (lines 496-517) stays exactly where it is,
unchanged — it's the documented workaround for a real, previously-diagnosed failure of
the built-in `StaticFileMiddleware` (`Program.cs:483-492`) and this feature does not
touch it. The SPA index.html-fallback half (lines 519-540) is removed from that early
`app.Use(...)` position and re-registered as `app.MapFallback(async context => { ...same
`wwwrootProvider`-based index.html read as today... })`, placed after
`app.MapControllers()` and every `app.MapHub<...>()` call (after `Program.cs:594`). The
manual prefix-exclusion list (`requestPath.StartsWith("/api")` / `"/openapi"` /
`"/health"`) is deleted entirely — no longer needed, since `MapFallback` is an
endpoint-routing endpoint with the lowest possible match priority, so any explicitly
mapped endpoint (every controller route, every `/hubs/*` hub, `/health`, `/openapi`) is
matched first automatically, by the routing system's own precedence rules, not by a
maintained list of prefixes someone has to remember to keep in sync.

**Rationale**: The investigation confirmed this is the actual root cause of the
`/hubs/panels` failure in the production console log, and — because the exclusion list
was never updated for `/hubs` — it equally affects all 6 mapped hubs
(`document-processing`, `retrieval-indexing`, `memory`, `agent-execution`,
`workflow-execution`, `panels`), matching FR-009/FR-011's "apply uniformly" requirement.
Critically, this is *not* a retry of the previously-failed built-in mechanism: the
earlier documented incident (`Program.cs:483-492`) was about `StaticFileMiddleware`/
`MapFallbackToFile` failing to serve *arbitrary static assets* from the PreBuildEvent-
populated `wwwroot`; `MapFallback` here still uses the same already-proven, manually
reading `wwwrootProvider.GetFileInfo("index.html")` code — it only changes *where in the
pipeline* that one, single, known-good file read is triggered from. The static-asset
serving path that previously broke is left completely untouched.

**Alternatives considered**: (a) Add `/hubs` to the existing exclusion list — rejected;
this fixes today's known failure but repeats the exact fragile pattern (a maintained
prefix list) that caused it, leaving the next new non-controller, non-hub route exposed
to the same class of bug, which is precisely what the investigation flagged as the
underlying risk. (b) Revert to `UseStaticFiles()` + `MapFallbackToFile()` wholesale —
rejected; this is the mechanism already tried and empirically failed for this
deployment's static assets (`Program.cs:483-492`), and reintroducing it would risk
regressing the previously-fixed static-asset-serving bug to "fix" this one.
