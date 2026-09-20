# Quickstart: Validating Branded Transactional Email Templates

## Prerequisites

- Backend running locally in Development (uses `ConsoleEmailSender`, so no real SMTP credentials
  are needed — see `src/AskLucy.Infrastructure/Email/ConsoleEmailSender.cs`).
- A browser or mail client capable of previewing raw HTML (or the three required clients from
  SC-002 — Gmail web, Apple Mail, Outlook desktop/web — for full cross-client sign-off).

## Trigger each of the five emails

1. **Registration confirmation** — `POST /api/v1/auth/register` with a new email/password.
   `ConsoleEmailSender` logs the rendered HTML/text to the console.
2. **Confirmation-link resend** — `POST /api/v1/auth/resend-confirmation` for an unconfirmed
   account.
3. **New-email confirmation** — while signed in, request an email change (see
   `RequestEmailChangeCommand`).
4. **Password reset link** — `POST /api/v1/auth/password/forgot` for an existing account.
5. **Password-changed notice** — complete a password reset or authenticated password change.

## Validate against the spec's acceptance criteria

- **US1 (brand consistency)**: Open all five logged HTML bodies side by side. Confirm each uses
  the same header wordmark placement, color treatment, typography, and footer — differing only in
  heading/body copy and call-to-action (spec.md Acceptance Scenarios 1–3).
- **US2 (light/dark legibility)**: Save a rendered HTML body to a `.html` file and open it in
  Gmail (upload/send to a test inbox), Apple Mail, and Outlook, toggling each client's light/dark
  setting. Confirm text stays readable and the call-to-action stays visible in both modes
  (SC-002).
- **US3 (single clear CTA)**: For the four actionable emails, confirm exactly one visually
  emphasized link/button is present, and that a "didn't request this?" line is visible without
  scrolling.
- **Edge cases**: Disable remote-image loading in the test client and re-open each email — the
  wordmark and CTA must remain visible (no image dependency). Test with a long display name/IP
  address value to confirm no layout breakage. Resize the preview (or use a mobile client/device
  preview) to a narrow (~375px) width and again at a wide (~1024px+) desktop width — confirm the
  layout stays single-column with no horizontal scrolling and the call-to-action is reachable
  without scrolling at both widths (FR-009, SC-003).
- **No tracking**: Inspect the rendered HTML source — confirm no `<img>` tracking pixel and no
  anchor `href` other than the direct destination URL passed by the caller (FR-010).

## Expected outcome

All five emails share one recognizable visual family, remain fully legible and on-brand in light
and dark mode across the three required clients, and every link is a direct, untracked URL to its
destination — matching SC-001 through SC-004.
