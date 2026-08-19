# Quickstart: Validating the Flumeria Public Landing Experience

**Feature**: [spec.md](./spec.md) | **Contracts**: [contracts/](./contracts/)

Manual/scripted validation scenarios proving the feature works end-to-end. Assumes the standard local dev setup for this repo (backend `dotnet run` from `src/AskLucy.Web`, frontend `npm run dev` from `src/AskLucy.Web/ClientApp`) is already working prior to this feature.

## Prerequisites

- Backend running locally with the existing dev database/config (no new migration to apply for this feature — see data-model.md).
- Frontend dev server running (`npm run dev` in `src/AskLucy.Web/ClientApp`).
- A test account with no existing session (for the sign-up journey) and a second test account with valid credentials (for the sign-in journey).

## Scenario 1 — New visitor discovers Flumeria and signs up (US1, P1)

1. In a private/incognito browser window, navigate to the app's root URL.
2. **Expect**: the new landing page renders (not a redirect to `/login`) — page `<title>` reflects Flumeria, not the old static "Ask Lucy — AI Workspace" title (verifies FR-001, FR-022).
3. **Expect**: content sections cover what Flumeria is, the problems it solves, how AI-assisted urban design works, how Lucy participates, how GIS/maps/2D-3D/spatial analysis integrate, how AI-generated analysis is visualized, and the differentiation from conventional tools (FR-002).
4. Resize the viewport from a small phone width up through a wide desktop width. **Expect**: no horizontal scroll, no overlapping content, all three CTAs remain reachable at every width (FR-012, SC-004).
5. If no `flumeria_public_consent` cookie exists yet, **expect** the public consent banner to appear; accept the "Analytics" category.
6. Click "Create Account / Sign Up". **Expect**: navigation to the sign-up page, visually consistent with the landing page just left (FR-005, FR-007).
7. Open browser dev tools → Network tab; confirm a `POST /api/v1/analytics/funnel-events` request fired with `eventType: "CtaClicked"`, `ctaId: "SignUp"` (FR-021) — and that no such request fired before step 5's consent was granted.
8. Complete sign-up with a new account. **Expect**: a branded confirmation-pending state appears ("check your email to confirm your account") — no session is issued and no redirect occurs; this matches the platform's existing, unchanged email-confirmation requirement (FR-008).
8a. Repeat sign-up in a fresh session, throttling the network to offline/flaky right after submitting. **Expect**: a visible, clear error message — not a stuck spinner or blank state (FR-017, edge case: visitor mid-signup who loses network connectivity).
9. Confirm a second `POST /api/v1/analytics/funnel-events` fired with `eventType: "FunnelCompleted"`, `funnelType: "SignUp"`, immediately after step 8's response, with no navigation attached to it (contracts/routing-and-consent-contract.md).
10. Using the confirmation email's link (or the test account's pre-confirmed token), confirm the account, then sign in (Scenario 2). **Expect**: this is the point — not step 8 — where the visitor actually reaches `/chat`, and where the workspace header (`MinimalTopBar`) shows the new minimal Flumeria brand-transition element alongside the existing "Ask Lucy" identity, with no other layout change to the chat workspace (FR-011).

**Pass criteria**: all "Expect" assertions above hold; elapsed time from step 1 to step 8's confirmation-pending state is under 3 minutes (SC-001).

## Scenario 2 — Returning user signs in (US2, P2)

1. In a private/incognito browser window, navigate to the app's root URL, then click "Sign In".
2. Enter valid credentials for the second test account. **Expect**: redirect straight into `/chat` with prior data (e.g. existing chat history) intact (FR-004, FR-008).
3. Repeat with an intentionally wrong password. **Expect**: a visible, clear error message on the restyled sign-in page; entered email is preserved; no silent failure (FR-017).
4. If the test account has 2FA enabled, **expect** the existing 2FA challenge step to still function exactly as before (FR-009).
5. If the platform has a social login provider configured, sign in via that provider. **Expect**: the OAuth round-trip completes through the restyled `ExternalLoginCompletePage` and redirects into `/chat`, with no regression (FR-009).

**Pass criteria**: elapsed time from landing-page arrival to workspace arrival (valid-credentials path) is under 30 seconds (SC-002).

## Scenario 3 — Already-authenticated visitor hits public URLs (US3, edge cases)

1. While signed in (from Scenario 1 or 2), navigate directly to the app's root URL.
2. **Expect**: immediate redirect to `/chat` — the marketing landing page is never shown (FR-015).
3. While signed in, click a "Try the Platform"-equivalent link/bookmark if available, or manually invoke the same route. **Expect**: routed directly into the workspace, not the sign-up flow (FR-006).
4. While signed out, click "Try the Platform" from the landing page. **Expect**: routed into the sign-up flow (FR-006, US3 Scenario 2).

## Scenario 4 — Direct navigation to auth-flow URLs bypassing the landing page

1. Signed out, navigate directly to `/login`, `/register`, `/confirm-email` (with a valid token query param from a test email if available), and `/auth/external-complete`.
2. **Expect**: each renders in the new, brand-consistent restyled treatment and functions identically to before this feature (FR-013, FR-019).

## Scenario 5 — Accessibility spot-check (FR-016)

1. On the landing page, tab through all interactive elements using only the keyboard. **Expect**: visible focus states on every CTA and interactive content element, in a logical order, with no keyboard trap.
2. Run the existing `jest-axe` automated check against `LandingPage` and each restyled auth page (part of the frontend test suite, not a manual step) — **expect** zero violations, consistent with constitution §10.

## Scenario 6 — Shared-link preview (SC-008)

1. Copy the app's root URL and paste it into a tool that renders Open Graph previews (e.g. a messaging app's link-preview renderer, or any standard OG-tag validator).
2. **Expect**: a correct title, description, and preview image render — not a blank/default preview (FR-022).

## Scenario 7 — Rate limiting / abuse protection (contracts/analytics-funnel-events-api.md)

1. From a script or REST client, send a burst of `POST /api/v1/analytics/funnel-events` requests exceeding the configured fixed-window limit from a single IP.
2. **Expect**: requests beyond the limit receive `429 Too Many Requests`; requests with a malformed `eventType` receive `400` with a Problem Details body.

## Scenario 8 — Landing page interactivity ahead of the hero visual (constitution §15)

1. Throttle the network to "Fast 3G" (or similar) in browser dev tools, then load the app's root URL.
2. **Expect**: the CTA bar and above-the-fold copy are visible and clickable before `LandingHero`'s three.js visual finishes loading — the hero must not block interactivity (plan.md Performance Goals).

---

None of these scenarios require a database migration, a new external analytics account, or a new environment variable/secret — every dependency used is already present in the existing dev environment (data-model.md, research.md).
