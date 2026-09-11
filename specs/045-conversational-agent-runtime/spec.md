# Feature Specification: Conversational Agent Runtime

**Feature Branch**: `045-conversational-agent-runtime`

**Created**: 2026-09-08

**Status**: Draft

**Input**: User description: "Make Lucy a real orchestrated multi-agent system instead of a hardcoded deterministic pipeline. Today every chat turn runs a fixed straight-line sequence that Lucy never chooses, action narration is canned template text bolted onto a reply written before the outcome was known, and the Agents table is empty in production — the agent runtime built by specs 020/021/022 has never run a conversation. A conversation should be driven by a system-provisioned orchestrator agent that reasons about intent, delegates to specialists, narrates what it is doing while it does it, and proposes grounded next actions the user can choose from — e.g. 'Show me Al Safa Park 2' → 'OK, let me find it first.' → 'I found Al Safa Park 2. I'll display it on the map and centre the viewer on it.' → 'What would you like to do next?' with a dynamically generated, capability-grounded option list."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Lucy Works the Request Out Loud (Priority: P1)

A user asks Lucy to do something that requires her to actually go and do work — "Show me Al Safa Park 2". Instead of a single reply that appears after everything has already happened, Lucy tells the user what she is about to do, does it, and then tells the user what she found, in her own words. The user watches the request being worked rather than waiting at a blank screen and then receiving a finished paragraph.

**Why this priority**: This is the difference the whole feature exists to make. Every other story in this spec — grounded options, delegation, audit — presupposes that a turn is a sequence of deliberate beats Lucy chose, rather than a fixed pipeline whose narration is stitched on afterwards. It is also the smallest slice that proves the runtime end-to-end.

**Independent Test**: Can be fully tested by sending a single request that requires an action ("show me <place>") and confirming the user receives, in order and as separate messages: an acknowledgement naming what Lucy is about to do, then a result message describing what actually happened, with the acknowledgement visible before the work begins rather than after it completes.

**Acceptance Scenarios**:

1. **Given** an active conversation, **When** the user sends a request that requires Lucy to perform an action, **Then** Lucy's first visible output is a short acknowledgement stating what she is about to do, delivered before that work starts.
2. **Given** the acknowledgement has been shown, **When** the work completes, **Then** Lucy reports the actual outcome in a new message written from the real result — naming what was found, and what she did with it.
3. **Given** the work takes a noticeable amount of time, **When** the user is waiting, **Then** a visible pending indication states what is currently in progress, and the conversation never presents a finished-looking message that is silently still working.
4. **Given** the user sends a message that needs no action at all (a plain question, a greeting), **When** Lucy responds, **Then** she answers directly with no acknowledgement beat and no artificial ceremony, and the response begins no later than it does today.
5. **Given** an action's outcome was a failure (not found, ambiguous, unavailable), **When** Lucy reports it, **Then** the report describes what actually went wrong in that turn, and the user is never shown a success narration for work that did not succeed.

---

### User Story 2 - Lucy Offers Only What She Can Actually Do (Priority: P1)

After completing an action, Lucy asks the user what they would like to do next and presents a short list of concrete options. The options are assembled from the capabilities Lucy actually has, filtered by what is possible given the current conversation state and available data — so after locating a site she may offer to outline its boundary or search an attached knowledge base for it, but she never offers something the platform cannot do.

**Why this priority**: A dynamically generated option list is the user-visible proof that Lucy is capability-aware rather than script-following, and it is what turns a one-shot answer into a guided workflow. Grounding is the non-negotiable half: an option list that can invent capabilities is worse than no option list, because it teaches the user to expect things the product will then fail to deliver.

**Independent Test**: Can be fully tested by completing an action in two different conversation states (e.g. with and without a knowledge base attached, with and without a site boundary already drawn) and confirming the offered options differ appropriately, that every offered option corresponds to a real registered capability, and that no option names a capability the platform does not have.

**Acceptance Scenarios**:

1. **Given** Lucy has just completed an action, **When** at least one further capability is available in the current context, **Then** she asks what the user would like to do next and presents a bounded list of selectable options, each with a short label and a one-line description, plus an explicit "nothing for now" choice.
2. **Given** the same completed action but a different conversation state (different attached data, different already-performed actions), **When** the options are presented, **Then** the offered set differs to reflect what is actually possible now — options already satisfied or not applicable are absent.
3. **Given** Lucy proposes an option that does not correspond to a registered, currently-available capability, **When** the response is assembled, **Then** that option is discarded before the user ever sees it and the discard is recorded for diagnosis.
4. **Given** no further capability is offerable in the current context, **When** the turn ends, **Then** no option list is shown at all rather than an empty or padded one.
5. **Given** an option list is displayed, **When** the user ignores it and types a normal message instead, **Then** the message is accepted normally and the stale offer is dismissed without error.
6. **Given** a turn that answered a plain question and invoked no capability, **When** it ends, **Then** no option list is shown and no offer step runs at all — the turn is indistinguishable from an ordinary reply.
7. **Given** the user declined the previous offer, **When** the next turn ends, **Then** the same suggestions are not presented again.
8. **Given** offerable capabilities exist but none would plausibly be wanted next, **When** the turn ends, **Then** no list is shown — the offer is never padded to reach a minimum length.

---

### User Story 3 - Choosing an Option Does Exactly That (Priority: P1)

The user selects one of Lucy's offered options. Lucy performs precisely that action, with the context she already established, without re-interpreting the choice as free text and without asking the user to restate what they meant.

**Why this priority**: Without reliable dispatch, the option list is decorative. This is also where the current architecture fails hardest today — a reply of "1" would be re-run through intent classification with no idea what "1" referred to.

