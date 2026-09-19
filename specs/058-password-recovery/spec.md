# Feature Specification: Password Recovery & Password Management

**Feature Branch**: `058-password-recovery`

**Created**: 2026-09-18

**Status**: Draft

**Input**: User description: "Forgot Password, Change Password, and Reset Password functionality"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Request a password reset link (Priority: P1)

A user who cannot sign in because they forgot their password enters their email address on a
"Forgot password" screen reachable from the sign-in screen. They receive an email containing a
time-limited link that lets them choose a new password. The screen shows the same confirmation
message whether or not an account exists for that address, so the page cannot be used to discover
who has an account.

**Why this priority**: Without this, a user who forgets their password is permanently locked out and
must contact support. It is the only currently unrecoverable state in the sign-in journey.

**Independent Test**: Submit a known email on the forgot-password screen, confirm the neutral
confirmation message appears and a reset email arrives; submit an unknown email and confirm the
identical message appears and no email is sent.

**Acceptance Scenarios**:

1. **Given** a registered account with a confirmed email, **When** the user submits that address on
   the forgot-password screen, **Then** a reset email is sent and a neutral confirmation is shown.
2. **Given** no account exists for the submitted address, **When** the user submits it, **Then** no
   email is sent and the same neutral confirmation is shown.
3. **Given** the user submits the same address repeatedly, **When** the request rate exceeds the
   allowed threshold, **Then** further requests are rejected without sending additional emails, and
   the user is told to try again later.
4. **Given** an account that has never confirmed its email address, **When** a reset is requested,
   **Then** no reset email is sent and the neutral confirmation is still shown.

---

### User Story 2 - Complete a password reset from the emailed link (Priority: P1)

The user opens the emailed link, lands on a reset screen, enters a new password twice, and on
success is told the password was changed and is directed to sign in.

**Why this priority**: Story 1 delivers no value on its own — the pair is the smallest slice that
actually recovers an account. Kept separate because the link-consumption half is independently
testable and carries the security-critical token rules.

**Independent Test**: With a valid reset link, set a new password and confirm sign-in succeeds with
the new password and fails with the old one; then reuse the same link and confirm it is rejected.

**Acceptance Scenarios**:

1. **Given** a valid, unexpired, unused reset link, **When** the user submits a policy-compliant new
   password, **Then** the password is updated and the user can sign in with it.
2. **Given** a reset link that has already been used, **When** the user submits the form, **Then**
   the request is rejected with a message explaining the link is no longer valid and offering to
   request a new one.
3. **Given** a reset link older than its validity window, **When** the user opens it, **Then** the
   same expired-link message and re-request affordance are shown.
4. **Given** a reset link, **When** the user submits a password that violates the password policy,
   **Then** the specific policy failure is shown inline and the link remains usable.
5. **Given** a successful reset, **When** the reset completes, **Then** all of that account's
   existing sessions are signed out and the user is notified by email that their password changed.
6. **Given** an account with two-factor authentication enabled, **When** the user completes a reset,
   **Then** the next sign-in still requires the second factor.

---

### User Story 3 - Change password while signed in (Priority: P2)

A signed-in user changes their password from account settings by entering their current password and
a new one. They stay signed in on the device they used, and their other sessions are ended.

**Why this priority**: A working change-password path already exists in the product; this story
raises it to the same standard as the reset path (confirmation field, policy feedback, session
revocation, notification email) rather than creating it from nothing.

**Independent Test**: From settings, change the password with the correct current password and
confirm the new one works; repeat with a wrong current password and confirm a clear inline error.

**Acceptance Scenarios**:

1. **Given** a signed-in user with a password, **When** they submit the correct current password and
   a policy-compliant new password, **Then** the password is updated, the current session stays
   valid, and other sessions are signed out.
2. **Given** a signed-in user, **When** they submit an incorrect current password, **Then** the
   change is rejected with an inline error that does not reveal anything about the stored password.
3. **Given** a signed-in user, **When** the new password does not meet policy or does not match its
   confirmation field, **Then** the specific failure is shown inline before any request is made.
4. **Given** a successful change, **When** it completes, **Then** the user is notified by email.

---

### User Story 4 - Set a first password for an external-only account (Priority: P3)

A user who created their account through Google, Microsoft, Facebook, or GitHub and has no password
can set one — so they gain an email-and-password fallback and can unlink a provider.

**Why this priority**: Valuable and closely related, but these users are not locked out today; they
can still sign in with their provider.

**Independent Test**: With an account that has no password, request a reset for its address, complete
the flow, and confirm email-and-password sign-in now works alongside the provider sign-in.

**Acceptance Scenarios**:

