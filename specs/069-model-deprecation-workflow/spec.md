# Feature Specification: Model Deprecation Workflow

**Feature Branch**: `069-model-deprecation-workflow`

**Created**: 2026-09-23

**Status**: Draft

**Input**: User description: "Model deprecation workflow (AI provider catalog): when a "sync from provider" check finds that a currently-Available model in the catalog is no longer listed by the vendor (specs/008 FR-006's removedFromVendor diff), give the administrator a reviewed way to formally deprecate it that goes beyond a simple status flip: (1) the administrator reviews and confirms the vendor-removal in the sync diff (existing behavior per specs/009) and the model transitions to Deprecated; (2) on that transition, notify the administrator(s) that a model was deprecated and why (vendor no longer lists it); (3) identify any end users who have that model set as an active preference (chat default, saved prompt, agent config, etc.) and notify them that their configured model is no longer available; (4) if the deprecated model was any provider's or user's default model, automatically reassign the default to the closest same-tier replacement model (matching capabilities/performance class) from the same provider's Available catalog, so no in-flight process (chat, agent, workflow) breaks — if no suitable replacement exists, flag it for administrator attention instead of leaving a broken default; (5) a Deprecated model must remain non-re-enable-able from the simple admin toggle (specs/008 FR-002, as amended) — re-enabling it, if ever needed, is a separate deliberate action, not in scope here unless decided otherwise during spec. Depends on and extends specs/008-ai-model-catalog-management and specs/009-selective-model-sync-review, and will likely touch the platform's Notification Engine and default-model resolution logic."

## Clarifications

### Session 2026-10-06

- Q: How is the "same tier" replacement chosen, given the catalog records no tier? → A: It is
  derived from existing model data: same provider, Available, and supporting every
  capability the retired model had, ranked by closest price, then a context size at least
  as large, then newest. The replacement is applied immediately, and administrators are
  notified to validate it.
- Q: What happens to end-user items that name the retired model? → A: Each item is
  permanently switched to the replacement to keep service running, and the owner is sent a
  notice by email (and in-app).
- Q: How are notices delivered? → A: Through the notifications hub (spec 067). Its in-app
  delivery is already built, so this feature uses it directly. Email delivery is part of
  spec 067's email channel (User Story 3), which is not built yet. This feature publishes
  its notices with email enabled and does not build its own email path, so the emails start
  going out when that channel ships.
- Q: Can administrators turn off the deprecation summary email? → A: No. Administrators
  cannot opt out, but Super Users can.

## Context

Three facts about today's product shape this feature:

- **Confirming a vendor removal does not deprecate anything today.** Per specs/008 FR-008
  and specs/009 FR-007, a confirmed "no longer listed by vendor" row is marked
  **Unavailable**, the same state a manual toggle produces. Nothing in the product ever
  moves a model to Deprecated, so the "Deprecated" status exists with no workflow behind it.
- **A retired default fails silently.** When a provider's default model, or a model
  assigned to a platform AI capability, stops being Available, the platform skips it
  without any visible signal and serves requests from whichever enabled provider sorts
  first. This silent cross-provider routing caused a multi-day production investigation in
  August 2026.
- **User-configured items handle a retired model inconsistently.** An existing
  conversation whose model is no longer Available refuses to send. Agents, saved prompts,
  and workflow steps keep calling the retired model without checking its status, and they
  only fail once the vendor itself rejects the call.

This feature makes deprecation a single, reviewed event with defined consequences for
every place the model is configured.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Administrator formally deprecates a vendor-removed model after seeing its impact (Priority: P1) 🎯 MVP

An administrator runs a catalog sync and sees that the vendor no longer lists a model that
is still Available. Before confirming, the review shows what deprecating that model will
affect: whether it is a provider's default or is assigned to any platform AI capability,
which replacement the system proposes for it, and how many end-user items (conversations,
agents, saved prompts, workflow steps) and distinct users reference it. The administrator
selects the row and confirms. The model becomes **Deprecated**, not Unavailable, with the
reason ("no longer listed by the vendor"), the time, and the confirming administrator
recorded.

