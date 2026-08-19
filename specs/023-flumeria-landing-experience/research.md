# Phase 0 Research: Flumeria Public Landing Experience

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

All Technical Context unknowns are resolved below; none required a new external dependency.

## Topic 1 — Per-route SEO/social-preview metadata (FR-022)

**Decision**: Use React 19's native document-metadata hoisting — render `<title>`, `<meta name="description">`, and `<meta property="og:*">` tags directly inside each page component (`LandingPage.tsx` and, where relevant, the restyled auth pages). React 19 automatically hoists any `<title>`/`<meta>`/`<link>` rendered anywhere in the component tree into the document `<head>`, deduplicating and cleaning up on unmount.

**Rationale**: The codebase is already on React 19.2.7 (confirmed in `package.json`), so this capability exists today at zero cost. `index.html` currently has one static, hardcoded `<title>` (line 13) and no per-route metadata mechanism exists anywhere in `src/`. Using the framework-native mechanism satisfies FR-022 without adding a dependency, consistent with constitution §III (avoid unnecessary dependencies) and §7 (design-system-first).

**Alternatives considered**: `react-helmet-async` — rejected as unnecessary now that React 19 handles this natively; it would also be a second, competing mechanism to reason about. A build-time static-HTML-per-route generator — rejected as disproportionate infrastructure change for a single new public route in an otherwise client-rendered SPA.

## Topic 2 — Consent gating for anonymous public pages (FR-020)

**Decision**: Introduce a new, anonymous-safe `PublicConsentGate` + `PublicConsentBanner` pair that lives entirely client-side (a first-party cookie, e.g. `flumeria_public_consent`, storing granted categories + policy version + decision timestamp — no server round-trip to save it). It reuses the same `COOKIE_CATEGORIES` taxonomy and visual banner language as the authenticated flow for consistency, but is a structurally separate mechanism from `ConsentGate`/`useCookieConsent`. Once a visitor authenticates (signs in or up), the existing authenticated `ConsentGate` (tied to their `UserId`) takes over immediately and unchanged, exactly as it does today.

**Rationale**: Investigation of the existing consent implementation found it is authenticated-only end to end:
- `CookieConsentController` (`src/AskLucy.Web/Controllers/v1/CookieConsentController.cs`) is `[Authorize]` at the class level; only the separate `GetCookiePolicyQuery` endpoint (backing the static `/privacy` page) is `[AllowAnonymous]`.
- `useCookieConsent()` (`src/AskLucy.Web/ClientApp/src/features/consent/hooks/useCookieConsent.ts`) calls `consentApi.getMyCookieConsent()`, which hits that authenticated endpoint.
- `CookieConsentRecord` (specs/004 data-model.md) requires a non-blank `UserId` FK to `ApplicationUser` — there is no concept of an anonymous/device-scoped consent record in the existing Domain model.

