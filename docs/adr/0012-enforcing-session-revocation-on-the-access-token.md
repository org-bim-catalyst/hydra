# ADR 0012: Enforcing Session Revocation on the Access Token via a Session Claim

**Status:** Accepted

**Date:** 2026-09-19

**Feature:** [specs/058-password-recovery](../../specs/058-password-recovery/spec.md) (post-release follow-up, FR-025/FR-026)

## Context

SPEC-058 committed, in SC-007, that "after any password change or reset, sessions on other devices
stop working on their next request." The implementation revoked the other refresh-token families
correctly and an automated multi-session test asserted it. Both were true, and the requirement was
still not met.

An access token is a self-contained JWT. Stateless validation checks its signature and its expiry
and nothing else. Revoking a refresh-token family stops the browser obtaining a *new* access token;
the one it already holds stays cryptographically valid until it expires. So "your other devices have
been signed out" was true of the refresh cookie and false of everything the existing bearer token
could still reach — for up to a full access-token lifetime.

The test missed it because it only ever called `/auth/refresh`, which reads the cookie. The bug
reached production and was reported from a two-browser walkthrough: the second browser kept
navigating and calling the API normally, and appeared signed out only after a page reload forced it
back through the session endpoint.

This is a general shape, not a quirk of password changes. Any revocation — sign-out, reuse
detection, a future "sign out all devices" — lands a whole token lifetime late unless something
*checks* it per request.

## Decision

Stamp every access token with a `sid` claim naming the refresh-token family it belongs to, and
refuse a token whose family is no longer active from `JwtBearerEvents.OnTokenValidated`.

* **`sid`, not a new bespoke claim name.** It is a registered JWT claim with exactly these
  semantics, and `JwtSecurityTokenHandler` maps it to `ClaimTypes.Sid` on the way in.
* **`OnTokenValidated` + `context.Fail(...)`, not `IClaimsTransformation`.** The repo already uses
  `IClaimsTransformation` (`CurrentAuthorizationClaimsTransformation`) to apply role changes live.
  It cannot serve here: it can alter a principal's claims but has no way to refuse the request.
* **A 30-second `IMemoryCache` plus immediate eviction**, mirroring the existing
  `CurrentAuthorizationClaimsTransformation` / `MemoryCacheAuthorizationCacheInvalidator` pair. The
  eviction is what satisfies "on its next request"; the expiry is only a safety net. Eviction runs
  *after* `SaveChangesAsync`, so the cache is never cleared for a revocation that then fails to
  commit.
* **Fail open on a missing `sid`, closed on an unparseable one.** Tokens minted before this existed
  carry no claim, and refusing them would sign every active user out on deploy; that window closes
  by itself within one access-token lifetime, and the case is logged rather than silently tolerated.
  A malformed value is a different matter — we cannot check what we cannot parse, and the client
  still holds a valid refresh cookie, so it simply gets a well-formed token.

## Alternatives considered

**ASP.NET Identity's `SecurityStamp`.** The obvious candidate: Identity already bumps it on a
password change, and a stamp claim compared per request is a well-trodden pattern.

Rejected because the stamp is not session-scoped and not password-scoped. Identity also bumps it on
**role changes**, which would sign a user out the moment an administrator adjusted their roles —
directly contradicting `CurrentAuthorizationClaimsTransformation`, which exists precisely so role
changes apply live *without* invalidating anyone's token. It also cannot express FR-010: a stamp
identifies the account, so it cannot keep the session that made the change alive while ending the
others.

**Short-lived access tokens (1–2 minutes).** Shrinks the window without a per-request check, but
trades it for a refresh round-trip every minute or two for every user, and still leaves a window.
It narrows the bug rather than fixing it.

**A denylist of revoked token ids (`jti`).** Equivalent in effect, but requires new storage with its
own retention policy and a write on every revocation. The token-family id is already stored,
already indexed, and already has exactly the lifetime we need.

## Consequences

* Revoking a session — by any path — now takes effect on the next request that carries the token.
  Four call sites evict: password change (skipping the acting family), password reset, sign-out, and
  the reuse-detection branch of refresh. Normal rotation does not, because it keeps the family alive.
* One extra `IMemoryCache` read on authenticated requests, and a single indexed `EXISTS` at most
  once per session per 30 seconds.
* **Any new way to end a session must evict alongside its database write**, or it inherits the
  original bug.
* The eviction cache is per-instance. On a multi-instance deployment only the instance that handled
  the revocation evicts and the rest fall back to the 30-second expiry. The pre-existing authorization
  invalidator has the identical limitation and the current host is single-instance; a distributed
  cache is required before scaling out.
* No migration and no contract change: the claim is minted per request, and the lookup uses the
  existing `TokenFamilyId` index.
