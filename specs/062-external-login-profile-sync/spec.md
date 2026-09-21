# Feature Specification: External Login Profile Sync

**Feature Branch**: `062-external-login-profile-sync`

**Created**: 2026-09-21

**Status**: Draft

**Input**: User description: "Google and Facebook both send name and profile-picture claims on every sign-in (not just at original signup), provided the right scopes/fields are requested and mapped. Persistence of those claims onto the user's profile should refresh on every login rather than only on first account creation, so existing users self-heal without a backfill job."

## Clarifications

### Session 2026-09-21

- Q: Should profile sync also trigger when a user links an additional social provider to an already-authenticated account, or only on a full sign-in? → A: Sync applies on both flows — every full sign-in AND every "link an additional provider" action refresh name/picture.
- Q: Should the profile-picture download-and-store happen synchronously as part of completing sign-in, or asynchronously after sign-in completes? → A: Picture sync is asynchronous (fire-and-forget after sign-in/linking completes); name/last-name sync stays synchronous.
- Q: When the provider's picture claim is unchanged from what's already stored, should the system still re-fetch and re-store it, or skip when unchanged? → A: Always re-fetch and re-store the picture on every sign-in, even if unchanged from what's stored.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - New user signs up via Google or Facebook (Priority: P1)

A person who has never used Ask Lucy signs in for the first time with their Google or Facebook account. Their account is created with their first name, last name, and profile picture already filled in, instead of landing in the workspace with a blank profile.

**Why this priority**: This is the highest-value, most common path — first impressions of a new account matter, and every new social sign-up benefits immediately without any additional backend work per user.

**Independent Test**: Create a brand-new account by signing in with a Google (or Facebook) account that has a name and profile photo. Verify the resulting Ask Lucy profile shows that first name, last name, and picture without the user having to enter them manually.

**Acceptance Scenarios**:

1. **Given** no Ask Lucy account exists for a given email, **When** the person completes sign-in via Google, **Then** the newly created account's first name, last name, and profile picture are populated from the Google claims.
2. **Given** no Ask Lucy account exists for a given email, **When** the person completes sign-in via Facebook, **Then** the newly created account's first name, last name, and profile picture are populated from the Facebook claims.
3. **Given** the external provider does not supply one or more of these fields (e.g., no picture set on the provider account), **When** the account is created, **Then** the account is created successfully and the missing field(s) are simply left blank rather than blocking sign-in.

---

### User Story 2 - Existing user's profile self-heals on their next social sign-in (Priority: P2)

Someone who created their Ask Lucy account via Google or Facebook before this capability existed signs in again. Without any manual backfill step, their profile now shows their name and picture, because every sign-in — not just the first — refreshes these fields from the provider.

**Why this priority**: Delivers value to the entire existing user base retroactively, but is secondary to P1 because it depends on the same underlying claim-mapping and persistence logic; it only adds "refresh on every login" behavior on top of it.

**Independent Test**: Take an existing account (created before this feature) that is missing first name, last name, or picture, sign in again via the linked provider, and verify the profile fields are now populated — with no separate migration or admin action performed.

**Acceptance Scenarios**:

1. **Given** an existing account with a linked Google or Facebook login and a blank first/last name and picture, **When** the user signs in again, **Then** those fields are populated from the current provider claims after that sign-in completes.
2. **Given** an existing account whose first/last name or picture was already populated (e.g., the user manually edited them in Ask Lucy), **When** the user signs in again via the linked provider, **Then** the provider's current values overwrite the existing ones (provider is always the source of truth for these fields when synced).
3. **Given** an existing account signs in via the provider but the provider claims for name/picture are unavailable in that specific response, **When** sign-in completes, **Then** the previously stored values are left unchanged rather than being cleared out.

---

### User Story 3 - User's provider-side name or picture changes over time (Priority: P3)

A user updates their name or profile photo on Google or Facebook. The next time they sign in to Ask Lucy, their Ask Lucy profile picks up the change automatically.

**Why this priority**: A natural consequence of "sync on every login" (User Story 2) rather than a separate mechanism — called out on its own because it's a distinct, testable behavior (ongoing sync vs. one-time backfill) even though it shares the same implementation.

**Independent Test**: Change the display name or profile photo on a linked Google/Facebook account, sign in to Ask Lucy again, and verify the Ask Lucy profile reflects the updated value.

**Acceptance Scenarios**:

1. **Given** a user changes their name on the linked external provider, **When** they next sign in to Ask Lucy, **Then** their Ask Lucy first/last name is updated to match.
2. **Given** a user changes their profile photo on the linked external provider, **When** they next sign in to Ask Lucy, **Then** their Ask Lucy profile picture is updated to match.

---

### Edge Cases

