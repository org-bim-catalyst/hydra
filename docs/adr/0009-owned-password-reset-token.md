# ADR 0009: An Owned Password Reset Token, Not ASP.NET Identity's

**Status:** Accepted

**Date:** 2026-09-19

**Feature:** [specs/058-password-recovery](../../specs/058-password-recovery/spec.md)

## Context

Ask Lucy had no account recovery. A user who forgot their password had no route back into their
account — the only unrecoverable state in the product. ASP.NET Identity ships the obvious answer:
`UserManager.GeneratePasswordResetTokenAsync` and `ResetPasswordAsync`, a stateless pair that needs
no schema, no migration and no repository.

Two requirements make that pair unusable here.

**FR-006 — a new link supersedes the old one.** Identity's `DataProtectorTokenProvider` tokens are
stateless: the server keeps no record that a token was ever issued, so there is nothing to
supersede. They are not single-use in their own right either; they stop working only as a side
effect of the security stamp changing, which is a blunt instrument that also invalidates unrelated
state and says nothing about *which* token was spent.

**Operational fragility.** Identity's tokens are encrypted with the Data Protection key ring. This
application persists that ring to `App_Data/keys` on disk, after a production incident where an
ephemeral ring silently invalidated every stored credential on each restart. Anything that wipes
that directory during a deploy would silently break every outstanding reset link for its full
one-hour life, presenting as "this link is invalid" — indistinguishable from user error.

## Decision

### 1. A `PasswordResetToken` domain entity, stored hashed

A first-class entity persisted in SQL Server, holding a SHA-256 hash of a 256-bit random token and
reusing `ITokenService.Hash` — deliberately mirroring the existing `RefreshToken`. The plaintext
exists only in the email that carries it, so reading the database yields nothing redeemable
(FR-016). Lookups are by hash; the entity's own `CanRedeemFor` decides redeemability.

This gives expiry and single use (FR-005), supersession and invalidation on password or email
change (FR-006), an auditable issue/consume record (FR-015), and it is testable without a host.

*Rejected:* Identity's built-in tokens — cheapest to write, cannot satisfy FR-006, and puts key-ring
survival on the account-recovery critical path.

*Rejected:* Identity tokens plus a database "used" marker — two sources of truth for one decision,
with the key ring still on the critical path. Strictly worse than either pure option.

*Rejected:* storing the token in plaintext — a direct FR-016 violation, and contrary to the
precedent `RefreshToken` already sets here.

### 2. Data Protection returns, but only between enqueue and dispatch

Issuing the token happens on a Hangfire worker (see §3), so the token becomes a job argument, and
Hangfire serialises job arguments into the same SQL database this ADR just committed to keeping
hash-only. Enqueuing plaintext would have reintroduced the exact leak §1 exists to prevent.

The token is therefore wrapped with `IDataProtector` before enqueuing and unwrapped inside the job.
Application sees only a narrow `IPasswordTokenProtector` abstraction, so it stays free of Data
Protection types.

This reintroduces the key-ring dependency §1 rejected — but on a different timescale. There, a lost
ring silently invalidated links for an hour. Here the ciphertext lives for the seconds between
enqueue and dispatch, and a ring loss in that window throws, failing the job visibly rather than
mailing a dead link. A bounded, observable failure is a materially different risk from a silent one.

### 3. The request thread does no account-dependent work

`POST /auth/forgot-password` enqueues and returns 202 with an identical body for every input. All
account-dependent work — eligibility read, throttle count, supersede sweep, save, send — happens on
the worker.

This is not tidiness. Every one of those steps costs a round trip against an address that has an
account and nothing against an address that does not; on a remote database the difference is over a
second, an enumeration oracle that a neutral response body does nothing to hide. Rate limits are
partitioned by IP, never by email address, for the same reason: a throttle keyed to an address is
itself an existence oracle. Redemption answers all six rejection causes — unknown, expired,
consumed, superseded, wrong user, tampered — with one indistinguishable 400; the specific cause goes
to the security log only.

### 4. Reset revokes every session; change spares the caller's own

Redeeming a link revokes all refresh tokens, because the person redeeming it may be recovering from
a compromise and cannot be assumed to control the other sessions. A signed-in password change
revokes every session *except* the acting one, identified by the token family behind the caller's
own httpOnly refresh cookie — read from the cookie, never the body, so a client cannot nominate
someone else's session to keep alive. An acting token that cannot be resolved gets no exemption.

### 5. Redemption never returns a session

The reset endpoint returns `204`, never tokens. The user signs in afresh, so an account with
two-factor enrolment still faces its second factor (FR-012); a reset that minted a session would
have been a 2FA bypass by design.

## Consequences

- One new table, one migration, and a daily retention sweep that deletes spent tokens after 90 days.
- Account recovery is independent of the Data Protection key ring surviving a deploy.
- Forgot-password responses are uniform in body *and* in latency, verified by an integration test
  rather than asserted.
- An external-provider-only account can now set a first password through the same flow, branching on
  `HasPasswordAsync` to Identity's `AddPasswordAsync` rather than its remove-then-add path (FR-014).

## Known gap, out of this feature's scope

While writing the two-factor coverage for redemption, a pre-existing defect surfaced: **sign-in
never challenges for a second factor.** `IdentityService.ValidateCredentialsAsync` validates with
`SignInManager.CheckPasswordSignInAsync`, which never reports `RequiresTwoFactor`, and
`ValidateTwoFactorCodeAsync` leans on `TwoFactorAuthenticatorSignInAsync`, which needs the
`TwoFactorUserId` cookie a JWT API never sets. A user who enables 2FA is not prompted for it. This
predates SPEC-058 and is untouched by it; it needs its own fix.