**Why this priority**: Every other story depends on this transition. It also closes the
gap that no path in the product reaches Deprecated today. The impact preview turns a
blind status flip into an informed decision.

**Independent Test**: Trigger a sync against a vendor state that omits an Available model
which is also a provider's default and is referenced by at least one user's conversation.
Confirm the review shows the default, the proposed replacement, and the reference counts.
Confirm the row. Verify that the model is Deprecated, the reason, time, and administrator
are recorded, and the model is no longer offered for new selections.

**Acceptance Scenarios**:

1. **Given** a sync diff with an Available model on the "no longer listed by vendor"
   side, **When** the administrator views that row, **Then** it shows: every platform
   default it currently fills, the proposed replacement (or "no suitable replacement"),
   and the count of affected end-user items by type plus the count of distinct affected
   users.
2. **Given** that row is selected, **When** the administrator confirms, **Then** the model's
   status becomes Deprecated and a deprecation record is kept with reason "no longer
   listed by the vendor", the confirmation time, and the confirming administrator.
3. **Given** the same row is left unselected, **When** the administrator confirms other
   rows, **Then** the model keeps its current status and appears again in a future sync
   (specs/009 FR-009, unchanged).
4. **Given** the review dialog, **When** the administrator views the "no longer listed"
   side, **Then** the dialog states plainly that deprecation is permanent through this
   workflow, unlike marking a model Unavailable.
5. **Given** a model was deprecated, **When** any end user opens a model picker, **Then**
   the model is not offered (specs/008 FR-003, unchanged), and every past message that used
   it still shows it as the model that produced it (specs/008 FR-004, unchanged).

---

### User Story 2 - Platform defaults move to a same-tier replacement, or are flagged (Priority: P1)

When a deprecated model was a provider's default or was assigned to a platform AI
capability (chat, image generation, and so on), the platform moves that default to the
replacement shown in the review, taken from the same provider's Available catalog. The
administrator can accept the proposed replacement or choose a different one before
confirming. If no suitable replacement exists, the default is not left pointing at a
Deprecated model. It is cleared and flagged on the AI Providers admin page so an
administrator can resolve it.

**Why this priority**: This story delivers the core promise that no in-flight process
breaks. Without it, deprecating a default reproduces the silent cross-provider routing
failure described in Context.

**Independent Test**: Make a model a provider's default and the explicit model for one
platform capability, then deprecate it through User Story 1. With an eligible replacement
available, verify both defaults now name the replacement. Repeat with no eligible
replacement and verify both defaults are cleared and flagged, and that the flag names the
retired model and states that no replacement was found.

**Acceptance Scenarios**:

1. **Given** a deprecated model that was a provider's default, **When** an eligible
   replacement exists, **Then** the provider's default becomes that replacement at the
   moment of confirmation.
2. **Given** a deprecated model that was explicitly assigned to a platform AI capability,
   **When** an eligible replacement exists, **Then** that capability's assignment becomes
   the replacement.
3. **Given** the review shows a proposed replacement, **When** the administrator picks a
   different Available model from the same provider before confirming, **Then** the
   administrator's choice is used instead of the proposal.
4. **Given** the administrator's chosen replacement lacks a capability the retired model
   had, **When** they select it, **Then** the review lists the capabilities that would be
   lost. Any default whose function needs a lost capability is flagged, not reassigned.
5. **Given** no eligible replacement exists, **When** the model is deprecated, **Then**
   each default it filled is cleared, never left naming a Deprecated model. Each cleared
   default is flagged on the AI Providers admin page, and the flag shows the provider and
   model now serving that function under existing default rules.
6. **Given** an open flag, **When** an administrator sets a new default or capability
   assignment for that function, **Then** the flag is resolved and the resolution is
   recorded on the deprecation record.
