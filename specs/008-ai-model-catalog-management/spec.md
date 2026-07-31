# Feature Specification: Admin AI Model Catalog Management

**Feature Branch**: `008-ai-model-catalog-management`

**Created**: 2026-07-31

**Status**: Draft

**Input**: User description: "Admin AI model catalog management. specs/005-multi-provider-ai-engine and specs/007-admin-ai-provider-ui together let an administrator enable/disable an AI provider and configure its credential, but the list of models offered under each provider is a fixed, hand-seeded catalog with no way for an administrator to add, deprecate, or otherwise curate models through the product. Each provider already has a working vendor model-list capability, but nothing in the product calls it. An administrator, from the existing AI Providers admin page, needs to: (1) see every model currently in the catalog for a given provider, including its capability flags, pricing, and current status (Available/Deprecated/Unavailable — a non-Available model must not be selectable by end users going forward, but must not affect any past conversation that already used it), (2) manually change a model's status, and (3) trigger a 'sync from provider' action that shows a diff (newly available at the vendor / no longer listed by the vendor) as a proposal the administrator must explicitly review and confirm before anything changes — never an automatic, unreviewed catalog change."

## Clarifications

### Session 2026-07-31

- Q: How should the sync diff treat a model an administrator deliberately deprecated, if the vendor still lists it? → A: Compare by model key against the entire catalog regardless of status — a model already known to the catalog (in any status) is never proposed as an addition again, and sync never automatically flips a non-Available model back to Available on its own; only a manual status change (User Story 2) can do that.
- Q: When a sync-confirmed diff adds a brand-new model, what status should it get by default? → A: Unavailable by default — an administrator must take a separate, explicit manual step (User Story 2) before it becomes selectable by end users.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Administrator reviews a provider's model catalog (Priority: P1)

An administrator, viewing a provider on the existing AI Providers admin page, opens that
provider's model catalog and sees every model the product currently knows about for it —
including ones no longer selectable — along with what it can do (capabilities), what it
costs, and its current status.

**Why this priority**: Nothing else in this feature is useful without this view — an
administrator can't decide what to deprecate or what a sync found without first seeing
what's already there. This alone is also a real improvement over today: an administrator
currently has no way to see the model catalog at all.

**Independent Test**: Open a provider's model list and confirm every model — Available,
Deprecated, and Unavailable alike — is shown with its capability flags, pricing (or a
clear "pricing unknown" indication), and status.

**Acceptance Scenarios**:

1. **Given** a provider with a mix of Available, Deprecated, and Unavailable models,
   **When** an administrator opens that provider's model catalog, **Then** every model is
   listed regardless of status, each showing its capabilities, pricing (or "unknown" if
   not set), and current status.
2. **Given** a model with no pricing information, **When** it's displayed, **Then** it
   shows as pricing-unknown, never a fabricated zero cost.

---

### User Story 2 - Administrator manually curates a model's status (Priority: P1)

An administrator marks a model Deprecated (a vendor is retiring it), Unavailable (it
should stop being offered for another reason), or Available (reinstating it) — and end
users immediately stop (or start) being able to select it, without affecting any
conversation that already used it.

**Why this priority**: This is the direct fix for the reported problem — an administrator
today cannot curate the catalog at all. This alone, without the sync capability in User
Story 3, already lets an administrator retire a model the moment they learn a vendor has
discontinued it.

**Independent Test**: Mark an Available model Deprecated, confirm it's no longer offered
to end users for a new selection, and confirm a past conversation that already used it is
completely unaffected.

**Acceptance Scenarios**:

1. **Given** an Available model, **When** an administrator marks it Deprecated or
   Unavailable and confirms, **Then** it immediately stops being offered to end users for
   any new selection.
2. **Given** a Deprecated or Unavailable model, **When** an administrator marks it
   Available again and confirms, **Then** it immediately becomes selectable again.
