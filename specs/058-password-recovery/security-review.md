# Security Review — SPEC-058 Password Recovery

**Constitution §16.6 review.** Date: 2026-09-19. Reviewer: implementing engineer.

Scope: the auth surface added or changed by this feature — `POST /auth/forgot-password`,
`POST /auth/reset-password`, `POST /auth/change-password`, `GET /auth/password/status`, the
`PasswordResetToken` entity, the issuance and email jobs, and the two new frontend pages plus the
Settings password section.

## 1. Auth surface

| Endpoint | Authorization | Rate limiting | Verdict |
|---|---|---|---|
| `POST /auth/forgot-password` | `[AllowAnonymous]` — must be, the caller cannot sign in | `auth-endpoints`, partitioned by IP | OK |
| `POST /auth/reset-password` | `[AllowAnonymous]` — the token is the credential | `auth-endpoints`, partitioned by IP | OK |
| `POST /auth/change-password` | `[Authorize]`, user id from the token's claims | `auth-endpoints` | OK |
| `GET /auth/password/status` | `[Authorize]`, answers only about the caller | inherits default | OK |

The user id for change-password comes from the access token's claims, never from the request body,
so one authenticated user cannot act on another's account. `password/status` returns a single
boolean about the caller alone — no account other than the caller's is observable through it.

**Finding:** none.

## 2. Token handling

- The plaintext reset token is 256 bits from `RandomNumberGenerator`, never a GUID or a timestamp
  derivative.
- Only its SHA-256 hash is persisted (`ITokenService.Hash`, the same primitive `RefreshToken` uses).
  Reading the `PasswordResetTokens` table yields nothing redeemable.
- Lookup is by hash; the entity's own `CanRedeemFor` decides redeemability, and it checks the email
  the token was issued to as well as expiry/consumption/supersession, so an address change between
  issue and redemption invalidates the link.
- Between enqueue and dispatch the token is `IDataProtector` ciphertext, because Hangfire serialises
  job arguments into the same database. An unprotect failure throws rather than mailing a dead link.
- **Log audit:** every `[LoggerMessage]` template across `PasswordResetIssuanceJob`,
  `ResetPasswordCommandHandler`, `ChangePasswordCommandHandler` and `PasswordEmailJob` was read.
  None interpolates a token; none interpolates an email address either. Identifiers are user ids.
- **Response audit:** no Problem Details body, validation message or success payload echoes a token.
- Enqueued-argument-is-ciphertext is asserted by
  `PasswordResetIssuanceJobTests.IssueAsync_ShouldEnqueueTheProtectedToken_NotThePlaintextOne`.

**Finding:** none.

## 3. Enumeration resistance

Defence is layered, and each layer was checked independently:

1. **Body** — `202` with an identical payload for all four account states (no account, healthy
   account, unconfirmed address, locked-out account).
2. **Latency** — every account-dependent operation happens on a Hangfire worker, so the request
   thread's work does not vary with whether the address has an account. Asserted, not assumed, by
   `ForgotPasswordEndpointTests`.
3. **Rate limiting** — partitioned by IP, never by email address. An email-partitioned limiter would
   itself be an existence oracle.
4. **Per-email throttle** — enforced silently inside the job (log only), never as a visible `429`.
5. **Redemption** — all six rejection causes (unknown, expired, consumed, superseded, wrong user,
   tampered) return one indistinguishable `400`. The cause is written to the security log only.
6. **Unconfirmed addresses** are refused a link, closing what would otherwise be an
   email-verification bypass: anyone who registered with someone else's address could take it over.

**Finding:** none.

## 4. Session revocation

- **Reset** revokes every refresh token. The person redeeming may be recovering from a compromise
  and cannot be assumed to control the other sessions.
- **Change** revokes every session except the acting one. The acting session is identified by the
  token family behind the caller's **httpOnly refresh cookie**, read from `Request.Cookies` and
  never from the request body — a client cannot nominate someone else's session to spare.
- An acting token that cannot be resolved gets **no** exemption; everything is revoked. Fail-safe,
  and asserted by `ChangePasswordCommandHandlerTests`.
- Redemption returns `204` and never a session, so an account with two-factor enrolment still faces
  its second factor on the next sign-in. A reset that minted a session would have been a 2FA bypass
  by design.
- A password change supersedes any outstanding reset links, so a link issued against the old
  password cannot still be redeemed from an inbox.

**Finding:** none.

## 5. Other

- Password policy is enforced by ASP.NET Identity in one place; the client restates only a length
  minimum and renders the server's per-rule messages rather than maintaining a second policy.
- A policy failure deliberately does **not** consume the reset link, so a weak first attempt does not
  cost the user their only route back in.
- Spent tokens are deleted after 90 days by the `password-reset-token-cleanup` recurring job, so the
  table does not become a permanent per-user record of every recovery attempt.

## 6. Pre-existing defect found, out of scope

**Sign-in never challenges for a second factor.** `IdentityService.ValidateCredentialsAsync`
validates with `SignInManager.CheckPasswordSignInAsync`, which never reports `RequiresTwoFactor`;
`ValidateTwoFactorCodeAsync` then leans on `TwoFactorAuthenticatorSignInAsync`, which needs the
`TwoFactorUserId` cookie a JWT API never sets. A user who enables 2FA is not prompted for it on
sign-in.

This predates SPEC-058 and is untouched by it — it surfaced while writing the two-factor coverage
for redemption (`ResetPasswordTwoFactorTests`, whose comment records the same finding). It needs its
own fix and is **not** resolved by this feature.

## Outcome

**Approved.** No findings against this feature's own surface. One pre-existing, unrelated
two-factor defect recorded above and in [ADR 0009](../../docs/adr/0009-owned-password-reset-token.md)
for separate remediation.
