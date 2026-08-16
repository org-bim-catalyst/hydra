# Feature Specification: Flumeria Public Landing Experience

**Feature Branch**: `[023-flumeria-landing-experience]`

**Created**: 2026-08-16

**Status**: Draft

**Input**: User description: "Implement the new public-facing experience for the AI Urban Design Platform (flumeria), integrating the existing Ask Lucy product identity into the new platform. The product architecture should treat Flumeria as the primary product and Ask Lucy as its underlying agentic AI engine/interface. The final experience should feel like one coherent product rather than two unrelated applications. Create the complete unauthenticated user journey, reusing the current ask-lucy authentication implementation: Landing Page → Sign In / Sign Up → Authenticated Flumeria Workspace. The landing page should communicate what Flumeria is, what problems it solves, how AI-assisted urban design works, how Lucy interacts with the urban design environment, how GIS/maps/2D-3D models/spatial analysis are integrated, how AI-generated analysis is visualized, and why the platform is different from conventional urban design software. Primary CTAs: Sign In, Create Account/Sign Up, Try the Platform. Use the supplied Readdy.ai design as the initial visual direction, refined with UI/UX Pro Max principles. Visual language must be premium, minimal, AI-native, spatial, modern, professional. Authentication should seamlessly transition into the main Flumeria workspace (the existing Ask Lucy main page). Do not redesign the authenticated viewer/chat workspace except where necessary to establish the transition. Existing authentication functionality must be preserved. Do not introduce dashboard-style navigation."

## Clarifications

### Session 2026-08-16

