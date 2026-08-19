# Contract: Public Route Table & Consent Behavior

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Topics 2, 6, 7)

This documents the frontend routing/consent "interface" this feature establishes — the contract other code (and tests) can rely on.

## Route table changes

| Route | Before this feature | After this feature |
|---|---|---|
| `/` | `<Navigate to="/chat" replace />` unconditionally (`router.tsx:102`) | `<PublicOnlyRoute><LandingPage /></PublicOnlyRoute>` — signed-out visitors see the landing page; signed-in visitors are redirected to `/chat` (FR-001, FR-015) |
| `/login` | Public, unwrapped, current visual style | Public, unwrapped (unchanged auth gating), restyled via extended `AuthLayout` (FR-007) |
| `/register` | Public, unwrapped, current visual style | Public, unwrapped (unchanged auth gating), restyled (FR-007) |
| `/confirm-email` | Public, unwrapped, current visual style | Public, unwrapped (unchanged auth gating), restyled (FR-007, post-clarification scope) |
| `/confirm-email-change` | Public, unwrapped, current visual style | Public, unwrapped (unchanged auth gating), restyled (FR-007, post-clarification scope) |
| `/auth/external-complete` | Public, unwrapped, current visual style | Public, unwrapped (unchanged auth gating), restyled (FR-007, post-clarification scope) |
| `/privacy` | Public, unwrapped | **Unchanged** — out of scope |
| `/chat` and other `ProtectedRoute`-wrapped routes | Behind `ProtectedRoute` | **Unchanged** — no layout/functionality change (FR-011); only `MinimalTopBar` gets the brand-transition addition |

No existing route path is removed, renamed, or has its authentication requirement changed. `PublicOnlyRoute` is new; `ProtectedRoute`/`AdminRoute` are untouched.

## `PublicOnlyRoute` contract

```text
Input: accessToken from authStore (same source ProtectedRoute already reads)
  accessToken present     → render <Navigate to="/chat" replace />
  accessToken absent       → render children (LandingPage)
```

Applies **only** to `/`, per FR-015's explicit scope. `/login`/`/register`/etc. are NOT wrapped in `PublicOnlyRoute` — an already-authenticated visitor who manually navigates to `/login` sees the (restyled) login page rather than being redirected, matching today's behavior and the spec's Assumptions (no scope creep beyond FR-015's named route).

## Consent gating contract

```text
Landing page (/) and restyled auth-flow pages:
  wrapped in <PublicConsentGate> (NEW)
    reads PublicConsentState from the flumeria_public_consent cookie (client-only, no API call)
    no decision yet → renders children AND <PublicConsentBanner> (blocking-optional: content is visible,
        banner is prominent but does not require dismissal before content is legible, mirroring
        the authenticated CookieConsentBanner's non-blocking-of-content pattern)
    decision present → renders children; banner shown only if functional/analytics/marketing
        differ from what the visitor most recently chose (mirrors requiresReconsent semantics)

Authenticated workspace (/chat and friends):
  UNCHANGED — still wrapped in the existing <ConsentGate>, still calls the authenticated,
  per-user /api/v1/cookie-consent/me endpoint. PublicConsentGate and ConsentGate never both
  wrap the same route.
```

## `useFunnelAnalytics()` contract

```text
recordCtaClick(ctaId: 'SignIn' | 'SignUp' | 'TryPlatform'): void
recordFunnelCompleted(funnelType: 'SignUp' | 'SignIn'): void

Both:
  1. Read current consent (PublicConsentState.analytics for anonymous callers; the authenticated
     consent status for calls that happen to fire post-login) — if analytics is not granted, return
     immediately, no network call (FR-021: "no such event MUST fire before consent is granted").
  2. Otherwise POST to /api/v1/analytics/funnel-events (see analytics-funnel-events-api.md),
     fire-and-forget: any failure is caught, warn-logged client-side, never surfaced to the user,
     never blocks the caller's own navigation action.
```

Callers: `LandingCtaBar` (Sign In / Sign Up / Try the Platform buttons) call `recordCtaClick`; `LoginPage` calls `recordFunnelCompleted('SignIn')` immediately before the redirect to `/chat` on successful authentication; `RegisterPage` calls `recordFunnelCompleted('SignUp')` immediately after a successful registration response, at the point the confirmation-pending state is shown — registration issues no session and triggers no redirect (spec.md FR-008, Clarifications), so this event fires without any navigation attached to it.