1. **Given** an account with external logins and no password, **When** the user requests a password
   reset for its confirmed address, **Then** the flow proceeds normally and sets a first password.
2. **Given** an account with no password, **When** the user opens account settings, **Then** the
   password section offers to set a password rather than asking for a current one.

---

### Edge Cases

- A reset link is opened after the account's email address has been changed → the link is rejected.
- A reset link is opened after the user has already changed their password another way → rejected.
- Two reset links are outstanding at once → only the most recently issued link is accepted.
- The reset link is tampered with, truncated by an email client, or missing its identifier → a clear
  invalid-link message, never a raw error page.
- The email provider is unavailable when a reset is requested → the failure is recorded for
  operators and the user still sees the neutral confirmation, so no account existence is leaked.
- A locked-out or disabled account requests a reset → no email is sent; completing a reset never
  re-enables a disabled account.
- The user submits the reset form twice quickly (double-click) → exactly one password change occurs
  and the second submission reports the link as already used rather than an unexplained error.
- The new password is identical to the current one → accepted on reset, rejected with a clear
  message on change-while-signed-in.
- A signed-in user changes their password in one browser tab while another tab holds a stale session
  → the stale tab is signed out on its next request rather than failing silently.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The sign-in screen MUST offer a clearly labelled path to the forgot-password screen.
- **FR-002**: The system MUST accept an email address on the forgot-password screen and, when it
  matches an account with a confirmed email that is neither locked out nor disabled, send that
  address a password reset message containing a single-use, time-limited link.
- **FR-003**: The forgot-password response MUST be identical in wording, and indistinguishable in
  timing to a degree that defeats casual probing, whether or not an account exists — no response
  may reveal account existence, confirmation state, or lockout state.
- **FR-004**: The system MUST rate-limit reset requests per email address and per client origin, and
  MUST tell a throttled user to try again later without sending further email.
- **FR-005**: A reset link MUST expire after a fixed validity window and MUST become unusable after
  one successful use.
- **FR-006**: A reset link MUST be invalidated by any subsequent password change, by a newly issued
  reset link for the same account, and by a change to the account's email address.
- **FR-007**: The system MUST validate a new password against the same password policy used at
  registration, and MUST report which specific rule failed.
- **FR-008**: Both the reset screen and the change-password form MUST require the new password to be
  entered twice and MUST block submission when the two entries differ.
- **FR-009**: On a successful reset, the system MUST invalidate every existing session for that
  account.
- **FR-010**: On a successful change-while-signed-in, the system MUST invalidate every session for
  that account except the one that performed the change.
- **FR-011**: The system MUST email the account holder a notification after any successful password
  change or reset, stating when it happened and how to respond if it was not them.
- **FR-012**: Completing a reset MUST NOT bypass, disable, or reset two-factor authentication;
  two-factor requirements apply at the next sign-in unchanged.
- **FR-013**: Users MUST be able to change their password from account settings by supplying their
  current password, and the current password MUST be verified before any change is applied.
- **FR-014**: An account that has no password MUST be able to set one through the reset flow, and
  account settings MUST present a "set password" affordance instead of asking for a current
  password.
- **FR-015**: The system MUST record a security event for every reset request, reset completion,
  password change, failed current-password attempt, and throttled request — including account
  identifier, timestamp, and client origin.
- **FR-016**: Reset link material MUST NOT be stored in a form that would let anyone with read access
  to storage or logs reconstruct a usable link, and MUST NOT appear in application logs, analytics,
  or error reports.
- **FR-017**: Every failure in these flows — invalid link, expired link, policy violation, throttle,
  wrong current password, email dispatch failure — MUST surface to the user as visible feedback on
  the screen they are on; no failure may be console-logged only or silently swallowed.
- **FR-018**: All three screens MUST be keyboard-navigable, screen-reader labelled, and available in
  both light and dark themes, consistent with the existing sign-in screens.

### Amendment 2026-09-19 — findings from the manual walkthrough (T061)

Walking the quickstart scenarios by hand surfaced six defects that automated coverage had missed,
because each is about what the user can *read and do* rather than what the API returns. The
requirements below were added and implemented as a post-release follow-up (Phase 8 in
[tasks.md](./tasks.md)).

- **FR-019**: The reset screen MUST tell the user a link is no longer redeemable **on arrival**, and
  offer a "request a new link" affordance — never after they have chosen and confirmed a new
  password. Checking a link MUST NOT consume it.
- **FR-020**: A sign-in refused because the account is locked out MUST say so, and MUST offer a way
  to reach an administrator without exposing an administrator's address. A sign-in refused because
  the email is unconfirmed MUST say so, name inbox/junk/spam, and offer a new confirmation link. A
  wrong password and an unknown address MUST remain indistinguishable. See
  [ADR 0010](../../docs/adr/0010-naming-sign-in-refusals.md).
