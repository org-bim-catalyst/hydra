# Feature Specification: Replace Credential Modal Polish

**Feature Branch**: `065-replace-credential-modal`

**Created**: 2026-09-22

**Status**: Draft

**Input**: User description: "Replace credential modal (AI Providers page) polish: 1. Widen the 'Replace credential {Provider}' modal to double its current width. 2. The API key input should show a placeholder like 'Please insert API key here' when empty, and should NOT show a masked/empty password value when no credential has been entered yet. 3. Add a show/hide (eye) toggle icon inside the input, positioned like standard email/password field patterns — but only show this toggle when the user has actually typed a value into the field. If the field is empty, hide the toggle icon. 4. This should apply consistently to the credential-replace modal for all providers (Anthropic, Google Gemini, OpenAI, OpenRouter), not just Gemini."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See what I'm typing when setting a credential (Priority: P1)

An administrator opens "Set credential" or "Replace credential" for any AI provider and starts typing an API key. Because the key is long and easy to mistype, they want a way to reveal the characters they've entered to check it before confirming, without the key ever being visible by default.

**Why this priority**: This is the core interaction the request is about — pasting or typing a long secret into a masked field with no way to verify it is error-prone, and a failed credential silently breaks that provider for every user until an admin notices.

**Independent Test**: Open the credential dialog for any provider, type a value into the API key field, and confirm a toggle appears that reveals and re-hides the typed characters on demand.

**Acceptance Scenarios**:

1. **Given** the credential dialog is open and the API key field is empty, **When** the administrator looks at the field, **Then** no show/hide toggle is visible.
2. **Given** the administrator has typed at least one character into the API key field, **When** they look at the field, **Then** a show/hide toggle icon appears inside the field, positioned the same way a standard password field's toggle is.
3. **Given** the toggle is visible and the field is currently masked, **When** the administrator clicks the toggle, **Then** the typed characters become visible in plain text and the toggle's icon changes to indicate "hide."
4. **Given** the field is currently showing plain text, **When** the administrator clicks the toggle again, **Then** the value is masked again.
5. **Given** the administrator deletes all characters from the field after having typed some, **When** the field becomes empty, **Then** the toggle disappears again.

---

### User Story 2 - Know the field is genuinely empty, not a hidden existing value (Priority: P1)

An administrator opens "Replace credential" for a provider that already has a credential configured. They want the field to clearly read as empty and ready for a new value, not as if it already contains a hidden secret they'd be overwriting blindly.

**Why this priority**: The dialog already states "The value is never shown again once saved," but an empty masked password field looks identical to a field holding a secret value — this ambiguity is confusing exactly when the stakes (accidentally clearing or overwriting a working credential) are highest.

**Independent Test**: Open the credential dialog for a provider that already has a credential configured, and confirm the field visibly reads as empty with explanatory placeholder text rather than looking pre-filled.

**Acceptance Scenarios**:

1. **Given** a provider already has a credential configured, **When** the administrator opens "Replace credential" for it, **Then** the API key field is empty and shows placeholder text such as "Please insert API key here."
2. **Given** the field shows the placeholder text, **When** the administrator begins typing, **Then** the placeholder disappears and their typed input takes its place, masked as in User Story 1.
3. **Given** the administrator closes the dialog without entering a value and reopens it, **When** the dialog reopens, **Then** the field is empty again with the placeholder shown — the existing credential is never displayed or pre-filled.

---

### User Story 3 - Enough room to work with a long credential (Priority: P2)

An administrator pastes a long API key into the credential dialog. They want the dialog to be wide enough that the field isn't cramped, making it easier to review the pasted value (especially once revealed via the toggle).

**Why this priority**: A usability refinement that supports Stories 1 and 2 rather than a standalone problem — a wider dialog makes the reveal toggle more useful for genuinely inspecting a long key, but the feature still delivers value at the current width.

**Independent Test**: Open the credential dialog on a desktop-width viewport and confirm it renders at roughly double the width it did before this change, while remaining readable and properly centered.

**Acceptance Scenarios**:

1. **Given** the administrator opens the credential dialog on a standard desktop viewport, **When** the dialog renders, **Then** it is approximately double its previous width, up to the dialog's usual responsive maximum.
2. **Given** the viewport is narrow (e.g., mobile), **When** the dialog renders, **Then** it still fits within the viewport without horizontal overflow (the width increase does not break small-screen layouts).

---

### Edge Cases

- What happens if the administrator pastes a value and then clears it entirely? The toggle must disappear along with the value, matching an empty field (User Story 1, Scenario 5).
- What happens on a provider that has never had a credential set ("Set credential" rather than "Replace credential")? Same empty-field placeholder and toggle behavior applies — there's no existing credential to distinguish from, but the interaction pattern must remain identical across "Set" and "Replace."
- What happens if the administrator toggles visibility, then clicks Confirm? The submitted value is whatever is currently in the field, unaffected by the visibility toggle — the toggle only changes how the value is displayed, never the value itself.
- What happens if the administrator toggles visibility and then closes the dialog without confirming? On reopen, the field resets to empty and masked, per existing dialog-reset behavior — the toggle state is not persisted across opens.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The credential dialog (both "Set credential" and "Replace credential" variants) MUST render at approximately double its current width on desktop viewports, while remaining responsive and non-overflowing on narrow viewports.
- **FR-002**: The API key field MUST display placeholder text (e.g., "Please insert API key here") whenever it is empty, for both "Set credential" and "Replace credential," regardless of whether the provider already has a credential configured.
- **FR-003**: The API key field MUST NOT display any pre-filled, masked, or otherwise non-empty value when no credential has been typed in the current dialog session — an existing stored credential is never surfaced in the field.
- **FR-004**: The API key field MUST mask its contents by default (as it does today) whenever it contains a typed value.
- **FR-005**: A show/hide toggle control MUST appear inside the API key field, positioned consistent with standard password-field conventions (e.g., trailing edge of the field), whenever the field contains at least one character.
- **FR-006**: The show/hide toggle MUST be hidden whenever the API key field is empty.
- **FR-007**: Clicking the show/hide toggle MUST switch the field between masked and plain-text display of its current value without altering the value itself.
- **FR-008**: The toggle's icon/state MUST reflect the field's current display mode (i.e., indicate the action that clicking it will perform next).
- **FR-009**: This behavior (width, placeholder, empty-state, toggle visibility) MUST be identical across the credential dialog for every provider (Anthropic, Google Gemini, OpenAI, OpenRouter, and any provider added later) — it is one shared dialog component, not a per-provider implementation.
- **FR-010**: Closing and reopening the credential dialog MUST reset the field to empty (masked, placeholder shown, toggle hidden), matching existing dialog behavior — no credential value or visibility state persists across opens.

### Key Entities

- **AI Provider Credential**: The API key an administrator sets for a given AI provider (Anthropic, Google Gemini, OpenAI, OpenRouter). Write-only from the UI's perspective — once saved, its value is never returned to or displayed in the client.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can visually confirm the exact API key they typed, in full, before submitting it — without needing to retype or paste it elsewhere to check.
- **SC-002**: An administrator opening "Replace credential" on a provider that already has a working credential cannot mistake the field for containing that existing secret — the field always visibly reads as empty and ready for new input.
- **SC-003**: The reveal/hide interaction and empty-state appearance are visually identical regardless of which of the four providers' credential dialog is opened.
- **SC-004**: The credential dialog remains fully usable (no clipped or overflowing content) on both a standard desktop width and a narrow mobile width.

## Assumptions

- This applies only to the API key entry dialog reached from each AI provider row's "Set credential" / "Replace credential" action; it does not change any other password/secret field elsewhere in the admin panel.
- "Double its current width" is a relative sizing instruction, not an exact pixel target — it is bounded by MUI's existing responsive dialog breakpoints so the dialog never exceeds a sane maximum on very wide screens or overflows on narrow ones.
- The show/hide toggle only affects the current in-progress input in the browser; it has no server-side or security implication since the key is never persisted or echoed back until the administrator explicitly confirms the save.
- No change to the underlying `setCredential`/`clearCredential` API contracts is required — this is a frontend-only presentational and interaction change.