**Independent Test**: Can be fully tested by completing an action, selecting an offered option, and confirming the corresponding capability runs against the already-established context, with no re-resolution of information Lucy already had and no clarifying question.

**Acceptance Scenarios**:

1. **Given** Lucy has offered options bound to the current context, **When** the user selects one, **Then** exactly that capability executes against that context, and the work Lucy already did in the prior turn is not repeated.
2. **Given** an option was offered, **When** the user selects the "nothing for now" choice, **Then** the offer closes with a brief acknowledgement and no work is performed.
3. **Given** an option list is displayed, **When** the user instead types their own instruction, **Then** the typed message is handled as a normal turn and the pending offer is discarded.
4. **Given** an option was offered several turns ago and the conversation has moved on, **When** the user tries to select it, **Then** the system either declines with a clear explanation or re-evaluates availability first — it never executes an action against context that is no longer valid.
5. **Given** the selected capability fails, **When** Lucy reports back, **Then** the user sees what failed and, where the action is safe to repeat, a way to try again.

---

### User Story 4 - Lucy Runs a Multi-Step Job End to End (Priority: P2)

Some requests are not one action but a short, ordered job whose steps depend on each other. "Show me Al Safa Park 2" is one: find the place, focus the viewer on it, then outline the site. Each step needs the one before it to have succeeded, and no user would want to be asked three separate times to continue something they already asked for.

Lucy runs the whole job, narrating as she goes. Before each step she says what she is about to do; when it finishes she reports what happened; then she says what comes next. The user watches a job progress rather than waiting at a blank screen or being interrogated between steps.

**Why this priority**: This is what makes Lucy feel like she is *working on the request* rather than answering it once and stopping. It is also the difference between a chatbot with buttons and an assistant: a related sequence should be carried through, not decomposed into a quiz. Multi-step jobs are common in this domain and the mechanism must be general — a job is a declared, reusable sequence, not a hardcoded special case for locations.

**Independent Test**: Can be fully tested by sending a single request that triggers a known multi-step job and confirming the steps run in order, each announced before it starts and reported when it finishes, with the whole job completing without further input.

**Acceptance Scenarios**:

1. **Given** a request that matches a known multi-step job, **When** Lucy plans the turn, **Then** she runs the whole job, in order, without asking the user to confirm each step.
2. **Given** a job is running, **When** each step begins, **Then** Lucy states what she is about to do *before* that step starts.
3. **Given** a step finishes, **When** Lucy reports, **Then** she states what actually happened, then names the next step she is moving to.
4. **Given** a step's result is required by the step after it, **When** the earlier step succeeds, **Then** its result is passed forward rather than re-derived.
5. **Given** a step fails or cannot proceed, **When** Lucy reports, **Then** the job stops there and she names what went wrong in that step — without also listing the steps that consequently did not run. Results already delivered stay valid.
11. **Given** the user's message clearly asks to be shown or taken to a place, **When** Lucy plans the turn, **Then** the whole flow runs automatically and no option list is offered for it.
12. **Given** the user's message asks *about* a place without asking to see it, **When** Lucy responds, **Then** she answers briefly and — if she judges an offer useful — presents a mixed list of logically related next steps: the flow's variants, any related capability, and conversational follow-ups composed for the situation, plus a decline.
13. **Given** such an offer is accepted, **When** the chosen variant runs, **Then** it runs from its first step with the same narration and dependency rules as if the user had asked navigationally.
14. **Given** a place is mentioned only in passing, **When** Lucy responds, **Then** neither the flow nor an offer for it appears, and the viewer does not move.
6. **Given** a step's work is already done (the site is already outlined), **When** the job reaches it, **Then** it is skipped with a brief note rather than repeated.
7. **Given** the user's request explicitly scopes the job ("just find it, don't outline it"), **When** Lucy plans the turn, **Then** she runs only the steps asked for.
8. **Given** a job is running, **When** the user interrupts it, **Then** it stops at the current step, what completed stays in the conversation, and the job is recorded as interrupted.
9. **Given** any step is still running, **When** the user is waiting, **Then** the conversation continuously shows progress naming that step, never a generic spinner and never an apparently finished message.
10. **Given** a step resolves, **When** Lucy reports, **Then** the in-progress indication is replaced by the actual outcome and never left showing progress for work that has stopped.

---

### User Story 5 - Lucy Delegates to Sub-Agents (Priority: P2)

A user makes a request that spans more than one area of expertise — for example, locating a site and then answering a question about it from an attached knowledge base. Lucy breaks the request into the parts that belong to different sub-agents, has each part handled by the right one, and reports a single coherent result rather than making the user ask twice.

**Why this priority**: Multi-part requests are where a single monolithic prompt visibly breaks down and where orchestration earns its cost. It depends on Stories 1-3 being in place, so it follows them, but it is the story that makes the platform genuinely multi-agent rather than single-agent-with-tools.

**Independent Test**: Can be fully tested by issuing one message that requires two distinct areas of work, and confirming both are performed in a sensible order, each narrated as it happens, and summarised together at the end.

**Acceptance Scenarios**:

1. **Given** a request spanning two areas of expertise, **When** Lucy plans the turn, **Then** each part is routed to the sub-agent responsible for it and the parts run in an order that respects their dependencies.
2. **Given** one delegation depends on another's result, **When** the first completes, **Then** its result is passed to the second rather than re-derived.
3. **Given** one delegation fails while another succeeds, **When** Lucy reports back, **Then** the user is told plainly which part succeeded and which did not, and receives the partial result rather than nothing.
4. **Given** a request that could loop indefinitely, **When** the turn reaches its delegation limit, **Then** the turn ends with the best result obtained so far and a clear statement that Lucy stopped, never an unbounded run or a silently truncated answer.
5. **Given** a sub-agent would require a capability the user is not permitted to use, **When** the turn is planned, **Then** that delegation does not occur and the limitation is explained to the user.

