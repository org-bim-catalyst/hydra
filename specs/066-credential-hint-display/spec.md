# Feature Specification: Credential Hint Display

**Feature Branch**: `066-credential-hint-display`

**Created**: 2026-09-22

**Status**: Draft

**Input**: User description: "Show a shortened, vendor-style hint of each AI provider's configured API key (first 4 characters + '...' + last 4 characters, e.g. sk-a...ygAA) always displayed next to the provider name in the last column of the AI Providers admin table — so an administrator can confirm at a glance whether a key has been set and recognize which key it is, without the full key ever being returned to the client."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Confirm at a glance whether a provider has a key set (Priority: P1)

An administrator viewing the AI Providers admin table wants to immediately see, per provider row, whether a credential has already been configured — without opening the "Set/Replace credential" dialog for each provider to find out.

**Why this priority**: This is the primary problem statement — the current UI only exposes this via a "hasCredential" boolean surfaced indirectly (menu label, enable/disable gating); there's no direct, at-a-glance visual confirmation in the table itself.

**Independent Test**: Load the AI Providers admin table with a mix of providers (some with a credential configured, some without) and confirm each row's dedicated Credential Hint column (positioned just before the Actions column, which stays last) clearly distinguishes "has a key" from "has no key."

**Acceptance Scenarios**:

1. **Given** a provider has a credential configured, **When** the administrator views the AI Providers table, **Then** the Credential Hint column for that row shows a shortened hint of the key.
2. **Given** a provider has no credential configured, **When** the administrator views the AI Providers table, **Then** the Credential Hint column for that row clearly indicates no key is set (not a blank cell that could be mistaken for a loading or error state).

---

### User Story 2 - Recognize which key is configured (Priority: P1)

An administrator managing multiple providers, each potentially with keys from different accounts or environments (e.g., a personal vs. an org OpenAI key), wants a recognizable hint of which specific key is currently active for a provider without needing to decrypt or re-enter it to check.

**Why this priority**: Equally central to the request — the hint's value is precisely in being a recognizable fingerprint (matching the vendor's own convention on OpenAI's and Anthropic's key-management pages), not just a boolean presence indicator.

**Independent Test**: Configure a known API key for a provider, confirm the displayed hint matches the vendor-style pattern (first 4 characters, three dots, last 4 characters) and is visually consistent with how OpenAI/Anthropic display their own key hints.

**Acceptance Scenarios**:

1. **Given** a provider's credential is `sk-ant-api03-C5f...ygAA` in full, **When** the administrator views the table, **Then** the hint shown is the first 4 characters, followed by `...`, followed by the last 4 characters of that same key.
2. **Given** the administrator replaces a provider's credential with a new key, **When** they view the table afterward, **Then** the displayed hint updates to reflect the new key, not the previous one.
3. **Given** the administrator clears a provider's credential, **When** they view the table afterward, **Then** the hint is removed and the row again shows the no-key-set state.

---

### Edge Cases

- What happens for a credential that was configured before this feature existed (no hint was computed at the time it was saved)? The hint must still be shown after this feature ships — it is backfilled once for existing credentials rather than requiring the administrator to re-enter every key (see Assumptions).
- What happens if a provider's API key is shorter than 8 characters (the minimum needed to show 4 + 4 distinct characters)? The full key must never be exposed; if the key is too short to split into two non-overlapping 4-character halves, fall back to fully masking it (e.g., `****`) rather than partially or fully revealing it.
- What happens while a credential is being set/replaced/cleared (in-flight mutation)? The table's Credential Hint column follows the same optimistic/refetch behavior already used for `hasCredential` elsewhere in this table today — no new loading state is introduced.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST compute a non-reversible, shortened hint of an API key — its first 4 characters, followed by `...`, followed by its last 4 characters — at the moment the key is set or replaced, from the plaintext value the administrator submits.
- **FR-002**: The system MUST persist this hint separately from the encrypted credential value, so it can be retrieved and displayed without decrypting the stored credential on every read.
- **FR-003**: The AI Providers admin table MUST have a dedicated Credential Hint column, positioned immediately before the Actions column (which remains the table's rightmost column), that displays for every provider row either the credential hint (if a credential is configured) or a clear "no key set" indicator (if not) — this column is always populated, never blank.
- **FR-004**: The credential hint MUST be shown in its own dedicated column, separate from the Provider column (which continues to show only the provider's display name).
- **FR-005**: Replacing a provider's credential MUST update its displayed hint to reflect the new key on the next table refresh.
- **FR-006**: Clearing a provider's credential MUST remove its displayed hint and revert that row to the "no key set" indicator.
- **FR-007**: The system MUST NOT return the full API key value to the client at any point through this feature — only the shortened hint (first 4 + `...` + last 4 characters) is ever transmitted.
- **FR-008**: If an API key is too short to yield two distinct, non-overlapping 4-character halves, the system MUST display a fully masked placeholder instead of the shortened hint for that credential.
- **FR-009**: Credentials that were configured before this feature was deployed MUST also display a hint after deployment, without requiring the administrator to re-enter them (see Assumptions for the backfill approach).
- **FR-010**: This behavior MUST be identical and consistently formatted across the credential hint for every provider (Anthropic, Google Gemini, OpenAI, OpenRouter, and any provider added later).

### Key Entities

- **AI Provider Credential**: The API key an administrator sets for a given AI provider. In addition to its existing encrypted value (never returned to the client), it now also has a **Credential Hint** — a short, non-reversible display value (first 4 + `...` + last 4 characters of the plaintext key, or a fully masked placeholder for short keys) that IS safe to return to and display in the client.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can determine, without opening any dialog, whether each of the four built-in providers has a credential configured, just by scanning the AI Providers table.
- **SC-002**: An administrator can distinguish which specific key is active for a given provider (e.g., to tell a personal key apart from an org key) using only the displayed hint, matching the recognizability of the vendor's own key-management UI.
- **SC-003**: At no point does the full API key value leave the server after this feature ships — network responses for the provider list contain only the shortened hint, never the complete key.
- **SC-004**: Existing credentials configured before this feature shipped show a correct hint without any administrator action.

## Assumptions

- The credential hint is displayed only in the AI Providers admin table's dedicated Credential Hint column (positioned before the Actions column, which stays last) — the "Set/Replace credential" dialog (spec 065) remains write-only/empty as already implemented; this feature does not change that dialog.
- **Backfill for pre-existing credentials (FR-009)**: computing hints for already-stored credentials requires decrypting them once. This uses the same decryption capability (`IAiCredentialProtector.Unprotect`) already relied on today to use those credentials for live provider calls (health checks, chat requests) — it is not a new decryption exposure, just a one-time pass to populate the new hint field, run during deployment (e.g., a startup/migration step).
- The hint is computed once, at write time (when a credential is set or replaced), and stored — it is not recomputed on every read, since that would require decrypting the credential on every table load.
- "No key set" is rendered as visible text (e.g., "Not set") rather than an icon-only indicator, for consistency with the table's existing text-based columns.
- No change to the "Set/Replace credential" dialog's behavior from spec 065 (it stays empty with placeholder text) — this feature is purely additive to the table.
