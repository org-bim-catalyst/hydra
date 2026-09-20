# Research: Branded Transactional Email Templates

## Topic 1: Where template composition should live (architecture)

**Decision**: Introduce `IEmailTemplateRenderer` in `AskLucy.Application.Abstractions`, taking a
small `AccountEmailContent` model (heading, greeting, message paragraphs, primary CTA text + URL,
safety note, subject) and returning a rendered `(HtmlBody, TextBody)` pair. Implemented in
`AskLucy.Infrastructure.Email` as `BrandedAccountEmailTemplateRenderer`. All five call sites
(`RegisterCommandHandler`, `RequestEmailChangeCommandHandler`, `AccountEmailJob`,
`PasswordEmailJob`) build the small content model and call the renderer instead of hand-rolling
raw HTML strings inline.

**Rationale**: Today each of the four send sites composes its own ad hoc HTML string
(`$"""<p>...</p>"""`). That is duplicated presentation logic (constitution §2 Principle III,
DRY) and, more importantly, the exact thing this feature needs to unify — five independent
strings can't be kept visually consistent by hand over time. Centralizing rendering in
Infrastructure keeps Application free of markup/CSS concerns (§2 Principle VI, Separation of
Concerns: HTML/CSS rendering is presentation-of-an-email, not business logic) while Application
still owns *what* each email says (subject, copy, links) via the small content model it passes in.
`IEmailSender` stays a pure transport abstraction (SMTP vs. console) and is not widened to know
about branding.

**Alternatives considered**:
- *Leave HTML inline per call site, just restyle each string identically*: rejected — guarantees
  drift the next time any one email's copy changes; exactly the problem being fixed.
- *Put the renderer in Application (pure string templating, no I/O)*: rejected but marginal —
  rendering has no I/O so it could live in Application without breaking the Dependency Rule.
  Infrastructure was chosen instead because it groups with the two existing email jobs and the
  senders it composes bodies for, keeping all "how an email looks/is delivered" code in one
  project; Application still fully controls *content* via the model it supplies.
- *A cshtml/Razor templating engine*: rejected — pulls in a template-file rendering dependency
  for five short, structurally-identical emails; a C# builder over inline strings is simpler
  (KISS/YAGNI, §2 Principle III) and keeps every email in one reviewable file.

## Topic 2: Cross-client HTML email compatibility (light + dark mode)

**Decision**: Build the shared shell as a single-column, table-based HTML layout with all styling
inlined on elements (not solely `<style>`-block-dependent), plus a `<style>` block carrying
`@media (prefers-color-scheme: dark)` overrides and `<meta name="color-scheme" content="light
dark">` / `<meta name="supported-color-schemes" content="light dark">` in the `<head>` for clients
that honor them. The primary call-to-action is rendered as a solid-background table cell/anchor
(not a background-image button), so it survives image-blocking and Outlook's Word rendering
engine. No remote images are required for brand identity — the brand mark is a styled text
wordmark, not a logo image (per spec Edge Cases and FR-007).

**Rationale**: The three required test targets (SC-002: Gmail web, Apple Mail, Outlook
desktop/web) split into two rendering families with materially different capabilities:
- **Apple Mail** (WebKit-based) has full CSS support including `prefers-color-scheme` and dark
  mode media queries — the easy case.
- **Gmail** supports a constrained CSS subset, ignores `<style>`-block dark-mode media queries on
  some surfaces, and can auto-dark-mode-invert unstyled content — mitigated by inlining colors
  explicitly on every element rather than relying on cascade, and using `color-scheme` meta hints.
- **Outlook desktop** renders HTML email with Word's engine (not a browser engine): no CSS
  background images, unreliable `border-radius`, and no flexbox/grid — mitigated by table-based
  layout (the universal safe baseline for HTML email) and avoiding any CSS feature Word cannot
  render, keeping the button a plain filled table cell rather than requiring VML fallback.

**Alternatives considered**:
- *Div/flexbox-based modern CSS layout*: rejected — breaks in Outlook desktop, one of the three
  required coverage clients (SC-002).
- *Single light-only design with no dark-mode handling*: rejected — fails User Story 2's
  acceptance criteria directly; several of the required clients default new accounts to dark
  mode.
- *Fully image-based design (logo + button as images)*: rejected — fails the "client blocks
  remote images" edge case and FR-007.

## Topic 3: Visual identity source and a dark-mode derivative

**Decision**: Use the existing Flumeria auth-flow palette (`flumeriaPalette.ts`:
`green #15803D`, `greenDark #116932`, `black #0A0A0A`, `offWhite #FAFAF8`, `heading #171717`,
`body #4B5563`) as the light-mode email palette, since these are the same account/auth-flow
touchpoints (registration, password reset, email confirmation) that already carry the Flumeria
identity in-app. For dark mode — which the Flumeria in-app palette does not define, since the
auth pages are deliberately light-only in-app — derive a dark variant for email specifically:
a near-black background (`#0A0A0A`/`#14130F`-family), off-white body text, and the same brand
green as the accent/CTA color (verified for AA contrast against the dark background).

**Rationale**: The spec's Assumptions already name the Flumeria identity as the source of truth
for this feature. Reusing its exact tokens keeps the email visually identical in spirit to the
confirm-email/reset-password *pages* the recipient lands on after clicking the email's link —
continuity across the email → landing-page journey. Because email dark mode is a property of the
recipient's mail client (not the in-app theme toggle Flumeria auth pages intentionally omit), a
dedicated dark derivative is required and does not conflict with keeping the in-app auth pages
light-only.

**Alternatives considered**:
- *Use the authenticated workspace's graphite/ink-blue palette instead*: rejected — that
  identity belongs to the signed-in product experience; these emails occur before/around
  authentication (confirm, reset, changed), matching the Flumeria auth-flow surfaces, not the
  workspace.
- *Invert every light color mechanically for dark mode*: rejected — naive inversion of the green
  accent shifts its hue perception and can fail contrast; contrast-checked hand-picked dark-mode
  values are used instead.

## Topic 4: Plain-text fallback and no-tracking constraint

**Decision**: Every send becomes a multipart email (HTML + plain-text alternative), extending
`IEmailSender.SendAsync` to accept both bodies; `BrandedAccountEmailTemplateRenderer` produces the
plain-text alternative from the same content model so the two can never drift out of sync. No
tracking pixel, open-beacon, or link-wrapping/redirect is introduced anywhere in the template or
send path — every anchor href is the direct destination URL supplied by the caller, unchanged
from today's behavior (FR-010, Clarification session 2026-09-20).

**Rationale**: A plain-text alternative is standard multipart-email practice for deliverability
and accessibility (assumption already recorded in spec.md) and is cheap to derive from the same
structured content model already needed for Topic 1. The no-tracking decision was resolved
directly in `/speckit-clarify` (Q1) and requires no further exploration — it constrains Topic 1's
model to never include a tracking-pixel field or click-redirect URL rewriting step.

**Alternatives considered**: N/A — this was settled by clarification, not left open for research.