- What happens when a user manually uploads their own avatar in Ask Lucy and then signs in again via a linked social provider? The provider's picture overwrites the manually uploaded one on next sync (provider claims always win per Acceptance Scenario 2 of User Story 2) — users who want a custom avatar are expected to keep it current on the provider side, or the account should not rely on a linked social provider for the picture.
- What happens when the same person is linked to both Google and Facebook and the two providers disagree on name/picture? Whichever provider is used to complete that specific sign-in is the one that updates the profile for that session; there is no reconciliation between multiple linked providers.
- What happens if fetching the profile picture from the provider's URL fails (network error, expired signed URL) during sync? Because picture sync runs after sign-in already completed (see Clarifications), the sign-in itself is entirely unaffected; the picture update is skipped for that sign-in and the previously stored picture (if any) is retained, consistent with the "no silent failures" principle — the failure is logged, not surfaced to the user as an error (their session is already active by the time it would occur).
- What happens if the provider's picture is an inappropriate size or format? Existing image validation/sanitization applied to manually uploaded avatars applies equally to provider-sourced pictures.
- What happens for a user who links a second social provider to an already-complete profile? The linking action itself (not just a subsequent sign-in) refreshes the profile from the newly linked provider's claims, following the same "provider claims overwrite" rule as any other social sign-in (see Clarifications).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST request the necessary permissions from Google and Facebook during sign-in to receive the user's first name, last name, and profile picture, in addition to what is already requested.
- **FR-002**: When a new account is created via Google or Facebook sign-in, the system MUST populate the account's first name and last name from that sign-in's claims before sign-in completes, and MUST populate the account's profile picture from that sign-in's claims shortly after sign-in completes, whenever the provider supplies them.
- **FR-003**: When an existing account signs in again via a linked Google or Facebook login, the system MUST refresh the account's first name and last name from that sign-in's current claims before sign-in completes, and MUST refresh the account's profile picture from that sign-in's current claims shortly after sign-in completes, whenever the provider supplies them.
- **FR-003a**: When an already-authenticated user links an additional Google or Facebook login to their account, the system MUST refresh the account's first name, last name, and profile picture from that linking action's claims, using the same rules as a full sign-in (FR-002 through FR-006).
- **FR-003b**: The system MUST NOT delay completion of sign-in or of a linking action while the profile picture is being fetched or stored — picture sync happens after the user is already signed in / the link is already established, so a slow or failing picture fetch never adds to that wait.
- **FR-004**: The system MUST leave a profile field (first name, last name, or picture) unchanged when the provider does not supply a value for that field on a given sign-in, rather than clearing it out.
- **FR-005**: The system MUST NOT block or fail a sign-in because a name or picture claim is missing, malformed, or fails to be retrieved.
- **FR-006**: The system MUST treat the external provider's picture as the source of truth for that field on every sign-in that supplies one, overwriting any previously stored picture — including one the user uploaded manually within Ask Lucy — and unconditionally re-fetches and re-stores it on every such sign-in, without comparing it to the previously stored value first.
- **FR-007**: The system MUST store the synced profile picture using the same storage and delivery mechanism as manually uploaded avatars (no raw external image URLs persisted or exposed to the client).
- **FR-008**: The system MUST log a diagnosable failure (without surfacing a sign-in error to the user) whenever a name or picture claim cannot be retrieved or persisted during sign-in.
- **FR-009**: This sync behavior MUST apply uniformly to accounts regardless of when they were originally created — no separate backfill or migration step is required for pre-existing accounts to benefit.

### Key Entities

- **User Profile**: The first name, last name, and profile picture associated with an Ask Lucy account; already exists today but is not currently populated from social sign-in.
- **External Login**: The link between an Ask Lucy account and a specific external provider identity (Google or Facebook); each sign-in through this link is the trigger point for the sync described here.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of brand-new accounts created via Google or Facebook sign-in have a first name, last name, and profile picture populated at account creation, whenever the provider account has those set.
- **SC-002**: Users with pre-existing social-login accounts see their name and picture appear in their Ask Lucy profile within one sign-in, with zero manual data entry or administrative action required.
- **SC-003**: A change to a user's name or picture on their linked social provider is reflected in their Ask Lucy profile the next time they sign in — name changes are visible immediately when sign-in completes, and the picture change follows within the same session shortly after, without adding to sign-in wait time.
- **SC-004**: Zero sign-in attempts fail or are delayed as a result of missing, malformed, or unretrievable name/picture data from the provider.

## Assumptions

- Users authenticate via Google or Facebook using the existing external sign-in flow; no new provider is introduced by this feature.
- "Profile picture" means the account's avatar as already surfaced elsewhere in the product; this feature only changes where its value comes from and when it is refreshed, not how it is displayed.
- A user manually editing their first/last name or uploading a custom avatar inside Ask Lucy is treated as a temporary override: it persists until the next sign-in via a linked social provider that supplies a differing value, at which point the provider's value takes over again (per FR-006 and the edge case above). No user-facing setting to "pin" a manual value against future syncs is in scope for this feature.
- This feature does not change behavior for accounts that sign in exclusively with a password (no linked external login) — those accounts are unaffected.

## Post-Implementation Notes

- **2026-09-21 (implementation complete)**: Backend implemented and validated —
  Domain 305/305, Application 1484/1484, Infrastructure 400/400, Web.Tests 496/496 pass.
  `IExternalProfilePictureSyncJob` and `IImageContentValidator` added per
  [ADR 0014](../../docs/adr/0014-external-profile-picture-fetch-hardening.md); no database
  migration was needed (research.md Decision 8). T001 (local OAuth credential setup) and T032
  (manual quickstart.md walkthrough with real Google/Facebook accounts) remain the user's own
  hands-on verification steps and are intentionally not automatable.
- **2026-09-21 (local test environment gotcha, not a feature defect)**: A local `dotnet test`
  run against `tests/AskLucy.Web.Tests` without `PERSISTENCE_TESTS_CONNECTION_STRING` set
  produces ~70 unrelated-looking failures (403 checks returning 500, `Invalid object name
  'PasswordResetTokens'`, `/health/ready` 503). Root cause: the suite's LocalDB fallback is
  permanently stuck 26 migrations behind because LocalDB cannot create full-text catalogs
  (`Cannot use full-text search in user instance`), a hard SQL Server LocalDB limitation
  unrelated to this feature. Point `PERSISTENCE_TESTS_CONNECTION_STRING` at the real shared
  test database (same one CI uses) to get a clean local run.