- Q: The spec only names "sign-in and sign-up pages" as needing the new visual treatment. What is the scope of pages that must be restyled to match the new brand? → A: All auth-flow pages (sign-in, sign-up, email confirmation, email-change confirmation, and external-login completion) get the new visual treatment.
- Q: Should the new public pages be covered by the platform's existing cookie-consent gate, and is analytics/conversion tracking in scope for this feature? → A: Wrap the public pages with the existing consent gate AND add consent-gated conversion/funnel analytics events for the three CTAs and funnel completion, so the funnel and coherence success criteria can be measured with real data.
- Q: Should the landing page be optimized for search engine discovery and social link previews as part of this feature? → A: Yes — the landing page MUST have accurate search-engine metadata (title, description) and a social preview (Open Graph image + description) so shared links render properly and the page is indexable.
- Q: The existing registration backend (`RegisterCommandHandler`) never issues a session token at sign-up — it requires email confirmation first and returns only a `UserId`, so "automatic sign-in with no extra manual steps" is not achievable for sign-up without changing authentication/token-issuance behavior, which conflicts with this feature's own assumption of reusing auth as-is. How should sign-up's outcome be handled? → A: Keep the existing email-confirmation requirement unchanged (no backend/security change); sign-up ends in a branded, restyled confirmation-pending state ("check your email to confirm your account"), not a redirect into the workspace. "Automatic sign-in, no extra manual steps" (FR-008) applies to sign-in only. A user reaches the workspace after sign-up by confirming their email and then signing in (US2's flow).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prospective user discovers Flumeria and creates an account (Priority: P1)

A visitor who has never used the product arrives at the public site, understands within moments what Flumeria is and how it helps with AI-assisted urban design, and — convinced of its value — creates an account, ready to confirm it and start.

**Why this priority**: This is the core acquisition journey. Without a landing page that explains the product and converts to a new account, there is no path for new users to ever reach the product at all. Every other story depends on this journey existing.

**Independent Test**: Can be fully tested by visiting the public root URL as a signed-out visitor, reading the page content, selecting "Create Account / Sign Up," completing sign-up, and confirming the branded email-confirmation-pending state appears — delivers a complete, demonstrable acquisition funnel on its own (reaching the workspace itself is completed via US2's sign-in flow after confirming the account by email, which is unchanged by this feature).

**Acceptance Scenarios**:

1. **Given** a signed-out visitor navigates to the application's public URL, **When** the page loads, **Then** they see a landing page (not a login form) that explains what Flumeria is, the problems it solves, how AI-assisted urban design and the Lucy AI assistant work, how GIS/maps/2D-3D models/spatial analysis are integrated, how AI-generated analysis is visualized, and how the platform differs from conventional urban design software.
2. **Given** the visitor is reading the landing page, **When** they select "Create Account / Sign Up," **Then** they are taken to a sign-up flow that is visually consistent with the landing page they just left.
3. **Given** the visitor completes sign-up successfully, **When** their account is created, **Then** they see a branded confirmation-pending state ("check your email to confirm your account") — no session is issued and no redirect occurs, matching the platform's existing, unchanged email-confirmation requirement.
4. **Given** the visitor is on a mobile device, **When** they view the landing page, **Then** all content, CTAs, and visuals render correctly and remain usable without horizontal scrolling or overlapping elements.

---

### User Story 2 - Returning user signs in and resumes work (Priority: P2)

A user who already has an account returns to the public site, quickly finds the sign-in option, authenticates, and is dropped back into their workspace without friction or confusion about which product they're using.

**Why this priority**: Returning users are a recurring, high-frequency journey; a fast, unambiguous sign-in path directly affects retention and day-to-day usability, but the product has no users to retain until Story 1 exists.

**Independent Test**: Can be fully tested by visiting the public URL as a signed-out visitor with an existing account, selecting "Sign In," authenticating with valid credentials, and confirming arrival in the same authenticated workspace used before — independently verifiable without touching sign-up.

**Acceptance Scenarios**:

1. **Given** a signed-out visitor with an existing account is on the landing page, **When** they select "Sign In," **Then** they reach a sign-in page that is visually consistent with the landing page.
2. **Given** the visitor enters valid credentials, **When** they submit the sign-in form, **Then** they are redirected into the same authenticated Flumeria workspace existing users already use, with their prior data intact.
3. **Given** the visitor enters invalid credentials or another recoverable error occurs, **When** they submit the form, **Then** they see a clear, visible error message and can retry without losing entered data — no silent or unexplained failure.
4. **Given** a user's authentication requires an additional step already supported today (e.g., email verification, two-factor authentication), **When** they sign in, **Then** that existing step still functions exactly as it does before this feature.

---

### User Story 3 - Visitor experiences Flumeria and Ask Lucy as one coherent product (Priority: P3)

A visitor who explores the platform — whether browsing the landing page, going through sign-in/sign-up, or exploring via "Try the Platform" — perceives Flumeria and Ask Lucy as a single, coherent product where "Lucy" is the AI capability inside Flumeria, not a second unrelated application they've been redirected into.

**Why this priority**: Brand and narrative coherence is what makes the rebrand succeed rather than merely relocate confusion; it matters most once the core funnel (Stories 1–2) already works, since coherence is judged across the whole journey.

**Independent Test**: Can be fully tested by walking through the entire journey (landing → auth → workspace) as both a signed-out and a returning visitor and confirming consistent naming, visual identity, and messaging appear at each step, including the moment of entry into the existing workspace — verifiable independently of whether Stories 1/2 are being tested for conversion.

**Acceptance Scenarios**:

1. **Given** a visitor reads the landing page, **When** the page references the AI assistant, **Then** it is introduced as "Lucy," the AI capability within Flumeria, using consistent naming and visual identity with the rest of the journey.
2. **Given** a visitor selects "Try the Platform" while signed out, **When** the action completes, **Then** they are routed into the sign-up flow (no separate guest/demo capability is implied or required).
3. **Given** an already authenticated user selects "Try the Platform" or otherwise arrives at the public landing URL, **When** the page would normally load, **Then** they are instead routed directly into the Flumeria workspace, never shown the marketing landing page while already signed in.
4. **Given** a user completes authentication and arrives in the existing chat/viewer workspace, **When** they look at the workspace's entry point (e.g., header/brand area), **Then** they see a minimal, consistent Flumeria/Ask Lucy brand transition element confirming they're still in the same product — without the workspace's core layout or functionality being redesigned.

---

### Edge Cases

- What happens when an already-authenticated user directly opens the public landing URL (e.g., via a bookmark or shared link)? They MUST be routed straight to the workspace, never shown the marketing landing page again (see US3, Scenario 3).
- What happens when a visitor navigates directly to any existing auth-flow URL (e.g., a bookmarked `/login` link, or an email confirmation/external-login-completion link) instead of arriving via the landing page? The page MUST still render in the new, visually consistent style and function identically.
- How does the system handle a visitor mid-signup who loses network connectivity? An error must be visibly surfaced (per the platform-wide no-silent-failure rule); no unexplained blank state or stuck spinner.
- How does the landing page behave for a visitor using a screen reader or keyboard-only navigation? All CTAs and content sections must remain reachable and operable.
- What happens if a session expires while a user is deep in the workspace and they are bounced back to sign-in? The sign-in page they see must be the new, brand-consistent version, and upon re-authenticating they must return to the workspace (not back to the landing page).
- What happens on very narrow (small phone) and very wide (ultra-wide desktop) viewports? Layout must remain legible and usable at both extremes, not just common breakpoints.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST present a public landing page to signed-out visitors at the application's primary public URL, replacing the current behavior of redirecting straight to sign-in.
- **FR-002**: The landing page MUST communicate, at minimum: what Flumeria is, what problems it solves, how AI-assisted urban design works, how the Lucy AI assistant interacts with the urban design environment, how GIS/maps/2D-3D models/spatial analysis are integrated, how AI-generated analysis is visualized, and why the platform differs from conventional urban design software.
- **FR-003**: The landing page MUST present exactly three primary calls to action: "Sign In," "Create Account / Sign Up," and "Try the Platform."
- **FR-004**: Selecting "Sign In" MUST take the visitor to the existing sign-in flow.
- **FR-005**: Selecting "Create Account / Sign Up" MUST take the visitor to the existing sign-up flow.
- **FR-006**: Selecting "Try the Platform" MUST route a signed-out visitor into the sign-up flow, and MUST route an already-authenticated visitor directly into the Flumeria workspace.
- **FR-007**: All auth-flow pages (sign-in, sign-up, email confirmation, email-change confirmation, and external-login completion) MUST be restyled to be visually consistent with the landing page's visual language (typography, color palette, spatial/AI-native design cues), replacing their current standalone visual treatment.
- **FR-008**: Upon successful sign-in, the user MUST be redirected into the existing authenticated Flumeria workspace (the current Ask Lucy chat/viewer page) with no additional manual steps. Upon successful sign-up, the user MUST see a branded confirmation-pending state ("check your email to confirm your account") — the platform's existing email-confirmation requirement is unchanged by this feature, so sign-up does not issue a session or redirect into the workspace; the user reaches the workspace by confirming their email and then signing in.
- **FR-009**: All existing authentication capabilities (email/password sign-in, sign-up, email verification, two-factor authentication, social login providers) MUST continue to function without regression.
- **FR-010**: Across the landing page, sign-in, sign-up, and the entry point into the workspace, "Ask Lucy" / "Lucy" MUST be presented as the AI engine/assistant capability within Flumeria — using consistent naming, visual identity, and messaging — rather than as a separate, unrelated application.
- **FR-011**: The existing authenticated chat/viewer workspace MUST retain its current layout and functionality; only a minimal, consistent brand-transition element (e.g., in the header/entry area) MAY be added to confirm continuity from the public experience into the workspace.
- **FR-012**: The landing page MUST be fully responsive, rendering correctly from small mobile viewports through large desktop viewports.
- **FR-013**: All restyled auth-flow pages (sign-in, sign-up, email confirmation, email-change confirmation, and external-login completion) MUST remain fully responsive and visually consistent with the landing page across all supported viewport sizes.
- **FR-014**: The landing page's navigation MUST NOT include dashboard-style multi-item navigation menus; navigation is limited to brand identity plus the three defined CTAs (and optional in-page anchor links within the single-page layout).
- **FR-015**: An already-authenticated visitor who navigates to the public landing URL MUST be redirected directly into the Flumeria workspace instead of seeing the marketing landing page.
- **FR-016**: All interactive elements on the landing, sign-in, and sign-up pages MUST be operable via keyboard and correctly labeled for assistive technology, consistent with the platform's existing accessibility requirements.
- **FR-017**: Every failure that can occur during sign-in or sign-up on the restyled pages (validation errors, network failures, server errors) MUST be surfaced to the user through visible UI feedback — no silent or unexplained failures.
- **FR-018**: The landing page MUST illustrate how GIS/maps, 2D/3D models, and spatial analysis integrate with AI-generated analysis through representative visuals, diagrams, or narrative content; a live, functional map or 3D viewer is not required on the marketing page itself.
- **FR-019**: Direct navigation to any existing auth-flow URL (sign-in, sign-up, email confirmation, email-change confirmation, external-login completion), bypassing the landing page, MUST render the same restyled, brand-consistent page and preserve identical functionality.
- **FR-020**: The landing page and all restyled auth-flow pages MUST be covered by a consent mechanism consistent with the platform's existing cookie-consent system (same category taxonomy and visual language as the authenticated experience), rather than being excluded from consent handling as the current public pages are.
- **FR-021**: The system MUST record a consent-gated analytics event whenever a visitor selects any of the three primary CTAs (Sign In, Create Account/Sign Up, Try the Platform), whenever a visitor successfully submits the sign-up form (reaching the confirmation-pending state), and whenever a visitor completes the sign-in funnel (reaching the workspace), so conversion and funnel-completion can be measured; no such event MUST fire before the visitor has granted consent.
- **FR-022**: The landing page MUST expose accurate search-engine metadata (page title and description reflecting Flumeria's value proposition) and a social link-preview (Open Graph image and description) so that shared links render a correct preview and the page is indexable by search engines.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time visitor can go from arriving on the landing page to successfully submitting the sign-up form (reaching the confirmation-pending state) in under 3 minutes, as measured by the consent-gated funnel analytics (FR-021) for consenting visitors. Reaching the workspace itself depends on the visitor confirming their email — a step outside the platform's control — and is covered by SC-002 once they sign in.
- **SC-002**: A returning user can go from arriving on the landing page to reaching their authenticated workspace via sign-in in under 30 seconds, as measured by the consent-gated funnel analytics (FR-021) for consenting visitors.
- **SC-003**: 100% of authentication capabilities available before this change (sign-in, sign-up, verification, two-factor authentication, social login) remain fully functional after launch, with zero regression in sign-in/sign-up success rate.
- **SC-004**: The landing page, sign-in page, and sign-up page render without layout defects (no broken content, no horizontal scrolling, no obscured or unreachable CTAs) across mobile, tablet, and desktop viewport widths.
- **SC-005**: In post-launch review with representative users, at least 90% correctly describe Ask Lucy as a capability within Flumeria rather than as a separate product.
- **SC-006**: No increase, relative to baseline, in support requests related to confusion about "where to sign in" or "what Ask Lucy is versus Flumeria" in the first month after launch.
- **SC-007**: Every one of the three primary CTAs (Sign In, Create Account/Sign Up, Try the Platform) resolves to the correct destination in 100% of test executions, for both signed-out and signed-in visitors.
- **SC-008**: A link to the landing page shared on common social/messaging platforms renders a correct title, description, and preview image; the page is indexed by search engines within the platform's standard indexing window.

## Assumptions

- The existing authentication backend, validation rules, and security controls (two-factor authentication, refresh tokens, social login providers) are unchanged by this feature; only the visual presentation of the auth-flow pages (sign-in, sign-up, email confirmation, email-change confirmation, external-login completion) and the surrounding page shell are updated, per the instruction to reuse the current implementation.
- "Try the Platform" routes a signed-out visitor into the sign-up flow and a signed-in visitor directly into the workspace; this feature does not introduce a separate anonymous/guest trial mode of the workspace.
- The existing authenticated chat/viewer workspace keeps its current functionality and layout; only minimal brand-transition elements (e.g., header identity) are added to bridge from the public experience, consistent with the instruction not to redesign the workspace except where necessary.
- "GIS, maps, 2D/3D models, and spatial analysis integration" is communicated on the landing page through illustrative/marketing visuals and narrative copy, not a live, functional spatial demo; any live spatial functionality lives in the product elsewhere and is out of scope here.
- The supplied Readdy.ai reference design is used as visual/UX inspiration to be refined per UI/UX best practices and adapted to the platform's existing design system and component library, not implemented as a pixel-exact copy.
- The application's primary public URL is repurposed to serve the new landing page for signed-out visitors; signed-in visitors hitting that same URL are redirected straight to the workspace.
- Localization/internationalization of the new landing, sign-in, and sign-up content is out of scope; copy ships in the platform's current default language, consistent with the rest of the product today.
- No changes to backend authentication contracts, token issuance, or the underlying data model are required — this is primarily a front-end presentation and routing change that reuses the existing authentication APIs; the only new behavior is client-side, consent-gated emission of funnel/CTA analytics events (FR-021) into the platform's existing consent/analytics handling, which does not require new authentication endpoints or schema changes.