Wrapping the new landing/auth pages in the literal existing `<ConsentGate>` would call an endpoint that 401s for every signed-out visitor, which `ConsentGate` renders as a full-page blocking error ("Couldn't load your cookie preferences") — breaking the landing page for every anonymous visitor, the opposite of the feature's intent. A client-only companion mechanism satisfies FR-020's actual intent (a consent decision is obtained, using the platform's established categories and visual language, before any tracking) without modifying specs/004's already-shipped, authenticated Domain entity/validation rules — keeping this feature's blast radius contained to itself, per constitution §III (YAGNI: don't redesign a shipped, working feature to accommodate a tangential need) and §I (Clean Architecture: no unrelated feature's aggregate is touched).

**Alternatives considered**:
- Extend `CookieConsentRecord`/`CookieConsentController` to support anonymous/device-scoped consent (nullable `UserId` + a device-id cookie) — rejected: a materially larger, higher-risk change to a different, already-shipped feature's Domain model and audit semantics than this feature's scope justifies; would need its own spec/ADR per constitution §17 (data-model change, cross-feature).
- Make the new public pages `[Authorize]`-exempt from consent entirely (Option C from the spec's clarification) — rejected: the user explicitly chose the "gate + add tracking" option during `/speckit-clarify`.
- Server-persist the anonymous consent decision for compliance audit — deferred as a documented limitation (see plan.md's Constitution Check note and this feature's Assumptions); can be built later as an extension of specs/004 if formal anonymous-consent audit evidence becomes a requirement.

## Topic 3 — Hero/spatial visuals for the landing page (FR-002/FR-018)

**⚠️ Course correction (post-implementation, user-flagged)**: this topic was decided twice. The first two decisions below (three.js reuse, then a CSS/SVG "drafting table" motif) were both made **without ever having actually rendered the Readdy.ai reference design** the original feature request named as the required visual direction — `WebFetch` on the three `readdy.cc` preview URLs returned only an empty SPA shell (client-side-rendered, no server HTML), and this was wrongly treated as "acceptable, adapt later" instead of being escalated. The user caught this after implementation was reported complete. The reference was then actually rendered with a headless Playwright browser (already a dependency in `tests/AskLucy.E2E.Tests`) and screenshotted section-by-section, which is what the **final decision** below is based on. Recorded here in full, including the superseded decisions, so the audit trail is honest about how this was actually resolved rather than presenting only the final answer.

**Final decision (superseded once more, then settled)**: Match the reference design's actual structure, palette, **and imagery** — hero (5-image rotating background, matching the reference's carousel dots) → 4-step "How It Works" timeline → **five** alternating image/text feature blocks (Site Intelligence, Urban Context, Design Analysis, AI Insights, Design Evaluation) → green stats band → black newsletter band → black footer; forest-green/black/off-white palette (`features/landing/theme/flumeriaPalette.ts`), scoped to the public landing + auth pages only (the authenticated workspace keeps its existing graphite/ink-blue identity unchanged, per spec.md Assumptions). Imagery is the reference's **own source images**, downloaded directly (`src/AskLucy.Web/ClientApp/src/assets/landing/*.png`, ~8.6 MB total) from the `readdy.ai/api/search-image?...` URLs each `<img>` on the reference actually points to, rather than recreated — `SiteIllustration` (the SVG-illustration approach below) was built, shipped, then removed once this was possible.

**Second correction (same session, user re-flagged)**: after shipping `SiteIllustration`, the user pasted full-resolution screenshots of the reference directly into the conversation and repeated the request more emphatically — the abstract SVG illustrations were not an acceptable substitute for the actual photographic/isometric-render look, regardless of the AI-generation quota blocker. Re-examining those screenshots also surfaced a **second, independent content error**: the original implementation invented a fabricated sixth "Design Comparison" feature block, duplicating text that actually belongs to the reference's real fifth block, "Design Evaluation" (which itself already contains a side-by-side comparison image). This was corrected by downloading the reference's actual `img[src]` URLs directly (via `page.context().request.get()` in a throwaway Playwright script, bypassing DOM screenshots entirely to avoid capturing the page's own overlay/watermark) — the five distinct `feature-*` image URLs (`seq=feature-site-intel`, `feature-urban-context`, `feature-design-analysis`, `feature-ai-insights`, `feature-design-eval`) confirm the reference has five feature blocks, not six.

**Rationale for using the reference's own images directly** (superseding the earlier "generate new AI imagery" plan): the user directed this explicitly after the illustrated fallback was rejected. The original licensing hesitation (§"don't reuse someone else's generated mockup's assets") does not apply cleanly here — the user commissioned/generated this exact Readdy.ai design as their own stated "initial visual direction" per the original feature request, and directed its reuse a second time after seeing the illustrated alternative. The images are downloaded once at implementation time and committed as static local assets (no runtime dependency on `readdy.ai`'s API).

**Alternatives considered, in the order actually tried**:
1. Reuse the existing three.js/`@react-three/fiber`/`@react-three/drei` stack (already a dependency, used in `features/chat/scene/`) — the original decision, made before the reference was ever rendered. Superseded: once the reference was actually seen, it uses photographic/isometric-render imagery, not an interactive 3D particle scene, so this was solving the wrong problem.
2. A CSS/SVG "drafting table" motif matching `AuthLayout`'s *pre-existing* dark panel pattern — the second decision, also made without the reference rendered, extending Ask Lucy's internal authenticated-app aesthetic onto the public marketing site instead of adapting the actually-supplied reference. This is the mistake the user flagged directly (first time).
3. Real map library (Mapbox/Leaflet) — rejected on its own merits regardless of the above: new dependency, unnecessary for a marketing page showing no live data (FR-018 already allows illustrative content).
4. AI-generated imagery in the reference's style — blocked by the provided `GEMINI_API_KEY`'s Google AI Studio project returning a real `429 RESOURCE_EXHAUSTED` (`limit: 0`) for every image-generation model tried (`gemini-3-pro-image-preview`, `gemini-2.5-flash-image`) — no image-generation quota exists on that project's current billing tier.
5. `SiteIllustration` — flat SVG geometry (organic park blob, isometric city grid, technical site-plan) as a stand-in, chosen (by the user, from an explicit menu) once option 4 was blocked. Shipped, verified in tests, then explicitly rejected by the user as not matching the reference closely enough — removed entirely once option 6 became viable.
6. **Final**: the reference's own source images, downloaded directly and committed as static assets — see decision above.

## Topic 4 — Funnel/CTA analytics storage (FR-021)

**Decision**: Record funnel/CTA analytics as structured Serilog log events (`Information` level, named properties: `EventType`, `CtaId`/`FunnelType`, `SessionId`, `OccurredAtUtc`) via a new `RecordFunnelEventCommand` handled in `Application/Analytics/`, exposed through a new anonymous, rate-limited `POST /api/v1/analytics/funnel-events` endpoint. **No new database table or EF Core migration.**

**Rationale**: Constitution §14 (Observability) already mandates structured Serilog logging shipped to a centralized sink and dashboarded metrics — funnel events are a natural fit for that existing pipeline rather than new bespoke storage. Constitution §III (Simplicity/YAGNI) disfavors a new CRUD-capable entity/table for what is fundamentally ephemeral telemetry, not core business data requiring relational querying, soft-delete, or GDPR-erasure semantics (§5's rules exist for user-owned data — funnel events carry no PII: `SessionId` is a client-generated ephemeral identifier, not tied to `UserId` or email). This also avoids a schema migration entirely for this feature.

**Alternatives considered**: A new `FunnelAnalyticsEvent` EF Core entity + table — rejected per above (disproportionate for non-authoritative telemetry data; §5's entity/index/migration rules would apply for no real benefit). A third-party analytics SDK (GA4, Mixpanel, PostHog, Amplitude) — rejected: introduces an external vendor dependency and a second, overlapping consent/privacy surface beyond the platform's own established consent categories, contrary to constitution §III and the provider-neutral ethos already applied to LLM providers (§9) — the same reasoning extends naturally to analytics vendors.

## Topic 5 — Auth-flow page restyling approach (FR-007/FR-013, post-clarification scope)

**Decision**: Extend the existing `AuthLayout.tsx` component (used today by `LoginPage`/`RegisterPage` and already implementing a distinctive "drafting sheet" premium visual identity from specs/010-lucy-brand-refresh) rather than building a new layout from scratch. Apply it — or a small variant for pages with less form content — to the two pages not currently using it if they aren't already (`ConfirmEmailPage`, `ConfirmEmailChangePage`, `ExternalLoginCompletePage`), and update its wordmark/copy area to introduce the Flumeria identity alongside "Ask Lucy" per FR-010.

**Rationale**: `AuthLayout.tsx` already delivers a premium, minimal, spatial visual language (dark title-block panel, drafting-pattern texture, `LucyPortrait`) — very close to what the spec asks for ("premium, minimal, AI-native, spatial, modern, professional"). Constitution §7 requires composing from the existing design system before writing bespoke components; a ground-up redesign here would violate that and needlessly re-litigate an already-approved (specs/010) visual identity. Only the wordmark/copy area needs to change to introduce Flumeria as primary/Ask Lucy as the capability within it (FR-010).

**Alternatives considered**: A brand-new, Flumeria-specific auth layout independent of `AuthLayout` — rejected: duplicates an already-good, already-shipped component and risks visual drift between the "old" and "new" auth pages if not every page is migrated together (the clarification session confirmed all five auth-flow pages must move together).

## Topic 6 — Root-route redirect behavior (FR-001/FR-015)

**Decision**: Add a `PublicOnlyRoute` component, structurally the inverse of the existing `ProtectedRoute` (`src/AskLucy.Web/ClientApp/src/routes/ProtectedRoute.tsx`): it reads `accessToken` from the same `authStore`, and renders its children (the new `LandingPage`) only when signed out; when an access token is present it renders `<Navigate to="/chat" replace />` instead. The router's root entry changes from the current unconditional `<Navigate to="/chat" replace />` (`router.tsx` line 102) to `<PublicOnlyRoute><LandingPage /></PublicOnlyRoute>`.

**Rationale**: Mirrors the existing, already-tested pattern (`ProtectedRoute`) rather than inventing a new auth-gating mechanism, satisfying constitution §VII (convention over configuration) and keeping the change minimal and consistent with how route-level auth gating already works everywhere else in this router.

**Alternatives considered**: A single combined component handling both directions — rejected: `ProtectedRoute` is already relied upon elsewhere and well-understood; a small, single-purpose `PublicOnlyRoute` is simpler to reason about and test in isolation (§II SRP) than overloading `ProtectedRoute` with a mode flag.

## Topic 7 — Workspace brand-transition placement (FR-011)

**Decision**: Extend the existing brand cluster in `MinimalTopBar.tsx` (`src/AskLucy.Web/ClientApp/src/features/chat/components/MinimalTopBar.tsx`, lines 52–60 — already rendering `BrandMark` + "Ask Lucy" text) to add a small, consistent Flumeria identity marker (e.g., a compact "Flumeria" wordmark with "Ask Lucy" retained as the AI-capability label beneath/beside it). No other change to `ChatPage.tsx` or the chat workspace.

**Rationale**: `MinimalTopBar` is confirmed (via `ChatPage.tsx` line 90) as the sole header rendered in the authenticated chat workspace — it is already exactly the "minimal, consistent brand-transition element" FR-011 calls for, and already deliberately minimal by design (per its own inline documentation: "Everything chat-specific... lives inside AssistantPanel instead"). This is the smallest possible touch that satisfies FR-011 without redesigning the workspace, directly honoring the spec's "do not redesign... except where necessary" constraint.

**Alternatives considered**: Adding a new persistent app-wide header/banner above `MinimalTopBar` — rejected: would be a workspace layout change beyond "minimal," and risks reading as the "dashboard-style navigation" the spec explicitly forbids (FR-014, by extension of intent).

## Topic 8 — Anonymous endpoint abuse protection (§8 Security, FR-021)

**Decision**: Register a new named fixed-window rate-limit policy for `POST /api/v1/analytics/funnel-events` in `Program.cs`, following the exact pattern already used for the solution's other anonymous/public endpoints (`AddRateLimiter` + `RateLimitPartition.GetFixedWindowLimiter`, partitioned per client IP).

**Rationale**: Constitution §6 mandates rate limiting on every public endpoint; §8 requires this explicitly for anonymous surfaces. `Program.cs` already establishes this exact pattern multiple times for other endpoints — reusing it is the convention-over-configuration path (§VII) rather than inventing a new limiting mechanism.

**Alternatives considered**: No rate limiting (relying on payload validation alone) — rejected: violates constitution §6/§8 directly for a newly-introduced anonymous endpoint.
