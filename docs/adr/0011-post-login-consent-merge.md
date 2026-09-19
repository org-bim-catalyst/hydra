# ADR 0011: Merging Anonymous Consent Into the Account on Login Instead of Re-Prompting

**Status:** Accepted

**Date:** 2026-09-19

**Feature:** [specs/004-cookie-consent-privacy](../../specs/004-cookie-consent-privacy/spec.md), [specs/023-flumeria-landing-experience](../../specs/023-flumeria-landing-experience/spec.md) (post-release follow-up)

## Context

Two independent cookie-consent stores exist, added in separate specs:

* **Authenticated** (`specs/004-cookie-consent-privacy`): `ConsentGate` blocks the app behind
  `CookieConsentBanner` until `GET /users/me/cookie-consent` shows a decision recorded for the
  current policy version, tied to the account.
* **Anonymous** (`specs/023-flumeria-landing-experience`): `PublicConsentGate` shows a
  non-blocking `PublicConsentBanner` on the landing/auth pages for signed-out visitors, since
  the authenticated endpoint 401s pre-login. It reads/writes a first-party
  `flumeria_public_consent` browser cookie instead.

These never interact. A brand-new user who accepts cookies on the landing page, then signs up
and logs in, hits `ConsentGate` with no account record — `RequiresReconsent: true` — and is
immediately re-blocked with the same three-button prompt they had just answered seconds
earlier. Every new user answers the same question twice: once anonymously (cosmetic, doesn't
gate anything), once for real right after their first login (blocking).

Checked against how large vendors handle this: Google, Microsoft, and Amazon all scope cookie
consent to the **browser/device**, not the account, and it survives login — signing in does not
re-trigger the banner if the browser already answered for the current policy version. Some
layer an account-level record on top for audit/cross-device sync, but login acts as a *merge
point* into that record, never a reset.

## Decision

`useLogin`, `useLoginTwoFactor`, and `useCompleteExternalLogin` (`useAuth.ts`) call
`migratePublicConsentToAccount()` in `onSuccess`, alongside the existing session refresh:

1. **Account already has a consent record** (any prior login on any device) → do nothing. The
   account's own decision always wins; this browser's anonymous cookie is never allowed to
   overwrite it.
2. **No account record yet, and this browser's `flumeria_public_consent` cookie is valid for
   the current policy version** → `PUT` it to `/users/me/cookie-consent`, seeding
   `ConsentGate`'s query cache with the result so the banner never flashes.
3. **Neither** → unchanged: `ConsentGate` prompts as before.

Migration is best-effort — a failed call is swallowed (`.catch(() => undefined)`) and
`ConsentGate` simply falls back to prompting. It must never fail the sign-in itself.

## Consequences

**New users answer the cookie prompt once, not twice**, matching the "ask once per browser,
never twice for the same policy version" pattern of Google/Microsoft/Amazon, while
`specs/004`'s account-tied, audit-trail model (FR-005, FR-016) is preserved — the migrated
decision becomes a real, timestamped `CookieConsentRecord` row like any other.

**One-directional merge, not a full sync.** The anonymous cookie is promoted to the account;
the account's decision is never pulled back down into the browser cookie. The public banner
only matters pre-login, so nothing consumes that direction.

**A stale/second-device browser cookie can never clobber a real decision.** Step 1's check is
unconditional on `hasConsented`, independent of policy version — even a `requiresReconsent`
account record blocks the migration, deferring to `ConsentGate`'s own re-consent flow rather
than letting a stale anonymous cookie silently answer for the user.

## Alternatives Considered

**Do nothing; the double-prompt is a deliberate two-step legal event.** Considered, since some
consent frameworks do treat anonymous browsing and account consent as separable. Rejected: it
matches no observed industry implementation and adds pure friction with no compliance benefit
— the account is the same natural person recording the same decision seconds apart.

**Bidirectional sync (also pull the account's decision down into the browser cookie).**
Rejected as unnecessary scope: the anonymous cookie's only purpose is bridging pre-login
visits into the first authenticated decision; once an account record exists, `ConsentGate`
already owns the authenticated experience and the anonymous cookie is never read again.

**Always overwrite the account record with the browser's cookie on every login.** Rejected —
would let a stale or default-rejected cookie on a shared/public/second device silently replace
a user's real, considered account-level decision. The "only if the account has none yet" gate
in Step 1 exists specifically to prevent this.

## Related

* [specs/023-flumeria-landing-experience/contracts/routing-and-consent-contract.md](../../specs/023-flumeria-landing-experience/contracts/routing-and-consent-contract.md) — "Post-login consent migration" section
* [specs/004-cookie-consent-privacy/spec.md](../../specs/004-cookie-consent-privacy/spec.md) — Post-Implementation Notes
* [docs/ARCHITECTURE.md §26 Consent & Privacy Engine](../ARCHITECTURE.md)
