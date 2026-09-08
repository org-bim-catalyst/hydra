# Specification Quality Checklist: Conversational Agent Runtime

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-08
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Items marked incomplete require spec updates before `/speckit-clarify` or `/speckit-plan`

### Validation record (iteration 1, 2026-09-08)

- **Implementation-detail sweep**: existing internal type names (`IAgentTool`, `SendChatMessageCommandHandler`, `LocationConfirmationTemplates`, `ChatStreamChunk`), transport mechanics (SSE event names, streaming chunk fields) and vendor/model names were deliberately excluded from the requirement text. They appear only in the **Input** paragraph, which quotes the originating request verbatim, and as spec-number references in *Dependencies and Specification Reconciliation* — both are provenance, not design.
- **Testability sweep**: every FR states an observable MUST/MUST NOT. Bounded quantities that vary by environment (delegation cap, per-turn time budget, offered-action cap) are stated as "configurable" with a default where one is sensible (FR-023), rather than left vague.
- **Scope-boundary sweep**: out-of-scope items (new AI vendor, retrieval/memory redesign, user-authored orchestrator agents, photo/360° imagery capability) are recorded in Assumptions so the "360° images" example from the originating request cannot be mistaken for a deliverable.
- **Deliberate decisions taken instead of raising clarification markers** (recorded here for the planning phase to revisit if the trade-off proves wrong):
  - *Sub-agent depth* — FR-016 routes parts of a request to genuine sub-agents with their own scoped capability sets, rather than a single orchestrator calling capabilities directly. This follows the originating request's explicit intent ("a real multi-agent orchestrated agentic platform") and is the more expensive of the two readings in tokens and latency; FR-020's per-turn cost bound and SC-008's cost budget exist to contain it.
  - *Fast path* — turns needing no capability bypass orchestration entirely (FR-006), so ordinary conversation absorbs neither the latency nor the cost of this feature.
  - *Non-blocking offer* — the suggested-action card never gates the composer (FR-031), diverging from a modal question card, because a chat surface must always accept free text.

### Revision 1 (2026-09-08) — progress narration for opted-in work

Author confirmed site-boundary resolution stays opt-in (FR-046) but required the conversation to visibly keep working while it runs, so the wait reads as progress rather than as a dropped request. Re-validated after these changes; all checklist items still pass.

- User Story 4 retitled and extended with three acceptance scenarios (announce-before-start, continuous named progress, indication replaced by outcome).
- **FR-005** strengthened: the in-progress indication must name the specific work, not show a generic busy state.
- **FR-005a** added: any action expected to exceed a few seconds is announced as its own message before it begins, stays visibly in progress until it resolves, and is then replaced by the actual outcome.
- **FR-046a** added: the boundary flow specifically must read "I found the site." → "I'll highlight its boundary now." → progress → outcome.
- **SC-004a** added: the conversation never goes longer than 5 seconds without a visible indication naming what is happening, held for the full duration of the slowest available action.
- Two edge cases added: work exceeding its expected duration, and the user leaving mid-work and returning.

### Revision 2 (2026-09-08) — terminology: "specialist" → "sub-agent"

Author asked whether "specialist" meant sub-agent. It did, so the spec now says so directly. Terminology change only — no requirement changed meaning, and all checklist items still pass.

