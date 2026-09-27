# Feature Specification: Restore Local Whisper as the Primary Dictation Engine

**Feature Branch**: `078-restore-local-whisper`

**Created**: 2026-09-27

**Status**: Draft

**Input**: User description: "OK, but this is not what I wanted and you fooled me because you
didn't make it clear that there are two whisper, please bring back the local one again, and as we
did in the voice generator where the user can set the default model or upload custom one with FTP,
we need to do the same for the transcription, so the main one will be the local whisper then if
fail the built in browser, and any time the user can change the primary model to Open AI or Eleven
Labs when their subscription is paid, so when they fail they fail to the browser built in also."
(Full context, including the removal this reverses, in the `/speckit-specify` invocation of
2026-09-27.)

**Naming, used consistently below** — the feature request turned on a naming mistake, so every
mention here is explicit:

- **Local Whisper**: an open-source speech-to-text model that runs in this server's own process.
  Free; no vendor account. This is the engine this spec restores and promotes to default.
- **OpenAI Whisper**: OpenAI's hosted `whisper-1` API. Paid, metered, requires OpenAI switched on
  under Admin → AI providers.
- **ElevenLabs realtime**: ElevenLabs' hosted live-dictation API. Paid, metered, requires
  ElevenLabs switched on under Admin → AI providers.
- **Browser built-in**: the web browser's own on-device speech recognition. Free; the dictation
  engine of last resort, never a vendor.

## Clarifications

### Session 2026-09-27

- Q: The browser built-in recognizer (Web Speech API `SpeechRecognition`) only hears the live
  microphone and cannot transcribe an already-recorded clip — so what does "fall back to the
  browser" mean when the primary engine fails? → A: One listener at a time (never the browser
  recognizer running alongside the recording). The primary engine's health is checked before
  recording starts; if it is down, that attempt dictates through the browser built-in from the
  start. If the primary engine fails on a clip already recorded, Lucy gently asks the user to
  repeat what she missed, and that retry dictates through the browser built-in.
- Q: Who chooses the primary dictation engine — an administrator platform-wide, each user by
  subscription plan, or both? → A: An administrator, platform-wide. "Subscription is paid" means
  the vendor is switched on under Admin → AI providers; there is no per-user engine choice and no
  plan-tier gating in this feature.
- Q: With several Whisper models deployed through Custom Models, which one does Local Whisper
  load? → A: The one an administrator explicitly selects. Deploying never activates a model by
  itself, so a new model can be deployed, then selected and tried, and the
  old one deleted only once the new one is proven.
- Q: How does an administrator test a model before switching everyone to it? → A: A "Try it"
  action per model: record a short sample, see the transcript and transcription time, with no
  change to the selection or to any user's dictation.
- Q: What happens when the vendor behind the primary engine (OpenAI Whisper or ElevenLabs
  realtime) stops being usable? → A: Two cases. (1) An administrator deliberately switches the
  vendor off under Admin → AI providers: the primary reverts to Local Whisper. (2) The vendor's
  subscription lapses or expires (still switched on, but requests fail for billing, credential or
  quota reasons): administrators are notified and all dictation uses the browser built-in
  recognizer until an administrator either renews and sets that engine back, or switches to Local
  Whisper. Transient outages are not a lapse; they only send that attempt to the browser.
- Q: ElevenLabs realtime only streams live, so which engine transcribes a Push-to-Talk clip while
  ElevenLabs realtime is primary? → A: Local Whisper, unless an administrator chooses another
  Push-to-Talk engine.
- Q: What does dictation use on a fresh deployment, with no Local Whisper model and no cloud engine
  set up? → A: The browser built-in, as the normal path, until an administrator adds AI providers
  and deploys and selects a Local Whisper model. Nothing is downloaded or activated automatically.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Dictation runs on Local Whisper by default, with no subscription (Priority: P1)

A user with no AI provider subscriptions dictates a chat message on a platform where an
administrator has deployed and selected a Local Whisper model. Their words are transcribed by Local
Whisper, running on Lucy's own server, at no per-use cost to the platform. On a fresh deployment,
before any model is selected, dictation still works through the browser built-in.

**Why this priority**: This is the reversal the whole feature exists for: a free, self-hosted
engine restored as the platform's normal, first-choice way to turn speech into text, rather than a
paid cloud engine being the only thing standing between the user and the browser's built-in
recognizer.