---

### User Story 6 - Lucy Is a Real, Inspectable Agent (Priority: P2)

The orchestrator and each sub-agent exist as actual, named, versioned agents in the platform's agent catalog, visible to the user and to administrators. Every turn leaves an inspectable record: which agent decided what, which capabilities ran with which inputs, what each returned, what it cost, and what was proposed but discarded.

**Why this priority**: The platform's own governing principles require every agent action to be authorized, bounded, logged and explainable after the fact, and an empty agent catalog is the current evidence that the conversation is not governed by any of that. This story is what makes the runtime auditable and upgradable rather than a hidden second implementation.

**Independent Test**: Can be fully tested by running a conversation turn that performs at least one action, then confirming the agent catalog lists the orchestrator and sub-agents as system-provisioned entries, and that the turn's decisions, delegations, outcomes and costs are retrievable afterwards.

**Acceptance Scenarios**:

1. **Given** a freshly provisioned environment, **When** the platform starts, **Then** the orchestrator agent and its sub-agents exist in the agent catalog as system-owned, versioned entries.
2. **Given** a user views the agent catalog, **When** system-provisioned agents are listed, **Then** they are clearly distinguished from user-created agents, are readable, and cannot be edited or deleted by a user.
3. **Given** a new platform release changes an agent's instructions or capability set, **When** the release is deployed, **Then** the system-provisioned agents are upgraded to the new version without manual intervention and without discarding history recorded against prior versions.
4. **Given** a completed turn, **When** its record is inspected, **Then** it shows each decision, each delegation with its inputs and outcome, each discarded suggestion with the reason, and the token/latency cost attributed to the initiating user.
5. **Given** a capability is invoked during a conversation, **When** it executes, **Then** it is subject to the same authorization, permission and bounding rules that already govern agent tool use — it gains no privileges by being reached from chat.

---

### User Story 7 - The Turn Works by Voice (Priority: P3)

A user working hands-free hears Lucy acknowledge the request, hears what she found, and hears the options read out. The structured mechanics of the option list never leak into what is spoken.

**Why this priority**: Voice is an established part of the product, and a turn split into several messages changes what gets spoken and when. Getting this wrong turns an improvement into a regression for voice users, but it does not block the visual experience from shipping.

**Independent Test**: Can be fully tested by completing an action-bearing turn with voice output enabled and confirming each beat is spoken as it arrives, that option labels are spoken while their descriptions and any structured payload are not, and that the voice persona is unchanged.

**Acceptance Scenarios**:

1. **Given** voice output is enabled, **When** each beat of the turn arrives, **Then** it is spoken as it becomes available rather than held until the whole turn ends.
2. **Given** an option list is presented, **When** it is spoken, **Then** the question and the option labels are read aloud and the per-option descriptions and any structured data are not.
3. **Given** any beat is spoken in any supported language, **When** the voice is produced, **Then** it uses the platform's established consistent voice persona.

---

### User Story 8 - Nothing Fails Silently, and History Tells the Truth (Priority: P3)

Whenever any part of the turn fails — the orchestrator's own reasoning, a delegation, an option dispatch — the user is told, in that same turn, what failed and what they can do about it. When the conversation is reopened later, it replays exactly what happened, including which options were offered and which one the user chose.

**Why this priority**: A multi-step turn has more places to fail than a single-shot reply, and a turn whose visible history omits the offer-and-selection sequence is unreadable after the fact. Both are correctness requirements rather than new capability, so they sit last in sequence while remaining mandatory to ship.

**Independent Test**: Can be fully tested by forcing a failure at each stage of the turn and confirming a user-visible explanation appears every time, then reloading the conversation and confirming the persisted transcript matches what was displayed live.

**Acceptance Scenarios**:

1. **Given** the orchestrator's own decision step fails, **When** the turn continues, **Then** the user receives a usable reply and a visible explanation that Lucy could not plan the turn, never an empty or hanging response.
2. **Given** a delegation fails, **When** Lucy reports back, **Then** the failure is named in the conversation and, where retrying is safe, a retry is offered.
3. **Given** an option dispatch fails, **When** the user is notified, **Then** the notification is visible in the interface rather than only in diagnostics.
4. **Given** a turn that offered options and had one selected, **When** the conversation is reopened later, **Then** the transcript shows the acknowledgement, the result, the offered options and the selection, in the order they occurred.
5. **Given** a turn was interrupted (the user navigated away or cancelled), **When** the conversation is reopened, **Then** the partial turn is shown as interrupted rather than as a completed turn or a missing one.

---

### Edge Cases