7. **Given** a replacement was applied, **When** an administrator opens the deprecation
   record, **Then** the replacement shows as awaiting validation. They can accept it, or
   change it to another eligible model from the same provider.
8. **Given** an administrator changes a replacement during validation, **When** the change
   is confirmed, **Then** every platform default and every end-user item that was switched
   to the first replacement moves to the new one. The exception is an item whose owner
   has since chosen a model themselves; it is left alone.

---

### User Story 3 - Affected end users keep working and learn what changed (Priority: P2)

A user who has the deprecated model configured on something they own (a conversation, an
agent, a saved prompt, or a workflow step) keeps working. Each of those items is switched to
the replacement, and the user receives one notice, in-app and by email. The notice lists
which of their items were switched, the model each now uses, and how to choose a different
model if they prefer.

**Why this priority**: This matters to end users, but it builds on User Story 2's
replacement choice. A deprecation that affects no user-owned items is fully served by the
P1 stories.

**Independent Test**: As a user, configure the soon-to-be-deprecated model on one
conversation, one agent, and one saved prompt. Deprecate it. Verify that the user
receives exactly one notice naming all three items. Verify that each item now names the
replacement and runs on it, and that no other user is notified.

**Acceptance Scenarios**:

1. **Given** a user with several items referencing the deprecated model, **When** the
   deprecation is confirmed, **Then** that user receives exactly one notice for the
   confirmation that lists every affected item.
2. **Given** one confirmation deprecates several models that the same user references,
   **When** notices are produced, **Then** the user still receives one notice, grouped
   by model.
3. **Given** a user whose only link to the model is past messages in conversations they
   have since deleted, **When** the deprecation is confirmed, **Then** they are not
   notified.
4. **Given** an affected item is used after the deprecation, **When** it runs, **Then**
   it runs on the replacement, and every new output is attributed to the replacement.
   Past outputs keep showing the retired model that produced them.
5. **Given** no replacement exists for the retired model, **When** an affected item is
   used, **Then** it fails with a clear message that names the retired model and explains
   how to choose another. It never fails without explanation.
6. **Given** a conversation reply or workflow step is already running at the moment of
   confirmation, **When** the deprecation is applied, **Then** that run finishes on the
   model it started with. The change applies from the next request or step.

---

### User Story 4 - Administrators receive a deprecation summary (Priority: P2)

Every administrator who manages AI providers is told that a model was deprecated and why.
The summary includes: provider, model, reason, time, and who confirmed it; each default
that was reassigned (from and to); each default that was flagged; and how many users were
notified.

**Why this priority**: The confirming administrator already sees the outcome in the
dialog. The summary keeps the other administrators informed and gives everyone a durable
record, but the workflow is still safe without it.

**Independent Test**: With two administrators, have one confirm a deprecation that
reassigns one default and flags another. Verify that both administrators receive the
summary with the correct reassignment, flag, and notified-user count.

**Acceptance Scenarios**:

1. **Given** a confirmed deprecation, **When** notices are produced, **Then** every user
   who holds AI-provider administration permission receives the summary, including the
   confirming administrator.
2. **Given** one confirmation deprecates several models, **When** the summary is
   produced, **Then** each administrator receives one summary covering all of them.
3. **Given** any administrator, **When** they open the deprecated model in the catalog,
   **Then** they can see its deprecation record: reason, time, confirming administrator,
   replacement, reassigned and flagged defaults, and notified-user count.

---

### User Story 5 - A Deprecated model stays retired at every entry point (Priority: P3)

The admin status control already refuses to re-enable a Deprecated model, but only the
screen enforces this today. A request that bypasses the screen can still set any status.
After this feature, the rule holds at every entry point: nothing moves a model out of
Deprecated, and the simple status control cannot move a model into Deprecated either,
because that would skip the reassignment and notices this workflow guarantees.

**Why this priority**: This is a small change that protects the workflow's guarantees,
and today's screen-level block already covers the common case.

