# Feature Specification: Branded Transactional Email Templates

**Feature Branch**: `061-branded-email-templates`

**Created**: 2026-09-20

**Status**: Draft

**Input**: User description: "All account emails (confirm email, reset password, password changed, confirm new email) need proper, consistent formats. Create a creative, stylish theme that matches the Flumeria brand, decorate it elegantly, and unify the look and feel of every message so it works flawlessly in both light and dark email clients."

## Clarifications

### Session 2026-09-20

- Q: Should the redesigned template include open/click tracking (e.g., a tracking pixel or wrapped links) for engagement analytics? → A: No — purely presentational template, no tracking of any kind.
- Q: Which email clients define the "flawless in light and dark mode" coverage bar for testing/acceptance? → A: Gmail (web), Apple Mail (macOS/iOS), and Outlook (desktop + web).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Recognizable, on-brand email at every account touchpoint (Priority: P1)

A visitor or member receives an Ask Lucy account email — confirming registration, confirming a changed email address, resetting a forgotten password, or being notified their password changed — and immediately recognizes it as a legitimate, professionally designed message from Ask Lucy, styled consistently with the product's Flumeria brand identity, rather than a generic plain-text notice.

**Why this priority**: These emails are often the first and most security-sensitive interaction a person has with the product outside the app itself. A generic or inconsistent email erodes trust, increases the chance a legitimate email is mistaken for phishing (or a phishing email is mistaken for legitimate), and reflects poorly on the brand at a moment when the user is already anxious (locked out, changing credentials, etc.).

**Independent Test**: Trigger each of the five account email types and confirm each one uses the same branded header, color treatment, typography, and footer — delivering a cohesive, recognizable experience without needing any of the other templates built first.

**Acceptance Scenarios**:

1. **Given** a person registers a new Ask Lucy account, **When** the confirmation email arrives, **Then** it displays the Ask Lucy/Flumeria brand mark, a clear heading, a single obvious "confirm" action, and a consistent footer.
2. **Given** a person requests a new confirmation link, a password reset link, a password-changed notice, or a new-email confirmation, **When** each corresponding email arrives, **Then** it shares the same visual language (brand mark placement, color treatment, typography, footer, safety disclaimer) as every other account email, differing only in the message and call-to-action specific to that event.
3. **Given** any two account emails are placed side by side, **When** a recipient compares them, **Then** they appear as a single coherent family of communication rather than five unrelated designs.

---

### User Story 2 - Legible and elegant in both light and dark mode (Priority: P1)

A member reads their Ask Lucy account email inside an email client set to dark mode (or light mode), and the email remains fully legible, visually elegant, and on-brand — text stays readable against its background, the call-to-action stays visible, and no element looks broken, inverted, or washed out.

**Why this priority**: Dark mode is now a default or near-default setting across major mail clients. An email that only accounts for light mode risks unreadable text (e.g., dark text on a dark auto-inverted background) precisely in the emails users most need to act on quickly, such as a password reset.

**Independent Test**: Open each of the five email types in a light-mode client and a dark-mode client and confirm text contrast, background treatment, and the call-to-action remain clear and on-brand in both, without manually toggling anything.

**Acceptance Scenarios**:

1. **Given** a recipient's email client is set to dark mode, **When** they open any account email, **Then** all text remains clearly readable against its background and no content becomes invisible or illegible.
2. **Given** a recipient's email client is set to light mode, **When** they open any account email, **Then** the email displays the intended light-mode color treatment without any dark-mode artifacts.
3. **Given** an email client that partially or automatically re-colors emails for dark mode, **When** the email is displayed, **Then** the primary call-to-action remains visibly distinguishable from its surrounding background.

---

### User Story 3 - Unmistakable, single call-to-action for security actions (Priority: P2)

A member who needs to confirm an email address or reset a password can identify, within a glance, exactly one clear action to take — with plain, reassuring language explaining what the email is for and what to do if they did not request it.

**Why this priority**: These emails exist to move a person through a specific, often time-sensitive action. Visual polish that buries or competes with the call-to-action, or omits the "didn't request this?" reassurance, would undermine the very purpose of the redesign even if the emails look attractive.

**Independent Test**: Open each actionable email type (confirmation, resend, email-change confirmation, password reset) and confirm a single, unambiguous primary action is presented, with a "wasn't you?" note visible without scrolling on common client/window sizes.

**Acceptance Scenarios**:

1. **Given** an email that requires the recipient to click a link to proceed (confirm email, reset password, confirm new email), **When** they open it, **Then** exactly one primary call-to-action is visually emphasized above any other link or element.
2. **Given** any account email, **When** the recipient reads it, **Then** it includes a plain-language note describing what to do (or that no action is needed) if they did not initiate the action themselves.

---

### Edge Cases