- What happens when the user selects an option whose preconditions have since changed (the active site was replaced by a later message)? Availability is re-evaluated at dispatch time; a no-longer-valid option is refused with a clear explanation instead of executing against stale context.
- What happens when two option lists are outstanding (the user scrolled back to an older offer)? Only the most recent offer is live; older offers are visibly inert.
- What happens when the user's message both selects an option and adds a new instruction ("do 1, and also tell me about the zoning")? The selection is honoured and the added instruction is planned as part of the same turn.
- What happens when the orchestrator proposes the same delegation repeatedly within a turn? Repeat detection stops it, and the turn ends with what has been achieved rather than looping.
- What happens when a capability requires an approval the user has not granted? The turn stops short of executing it and explains what permission is missing — it never executes and reports afterwards.
- What happens when the user disables suggested actions in settings? Turns still narrate their beats and still perform requested work; no option lists are shown, and previously offered lists in history render as plain text.
- What happens when the conversation has no capabilities available at all (a plain chat with nothing attached)? The turn behaves as an ordinary conversational reply with no orchestration overhead visible to the user.
- What happens when the orchestrator returns a well-formed decision naming a capability the user's subscription tier does not include? Treated identically to an unavailable capability — dropped before display, logged, never offered.
- What happens when a delegation returns a result the orchestrator cannot make sense of? The turn reports that it could not use the result rather than fabricating a narration around it.
- What happens when the user cancels mid-turn? Work in flight stops, already-delivered beats remain in history, and the turn is marked interrupted rather than failed.
- What happens when the same option is offered in two consecutive turns and selected twice? The second selection is either satisfied from the already-produced result or re-run deliberately — never silently duplicated work presented as new.
- What happens when the first step of a flow fails (the place cannot be found)? The flow stops immediately and Lucy says the place could not be found. She does not add that the viewer and boundary steps were skipped — that follows from the sequence and does not need saying.
- What happens when a middle step fails (the viewer cannot be focused)? The flow stops; the confirmed location stays valid and visible, and Lucy names the focusing failure only.
- What happens when the intent is genuinely borderline ("what's at Al Safa Park 2?")? Treated as informational — Lucy answers and offers. Erring toward offering is safe; erring toward acting moves the user's viewer uninvited.
- What happens when an informational message names a place and the user then accepts the offer? The flow runs from its first step, geocoding the place for the first time — the earlier answer did not require resolving it.
- What happens when a flow's last step fails? Every earlier result stands; only the last step is reported as failed.
- What happens when the same flow is requested twice for the same place? Steps whose work is already done are skipped with a brief note (FR-057), so the second run is fast rather than a repeat.
- What happens when a step is sub-second (viewer focus)? It is announced and reported exactly like every other step (FR-052) — uniformity is the rule, and pairing its completion with the next step's announcement (FR-053) means it costs no extra message.
- What happens when a flow is interrupted between two steps? The completed steps stay in the conversation, the flow is recorded as interrupted, and the unattempted steps are named.
- What happens on a long run of ordinary conversational turns with no actions? No offer appears on any of them, and no offer model call is made — the conversation costs and reads exactly as it does today.
- What happens when the only offerable capability is the one that just ran? It is not offered again; if nothing else is offerable, the turn ends with no list.
- What happens when the user declines three offers in a row? Each decline suppresses the next turn's offer, so a user who is not interested is not repeatedly asked.
- What happens when a request needs no capability but the orchestrator wrongly selects one? The capability's own preconditions reject the call, the turn recovers into a plain reply, and the misfire is recorded.
- What happens when accepted work runs far longer than expected (a boundary lookup that takes tens of seconds)? The in-progress indication stays visible and keeps naming the work for as long as it runs, up to the turn's time budget; when the budget is reached, the indication is replaced by a statement that Lucy stopped waiting, never left spinning indefinitely.
- What happens when the user closes or navigates away from the conversation while announced work is still running? On return, the transcript shows the announcement and the work's terminal state — completed, failed or interrupted — never a permanently pending indication.

## Requirements *(mandatory)*

### Functional Requirements

#### Turn orchestration

- **FR-001**: Every conversation turn MUST be planned and driven by a designated orchestrator agent that decides which capabilities (if any) to invoke, in what order, based on the user's message and the current conversation context.
- **FR-002**: The orchestrator MUST run as a bounded loop of decide → acknowledge → act → report → offer, and MUST NOT be able to recurse or delegate without limit; a maximum number of capability invocations per turn MUST be enforced and MUST be configurable per environment.
- **FR-003**: When the orchestrator determines a turn requires one or more actions, the system MUST deliver a short acknowledgement to the user stating what is about to happen, before that work begins.
- **FR-004**: The system MUST deliver each distinct beat of a turn (acknowledgement, per-action result, closing offer) as its own conversation message rather than appending to a message the user may already have read.
- **FR-005**: While a beat's work is in progress, the system MUST continuously display what is currently happening, naming the specific work rather than showing a generic busy indication; a message that is still doing work MUST NOT be presented as complete.
- **FR-005a**: Any **standalone** action (one not run as part of a flow, whose steps are governed by the stricter FR-052) expected to take longer than a few seconds MUST be announced as its own message stating what is starting, *before* that action begins, and the in-progress indication MUST remain visible until the action resolves — at which point it MUST be replaced by the actual outcome. The conversation MUST NOT go quiet during long-running work, and MUST NOT continue to show progress for work that has already stopped.
- **FR-006**: When the orchestrator determines no action is required, the turn MUST proceed as an ordinary conversational reply — no acknowledgement beat, **no offer** (FR-025a.1), and no added user-perceptible delay before the reply begins. A plain question answered plainly must be indistinguishable from today's behaviour.
- **FR-007**: The narration of an action's outcome MUST be produced from the real outcome of that action, MUST accurately reflect success, partial success or failure, and MUST NOT be a fixed template sentence in the normal path.
- **FR-008**: Fixed fallback wording MUST exist for every outcome type and MUST be used only when the orchestrator cannot produce a narration (for example, the model call fails), so a turn is never left without a user-visible statement of what happened.
- **FR-009**: When a turn's cumulative work exceeds its configured time budget, the system MUST end the turn with the results obtained so far plus an explicit statement that it stopped, and MUST NOT leave the user with an indefinitely pending turn.

#### Capability catalog