**Independent Test**: Send status changes that bypass the admin screen: Deprecated to
Available, Deprecated to Unavailable, and Available to Deprecated. Verify that each is
rejected with a clear reason and that the model's status is unchanged.

**Acceptance Scenarios**:

1. **Given** a Deprecated model, **When** any status change to Available or Unavailable
   is requested through any entry point, **Then** it is rejected with a clear reason and
   the status is unchanged.
2. **Given** an Available or Unavailable model, **When** the simple status control is asked
   to set it to Deprecated, **Then** the request is rejected with a reason stating that
   deprecation happens only through the sync review.
3. **Given** a Deprecated model's row in the catalog, **When** an administrator views it,
   **Then** its status control stays disabled (specs/008 User Story 2 scenario 3,
   unchanged).

---

### Edge Cases

- **Several models deprecated in one confirmation**: A model deprecated in the same
  confirmation is never chosen as another model's replacement. Replacements and flags for
  the whole batch are worked out together.
- **Chained deprecation**: If model A was replaced by B and B is later deprecated, anything
  that relied on A follows B's replacement. Nothing ever resolves to a Deprecated model.
- **Replacement check at confirmation time**: The proposal shown in the review is checked
  again when the administrator confirms. If it is no longer Available, or another
  administrator changed the affected default in the meantime, the current state wins. The
  result reports what was actually applied, and it can differ from the preview.
- **Partial failure** (specs/009 best-effort apply): For each model, the deprecation and
  its platform-default reassignments or flags succeed or fail together. A model is never
  left Deprecated while still filling a default. A failed row is named with its reason and
  stays in its prior state, eligible for the next sync.
- **Notice delivery failure**: A notice that cannot be delivered never undoes or blocks
  the deprecation. It is recorded durably at confirmation time and delivered afterwards,
  and any delivery failure is recorded and visible to administrators.
- **Capability that requires an explicit model** (for example, image generation): If no
  replacement supports what the capability needs, the capability reports itself as not
  configured, with a clear message to the user. It is never served by an unsuitable model.
- **Provider left with no Available models**: Every default the deprecated model filled is
  flagged. No model from a different provider is chosen as a replacement.
- **Vendor list temporarily incomplete**: Deprecation cannot be undone through this
  workflow (see Assumptions). The review's explicit permanence warning (User Story 1,
  scenario 4) is the safeguard, and the administrator can leave an uncertain row
  unselected.
- **Large blast radius**: A model referenced by thousands of users is confirmed as quickly
  as any other. User notices are produced after confirmation and never make the
  administrator wait.
- **User changed the item after the switch**: If the owner picks a different model for
  an item after it was switched, that choice is final. A later validation change (FR-011b)
  does not overwrite it.
- **Replacement with unknown pricing**: Price ranking puts it last, but it can still be
  proposed when it is the only eligible candidate. Administrator validation is the check
  on that choice.
- **Existing Deprecated models**: Models already Deprecated before this feature ships are
  not processed retroactively. The lock in User Story 5 applies to them.

## Requirements *(mandatory)*

### Functional Requirements

**Deprecation transition**

- **FR-001**: Confirming a selected "no longer listed by vendor" row in the sync review MUST
  set that model's status to **Deprecated**. This amends specs/008 FR-008 and specs/009
  FR-007 for the removed-from-vendor side only. Newly added models are still created as
  Unavailable.
- **FR-002**: Every deprecation MUST produce a durable deprecation record holding the
  model, provider, reason (this feature records only "no longer listed by the vendor"),
  confirmation time, and confirming administrator, plus the outcome: the replacement,
  each default reassigned (from and to), each default flagged, and the number of users
  notified.
- **FR-003**: Before confirmation, the sync review MUST show, for each "no longer listed"
  row: every platform default the model fills, the proposed replacement or "no suitable
  replacement", counts of affected end-user items by type, and the count of distinct
  affected users. It MUST show counts only, never user identities or item contents.
- **FR-004**: The sync review MUST state that deprecation is permanent through this
  workflow, as distinct from marking a model Unavailable.