**Independent Test**: On a server where an administrator has deployed and selected a Local Whisper
model and made no other dictation choice, dictate a sentence in Push-to-Talk mode and in Continuous
mode. Both produce the correct transcript, and no OpenAI or ElevenLabs request is made. On a fresh
deployment with no model selected, the same dictation is served by the browser built-in with no
degraded notice.

**Acceptance Scenarios**:

1. **Given** an administrator has selected a Local Whisper model and made no other dictation
   choice, **When** a user dictates, **Then** Local Whisper transcribes it.
2. **Given** Local Whisper is transcribing, **When** the request completes normally, **Then**
   nothing is recorded on the operational failure trail (specs/074) — this is the normal path, not
   a degraded one.
3. **Given** a fresh deployment where no Local Whisper model is selected and no cloud engine is set
   as primary, **When** a user dictates, **Then** the browser built-in serves it with no degraded
   notice, and nothing is recorded as a failure (FR-002).

---

### User Story 2 - Local Whisper fails over to the browser, never to a paid engine (Priority: P1)

Local Whisper cannot transcribe a user's clip (the selected model's file is missing, the process
is overloaded, or an unexpected error occurs). The user's dictation still works, using the browser's
own built-in recognizer, without the platform silently starting to spend money on a cloud engine
it never chose to use for that user.

**Why this priority**: The user was explicit that a failure must land on the browser built-in, not
on a paid vendor picking up the free engine's failed request unasked. Equally important the other
direction: whichever cloud engine an administrator has deliberately chosen as primary must not
quietly fail over to Local Whisper or to the other cloud engine either — every primary's failure
path is the same one step, to the browser.

**Independent Test**: Force Local Whisper to fail (e.g., the selected model's file is removed from
the server). Dictate in both
capture modes. The browser's built-in recognizer serves the transcript in both, no OpenAI or
ElevenLabs request is made, and the failure is recorded on the operational failure trail with no
vendor detail shown to the user.

**Acceptance Scenarios**:

1. **Given** Local Whisper is primary and already unhealthy before recording starts, **When** a
   user dictates, **Then** that attempt dictates through the browser built-in recognizer from the
   start, and no words are lost.
2. **Given** Local Whisper is primary and fails on a clip the user has already spoken, **When**
   the failure occurs, **Then** Lucy gently asks the user, in their language, to repeat what she
   missed, and the retry dictates through the browser built-in recognizer.
3. **Given** Local Whisper is primary and fails, **When** the failure is recorded, **Then** an
   administrator can see engine, reason and correlation id on the operational failures page
   (specs/074), and the end user never sees vendor or internal detail.
4. **Given** an administrator has instead set OpenAI Whisper or ElevenLabs realtime as primary,
   **When** that engine fails, **Then** dictation falls to the browser built-in directly — never to
   Local Whisper or to the other cloud engine first.

---

### User Story 3 - An administrator changes the primary dictation engine (Priority: P1)

An administrator whose organization has a paid OpenAI or ElevenLabs subscription opens the admin
panel and switches the platform's primary dictation engine from Local Whisper to that paid vendor,
the same place they already manage the platform's spoken-voice (text-to-speech) engine.

**Why this priority**: This is the admin-configurability the request specifically asked for,
mirrored on the existing voice-generator admin page rather than inventing a new one.

**Independent Test**: With OpenAI switched on under Admin → AI providers, open the dictation
engine setting, choose OpenAI Whisper, save, then dictate. The transcript comes back through
OpenAI Whisper. Switching OpenAI back off removes it from the list of choices and reverts the
primary to Local Whisper (FR-015).

**Acceptance Scenarios**:

1. **Given** the admin dictation-engine setting, **When** an administrator opens it, **Then** they
   see Local Whisper, OpenAI Whisper and ElevenLabs realtime, with OpenAI Whisper and ElevenLabs
   realtime only choosable while their respective vendor is switched on under Admin → AI providers.
2. **Given** an administrator picks OpenAI Whisper as primary, **When** they save, **Then** every
   subsequent dictation request platform-wide uses OpenAI Whisper as primary, with the browser
   built-in as its only fallback.
3. **Given** no administrator has ever changed the setting, **When** the platform is queried,
   **Then** the primary engine is Local Whisper.
4. **Given** an administrator sets ElevenLabs realtime as primary, **When** a user dictates in
   Push-to-Talk mode, **Then** the clip is transcribed by the Push-to-Talk engine — Local Whisper
   unless the administrator chose another (FR-017) — since ElevenLabs realtime only streams live.