- **FR-010**: The system MUST maintain a registry of capabilities available to conversation, each declaring a stable key, a user-facing label, a short user-facing description, an input contract, its required permissions, and its risk level.
- **FR-011**: Each capability MUST declare whether it is available for a given turn, evaluated against a turn context comprising at least: the active confirmed location, the active site boundary, attached knowledge bases, attached documents, memory availability, open visual panels, and the user's permissions and subscription entitlements.
- **FR-012**: Adding a capability MUST require only registering a new capability; it MUST NOT require modifying the orchestrator, the turn pipeline, or the conversation user interface.
- **FR-013**: The following existing platform operations MUST be exposed as capabilities: resolving a named place to a confirmed location, resolving a site boundary for a confirmed location, adjusting viewer zoom/focus, searching attached knowledge bases, searching user memory, and opening a visual panel of an already-registered panel type.
- **FR-014**: The orchestrator MUST be given, for each turn, only the capabilities the catalog reports as available for that turn; it MUST NOT be shown or able to invoke capabilities outside that set.
- **FR-015**: Capability invocation from conversation MUST be subject to the same authorization, permission-checking, risk/approval evaluation, bounding (timeouts, retry limits) and audit logging that already govern agent tool invocation elsewhere in the platform, with no relaxation for being reached from chat.

#### Sub-agent delegation

- **FR-016**: The system MUST support routing distinct parts of one request to distinct sub-agents, each with its own scope of expertise and its own scoped capability set.
- **FR-017**: When one delegation's result is required by another, the system MUST pass the first result forward rather than re-deriving it.
- **FR-018**: When one delegation fails and others succeed, the system MUST deliver the successful results and state clearly which part failed.
- **FR-019**: Repeated identical delegations within a single turn MUST be detected and prevented from looping.
- **FR-020**: The total cost of a turn (tokens, latency, invocation count) MUST be bounded by configurable per-turn limits and recorded against the initiating user.

#### Suggested actions

- **FR-021**: At the end of a turn, when one or more capabilities are **offerable** (FR-025b) and no suppression rule applies (FR-025a), the system MUST present a bounded list of selectable next actions, each carrying a stable capability key, the arguments needed to run it, a short label and a one-line description. An offer is never mandatory: a turn with nothing worth suggesting ends without one.
- **FR-021a**: An offer MUST be able to mix **kinds** of next step, not only one. The system MUST support at least:
  1. **Flow variant** — runs a declared flow prefix ("Focus the viewer on it", "Focus and outline the site").
  2. **Capability action** — runs one capability ("Search my knowledge bases for it").
  3. **Conversational follow-up** — runs nothing; Lucy simply says or asks something. Composed for the turn, not chosen from a fixed list (FR-021b).
  4. **Decline** — performs no work (FR-022).
  The list is assembled from whatever is logically related to what just happened, across these kinds — it is not restricted to the steps or variants of one flow.
- **FR-021b**: Conversational follow-ups MUST be **composed for the situation**, not selected from a registered set. The system MUST compose them from, at least:
  1. **What Lucy has learned from comparable situations** — the memory subsystem's record of what this user and similar turns went on to want.
  2. **Clarification** — when the request was ambiguous, a follow-up MAY be a question that resolves the ambiguity (which candidate place, which of two documents, which time range).
  3. **Likely next asks derivable from the capability index** — where a capability suggests an obvious successor to what was just done.
  4. **A useful recommendation** Lucy judges worth raising.
- **FR-021c**: A conversational follow-up MUST be deliverable **by Lucy talking** — elaborating, asking, comparing, summarising or recommending, using the turn's context, retrieved material, or her own knowledge. It MUST NOT promise that the platform will *do* something. Anything that implies doing MUST be proposed as a capability or flow row instead, where it is validated against the registry (FR-024). Selecting a follow-up MUST NOT invoke any capability, under any circumstances — this is a structural guarantee, not a matter of the wording used.
- **FR-022**: The list MUST always include an explicit decline option that performs no work, as the last row.
- **FR-023**: The number of offered actions MUST be capped at a small configurable maximum (default 5, including the decline row — so at most 4 substantive choices).
- **FR-024**: Grounding MUST be enforced per offer kind:
  1. **Flow variant and capability rows** — any proposed row whose flow/variant/capability key is not present in the turn's available set, or whose arguments do not satisfy that capability's input contract, MUST be discarded before reaching the user, with the proposed key and reason logged. This check is absolute.
  2. **Conversational follow-up rows** — MUST be discarded when the follow-up implies platform work rather than Lucy talking (FR-021c). Because a follow-up is composed prose with no key to check, this check is best-effort and MUST be backed by the structural guarantee that selecting a follow-up can never invoke a capability. Every discard MUST be logged with the proposed text and reason.
- **FR-024a**: The offer step MUST be given the turn's available capability index, so that any next step which requires *doing* is proposed as a capability or flow row — where FR-024.1's absolute check applies — rather than as a follow-up.
- **FR-025**: When no capability is offerable, the system MUST present no list at all rather than an empty, padded or placeholder one. Silence is a valid and expected way for a turn to end.
- **FR-025a**: The system MUST skip the offer step **entirely — no model call, no event** — when any of the following holds:
  1. The turn took the fast path and invoked no capability (FR-006). Answering a question is not a reason to ask what to do next.
  2. No capability is offerable for the resulting turn state (FR-025b).
  3. The user declined the immediately preceding offer. A decline is an answer, and re-asking is nagging.
  4. Every offerable action was already offered on the preceding turn and neither selected nor acted upon.
  5. The user has suggested actions disabled (FR-032).
