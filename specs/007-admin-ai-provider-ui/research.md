# Research: Admin AI Provider Configuration UI

No `NEEDS CLARIFICATION` markers were left in Technical Context — this feature reuses an
already-implemented backend (`005-multi-provider-ai-engine`) and already-established
frontend patterns within the same codebase, so "research" here is confirming and citing
those existing patterns rather than evaluating unfamiliar technology.

## Decision 1: List/detail page shape

**Decision**: Build `AdminAiProvidersPage.tsx` as an MUI `Table` (one row per provider)
with a per-row actions menu, directly mirroring `AdminUsersPage.tsx`
(`src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminUsersPage.tsx`).

**Rationale**: Constitution §7 requires new UI to compose from the existing design system
and shared patterns before writing anything bespoke. The provider list is small (4 seeded
providers today) and admin-curated, so none of `AdminUsersPage`'s pagination/search/sort
machinery is needed — just the table + row-actions shape.

**Alternatives considered**:
- A card-grid layout — rejected: no precedent in this admin area, and a table is a better
  fit for scanning enabled/health-status columns at a glance (User Story 2).
- A multi-step "add provider" wizard — rejected: providers are a fixed, pre-seeded catalog
  (spec 005), not something an admin creates; there is nothing to wizard through beyond
  "configure a credential and flip a switch."

## Decision 2: Confirmation dialog for every state-changing action

**Decision**: One `AiProviderActionsMenu.tsx` component per row, using a single
`pendingAction` state plus a `CONFIRM_COPY` lookup table for dialog title/body — directly
mirroring `UserActionMenu.tsx`
(`src/AskLucy.Web/ClientApp/src/features/admin/components/UserActionMenu.tsx`)'s existing
lock/force-2FA/delete confirmation pattern, extended to cover all four actions this
feature needs (enable, disable, set credential, clear credential) per the spec's
clarified FR-010.

**Rationale**: This exact confirm-before-apply idiom already exists, is already tested,
and already satisfies the "no accidental destructive action" concern this spec raises.
Introducing a second, different confirmation mechanism in the same admin area would
violate Convention Over Configuration (§2.VII) for no benefit.

**Alternatives considered**:
- Inline toggle switches with an "undo" snackbar after the fact — rejected: FR-010
  requires the administrator to confirm *before* the action takes effect, not be offered
  an undo window afterward.
- A single shared "are you sure?" modal reused verbatim from `UserActionMenu` via a shared
  component extraction — considered, but deferred: `UserActionMenu`'s version is coupled
  to user-management copy/actions. Per Simplicity/YAGNI (§2.III), a second, very similar
  local component is preferred over a premature shared abstraction until a third
  similar use case appears.

## Decision 3: Credential input never lives longer than it has to

**Decision**: The "set/replace credential" dialog holds the typed value in a single
component-local `useState<string>`, sent directly as the body of `PUT
/api/v1/admin/ai/providers/{id}/credential`, and is cleared/unmounted on dialog close
(success or cancel) — masked via `TextField type="password"`, matching `SecurityTab`'s
existing password-field convention in Settings
(`src/AskLucy.Web/ClientApp/src/features/settings/pages/SettingsPage.tsx`).

**Rationale**: SC-002 requires the credential is never shown again after submission, and
constitution §8 treats secrets as needing minimal-lifetime handling even client-side. A
plain local `useState` scoped to the dialog's lifetime is the simplest mechanism that
satisfies this — no form library, no persisted draft.

**Alternatives considered**:
- React Hook Form (used elsewhere in Settings for multi-field forms) — rejected here: this
  is a single field with no cross-field validation, so RHF adds a dependency for no
  benefit (KISS, §2.III).

## Decision 4: No backend changes

**Decision**: Confirmed by direct inspection of `AdminAiProvidersController.cs` (and its
backing commands/queries) that `GET/PATCH /api/v1/admin/ai/providers` and
`PUT/DELETE /api/v1/admin/ai/providers/{id}/credential` already exist, already return the
exact shape this UI needs (`AdminAiProviderDto`: `id, providerKey, displayName, isEnabled,
hasCredential, credentialLastRotatedAtUtc, defaultModelId, healthStatus,
healthStatusCheckedAtUtc`), and already enforce
`[Authorize(Policy = "AdministratorOrSuperUser")]` plus the `admin-endpoints` rate-limit
policy. This feature adds zero backend code.

**Rationale**: Spec 007's own Assumptions section states this explicitly; this decision
record exists to make the verification traceable rather than asserted.

**Alternatives considered**: N/A — this is a factual confirmation, not a choice among
options.

## Decision 5: Health-status presentation

**Decision**: Render `healthStatus` as an MUI `Chip` with the same `success`/`error`/
`default` color mapping `AdminUsersPage.tsx` already uses for its `isLockedOut`/
`twoFactorEnabled` status chips (`Healthy` → success, `Unhealthy` → error, `Unknown` →
default), plus the `healthStatusCheckedAtUtc` timestamp rendered as localized date/time
text next to it (same `toLocaleDateString()`-style formatting `AdminUsersPage` uses for
`createdAtUtc`).

**Rationale**: Visual consistency with the sibling admin page; satisfies User Story 2's
"health status at a glance" without inventing new status-color semantics.

**Alternatives considered**: A traffic-light icon — rejected, `Chip` already covers this
elsewhere and keeps the row visually consistent with the Users table.