- **FR-021**: Lockout MUST take effect after 3 failed attempts, not 5 — permissible only because
  FR-020 now tells the locked-out user what happened and how to recover.
- **FR-022**: The password policy MUST be presented as a live checklist, one line per rule, each
  marked met/unmet as the user types, accompanied by a segmented strength indicator. A single
  run-on sentence is ambiguous: the user cannot tell whether it is a hint or an error.
- **FR-023**: The registration screen MUST link to the sign-in screen, mirroring the existing link
  in the other direction.
- **FR-024**: Every text input on the auth screens MUST meet contrast requirements in both themes,
  including the masked characters of a password field and text rendered on a fixed-light panel.
  Colour choices MUST be resolved against the active theme, never hard-coded for one of them.

### Amendment 2026-09-19b — revocation was recorded but not enforced

A second walkthrough signed the same account in on two browsers and changed the password from one
of them. The other browser went on working normally — menus, map style, ordinary API calls — and
only appeared signed out after a page reload. This is **SC-007 failing in production while its
automated test passed**: the test asked the session-refresh path, which reads the refresh cookie and
did correctly refuse, but never asked an ordinary authenticated call.

The underlying reason is that an access token is self-contained. Revoking a session stops the
browser obtaining a *new* access token; the one it already holds remains valid on its own signature
until it expires. "Signed out everywhere" was therefore true of the refresh cookie and false of
everything the existing token could still reach, for up to a full access-token lifetime.

- **FR-025**: An access token belonging to a revoked session MUST be refused on the next request
  that carries it, not merely at its next refresh. Every revocation path — password change, password
  reset, sign-out, and refresh-token reuse detection — MUST take effect on this basis.
- **FR-026**: An access token MUST identify the session it was issued for, so that FR-025 can be
  decided from the token itself. The session a request is acting under MUST survive its own password
  change (FR-010), so the identifier MUST distinguish sessions, not merely accounts.

SC-007 is amended accordingly: "stop working on their next request" MUST be verified by an ordinary
authenticated API call, not only by a session refresh. See
[ADR 0012](../../docs/adr/0012-enforcing-session-revocation-on-the-access-token.md).

### Key Entities *(include if data involved)*

- **Password Reset Request**: The issued right to set a new password for one account. Attributes:
  the account it belongs to, issue time, expiry time, used/unused state, and the origin that
  requested it. At most one is valid per account at a time.
- **User Account**: Existing entity. Relevant attributes are the credential itself, whether one is
  set at all, the confirmed email address, lockout/disabled state, and two-factor enrolment.
- **Session**: Existing entity representing a signed-in device. Revoked in bulk by FR-009/FR-010.
- **Security Event**: Existing audit record extended with the password-flow events in FR-015.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user who has forgotten their password can regain access — from sign-in screen to
  signed in with a new password — in under 3 minutes, without contacting support.
- **SC-002**: The reset email is queued for sending before the request completes, and the
  forgot-password screen responds in under 500 milliseconds at the 95th percentile — identically
  whether or not the submitted address has an account. End-to-end delivery within 60 seconds for
  95% of requests is an operational target monitored in production rather than a build gate,
  because delivery speed belongs to the mail provider.
- **SC-003**: 90% of users who start the forgot-password flow complete it on the first attempt
  without requesting a second link.
- **SC-004**: Password-lockout support requests drop to zero, because no path requires human help.
- **SC-005**: No response from the forgot-password screen allows an outside observer to determine
  whether a given email address has an account, verified by testing existing, non-existent,
  unconfirmed, and locked-out addresses.
- **SC-006**: A used or expired reset link never grants a password change, verified by automated
  tests covering reuse, expiry, supersession, and tampering.
- **SC-007**: After any password change or reset, sessions on other devices stop working on their
  next request, verified by an automated multi-session test.

## Assumptions

- The existing ASP.NET Identity account store, email sender, password policy, session/refresh-token
  model, and security-event log are reused; this feature adds no new identity provider.
- Reset links are valid for 1 hour — long enough for email delivery and a distracted user, short
  enough to limit exposure of a link sitting in an inbox.
- Reset links are delivered by email only. SMS, security questions, and support-operated resets are
  out of scope.
- Accounts whose email has never been confirmed cannot reset; they use the existing resend-
  confirmation path first. This prevents a reset from becoming an email-verification bypass.
- An administrator-initiated reset for another user is out of scope for this feature; it belongs
  with the existing user-administration work.
- The change-password flow keeps the acting session alive because signing the user out of the device
  they are actively using is a worse experience than the marginal security gain.
- Email templates follow the existing confirmation/change-email template style and localisation
  approach already in the product.