- **FR-025b**: **Offerable** is a stricter test than available, and the two MUST be evaluated separately. A capability is available when it *could run*; it is offerable when running it would *plausibly be wanted next*, given what just happened. A capability that is always invocable is not thereby always worth suggesting. Where the two coincide, offerable defaults to available.
- **FR-025c**: An offer is **never obligatory**. Whether to offer at all is Lucy's judgement about whether the user would find one useful here — not a step the turn owes. The offer step MUST be permitted to conclude that nothing is worth suggesting even when offerable capabilities exist, and that conclusion MUST be honoured: the system MUST NOT pad an offer to reach a minimum length, MUST NOT substitute a generic suggestion for an absent one, and MUST NOT show a list merely because one could be constructed. A turn ending with no offer is the expected common case.
- **FR-026**: The offered actions MUST be persisted with the assistant message that offered them, so a reopened conversation renders the same offer that was shown live.
- **FR-027**: Selecting an offered action MUST dispatch that capability directly with its bound arguments, without re-running intent interpretation on the user's selection.
- **FR-028**: Availability MUST be re-evaluated at dispatch time; a selection whose preconditions no longer hold MUST be refused with a user-visible explanation rather than executed against stale context.
- **FR-029**: Only the most recently offered action list MUST be live; earlier lists in the transcript MUST render as inert history.
- **FR-030**: A pending offer MUST be dismissed without error when the user sends an ordinary message instead of selecting, and the user's typed message MUST be handled as a normal turn.
- **FR-031**: The user MUST be able to select an action without typing, and MUST also be able to state their own instruction instead at any time — the composer MUST remain usable while an offer is displayed.
- **FR-032**: Users MUST be able to turn suggested actions off in settings; with them off, turns still narrate and still perform requested work, and historical offers render as plain text.

#### System-provisioned agents

- **FR-033**: The platform MUST provision the orchestrator agent and its sub-agents into the existing agent catalog as system-owned, versioned entries, present in every environment without manual setup.
- **FR-034**: System-provisioned agents MUST be visibly distinguished from user-created agents, MUST be readable by users, and MUST NOT be editable or deletable by users.
- **FR-035**: A platform release that changes a system-provisioned agent's instructions or capability set MUST upgrade those entries automatically on deployment, preserving history recorded against earlier versions.
- **FR-036**: All prompts and instructions used by the orchestrator and sub-agents MUST be versioned artifacts, reviewable and testable independently of any model call.
- **FR-037**: Model selection for the orchestrator's decision step MUST be expressed as a capability requirement resolved through the platform's existing provider/model policy, never a hardcoded vendor or model.

#### Observability and failure behaviour

- **FR-038**: Every turn MUST produce an inspectable record containing: the orchestrator's decision, each delegation with its inputs and outcome, each discarded suggestion with its reason, the outcome narration produced, and the token/latency cost attributed to the initiating user.
- **FR-039**: Failure of the orchestrator's decision step MUST NOT fail the turn; the turn MUST degrade to a plain conversational reply and the user MUST be told that Lucy could not plan the turn.
- **FR-040**: Failure or timeout of any single capability MUST be confined to that capability — results already delivered in the turn MUST remain valid and visible.
- **FR-041**: Every failure at any stage — decision, delegation, dispatch, persistence — MUST reach the user as visible feedback in the interface in the same turn, and MUST offer a retry where the action is safe to repeat.
- **FR-042**: A cancelled or interrupted turn MUST retain its already-delivered beats in history and MUST be recorded as interrupted, distinguishable from both completed and failed turns.

#### Voice

- **FR-043**: Each beat MUST be speakable as it arrives rather than held until the turn completes.
- **FR-044**: When an offer is spoken, the question and the option labels MUST be spoken and the option descriptions and any structured payload MUST NOT be.
- **FR-045**: Spoken output MUST continue to use the platform's established consistent voice persona across every supported language.

#### Migration of existing behaviour

- **FR-046**: Site-boundary resolution MUST NOT be an independently offered option. It is the final step of the **Locate a place** flow (FR-050), reached only after location resolution and viewer focus have succeeded, and it MUST NOT run when the flow stopped before it.
- **FR-046a**: The **Locate a place** flow MUST narrate as specified in FR-052–FR-054: "I found Al Safa Park 2." → "Now focusing the viewer on it." → "Done. Next I'll outline the site boundary." → progress → outcome. The user MUST NOT be left reading an apparently finished conversation while a step is still running.
- **FR-047**: Keyword-triggered viewer zoom MUST be replaced by the corresponding capability, with no second parallel detection path remaining.
- **FR-048**: The payload shapes already used to deliver a confirmed location and a confirmed site boundary to the viewer MUST NOT change, and the viewer's existing reaction to them MUST continue to work unmodified.
- **FR-049**: A conversation that predates this feature MUST still open, render and continue correctly.

#### Capability flows

- **FR-050**: The system MUST support **capability flows** — named, ordered sequences of capabilities that run as a single job because their steps are logically dependent. A flow MUST be a declared, registered artifact, not logic embedded in the orchestrator, so that adding a flow is a registration and never a change to the runtime (the same extensibility rule as FR-012).
- **FR-051**: The system MUST provide the **Locate a place** flow: resolve the named place (the viewer focuses on it automatically as a side effect of the confirmed location reaching the client — no separate flow step forces an additional zoom on top of that, since doing so double-zoomed a site already correctly framed) → resolve and draw its site boundary. Each step depends on its predecessor having succeeded.
- **FR-051a**: Whether a flow runs automatically or is offered MUST depend on how clearly the user's message asks for it. The system MUST distinguish three intents toward a mentioned place:
  1. **Navigational** — the user asks to be shown, taken to, or moved to the place ("show me X", "take me to X", "go to X", "centre on X"). The flow MUST run automatically and in full; the user has already asked, and offering would be requesting permission twice.
  2. **Informational** — the user asks *about* the place without asking to see it ("do you know X?", "what is X?", "tell me about X"). The system MUST answer the question briefly and then **offer** the flow rather than running it.
  3. **Passing mention** — the place is named incidentally, in comparison, recollection or analysis. The system MUST neither run nor offer the flow, and the viewer MUST NOT move.
