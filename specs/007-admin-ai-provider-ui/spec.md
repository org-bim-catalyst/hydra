# Feature Specification: Admin AI Provider Configuration UI

**Feature Branch**: `007-admin-ai-provider-ui`

**Created**: 2026-07-31

**Status**: Draft

**Input**: User description: "Add the missing administrator-facing UI for the AI Provider Engine. Context: multi-provider AI support (spec 005-multi-provider-ai-engine) has been implemented end-to-end on the backend and on the end-user side (provider/model selection in chat, a Settings > AI Providers default-preference tab), but there is no administrator page anywhere in the product for enabling a provider or configuring its credential. Every AI provider therefore stays disabled forever, which means the end-user-facing provider catalog is permanently empty and the Settings tab permanently shows 'No AI providers are enabled yet — an administrator needs to configure one first,' even to an administrator, because there is nowhere in the UI to change that. This closes the gap between the already-built administrative capability and the complete absence of any way to reach it from the product's UI, which is what is currently blocking the whole multi-provider feature from being usable end-to-end."

## Clarifications

### Session 2026-07-31

- Q: Which state-changing actions on this page should require an explicit confirmation step before applying? → A: Every action here requires confirmation (enable, disable, set credential, clear credential).
- Q: Should this admin page display who last changed each provider's configuration and when? → A: No — out of scope for this UI; that information stays in the backend's structured logs only, not surfaced on any admin-facing screen.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Administrator enables a provider for the first time (Priority: P1)

An administrator opens the admin area, finds the AI providers area, picks a disabled provider (e.g. OpenAI), supplies its API credential, and turns it on. From that moment, that provider becomes selectable by end users everywhere in the product that offers a provider/model choice (chat, and the personal default in Settings).

**Why this priority**: Every other multi-provider capability already built for this product — the chat provider/model picker, the Settings default-preference tab, per-message attribution — is inert until at least one provider is enabled. Nothing else in the multi-provider feature can be exercised, demonstrated, or used by a real user until this exists.

**Independent Test**: Log in as an administrator, enable exactly one provider by supplying a credential, then confirm — without touching any other page — that the provider now appears wherever an end user picks an AI provider.

**Acceptance Scenarios**:

1. **Given** an administrator viewing the AI providers area, **When** they open a disabled provider that has no credential configured, **Then** they see it is disabled and not yet configured, with no credential value shown (there is none).
2. **Given** an administrator supplies a valid-looking credential for a disabled provider, **When** they submit it and confirm the action in a confirmation step, **Then** the provider now shows as having a credential configured, the credential's own value is never displayed back to the administrator (not even partially), and the administrator can now enable it.
3. **Given** a provider that already has a credential configured, **When** the administrator chooses to turn it on and confirms that action, **Then** the provider immediately shows as enabled, and it becomes available for end users to select going forward.
4. **Given** a disabled provider that has no credential configured yet, **When** the administrator tries to enable it directly without first setting a credential, **Then** the action is rejected with a clear, specific explanation ("this provider needs a credential before it can be enabled"), not a generic error — no confirmation step is even reached.

---

### User Story 2 - Administrator reviews provider status at a glance (Priority: P2)

An administrator returns to the AI providers area at any time to see, for every provider the product knows about, whether it's currently enabled, whether it has a credential configured, and whether it's currently healthy — without needing to check logs or another tool.

**Why this priority**: Once at least one provider is enabled (User Story 1), an administrator needs an ongoing way to confirm things are still working and to notice a problem (e.g., a provider went unhealthy) before an end user reports it.

**Independent Test**: With one or more providers already configured in varying states (enabled/disabled, healthy/unhealthy/unknown), open the AI providers area and confirm every provider's current state is visible without any additional action.

**Acceptance Scenarios**:

1. **Given** multiple providers exist in different states, **When** the administrator opens the AI providers area, **Then** every provider is listed with its enabled/disabled state and its most recently observed health status.
2. **Given** a provider's health status was last checked some time ago, **When** the administrator views it, **Then** they can see roughly when that health status was last confirmed, so they know how current the information is.

---

### User Story 3 - Administrator disables a provider or rotates/removes its credential (Priority: P2)

An administrator needs to turn a provider off (e.g., a vendor contract ended, or a key was compromised) or replace an existing credential with a new one, and be confident the old credential is fully gone afterward.

**Why this priority**: Enabling a provider (User Story 1) is only safe to offer if there is an equally straightforward, trustworthy way to reverse it — this is the safety valve that makes User Story 1 acceptable to ship.

**Independent Test**: With a provider already enabled, remove its credential and confirm it is immediately disabled and no longer offered to end users; separately, replace an existing credential with a new one and confirm the provider keeps working under the new credential.

**Acceptance Scenarios**:

1. **Given** an enabled provider, **When** the administrator chooses to turn it off and confirms that action, **Then** it immediately stops being offered to end users, while any conversation history that already used it keeps showing which provider produced it.
2. **Given** an enabled provider, **When** the administrator chooses to clear its credential and confirms an action whose confirmation explicitly states it will also disable the provider, **Then** the provider is automatically turned off at the same time (a provider can never remain enabled with no credential).
3. **Given** a provider with an existing credential, **When** the administrator submits a replacement credential and confirms, **Then** the old credential is no longer used for any subsequent request, and the new one takes effect without the administrator needing to also separately disable/re-enable the provider.

### Edge Cases

- What happens if two administrators are viewing the AI providers area at the same time and one disables a provider the other just enabled? The most recent action wins; the next time either administrator's view refreshes, it reflects the current state.
- What happens if an administrator submits an empty or obviously incomplete credential? The action is rejected with a clear explanation before anything is saved.
- What happens if a provider's health check hasn't run yet (brand new / never enabled)? Its health is shown as unknown rather than implying either healthy or unhealthy.
- What happens if a non-administrator user tries to reach this capability directly (e.g., a guessed link)? They are denied access the same way any other admin-only area denies them, with no provider information disclosed.
- What happens to a provider that was enabled and used in past conversations, then later disabled or has its credential removed? Past conversations keep displaying the provider/model that actually produced each message; only future selection of that provider is blocked.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST let an administrator view every AI provider the product knows about, including ones that are currently disabled, each showing whether it is enabled, whether it has a credential configured, and its most recently observed health status.
- **FR-002**: System MUST let an administrator set a credential for a provider, and MUST NOT display that credential's value back to any administrator once it has been submitted.
- **FR-003**: System MUST let an administrator enable a provider only when it already has a credential configured, and MUST reject an attempt to enable a provider with no credential with a specific, actionable explanation rather than a generic failure.
- **FR-004**: System MUST let an administrator disable a currently-enabled provider, taking effect immediately for any new selection by an end user.
- **FR-005**: System MUST let an administrator clear a provider's existing credential, and MUST automatically disable that provider at the same time, making that automatic side effect clear to the administrator.
- **FR-006**: System MUST let an administrator replace an existing credential with a new one without requiring a separate disable/re-enable step.
- **FR-007**: System MUST restrict every capability in this specification to administrators only; a non-administrator MUST NOT be able to view provider configuration state, credential-configured status, or health status, or perform any enable/disable/credential action.
- **FR-008**: System MUST give the administrator clear, immediate, visible feedback for every action in this specification — success or failure — never a silent no-op.
- **FR-009**: System MUST leave every already-produced message's recorded provider/model attribution unaffected by later enabling, disabling, or reconfiguring that provider (past attribution is a historical fact, not a live reference).
- **FR-010**: System MUST require the administrator to explicitly confirm before any of the following takes effect: enabling a provider, disabling a provider, setting or replacing a provider's credential, or clearing a provider's credential. The confirmation for clearing a credential MUST explicitly state that it will also disable the provider.

### Key Entities

- **AI Provider**: One vendor the product can generate AI responses through (e.g., OpenAI, Anthropic). Has a display name, whether it is currently enabled, whether it currently has a credential configured, and its most recently observed health status (and roughly when that was last observed).
- **Provider Credential**: The secret an administrator supplies to authenticate with a given AI Provider. Write-only from the administrator's point of view — it can be set, replaced, or cleared, but its value is never displayed once submitted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new administrator, with no prior guidance, can go from "zero AI providers enabled" to "one AI provider enabled and usable by end users" in under 2 minutes.
- **SC-002**: 100% of the time, a previously-submitted provider credential's value is never shown anywhere in the administrator-facing UI after submission.
- **SC-003**: 100% of attempts by a non-administrator account to view or change AI provider configuration are denied, verified by testing.
- **SC-004**: An administrator can determine every provider's current enabled/health state in a single view, with zero need to consult logs, a database, or any tool outside the product.
- **SC-005**: Disabling a provider or clearing its credential takes effect for new end-user selections within the same interaction — no perceptible delay or required page reload beyond normal navigation.

## Assumptions

- The underlying capability to list providers, enable/disable them, and set/clear their credentials already exists as a backend capability (delivered under spec 005-multi-provider-ai-engine); this feature is specifically the missing administrator-facing surface to reach that capability, not a new backend capability.
- Administrator-only access follows the same access-control approach already used for the product's other administrator-only areas (e.g., user management) — no new access-control mechanism is introduced by this feature.
- A provider credential is treated as an opaque secret string; validating that a given credential actually works against the vendor is covered by the existing periodic health-check behavior, not by this feature's save action.
- Managing the catalog of individual models offered by a provider (adding, deprecating, or syncing models from a vendor) is out of scope for this feature — this feature covers provider-level enable/disable/credential only.
- Triggering an on-demand health check from this UI is out of scope; the health status shown is whatever was most recently observed by the product's existing periodic check.
- This feature reuses the product's existing pattern for administrator-only list/detail pages rather than introducing a new navigational pattern.
- Who last changed a provider's configuration and when is out of scope for this UI; that stays in the backend's structured application logs (not a queryable, UI-facing audit history), consistent with keeping this feature scoped to the UI gap rather than adding a new backend capability.