- What happens when the recipient's email client blocks remote images entirely? The brand identity and call-to-action must still be recognizable and usable using text/CSS-based styling alone, not solely an image-based logo or image-based button.
- What happens when an email client strips or ignores CSS (styles-in-`<style>` or dark-mode media queries)? The email must still degrade to a readable, correctly ordered, plain layout with working links.
- How does the design handle a very long display name, email address, or IP address value in the body copy? Text must wrap without breaking the layout or pushing the call-to-action off-screen.
- What happens on a narrow (mobile) email client viewport versus a wide (desktop) one? The layout must remain single-column and readable at both, with the call-to-action reachable without horizontal scrolling.
- What happens for a recipient using a screen reader? Reading order must proceed logically (brand → message → action → footer) and the call-to-action link text must be descriptive on its own (not just "click here").

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST present every outbound account email — registration confirmation, confirmation-link resend, new-email confirmation, password reset link, and password-changed notice — using one consistent, branded visual template family.
- **FR-002**: The shared template MUST carry the Ask Lucy / Flumeria brand identity (brand mark, color palette, and typography consistent with the product's existing brand presentation) so every account email is recognizable as coming from Ask Lucy at a glance.
- **FR-003**: Every account email MUST remain fully legible — readable text contrast, intact layout, and a clearly visible call-to-action — in both light-mode and dark-mode email clients, without requiring the recipient to take any manual action.
- **FR-004**: Each actionable email (registration confirmation, confirmation resend, new-email confirmation, password reset) MUST present exactly one visually emphasized primary call-to-action matching that email's purpose.
- **FR-005**: Each account email MUST include a plain-language safety note describing what the recipient should do (or that no action is required) if they did not initiate the event the email describes.
- **FR-006**: Every account email MUST share a consistent footer identifying the sender (Ask Lucy) and how to get help.
- **FR-007**: The template MUST remain functional and legible when a recipient's client blocks remote images and/or ignores custom styling — brand identity and the call-to-action link must not depend solely on an image loading.
- **FR-008**: The visual redesign MUST NOT change the underlying security behavior, wording accuracy, links, or expiry information already carried by each email — only its presentation.
- **FR-009**: Each account email MUST remain readable on both narrow (mobile-width) and wide (desktop-width) screens without horizontal scrolling.
- **FR-010**: The template MUST NOT include any open-tracking pixel, click-tracking redirect, or other engagement-analytics mechanism — every link MUST go directly to its destination with no intermediary tracking.

### Key Entities *(include if feature involves data)*

- **Account Email Template**: The shared visual design family applied to every outbound account email. Comprises a brand header, a message body area (greeting, explanation, safety note), a single primary call-to-action, and a footer — consistent across all five email types, with only the message and call-to-action wording varying per type.
- **Account Email Type**: One of the five distinct account emails this feature covers — registration confirmation, confirmation-link resend, new-email confirmation, password reset link, password-changed notice — each with its own subject line and body copy but sharing the one template.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All five identified account email types (registration confirmation, confirmation resend, new-email confirmation, password reset, password-changed notice) use the unified branded template, with zero remaining emails using the prior unstyled format.
- **SC-002**: Every account email maintains readable text contrast and an intact, visible call-to-action when previewed in both light mode and dark mode across Gmail (web), Apple Mail (macOS/iOS), and Outlook (desktop and web).
- **SC-003**: A recipient can identify the sender as Ask Lucy and locate the single primary action within the first screen of the email, without scrolling, on both mobile-width and desktop-width previews.
- **SC-004**: The primary call-to-action link remains clickable and correctly destined in every email type after the redesign, with no change in the underlying action it performs.

## Assumptions

- "All account emails" refers to the five member-facing transactional emails already sent by the product today: registration confirmation, confirmation-link resend, new-email-address confirmation, password reset link, and password-changed notice. The internal "account access support request" email — sent to the support mailbox on the user's behalf rather than to the user — is an operational/staff-facing message, not a branded customer touchpoint, and is out of scope for this redesign.
- The Flumeria brand identity (color palette, typography, brand mark) already established for the product's landing and studio experience is the source of truth this email template should visually align with; no new brand identity is being defined here.
- "Dark mode" support means the email adapts to the recipient's email client's light/dark setting automatically (e.g., via the client's native dark-mode handling or CSS media queries where supported) — it does not require a manual light/dark toggle inside the email itself.
- A plain-text (or minimally styled) fallback experience is acceptable and expected for clients that strip styling entirely; the requirement is that the email stays usable and on-message, not that it looks identical everywhere.
- No new account email types are being introduced by this feature — only the presentation of the five existing ones changes.

### Amendment 2026-09-20 — Image-based brand mark, revised footer wording

Production feedback (a member comparing the delivered email against the in-app Lucy character)
led to two presentation changes on top of the original text/CSS-only header:

- **FR-002/FR-007 refined**: The header now includes Lucy's actual character portrait
  (`lucy-portrait.png`, the same asset used in the chat toggle and auth pages, per
  `LucyPortrait.tsx`) next to the "Ask Lucy" text wordmark, rather than a text-only wordmark. FR-007
  still holds: the image has empty alt text and sits beside the always-rendered text wordmark, so
  brand identity survives entirely on text/CSS alone if the client blocks remote images — the image
  is additive, not load-bearing. This is a deliberate, narrow exception to the original
  "no image-based logo" framing in the Edge Cases section, not a reversal of it: the button and
  every other brand cue remain non-image.
- **FR-006 refined**: The footer no longer includes a "need help? Contact support." line. It now
  reads "This is an automated message, please do not reply to this email." — standard
  do-not-reply wording, since these are all system-triggered security emails, not a support
  channel. "Identifying the sender (Ask Lucy)" is still satisfied by the header, not the footer.
- The renderer (`BrandedAccountEmailTemplateRenderer`) now takes `IOptions<AppOptions>` to build
  the logo's absolute URL from `AppOptions.FrontendBaseUrl`; the image is served as a static asset
  from the frontend's own public root (`ClientApp/public/lucy-portrait.png`), never a third-party
  or tracking-analytics domain, consistent with FR-010's no-tracking requirement.