- **FR-005**: For each model, the deprecation and all of its platform-default
  reassignments and flags MUST be applied together or not at all. This refines specs/009
  FR-007a's per-row best-effort rule.

**Replacement selection and platform defaults**

- **FR-006**: The system MUST propose at most one replacement for each deprecated model,
  chosen from the same provider's Available catalog. The candidate MUST NOT be a model
  being deprecated in the same confirmation. An eligible candidate MUST support every
  capability the retired model supports. Eligible candidates MUST be ranked by: (1) the
  closest price to the retired model, with candidates of unknown price ranked after all
  candidates of known price; (2) a context size at least as large as the retired model's
  before smaller ones; (3) the newest release date. The top-ranked candidate is
  proposed. If no candidate is eligible, "no suitable replacement" is shown.
- **FR-007**: Before confirming, the administrator MUST be able to replace the proposal
  with any other Available model from the same provider, or choose "no replacement". If
  the chosen model lacks a capability the retired model had, the review MUST list the
  missing capabilities.
- **FR-008**: On confirmation, every provider default and every platform AI capability
  assignment that names the deprecated model MUST be changed to the replacement. The
  exception is a default whose function needs a capability the replacement lacks; that
  default is handled by FR-009.
- **FR-009**: When no suitable replacement exists for a default, that default MUST be
  cleared, never left naming a Deprecated model. It MUST then be flagged on the AI
  Providers admin page with: the retired model, the reason no replacement was used, and
  the provider and model now serving that function under the existing default rules.
- **FR-010**: A flag MUST stay visible until an administrator sets a new default or
  capability assignment for that function. Its resolution (who, when, what was chosen)
  MUST be added to the deprecation record.
- **FR-011**: A platform capability that cannot work without an explicit model MUST, while
  flagged, report itself to users as not configured with a clear message. It MUST NOT
  fall back to a model that lacks the capability.
- **FR-011a**: Every applied replacement MUST be marked as awaiting validation on its
  deprecation record until an administrator accepts it or changes it. The administrator
  summary (FR-018) MUST ask administrators to validate it and link to the record.
  Validation never delays the replacement: service continues on it while validation is
  pending.
- **FR-011b**: If an administrator changes a replacement during validation, every platform
  default and every end-user item switched to the first replacement MUST move to the new
  one. Items whose owner has since chosen a model themselves are not changed. Affected
  users MUST receive a follow-up notice. Who validated, when, and the outcome MUST be
  added to the deprecation record.

**End-user items**

- **FR-012**: The system MUST identify every end-user item the user can still use that
  names the deprecated model: conversations (including archived ones), agents, saved
  prompts, workflow steps, and any user-level default-model setting. Deleted items and
  past messages MUST NOT count as references.
- **FR-013**: A run already in progress when the deprecation is confirmed MUST finish on
  the model it started with. The deprecation applies from the next request or step.
- **FR-014**: When a replacement exists, every affected end-user item MUST be permanently
  switched to it on confirmation, so it keeps working without any action from the user. A
  record of each switch (item, previous model, new model) MUST be kept, so the notice
  can list it and a later validation change (FR-011b) can find it. A user may switch the
  item to any other Available model afterwards.
- **FR-015**: When no replacement exists, an affected item MUST keep naming the retired
  model. Using it MUST fail with a clear message that names the retired model and explains
  how to choose another. It MUST NOT be silently served by any other model.
- **FR-016**: A deprecated model MUST NOT be offered as a choice in any model picker
  (specs/008 FR-003, unchanged). Where an item still names it (FR-015), the item's settings
  MUST mark it as retired and prompt a new choice. Every new output MUST be attributed to
  the model that actually produced it, and past outputs keep their original attribution
  (specs/008 FR-004).

**Notices**

- **FR-017**: On each confirmation that deprecates at least one model, each affected end
  user MUST receive exactly one notice, in-app and by email. It lists, per deprecated
  model, their affected items, the model each item now uses (or that none could be
  assigned), and how to choose a different model. Users with no affected items MUST NOT be
  notified.
