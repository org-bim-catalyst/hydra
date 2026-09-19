# Phase 0 Research: Password Recovery & Password Management

**Feature**: 058-password-recovery | **Date**: 2026-09-18

The spec left no `[NEEDS CLARIFICATION]` markers, so this phase resolves implementation
unknowns instead: how to represent a reset token, where session revocation lives, and how the
existing auth surface must change.

---

## Topic 1 — Reset token representation

**Decision**: A first-class `PasswordResetToken` domain entity persisted in SQL Server, storing
only a SHA-256 hash of a 256-bit random token, mirroring the existing `RefreshToken` entity and
reusing `ITokenService.Hash`.

**Rationale**:

- The spec's FR-006 requires a newly issued link to supersede the previous one. ASP.NET Identity's
  built-in `GeneratePasswordResetTokenAsync`/`ResetPasswordAsync` pair cannot do this: its
  `DataProtectorTokenProvider` tokens are stateless, so the server has no record of an outstanding
  token to supersede. Identity's tokens are also not single-use in their own right — they are only
  invalidated as a side effect of the security stamp changing.
- Identity's stateless tokens are encrypted with the Data Protection key ring. That key ring is
  persisted to `App_Data/keys` on disk ([DependencyInjection.cs:206](../../src/AskLucy.Infrastructure/DependencyInjection.cs#L206))
  after an earlier production incident where an ephemeral ring silently invalidated stored
  credentials on every restart. Anything wiping that directory during deploy would silently break
  every outstanding reset link, with a "token invalid" symptom that looks like user error.
- A stored, hashed token gives FR-005 (expiry, single use), FR-006 (supersession, invalidation by
  password change and email change), FR-015 (auditable issue/consume records) and FR-016
  (unusable if storage is read) directly and observably, and it is testable without a host.

**Alternatives considered**:

- *Identity's built-in reset tokens* — rejected above. Cheapest to write, but cannot satisfy
  FR-006 and couples account recovery to the Data Protection key ring's survival.
- *Identity tokens plus a DB "used" marker* — two sources of truth for one decision, with the key
  ring still on the critical path. Strictly worse than either pure option.
- *Storing the token in plain text* — direct FR-016 violation and contrary to the precedent
  `RefreshToken` already sets in this codebase.

---

## Topic 2 — Revoking sessions on password change

**Decision**: Extend `IRefreshTokenRepository` with
`ListActiveByUserAsync(userId)` and revoke through the existing `RefreshToken.Revoke()` domain
method in the command handler. Reset revokes all; change-while-signed-in revokes all except the
token family of the calling session.

**Rationale**: `RefreshToken` already models revocation and already keeps revoked rows for audit.
Sessions are refresh-token families, so "sign out my other devices" is exactly "revoke every
active refresh token except this family". The access token remains valid until it expires; that
window is bounded by the existing short access-token lifetime and is the same trade-off already
accepted for logout, so this feature does not introduce a new revocation mechanism for it.

**Alternatives considered**:

- *Bump the Identity security stamp and validate it per request* — a correct and stronger design,
  but it means adding stamp validation to the JWT pipeline for every request on every endpoint.
  That is a platform-wide change well beyond this feature, and belongs in its own spec if the
  access-token window is ever judged unacceptable. Noted in the plan's Complexity Tracking.
- *Delete refresh token rows* — loses audit history the entity deliberately retains.

---

## Topic 3 — Account enumeration and response shape

**Decision**: `POST /auth/password/forgot` always returns `202 Accepted` with an empty body, for
every input, including malformed-but-syntactically-valid addresses, unconfirmed accounts, locked-out
accounts and rate-limited callers. The rate limiter for this endpoint returns 202 as well rather
than 429 for the per-email partition; only the per-IP partition returns 429.

**Rationale**: FR-003 requires responses to be indistinguishable. A 429 keyed on the email address
is itself an oracle — it proves the address was requested before, and combined with a 202 it leaks
which addresses are being targeted. Keying the visible 429 to the caller's IP preserves abuse
protection without tying the signal to an account. Email dispatch is enqueued as a background job
and the 202 is written without waiting for it, so response time is independent of whether an
account exists and of SMTP latency. Dispatch failure is logged by the job, with retries, and is
never surfaced to the caller (FR-003 wins over showing the user an SMTP error) — consistent with
"no silent failures" meaning captured and diagnosable, not necessarily exposed.

**Alternatives considered**:

- *Return 404 for unknown addresses* — trivially enumerable, rejected outright.
- *Artificial constant-time delay* — real timing equalisation would require padding to the worst
  case (an SMTP round trip) on every request. Moving the send out of the request path, as decided
  above, achieves more even timing than a sleep ever could, using the Hangfire infrastructure the
  repository already reaches from Application through injected job abstractions.

---

## Topic 4 — Rate-limit policy

**Decision**: Add an `auth-endpoints` rate-limit policy in `Program.cs`, partitioned on client IP,
and apply it to the new password endpoints. Add a second application-level per-email throttle
(3 requests per 15 minutes) enforced by querying issued-token timestamps.

**Rationale**: `Program.cs` already defines 20-plus fixed-window policies by feature area, and none
covers `/auth/*` — a standing gap against constitution §6's "every public endpoint is rate-limited".
This feature closes it for the endpoints it adds. The per-email throttle cannot live in the ASP.NET
rate limiter without the enumeration leak from Topic 3, so it lives in the handler where it can
silently no-op.

**Alternatives considered**: reusing `admin-endpoints` (wrong partition key — it keys on identity,
and these callers are anonymous) or no policy at all (constitution violation).

---

## Topic 5 — Email delivery

**Decision**: Render reset and notification emails in-memory per request, following
`RegisterCommandHandler`'s established pattern, with links built from
`AppOptions.FrontendBaseUrl`. Dispatch through the existing `IEmailSender`.

**Rationale**: The pattern, its HTML-encoding discipline and its rationale (never mutate a shared
template file) are already set by the register and change-email flows. Introducing a templating
engine for two more emails would violate §III (Simplicity First) for no gain.

**Alternatives considered**: a Razor-based template service — worth doing when the product needs
localised or designer-edited email, which is not this feature and is noted as future work.

### Topic 5a — Keeping the token out of the Hangfire job store

Moving dispatch off the request path (Topic 3) means the plaintext token travels to the job as an
argument, and Hangfire serialises job arguments into its SQL store. Left as-is, that writes a
usable reset link into the same database that Topic 1 deliberately keeps hash-only — a direct
FR-016 violation, and one that would have gone unnoticed until the Phase 7 leakage audit.

**Decision**: protect the token with `IDataProtector` before enqueueing and unprotect it inside the
job. Application receives a narrow protector abstraction so it stays free of Data Protection types.

**Rationale**: this reintroduces a Data Protection dependency, which Topic 1 rejected — but on a
different timescale. Topic 1's objection was that a lost key ring silently invalidates links for
their full one-hour life. Here the ciphertext lives only for the seconds between enqueue and
dispatch, and a key-ring loss in that window fails the job loudly rather than mailing a dead link.
A bounded, observable failure is a materially different risk from a silent hour-long one.

**Alternatives considered**: plaintext in job arguments (straight FR-016 violation); reverting to
an inline send (gives back the timing oracle Topic 3 exists to close); a second single-use lookup
key stored hashed (a third secret to manage, protecting something already protected).

---

## Topic 6 — Change-password hardening scope

**Decision**: Keep the existing `ChangePasswordCommand` and its endpoint; extend the handler with
session revocation and the notification email, and replace the Settings form with one that has a
confirmation field, policy feedback and a "set password" mode for accounts with no password
(`IIdentityService.HasPasswordAsync` already exists and is already used by the unlink flow).

**Rationale**: The flow works today; the spec's US3 is hardening, not construction. Rewriting it
would churn passing tests for no user-visible gain.

**Alternatives considered**: folding change-password into the reset pipeline — rejected, because
the two have genuinely different authorisation models (a verified current password versus a mailed
token) and merging them would put an anonymous code path one boolean away from an authenticated one.