---

### User Story 4 - An administrator deploys and selects a Local Whisper model (Priority: P1)

An administrator gives Local Whisper a model to run — the recommended multilingual model on first
setup, or later a different, better, or newly-released one. They use the same Custom Models deployment flow already used for the
voice-generator's Supertonic model: paste a Hugging Face URL, name a destination, and the server
fetches and deploys the file(s) to production without the administrator's browser downloading
anything.

**Why this priority**: Explicitly requested, and it reuses an existing, already-shipped mechanism
rather than building a second way to deploy a model. It is P1 because nothing is downloaded
automatically: until an administrator deploys and selects a model, Local Whisper cannot serve anyone
(FR-002).

**Independent Test**: On a test deployment target, submit a Hugging Face URL pointing at a single
Whisper model file with a destination folder. The job completes; once selected it transcribes
correctly; and switching it Unavailable sends dictation to the browser built-in, with the admin
dictation page saying why.

**Acceptance Scenarios**:

1. **Given** the Custom Models section, **When** an administrator adds a model from a Hugging Face
   URL that names one specific file (for example a `/resolve/<revision>/<file>` URL), **Then** only
   that file is fetched and deployed — not every file in the repository.
2. **Given** a Whisper model deployment has just Completed, **When** a user dictates, **Then**
   Local Whisper keeps using its currently selected model — a deployment never becomes active on
   its own.
3. **Given** an administrator selects that deployment as the Local Whisper model, **When** a user
   dictates, **Then** Local Whisper loads and transcribes from that deployed file.
4. **Given** no model is selected, or the selected deployment is marked Unavailable, **When** a
   user dictates, **Then** dictation uses the browser built-in, and the admin dictation page says
   why (FR-010).
5. **Given** a deployed Whisper model that is not selected, **When** an administrator uses "Try
   it" and records a short sample, **Then** they see its transcript and transcription time, and
   every user's dictation keeps using the selected model.

---

### Edge Cases

- What happens when the audio a user recorded needs converting before Local Whisper or OpenAI
  Whisper can read it (both need a 16kHz mono WAV clip; browsers record webm/ogg)? See FR-013.
- What happens when an administrator switches off the vendor backing the engine currently selected
  as primary (e.g., OpenAI Whisper is primary, then OpenAI is switched off under Admin → AI
  providers)? The primary reverts to Local Whisper (FR-015).
- What happens when the primary cloud engine's subscription lapses while it stays switched on?
  Administrators are notified, dictation is suspended onto the browser built-in recognizer for
  everyone, and the vendor is no longer called until an administrator decides (FR-016).
- What happens when a cloud engine times out or returns a server error once? That attempt falls to
  the browser built-in (FR-005/FR-005b); it is not treated as a lapsed subscription.
- What happens when an administrator changes the selected Local Whisper model while a dictation
  request is already in flight? Mirrors Supertonic's existing behavior: the in-flight request
  finishes on the model it started with; the next request picks up the new one.
- What happens when an administrator tries to delete the Custom Models deployment currently
  selected as the Local Whisper model? The deletion is refused with a message asking them to
  select a different model first, so an old model is only removed after its replacement is
  proven.
- What happens on a fresh deployment with no Local Whisper model and no cloud engine set up?
  Dictation uses the browser built-in as its normal path (FR-002). The English-only file already on
  the production server is not a deployed Custom Model, so it is not picked up.
- What happens to Push-to-Talk while ElevenLabs realtime, which only streams live, is primary? The
  clip goes to the Push-to-Talk engine — Local Whisper unless an administrator chose another
  (FR-017).
- What happens in Continuous mode, which today streams live audio only to ElevenLabs realtime?
  With Local Whisper or OpenAI Whisper primary, Continuous mode uses its already-existing
  recording-and-transcribe fallback path (the same one used today when ElevenLabs realtime cannot
  be reached) as its normal path, not as a degraded one.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST provide a self-hosted, in-process Local Whisper transcription
  engine that requires no vendor account, no API key and no per-use cost.
- **FR-002**: The platform MUST treat Local Whisper as the primary dictation engine until an
  administrator chooses otherwise. While no Local Whisper model is selected (as on a fresh
  deployment), dictation that would use Local Whisper MUST use the browser built-in recognizer as
  its normal path — with no degraded notice and nothing recorded as a failure — until an
  administrator selects a Local Whisper model or sets a cloud primary engine.