- **FR-018**: On each such confirmation, every user holding AI-provider administration
  permission (including the confirming administrator) MUST receive one summary covering
  every model deprecated in it, with the contents listed in FR-002.
- **FR-019**: Notices MUST be recorded durably together with the deprecation and delivered
  afterwards. Slow or failed delivery MUST NOT delay, fail, or undo the deprecation, and
  every delivery failure MUST be recorded and visible to administrators.
- **FR-020**: Notices MUST be requested through the notifications hub (spec 067) as new
  notification types in the System category. This feature MUST NOT deliver on any channel
  itself (spec 067 FR-001/FR-002). The in-app copy MUST link to the item (end users) or to
  the deprecation record (administrators). Email MUST be on by default for both types.
- **FR-020b**: The administrator summary's email MUST be mandatory for every recipient
  except Super Users. That covers Administrators and holders of a custom role with the
  AI-provider administration permission, and their preferences screen MUST show it as
  locked. A Super User MAY turn the summary email off. A recipient who holds both the
  Administrator and the Super User role is treated as an Administrator. The in-app copy
  of the summary MUST always be delivered to every recipient, Super Users included.
- **FR-020a**: In-app delivery MUST work when this feature ships. Email delivery is provided
  by spec 067's email channel (its User Story 3). Until that channel ships, the notices
  MUST still be recorded with email requested, so emails go out when it ships with no
  change to this feature.

**Status lock and access**

- **FR-021**: Every entry point MUST reject a status change that moves a Deprecated model
  to any other status, with a clear reason. This makes specs/008 FR-002 hold everywhere,
  not only in the admin screen.
- **FR-022**: The simple status control MUST reject any request to set a model to
  Deprecated. Deprecation MUST happen only through this workflow.
- **FR-023**: Every administrator capability in this feature (impact preview, replacement
  choice, confirmation, flag resolution, viewing deprecation records) MUST require the
  AI-provider administration permission already required by specs/008 FR-009.
- **FR-024**: Every deprecation, reassignment, flag, flag resolution, and rejected status
  change MUST be logged as an administrative event with the acting administrator and a
  correlation identifier.
- **FR-025**: Every action in this feature MUST give the administrator immediate, visible
  feedback on success, failure, or partial result (specs/008 FR-011, specs/009 FR-012),
  and never a silent no-op.

### Key Entities

- **AI Model** *(existing, specs/008)*: Its Deprecated status is now reachable only
  through this workflow and cannot be left once set.
- **Deprecation Record**: One per deprecated model. Holds the reason, time, confirming
  administrator, chosen replacement (or none), the platform defaults reassigned (from and
  to) and flagged, the number of users notified, and later flag resolutions. Kept for as
  long as the model record exists.
- **Replacement**: The single model chosen to take over from a deprecated model: proposed
  by the system, optionally changed by the administrator, or absent. It is always from the
  same provider and was Available when confirmed. It stays awaiting validation until an
  administrator accepts or changes it.
- **Item Switch**: A record that one end-user item (conversation, agent, saved prompt, or
  workflow step) was moved from the deprecated model to the replacement. It holds the
  item, its owner, the previous model, and the new model. Notices are built from these
  records, and a validation change uses them to re-point items.
- **Default Attention Flag**: An open item on the AI Providers admin page for a platform
  default that was cleared because no suitable replacement existed. It stays open until an
  administrator resolves it.
- **Deprecation Notice**: One notice per recipient per confirmation. End users receive a
  list of their affected items; administrators receive a summary of the whole
  confirmation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of confirmed vendor removals end in Deprecated status with a complete
  deprecation record (reason, time, administrator, outcome). Zero end in Unavailable.
- **SC-002**: Immediately after any deprecation, zero platform defaults name a Deprecated
  model. Every one is either reassigned or cleared and flagged.
- **SC-003**: Zero requests are silently served by a different provider because a
  default was deprecated. Every fallback is shown as an open flag on the AI Providers
  admin page.