3. **Given** a model that has already been used in past conversations, **When** its status
   later changes in either direction, **Then** every past message that used it keeps
   showing exactly the provider/model that actually produced it, unaffected by the status
   change.

---

### User Story 3 - Administrator syncs the catalog from the vendor (Priority: P2)

An administrator triggers a check against a provider's own list of models, sees a
proposed diff — models the vendor now offers that aren't in the catalog yet, and models
in the catalog the vendor no longer lists — and explicitly reviews and confirms it before
anything actually changes.

**Why this priority**: This makes keeping the catalog current far less manual than User
Story 2 alone, but it's an efficiency improvement on top of a feature (User Story 2) that
is already useful without it.

**Independent Test**: Trigger a sync for a provider whose vendor catalog has diverged from
the product's stored catalog; confirm a diff is shown and nothing changes until the
administrator explicitly confirms it; confirm the catalog matches the diff afterward.

**Acceptance Scenarios**:

1. **Given** a provider whose vendor now lists a model not yet in the catalog, **When** an
   administrator triggers a sync, **Then** that model appears in the proposed diff as an
   addition, and the catalog is unchanged until the diff is confirmed.
2. **Given** a provider whose vendor no longer lists a model that's still in the catalog as
   Available, **When** an administrator triggers a sync, **Then** that model appears in
   the proposed diff as no-longer-listed, and the catalog is unchanged until confirmed.
3. **Given** a proposed diff, **When** the administrator confirms it, **Then** each
   newly-listed model is added to the catalog as Unavailable (not yet selectable by end
   users — a deliberate separate step, User Story 2, makes it Available) and each
   no-longer-listed model is marked Unavailable (never deleted — past conversations may
   still reference it).
4. **Given** a proposed diff, **When** the administrator dismisses it without confirming,
   **Then** the catalog is completely unchanged.
5. **Given** a provider whose vendor catalog exactly matches the stored catalog, **When**
   an administrator triggers a sync, **Then** the result clearly shows there is nothing to
   review, with nothing to confirm.

### Edge Cases

- What happens if the vendor's model-list check itself fails (the provider is unreachable
  or rejects the request)? The sync attempt fails with a clear explanation, and the
  catalog is left completely unchanged — the same "never applied without confirmation"
  guarantee, applied to a failure as well as a success.