- **FR-051b**: A flow MUST be able to declare named **variants** — contiguous prefixes of its steps offered as distinct choices. The **Locate a place** flow MUST declare at least: *focus only* (resolve the place; the viewer focuses on it automatically) and *focus and outline* (both steps — also outline the site boundary). When a flow is offered under FR-051a.2, its variants are what the user chooses between.
- **FR-051c**: Accepting an offered flow variant MUST run that variant from its first step, exactly as if the user had asked navigationally, with the same narration (FR-052–FR-054) and the same dependency and failure rules (FR-055, FR-056).
- **FR-052**: **Every** step of a flow MUST be announced before it runs — uniformly, regardless of how long that step takes. No step is silently folded into another.
- **FR-053**: A flow's narration MUST combine each step's result with the next step's announcement in a single message, so the sequence reads as one continuous account rather than alternating pairs. The first message announces step 1 alone; each subsequent message reports the step that just finished and names the step now starting; the final message reports the last step alone. A flow of N steps therefore produces N+1 messages, each of which names at least one step. Example for the **Locate a place** flow:
  1. "Looking for Al Safa Park 2."
  2. "Location found. Now focusing the viewer on it."
  3. "Site focused. Now highlighting the boundary."
  4. "Boundary highlighted — about 4.2 hectares."
- **FR-054**: While a step is running, the system MUST continuously show progress naming that step (FR-005/FR-005a apply unchanged to every step of a flow).
- **FR-055**: A flow's steps MUST be dependency-ordered: a step MUST NOT run unless the step it depends on succeeded, and each step MUST receive its predecessor's result rather than re-deriving it.
- **FR-056**: When a step fails, cannot proceed, or exceeds its budget, the flow MUST stop at that step and the system MUST name **the cause** — what went wrong in that step. It MUST NOT additionally enumerate the steps that were therefore not attempted: a dependent sequence stopping at a failure is self-evident, and spelling it out is noise. Results already delivered in earlier steps MUST remain valid and visible. The unattempted steps and their reason MUST still be recorded (FR-061), for diagnosis rather than for the user to read.
- **FR-057**: A step whose work is already done for the current context MUST be skipped with a brief note rather than repeated, and the flow MUST continue to the next step.
- **FR-058**: The user MUST be able to scope a flow in their request ("just find it, don't outline it"), in which case only the requested steps run; and MUST be able to interrupt a running flow, in which case it stops at the current step, completed steps remain in the conversation, and the flow is recorded as interrupted.
- **FR-059**: A flow MUST be bounded by the same per-turn budget as any other turn (FR-002/FR-020); its steps MUST NOT collectively exceed it, and reaching the budget MUST follow FR-056's stop-and-explain path.
- **FR-060**: A flow's individual steps MUST NOT be offered as independent next actions. What is offerable is the **flow, by variant** (FR-051b) — "focus the viewer on it", "focus and outline the site" — never "run step 2". Flow variants MUST be offerable only when the turn's intent toward the place was informational (FR-051a.2); after a navigational request the flow has already run, so there is nothing to offer. Variants sit alongside the other offer kinds of FR-021a in the same list, never in a list of their own.
- **FR-061**: A flow MUST produce the same inspectable record as any other turn (FR-038), with one recorded step per flow step, including skipped and unattempted ones and the reason for each.

### Key Entities *(include if feature involves data)*