- Every occurrence of "specialist"/"specialist agent" in the requirement text replaced with "sub-agent"; User Story 5 is now *Lucy Delegates to Sub-Agents*, and the FR group is *Sub-agent delegation*.
- The **Sub-Agent** key entity was expanded to define the term plainly (an agent Lucy calls, not one the user calls; same kind of agent a user can create, but platform-owned, orchestrator-invoked, and holding only its own area's capabilities) and to record that the narrow capability scope is a safety property — the knowledge sub-agent holds no viewer capabilities, so it cannot move the map.
- A **Terminology** note added to Assumptions: the Input paragraph still reads "specialists" because it quotes the originating request verbatim; "sub-agent" is the term for the plan, tasks and code.

### Revision 3 (2026-09-08) — the offer is optional, and the mechanism now enforces it

Author asked whether the offer is mandatory or conditional. The spec already said conditional (FR-025, US2 AC4) but the mechanism could not deliver it: the offer was gated on **availability**, and `resolve_location` is specced "available: always", so `AvailableFor(context)` could never return empty and an offer would have appeared on essentially every turn — including plain conversational ones. FR-025's "available **or relevant**" left relevance undefined and unenforced. A genuine spec-versus-mechanism contradiction, found by inspection rather than by testing.

- **FR-025b** added: **offerable** is a separate, stricter predicate than **available** — could this run, versus would this plausibly be wanted next.
- **FR-025a** added: five suppression rules that skip the offer step entirely, with no model call and no event — fast-path turn, nothing offerable, user just declined, same options ignored last turn, feature disabled.
- **FR-025c** added: the offer step may conclude nothing is worth suggesting even when offerable capabilities exist; that answer is honoured, never padded to a minimum length or filled with a generic substitute.
- **FR-021** and **FR-025** rewritten against the new predicate; **FR-006** now states explicitly that a fast-path turn produces no offer.
- **US2** gained three acceptance scenarios (fast-path silence, no re-ask after a decline, no padding) and three edge cases.
- `IConversationCapability` gained `IsOfferable(context, justCompleted)`, defaulting to `IsAvailable`. The registered-capability table now carries both columns — and records that **three of the seven capabilities are never offered at all**.
- Recorded as [research.md D16](../research.md), including the cost consequence: plain conversational turns now make one model call, not two, which removes the cost regression D11 had accepted.

### Revision 4 (2026-09-08) — capability flows; boundary is a step, not an option

Author directed that finding a place should chain automatically into focusing the viewer and then outlining the boundary, as one dependent end-to-end job with narration between steps — explicitly generalisable to other cases later. This **reverses** the opt-in boundary decision taken in Revision 1 and the offerability rule from Revision 3, deliberately and on the record.

- **User Story 4** rewritten as *Lucy Runs a Multi-Step Job End to End* (10 acceptance scenarios): steps in order, announced before and reported after, dependency-gated, stoppable, scopable, skip-when-satisfied.
- **FR-050 – FR-061** added: capability flows as declared, registered artifacts; the `locate_a_place` flow; the announce/report/name-next cadence; dependency ordering; stop-and-explain on failure; skip-when-satisfied; request scoping and interruption; per-turn budget; flow steps are never independently offerable; full record including skipped and unattempted steps.
- **FR-046 / FR-046a** rewritten: the boundary is step 3 of the flow, not an offered action.
- **FR-006** unchanged, but six new edge cases cover failure at each step position, repeat requests, sub-second steps and mid-flow interruption.
- **SC-004 withdrawn and replaced.** The old criterion claimed a ≥30 s speed win from *not* doing the boundary work. That work is now always done, so the saving does not exist. The replacement measures legibility: at every point the user can say which step is running and which remain. SC-004a's five-second no-silence rule now carries the weight the speed target used to.
- New contract [capability-flow.md](../contracts/capability-flow.md); [conversation-capability.md](../contracts/conversation-capability.md) updated (**five of seven** capabilities are now never offered — three are flow steps); [turn-stream.md](../contracts/turn-stream.md) beat sequence rewritten for a single-turn flow plus a stopped-flow example.
- Recorded as [research.md D17](../research.md), including why the existing specs/022 DAG workflow engine is *not* reused: it is Hangfire-backed, non-streaming and suspends for approval, and a flow is a line rather than a graph.

**Open tension to watch during implementation**: the turn is now longer than the Revision 1 design intended. If narrated waiting proves worse in practice than a shorter turn with an offer, FR-058's request scoping is the escape hatch already in place, and the decision to trade speed for continuity is recorded in D17 rather than buried.

### Revision 5 (2026-09-08) — intent gates the flow; failures name the cause only

Author refined Revision 4 on two points and asked for clarification on a third.

**1. The flow runs automatically only for a clear navigational request.** Revision 4 made it unconditional, which was right for "show me X" and wrong for "do you know X?" — running a three-step job including a viewer move and a thirty-second boundary lookup because someone asked a *question* is the same overreach the feature exists to remove.

- **FR-051a** added: three intents — navigational (run the flow), informational (answer briefly, then offer), passing mention (do nothing). Borderline resolves to informational, because offering when Lucy should have acted costs a click while acting when she should have offered moves the user's viewer uninvited.
- **FR-051b / FR-051c** added: a flow declares named **variants** (contiguous step prefixes) — `locate_a_place` declares *focus only* and *focus and outline*. Variants are what is offered; accepting one runs it from step 1 with identical narration.
- **FR-060** rewritten: individual steps are never offerable; **variants** are, and only after informational intent.
- Decision document gains `intent: "suggest"` plus `flowKey`/`flowVariantKey` ([turn-stream.md](../contracts/turn-stream.md)).
- US4 gained four acceptance scenarios (11–14) covering all three intents and accepted variants; two new edge cases.
- Recorded as [research.md D18](../research.md).

**2. A stopped flow names the cause, not the consequence.** Revision 4 had Lucy say *"…so I haven't focused the viewer or outlined anything"*, which restates the definition of a dependent sequence.

- **FR-056** rewritten: name what went wrong in the failing step; do not enumerate the steps that consequently did not run. Those still go to the record (FR-061) for diagnosis.
- Recorded as [research.md D19](../research.md).

**3. Clarified for the author**: why `adjust_viewer_focus` folded into the neighbouring report rather than getting its own announcement. Author then chose uniform announcements — see Revision 6.

### Revision 6 (2026-09-08) — mixed offers, and uniform step announcements

Two author decisions after seeing worked examples.

**1. An offer is a mixed list, not only flow variants.** The example offer the author wrote included "Give you more information about it" and "Something else", neither of which is a flow prefix.

- **FR-021a** added: five offer kinds — flow variant, capability action, **conversational follow-up** (runs no capability; re-prompts Lucy with a scoped instruction), **"Something else"**, decline. The list is assembled from whatever is logically related to what just happened, across kinds.
- **FR-021b** added: a follow-up may only promise elaboration Lucy can deliver in words from what she already has, and is drawn from a **registered** set (`IConversationFollowUp`) so grounding stays one mechanism. Free-form follow-up text was rejected precisely because it would be the one row a model could invent at will — the "360° images" failure through the back door, with nothing for the grounder to check.
- **FR-022a** added: a **"Something else"** row, always present immediately before the decline. This **reverses** the earlier "no Other row" decision, which was right about capability (the composer is always live) and wrong about discoverability. It submits nothing — it closes the card and focuses the composer — so FR-031 is untouched.
- **FR-023** cap raised from 4 to 6 rows, of which at most 4 are substantive.
- Recorded as [research.md D20](../research.md). [suggested-actions-api.md](../contracts/suggested-actions-api.md) gains a new §0 on offer composition, and `selectedAction` gains a `kind` discriminator.

**2. Announcements are uniform; completion pairs with the next announcement.** Revision 4's `AnnounceSeparately` flag is removed.

- **FR-052** rewritten: every step announced before it runs, regardless of duration.
- **FR-053** rewritten around the author's own phrasing: each message after the first reports the step that just finished *and* names the one now starting. An N-step flow produces N+1 messages ("Looking for Al Safa Park 2." → "Location found. Now focusing the viewer on it." → "Site focused. Now highlighting the boundary." → "Boundary highlighted — about 4.2 hectares.").
- `FlowStep.AnnounceSeparately` removed; `CompletionTemplate` added.
- Recorded as [research.md D21](../research.md). The pairing is what makes uniformity affordable: separate done/starting messages would produce 2N, and a 200 ms step would get two of its own about work already finished. It also removes a per-step judgement call from every future flow author.

### Revision 7 (2026-09-08) — follow-ups are composed, not registered; no free-text row

Author corrected two points from Revision 6.

**1. Follow-ups come from Lucy's judgement, not a registry.** They are composed per situation from: what she has learned from comparable turns (memory subsystem), the need to clarify an ambiguous request, likely successors implied by the capability index, and recommendations she judges worth raising.

- **FR-021b** rewritten: composed, with those four sources named.
- **FR-021c** added: a follow-up must be deliverable by Lucy *talking*; anything implying platform work must be an action row instead. **Selecting a follow-up can never invoke a capability — a structural guarantee, not a wording one.**
- **FR-024** split by kind: absolute registry check for action rows; structural guarantee plus best-effort phrasing check for follow-ups. **FR-024a** added: the offer step is given the capability index so anything requiring *doing* is proposed as an action row where the absolute check applies.
- **SC-002 split into three.** This is the honest part: the old single "100% of offered options are grounded" could not survive open-ended follow-ups. Now SC-002 keeps 100% for action rows (registry-enforced), SC-002a guarantees zero capability invocations from follow-ups (structural), and SC-002b states a measured ≤2% ceiling for follow-up phrasing rather than pretending a guarantee exists. Keeping one unenforceable criterion would have been worse than admitting the split.
- `IConversationFollowUp` removed from the contracts; `followUp` rows now carry composed `text`, no key.
- Bonus noted in [research.md D20](../research.md): clarification-as-follow-up gives a home to the multi-candidate place disambiguation described in spec 035 US2 and left out of scope by spec 037.

**2. No "Something else" row.** The author's example listed it as an illustration, not a requirement; the composer is live throughout, so the row was redundant.

- **FR-022a removed.** This restores the original Revision-0 position, which Revision 6 had reversed on discoverability grounds.
- **FR-023** cap back to 5 rows (4 substantive + decline).
- Card visual contract updated: substantive rows, then decline; no free-text row.

### Revision 8 (2026-09-08) — `/speckit-analyze` remediation

Cross-artifact analysis found 15 issues: 1 critical, 3 high, 7 medium, 4 low. All applied.

- **Critical (constitution §10/§16.5)** — no performance-test task despite four stated performance goals. Added **T130** (capture the pre-feature baseline *before* Phase 2, since every perf and cost criterion is relative to it) and **T131** (perf tests behind the repo's existing `RUN_SCALE_PERFORMANCE_TESTS` gate).
- **High** — no E2E task (**T132**, §10 names "first chat" as a critical journey); plan.md still cited the withdrawn ≥30 s SC-004; quickstart.md Scenarios 1/3/4 still described the opt-in boundary design that Revision 4 reversed. All three rewritten.
- **Medium** — data-model.md never received the offer `Kind`/`Text` discriminator or the 5-row cap; plan.md's source tree had no `Flows/`; added **T133** (metrics, §14), **T135** (API + database docs, §13), **T134** (benchmark harness — SC-002b/SC-003/SC-005 were unverifiable without one), **T136** (§16 gate checklist); entitlement filtering moved explicitly onto the catalog in T021/T032 (FR-011 rule 3 had a field but no enforcement task).
- **Low** — FR-005a scoped to standalone actions so FR-052 unambiguously governs flow steps; FR-030 and FR-045 assertions folded into T080 and T114.

Task count 129 → 136. The withdrawn ≥30 s criterion survives only in research.md D17's reversal table, where it is deliberate history.

**3. Offers are never obligatory** — re-emphasised by the author and already covered by FR-025a/FR-025c. **FR-025c** strengthened to say so in terms of Lucy's judgement: an offer is shown because she thinks it useful here, not because one could be constructed. A turn ending with no offer is the expected common case.