- What happens if the administrator confirms a diff that has gone stale (someone else
  changed the catalog after the diff was generated but before it was confirmed)? The
  administrator confirms exactly the diff they reviewed; any further drift is caught on
  the next sync, the same way any other concurrent-edit staleness in this admin area is
  handled (Notes reference specs/007's equivalent edge case resolution).
- What happens to a model's capability flags and pricing when it's newly added by a sync?
  The vendor's own model-list responses typically don't include reliable pricing (and
  sometimes not full capability detail either); a newly-added model is added with
  whatever the vendor reported and pricing left unknown rather than guessed — correcting
  or completing that metadata is out of scope for this feature (see Assumptions).
- What happens if an administrator tries to select a Deprecated or Unavailable model for
  an existing conversation going forward (not a past message)? It is not offered as a
  choice, the same as it wouldn't be for a brand-new conversation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST let an administrator view every model in a given provider's
  catalog regardless of status, each showing its capability flags, pricing (or an
  explicit "unknown" indication when unset), and current status.
- **FR-002**: System MUST let an administrator change a model's status among Available,
  Deprecated, and Unavailable, in any direction.
- **FR-003**: A model whose status is Deprecated or Unavailable MUST NOT be offered to any
  end user as a selectable choice from the moment its status changes.
- **FR-004**: Changing a model's status MUST NOT alter any already-recorded message's
  provider/model attribution — past conversations always keep showing what actually
  produced them, independent of the model's current status.
- **FR-005**: System MUST let an administrator trigger a check of a provider's own list of
  models against the product's stored catalog for that provider.
- **FR-006**: That check MUST produce a proposed diff by comparing the vendor's list against
  the provider's **entire** stored catalog, regardless of status — a model already known to
  the catalog in any status (Available, Deprecated, or Unavailable) MUST NOT be proposed as
  an addition again, even if the vendor still lists it. The diff consists of: models the
  vendor lists that are not yet in the catalog at all (a genuine addition), and
  currently-**Available** models in the catalog the vendor no longer lists (a model already
  Deprecated or Unavailable is never surfaced on this side either, since re-flagging an
  already-non-Available model would be redundant noise). This check MUST NOT change the
  catalog itself.
- **FR-007**: System MUST require the administrator to explicitly review and confirm a
  proposed diff before any addition or status change from it is applied; dismissing a
  diff without confirming MUST leave the catalog completely unchanged.
- **FR-008**: Confirming a diff MUST add each newly-listed model to the catalog with status
  Unavailable (not immediately selectable — a separate manual status change per FR-002 is
  required before end users can select it) and mark each no-longer-listed model Unavailable
  — never delete a model row, since past conversations may still reference it.
- **FR-009**: System MUST restrict every capability in this specification to
  administrators only, matching the access restriction already established for AI
  provider administration (specs/007-admin-ai-provider-ui FR-007).
- **FR-010**: System MUST require the administrator to explicitly confirm before any
  state-changing action in this specification takes effect (manual status change,
  applying a sync diff) — the same confirm-before-applying pattern already established for
  AI provider administration (specs/007-admin-ai-provider-ui FR-010).
- **FR-011**: System MUST give the administrator clear, immediate, visible feedback for
  every action in this specification — success or failure — never a silent no-op.

### Key Entities

- **AI Model**: One model offered by an AI Provider (e.g., "GPT-4.1" under OpenAI). Has a
  display name, capability flags (what it can do — e.g. vision, streaming, function
  calling), pricing (or unknown), and a status (Available, Deprecated, or Unavailable)
  that governs whether it can be newly selected — independent of its capability/pricing
  data, and independent of any past conversation's already-recorded use of it.
- **Model Sync Proposal**: A one-time, reviewable comparison between a provider's stored
  catalog and what the vendor itself currently lists — additions and no-longer-listed
  entries. Exists only to be reviewed and either confirmed or dismissed; it is not itself
  a persisted, ongoing record.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can locate and deprecate a specific model in under one
  minute from the AI Providers admin page.
- **SC-002**: 100% of catalog changes originating from a sync are presented for explicit
  administrator review before being applied — zero automatic, unreviewed catalog changes.
- **SC-003**: 100% of past conversations continue to display their original, accurate
  provider/model attribution after any status change or catalog sync, verified by testing.
- **SC-004**: 100% of attempts by a non-administrator account to view or change the model
  catalog are denied, verified by testing.
- **SC-005**: An administrator can determine a provider's full model picture — every
  model's capabilities, pricing, and status — in a single view, with no need to consult
  another tool.

## Assumptions

- This feature is additive to the existing AI Providers admin page (specs/007-admin-ai-provider-ui)
  — it does not introduce a new top-level admin area or navigational pattern.
- Every provider's vendor model-list capability already exists and is reliable enough to
  drive a sync check; this feature does not need to build that capability, only expose it
  through an administrator-reviewed workflow.
- Correcting or completing a model's capability flags or pricing (beyond what the vendor's
  own list reports, or beyond leaving pricing unknown) is out of scope for this feature —
  a model added via sync may need further administrator attention to have complete,
  accurate metadata; providing a dedicated editing capability for that is a candidate for
  a further follow-up, not part of this specification.
- Confirmation-before-applying (FR-010) and admin-only access (FR-009) intentionally reuse
  the pattern already established in specs/007-admin-ai-provider-ui rather than
  introducing a new one, for consistency within the same admin area.
- A model catalog realistically has, at most, a few dozen entries per provider — this
  specification does not require pagination or search within a single provider's model
  list.