- **Conversation Turn**: One user message and everything Lucy did in response — the orchestrator's decision, the ordered beats delivered, the delegations performed, the offer made, the cost incurred, and the terminal state (completed, degraded, failed, interrupted).
- **Orchestrator Agent**: The system-provisioned agent responsible for planning a turn, choosing capabilities, delegating, and writing the user-facing narration. Versioned; not user-editable.
- **Sub-Agent**: An agent Lucy calls rather than one the user calls. Structurally the same kind of agent a user can create, with three differences: it is owned by the platform rather than by a user, it is invoked by the orchestrator rather than started by a person, and it holds only the capabilities its own area of expertise needs. Four are provisioned initially — site/location, knowledge and documents, memory, and viewer control. The narrow capability scope is a safety property, not just tidiness: the knowledge sub-agent holds no viewer capabilities at all, so it cannot move the map even if its reasoning goes wrong.
- **Capability**: A registered unit of work Lucy can perform, declaring a stable key, user-facing label and description, input contract, required permissions, risk level, and a per-turn availability rule.
- **Turn Context**: The snapshot of conversation state against which capability availability is evaluated — active location, active site boundary, attached knowledge bases and documents, memory availability, open panels, user permissions and entitlements.
- **Suggested Action**: One offered next step — capability key, bound arguments, label, description — carried with the assistant message that offered it, and either selected, declined or expired.
- **Delegation Record**: One sub-agent or capability invocation within a turn: who invoked it, with what inputs, what it returned, how long it took, what it cost, and whether it succeeded.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a request that requires an action, the user sees Lucy's first acknowledgement no later than they see the first words of a reply today, and that acknowledgement correctly names the work about to be performed in at least 95% of a representative benchmark of action requests.
- **SC-002**: Across a representative benchmark of turns, **100%** of offered flow-variant and capability rows correspond to flows/capabilities that are registered and available in that turn's context — zero action rows referring to something the platform does not have. This is an absolute, registry-enforced guarantee.
- **SC-002a**: Selecting a conversational follow-up row invokes **zero** capabilities, in 100% of cases — a structural guarantee, not a wording one, so a badly-composed follow-up can never trigger platform work it should not.
- **SC-002b**: Across the same benchmark, at most 2% of conversational follow-up rows promise platform work rather than something Lucy says or asks, and every such row that does reach a user is logged. Unlike SC-002 this is a measured ceiling rather than a guarantee, because a composed follow-up has no key to check — see the Assumptions section for why that trade was accepted.
- **SC-003**: Selecting an offered option performs the intended action without any further clarification from the user in at least 95% of selections, and never re-resolves information Lucy already established in the prior turn.
- **SC-004**: A "show me this place" request runs the full three-step flow to completion, and at every point the user can state which step is running and which remain, verified by observation across a representative benchmark. Wall-clock time is **not** expected to improve over today — the same work is done; what changes is that the wait is narrated rather than silent (see SC-004a).
- **SC-004a**: From the moment a request is accepted until the turn ends, the conversation never goes longer than 5 seconds without a visible indication naming what is currently happening — measured across turns that include the slowest available action, and verified to hold for the full duration of that action.
- **SC-005**: For multi-part requests spanning two areas of expertise, the user receives both parts from a single message in at least 90% of a representative benchmark, without having to ask a second time.
- **SC-006**: Every completed turn produces a retrievable record naming each decision, delegation and discarded suggestion, verified for 100% of turns in an audit sample.
- **SC-007**: Every induced failure at every stage of the turn produces user-visible feedback in the same turn — zero failures observable only in diagnostics.
- **SC-008**: The added cost of orchestration keeps median turn cost within a documented budget, and turns that require no action cost no more than they do today.
- **SC-009**: A reopened conversation reproduces the beats, offers and selections exactly as they were displayed live, for 100% of turns in a replay sample.
- **SC-010**: With voice output enabled, each beat is spoken as it arrives, and no structured option data is ever spoken, across every supported language.
- **SC-011**: The orchestrator and its sub-agents are present in the agent catalog in every environment immediately after deployment, with no manual provisioning step.

## Assumptions

- The platform's existing agent runtime concepts — scoped tool sets, permission checks, risk/approval policy, budget guards, duplicate-call detection and audit logging — are reused by this feature rather than reimplemented. The conversational runtime differs from the existing background agent runtime in that it streams within a request and does not pause for out-of-band approval; where a capability would require approval, it is surfaced as an action the user confirms in the conversation itself.
- A turn that requires no capability takes a fast path that skips orchestration overhead, so ordinary conversation is not made slower or more expensive by this feature.
- The suggested-action card is non-blocking: it never prevents the user from typing, and it is dismissed by any ordinary message.
- System-provisioned agents are visible to users in read-only form so that "which agent did this" is answerable, but authoring or editing an orchestrator agent is out of scope for this feature.
- The example option "display available photos and 360° images" from the original request is illustrative of dynamic generation only. No photo or 360° imagery capability exists in the platform, and this feature does not build one — it guarantees that such an option is never offered until the capability exists.
- Suggested actions default to enabled for all users, with a per-user setting to disable them.
- Existing viewer payload contracts for confirmed locations and site boundaries are treated as fixed; this feature changes when and why they are produced, never their shape.
- Turn records are retained for the same period as the conversation history they belong to.
- The number of sub-agents provisioned initially is small (site/location, knowledge and documents, memory, viewer control); additional sub-agents are later additions, not part of this feature.
- Adding a new AI vendor, redesigning the retrieval or memory engines, allowing users to author orchestrator agents, and building any photo/360° imagery capability are all explicitly out of scope.
- **Terminology**: this spec uses **sub-agent** throughout for an agent the orchestrator calls. The **Input** paragraph above says "specialists" because it quotes the originating request verbatim; the two words mean the same thing here, and "sub-agent" is the term to use in the plan, tasks and code.

### Dependencies and Specification Reconciliation

This feature changes behaviour defined in existing specifications. Each MUST be reconciled as part of implementation:

- **002 (Chat History)** — messages must carry offered and selected actions, and turns must be replayable including their beat structure.
- **010 (Lucy Brand & Voice)** — Lucy's voice guidance gains turn-taking; the standing "confirm and stop" reply framing is replaced by "confirm, then offer grounded next steps".
- **016 (RAG) / 018 (Memory)** — retrieval and memory become capabilities the orchestrator may invoke, rather than unconditional pipeline stages; their own engines are unchanged.
- **020 (Agent Framework) / 021 (MCP) / 022 (Workflows)** — the tool abstraction gains user-facing labelling and per-turn availability; the runtime's guards and audit are reused by the conversational runtime; MCP tools become conversation capabilities subject to the same rules.
- **025 (Chat Configuration)** — adds the user setting to disable suggested actions.
- **026 / 039 / 040 / 041 (Chat UI shell)** — the conversation surface hosts the suggested-action card and multi-beat turns.
- **028 (Floating Panels)** — the existing panel-type registry is exposed as a capability rather than a separate AI-to-UI path.
- **035 / 037 (Location)** — acknowledge-first replaces classify-concurrently-with-the-reply; the deterministic confirmation templates become fallback wording only.
- **038 (Viewer Zoom)** — the keyword detector is retired in favour of a capability.
- **042 (Site Boundary)** — automatic resolution on every new site becomes an offered action, and when it does run it is announced before it starts and shown as in progress until it resolves (FR-046a).
- **044 (Location/Boundary Regression)** — the ordering and isolation guarantees it established must be restated and preserved for the orchestrated turn.