- **SC-004**: When a replacement exists, 100% of affected end-user items keep working with
  no action from the user. When none exists, every use fails with a message that names the
  retired model and how to fix it. Zero unexplained failures.
- **SC-005**: Every affected user receives exactly one notice per confirmation, and zero
  unaffected users are notified.
- **SC-006**: Every administrator sees the deprecation summary in-app within 5 minutes of
  confirmation. Once spec 067's email channel is live, the summary email also arrives
  within the same 5 minutes.
- **SC-010**: 100% of applied replacements are visible as awaiting validation until an
  administrator accepts or changes them. An administrator can do either in under one
  minute from the summary notice.
- **SC-007**: Confirming a deprecation completes in under 10 seconds, whether the model is
  referenced by thousands of users or by none.
- **SC-008**: 100% of attempts to move a model out of Deprecated, or into it outside this
  workflow, are rejected, including attempts that bypass the admin screen.
- **SC-009**: An administrator can resolve an open flag in under one minute from the AI
  Providers admin page.

## Assumptions

- This feature extends the sync review from specs/008 and specs/009. It does not change
  how a sync is triggered, how the diff is computed, row selection and filtering, or the
  added-side rule (new models start Unavailable). It changes only the outcome of confirming
  a removed-from-vendor row (FR-001) and adds the impact preview and replacement choice to
  that row.
- Only the sync path deprecates a model, and the only recorded reason is "no longer listed
  by the vendor". Deprecating a model the vendor still lists (for example, after an
  announced retirement date) is out of scope. The deprecation record allows other reasons
  to be added later.
- **Re-enabling a Deprecated model is out of scope, as decided during specification.**
  As a consequence, a mistaken deprecation, such as one caused by a vendor list that was
  temporarily incomplete, cannot be undone in this feature. The permanence warning
  (FR-004) and the option to leave a row unselected are the safeguards. A deliberate,
  audited "reinstate" action is a candidate follow-up.
- Marking a model Unavailable through the simple toggle does not trigger this workflow.
  Existing behavior for Unavailable models is unchanged, including the known gap that
  agents, saved prompts, and workflow steps do not check a model's status before using it.
  This feature closes that gap only for Deprecated models.
- Model names written into platform configuration files (outside the catalog), the
  separate embedding-model catalog, and text-to-speech voice models are not catalog
  references and are out of scope.
- **Who chooses models.** Platform defaults (each provider's default model and the AI
  capability assignments) can be changed only by users with AI-provider administration
  permission, i.e. Administrator, Super User, or a custom role granted that permission.
  End users choose models for what they own: the model of each of their conversations (in
  Chat settings), their agents (Agent Builder), their saved prompts, and the AI steps of
  their workflows. Those user-owned items are why FR-012 through FR-017 exist.
- Permanently switching user-owned items (FR-014) is a deliberate choice that puts
  continuity of service first. The previous model is not lost: it is kept in the switch
  record and named in the notice.
- Spec 067's in-app delivery (notification center, live updates, outbox) is built, and
  this feature depends on it. Spec 067's email channel, notification preferences, and
  administrator delivery monitoring are still to be built. Email notices and the
  administrator view of failed deliveries (FR-019) arrive when those parts ship. Until
  then, delivery failures are still recorded and logged (spec 067 FR-030).
- The role-dependent lock in FR-020b goes beyond spec 067's mandatory rule (FR-032), which
  locks a notification type for everyone. Spec 067's preferences work (its User Story 4,
  not built yet) has to support a lock that depends on the user's role.
- "Administrators" for notices means users holding the AI-provider administration
  permission from the role and permission catalogue (specs/055), not only the built-in
  administrator roles.
- The user-level default-model setting exists but the product does not currently use it.
  It is covered by FR-012 only so that a value stored there is not missed.
- A provider has at most a few dozen models, so replacement selection needs no search or
  paging. The number of affected users can reach the thousands (SC-007).