- **FR-003**: An administrator MUST be able to set the platform's primary dictation engine to one
  of: Local Whisper, OpenAI Whisper, or ElevenLabs realtime, from the same admin area that already
  manages the platform's spoken-voice (text-to-speech) engine.
- **FR-004**: OpenAI Whisper MUST be offered as a choosable primary dictation engine only while
  OpenAI is switched on under Admin → AI providers; ElevenLabs realtime likewise only while
  ElevenLabs is switched on there.
- **FR-005**: Whichever engine is primary, its failure for any reason MUST make the current
  dictation attempt fall to the browser built-in recognizer. No engine MUST fail over to another
  cloud engine, and no engine MUST fail over to Local Whisper except when Local Whisper is itself
  primary. (Push-to-Talk's configured engine under ElevenLabs realtime, FR-017, is a configured
  choice, not a failover.)
- **FR-005a**: Only one listener MUST capture the microphone at a time — the browser built-in
  recognizer MUST NOT run alongside a recording. Before each recording starts, the platform MUST
  check the primary engine's health; if it is unhealthy, that attempt MUST dictate through the
  browser built-in recognizer from the start.
- **FR-005b**: When the primary engine fails on a clip the user has already spoken, Lucy MUST
  gently ask the user to repeat what she missed, in the user's language: always as a visible
  message, and also spoken aloud in Lucy's own voice persona when voice replies are on. The retry
  MUST dictate through the browser built-in recognizer — in Continuous mode Lucy starts listening
  again automatically; in Push-to-Talk the user presses the control again. Later attempts return
  to the primary engine once its health check passes.
- **FR-006**: A dictation request served successfully by whichever engine is primary MUST NOT be
  recorded as a failure or a failover on the operational failure trail — only an actual failure is.
- **FR-007**: Every dictation failure (engine unreachable or erroring, model missing or marked
  Unavailable, audio the engine could not process) MUST be recorded on the existing operational
  failure trail (specs/074) with engine, a sanitized reason and a correlation id, and MUST NOT
  surface vendor or internal detail to the end user, who sees only a generic notice.
- **FR-008**: The recommended Local Whisper model — documented for administrators to deploy
  first — MUST be a multilingual model covering English, Arabic, Spanish, French and German,
  replacing today's English-only file. No model is downloaded or activated automatically.
- **FR-009**: An administrator MUST be able to deploy a different Local Whisper model the same way
  the voice generator's Supertonic model is deployed today: a Hugging Face repository URL and a
  destination, fetched and pushed to production by the server, with the administrator's browser
  never downloading the model. A model may come from any Hugging Face repository, not only the
  official Whisper one.
- **FR-009a**: Deploying a model MUST NOT change which model Local Whisper uses. An administrator
  MUST explicitly select the Local Whisper model — one Completed Custom Models deployment holding
  a readable Whisper model file — next to the primary dictation engine setting.
  A selection that is not a readable Whisper model MUST be refused with a clear reason.
- **FR-009c**: An administrator MUST be able to try any selectable Local Whisper model without
  selecting it: record a short sample and see the transcript and how
  long transcription took. Trying a model MUST NOT change the selection or affect any user's
  dictation; if the try fails, the administrator sees the reason.
- **FR-009b**: A Custom Models deployment currently selected as the Local Whisper model MUST NOT
  be deletable until another model is selected.
- **FR-010**: When no Local Whisper model is selected, or the selected deployment is marked
  Unavailable, dictation that would use Local Whisper MUST use the browser built-in recognizer, and
  the admin dictation page MUST say why. This is not a failure. A selected, Available model whose
  file is missing or unreadable is a failure (FR-007).
- **FR-011**: When a Hugging Face source URL names one specific file (for example a
  `/resolve/<revision>/<file>` or `/blob/<revision>/<file>` URL), a custom model deployment MUST
  fetch and deploy only that named file, not every file in the repository. A source URL naming no
  specific file continues to deploy the whole repository, unchanged from today.
- **FR-012**: Both existing dictation capture paths MUST keep working with every primary engine:
  Push-to-Talk's record-then-transcribe flow, and Continuous mode, which streams live only to
  ElevenLabs realtime when that is reachable and otherwise uses the same record-then-transcribe
  flow as Push-to-Talk. While ElevenLabs realtime is primary, Push-to-Talk uses the Push-to-Talk
  engine (FR-017).
