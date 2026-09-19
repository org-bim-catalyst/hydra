# ADR 0010: Naming Sign-In Refusals Instead of Returning One Generic Error

**Status:** Accepted

**Date:** 2026-09-19

**Feature:** [specs/058-password-recovery](../../specs/058-password-recovery/spec.md) (post-release follow-up)

## Context

Until this change, `POST /auth/login` answered every refusal with the same `401 Unauthorized` and the
same message: *"Invalid username or password."* That is the textbook anti-enumeration answer, and
ADR 0009 and the `202 Accepted` on `password/forgot` apply the same reasoning to the anonymous
recovery flows.

Manual testing of the recovery journey showed what it costs in practice. Three distinct situations
produced one indistinguishable dead end:

1. **Locked out.** After repeated failed attempts, a user with the *correct* password was told their
   password was wrong. Nothing named the lockout, nothing said how long it lasted, and nothing
   offered a route out. The rational response is to keep retrying, which extends the lockout.
2. **Unconfirmed email.** A registered user who never clicked the confirmation link was told their
   credentials were invalid. The actual fix — open the confirmation email, or ask for a new one —
   was never mentioned anywhere in the UI.
3. **Wrong password / unknown account.** The genuine ambiguous case, and the only one the generic
   message is really for.

The lockout threshold was also under review. Five failed attempts is a compromise: low enough to
blunt online guessing, high enough that a user who mistypes a few times is not stranded by a message
they cannot interpret.

## Decision

`POST /auth/login` distinguishes the refusals that can only be reached by someone who already holds
the correct password, and stays silent about the one that cannot:

| Outcome | Status | Disclosed? |
|---|---|---|
| Wrong password, or no such account | `401` | No — one message for both |
| Email not confirmed | `403` | Yes, plus a "send me a new link" affordance |
| Account locked out | `423` | Yes, plus a contact-an-administrator form |

`AuthController.ToActionResult(AuthResult)` performs the mapping; the frontend switches on the
status code, not on message text.

Two supporting endpoints exist so each named refusal carries a way out rather than just a label:

* `POST /auth/confirm-email/resend` — re-issues a confirmation link.
* `POST /auth/account-support` — relays a message to the support mailbox. The destination address is
  server-side configuration and never appears in the contract, so the page can say "contact an
  administrator" without publishing an address for scraping.

Both are `[AllowAnonymous]`, both return a neutral `202 Accepted` on the same terms as
`password/forgot` — identical body and latency whether or not the address exists.

With a locked-out user now told what happened and given a route out, the lockout threshold was
tightened from **5 to 3** failed attempts (`Lockout.MaxFailedAccessAttempts`, 5-minute duration).

## Consequences

**The enumeration surface grows, but only behind a correct password.** `403` and `423` are
reachable only by a caller who has already authenticated the password factor. An attacker who can
provoke either one has the credential; learning that the account is additionally unconfirmed or
temporarily locked tells them nothing they could not discover by signing in successfully. The
anonymous flows — `password/forgot`, `confirm-email/resend`, `password/reset/validate` — remain
uniformly silent, because there the password is *not* a gate.

**One residual leak, accepted.** A `423` after a burst of wrong guesses confirms the address is
registered, since an unknown address can never lock out. The rate limiter on `auth-endpoints` bounds
how cheaply that can be harvested, and the alternative — silently locking a legitimate user out with
no explanation — was judged the worse outcome for a product whose users are not security
specialists.

**Tighter lockout, more support contacts.** Three attempts will lock out honest users more often
than five did. That is deliberate and is precisely why the `account-support` route exists; without
it the tightening would not have been acceptable.

**Message text is not a contract.** Clients must branch on status code. Changing a title must not
break a client.

## Alternatives Considered

**Keep the single generic `401`.** The status quo, and the safest answer on paper. Rejected because
the manual testing above showed it produces a dead end a user cannot reason their way out of — the
locked-out case in particular actively encourages the behaviour that prolongs the lock.

**Name the refusal only after a correct password, in a second step.** Functionally what we do; the
status code *is* that second step, since these outcomes are only produced once the password check
passes. A separate round trip would have added a state machine for no additional secrecy.

**Disclose on the anonymous flows too, for consistency.** Rejected outright. `password/forgot` and
`confirm-email/resend` accept an address and nothing else; disclosing there turns them into a
straightforward account-existence oracle for anyone on the internet.

## Related

* [ADR 0009 — An Owned Password Reset Token](0009-owned-password-reset-token.md)
* [docs/SECURITY.md §6 Password Policy](../SECURITY.md)
* [docs/API_GUIDELINES.md §13 — Sign-in failure disclosure](../API_GUIDELINES.md)