- **FR-013**: Whatever audio format the browser records, a clip reaching Local Whisper or OpenAI
  Whisper MUST arrive as a 16kHz mono WAV clip, since both require that format for a single
  request; the existing ElevenLabs realtime streaming path is unaffected, since it does not use
  this conversion.
- **FR-014**: The English-only Local Whisper model file still present on the production server
  today (left behind when the Local Whisper code was removed) MUST NOT be deleted by this
  feature — it is retired only once the recommended model (FR-008) is deployed through Custom
  Models, selected, and confirmed working.
- **FR-015**: When an administrator switches off the vendor behind the current primary dictation
  engine, the primary MUST revert to Local Whisper, and the admin dictation page MUST show that it
  was reverted and why. A Push-to-Talk engine (FR-017) set to that vendor likewise reverts to Local
  Whisper.
- **FR-016**: When the primary cloud engine fails for a subscription reason (billing, expired or
  rejected credential, exhausted quota) — as distinct from a transient outage — the platform MUST:
  notify administrators; mark the dictation engine setting Suspended, with the lapsed engine,
  reason and time; stop calling that vendor for dictation; and route all dictation that would use
  that vendor to the browser built-in recognizer until an administrator sets an engine again (the
  same cloud engine after renewing, or another), which clears the suspension. Dictation served by
  an engine that does not depend on that vendor (for example Local Whisper as the Push-to-Talk
  engine) continues unaffected. The admin dictation page MUST show that the
  browser built-in recognizer is in use while Suspended.
- **FR-017**: While ElevenLabs realtime is primary (it only streams live), Push-to-Talk clips MUST
  be transcribed by an administrator-chosen Push-to-Talk engine: Local Whisper by default, OpenAI
  Whisper (only while OpenAI is switched on), or the browser built-in. Its failure falls to the
  browser built-in like any other engine (FR-005).

### Key Entities

- **Dictation engine setting**: the platform-wide administrator choice of primary dictation engine
  (Local Whisper / OpenAI Whisper / ElevenLabs realtime), plus the Push-to-Talk engine used while
  ElevenLabs realtime is primary (FR-017). Exactly one value each at a time; applies to every
  user. State: Active, or Suspended (with reason and time) after a subscription failure
  (FR-016); records when and why it was last reverted to Local Whisper (FR-015).
- **Local Whisper model selection**: the platform-wide administrator choice of which model Local
  Whisper loads — none (a fresh deployment, FR-002/FR-010) or a reference to one Completed Custom
  Models deployment (FR-009a). Exactly one value at a time; a referenced deployment cannot be deleted
  while selected (FR-009b).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With only a Local Whisper model selected and no other administrator configuration,
  100% of new dictation requests are served by Local Whisper rather than a paid vendor; on a fresh
  deployment with nothing configured, 100% are served by the browser built-in.
- **SC-002**: When the primary dictation engine cannot serve a request, 100% of those requests still
  produce a transcript via the browser built-in recognizer, with zero requests reaching a different
  cloud vendor than the one an administrator chose.
- **SC-003**: An administrator can change the platform's primary dictation engine in under 1 minute,
  with no code change or deployment.
- **SC-004**: Every dictation failure appears on the operational failures page within the same
  latency the existing voice-generation failures already do, with enough detail (engine, reason,
  correlation id) for an administrator to act without reading server logs.
- **SC-005**: Deploying a single-file Local Whisper model update transfers only that file's size to
  production, not the size of every model variant in its source repository.
- **SC-006**: Administrators are notified on the first dictation that fails for a subscription
  reason, and after that no further dictation requests reach that vendor until an administrator
  acts.

## Assumptions

- "The user's subscription is paid" (from the feature request) means an administrator has switched
  the corresponding vendor (OpenAI or ElevenLabs) on under Admin → AI providers — the same
  switched-on/off state already governing every other capability those vendors serve. This spec
  does not add a second, dictation-specific subscription concept.
- The recommended Local Whisper model (FR-008) is a multilingual model covering English, Arabic,
  Spanish, French and German — the platform's existing supported languages — rather than every
  language Whisper models can support, matching the scope of every other localized feature in the
  product today.
- Local Whisper and OpenAI Whisper remain clip-based (record, then transcribe) for this feature;
  neither gains live-streaming support. Only ElevenLabs realtime streams live, exactly as today.
- Deployment of a Local Whisper model reuses the existing Custom Models configuration-backed FTP
  target (ADR 0016) rather than the not-yet-built deployment-connector abstraction (specs/071).
