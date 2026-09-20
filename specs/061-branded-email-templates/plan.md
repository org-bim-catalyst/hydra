# Implementation Plan: Branded Transactional Email Templates

**Branch**: `061-branded-email-templates` | **Date**: 2026-09-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/061-branded-email-templates/spec.md`

## Summary

Today the five member-facing account emails (registration confirmation, confirmation resend,
new-email confirmation, password reset link, password-changed notice) each hand-roll their own
ad hoc inline HTML string at four different call sites, with no shared visual identity and no
dark-mode handling. This feature introduces one shared `IEmailTemplateRenderer` (Infrastructure)
that wraps a small Application-owned content model (`AccountEmailContent`) in a single-column,
table-based HTML shell carrying the Flumeria auth-flow brand identity, legible in both light and
dark mode across Gmail, Apple Mail, and Outlook, with a plain-text multipart alternative and no
tracking of any kind. All five call sites switch from building raw HTML to building the content
model and calling the shared renderer; no new email types, endpoints, or persisted data are
introduced.

## Technical Context

**Language/Version**: C# / .NET 10 (backend only — no frontend change)

**Primary Dependencies**: MailKit/MimeKit (already used by `SmtpEmailSender`) — no new package
dependencies required; the renderer is plain C# string composition.

**Storage**: N/A — no persisted data; content is built and rendered in-memory per send, matching
every existing email call site's current pattern.

**Testing**: xUnit + NSubstitute, matching `tests/AskLucy.Application.Tests` (handler content
model construction) and `tests/AskLucy.Infrastructure.Tests/Email` (renderer output assertions —
presence of brand markers, dark-mode media query, single CTA, absence of tracking elements).

**Target Platform**: Existing ASP.NET Core host (`AskLucy.Web`); rendered output is consumed by
third-party email clients (Gmail, Apple Mail, Outlook), not the product's own frontend.

**Project Type**: Backend-only change within the existing Clean Architecture solution
(`Application`, `Infrastructure`).

**Performance Goals**: N/A — template rendering is synchronous, in-memory string composition, not
on any latency-sensitive request path with a stated performance budget.

**Constraints**: Must work with Outlook desktop's Word-based HTML rendering engine (no flexbox/
grid, no CSS background images) per research.md Topic 2. Must not introduce any tracking pixel,
click-redirect, or analytics mechanism (FR-010). Must not change any email's underlying link,
token, or expiry behavior (FR-008) — presentation-only change.

**Scale/Scope**: One new Application-owned abstraction (`IEmailTemplateRenderer` +
`AccountEmailContent`/`EmailAction`), one Infrastructure implementation, one `IEmailSender`
signature extension (add `textBody`), and updates to the five existing send call sites plus their
two existing transport implementations (`SmtpEmailSender`, `ConsoleEmailSender`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I (Dependency Rule) / §3 Architecture Rules**: `IEmailTemplateRenderer` and
  `AccountEmailContent`/`EmailAction` are defined in `Application.Abstractions` (an abstraction
  Application owns); the implementation lives in `Infrastructure.Email`, matching the existing
  `IEmailSender`/`SmtpEmailSender` split. No Application/Domain code references MailKit/MimeKit
  or any markup/CSS concern directly. **PASS.**
- **Principle III (DRY/KISS/YAGNI)**: Directly fixes an existing DRY violation (four independent
  inline-HTML strings) with the simplest sufficient design — a plain C# string-building renderer,
  not a templating engine or file-based system, for five structurally-identical emails. **PASS.**
- **Principle V (Dependency Inversion & Testability)**: `IEmailTemplateRenderer` is injected via
  constructor like every existing Application dependency; the renderer itself has no I/O, so
  unit tests exercise it directly with no database/network/filesystem access. **PASS.**
- **Principle VI (Separation of Concerns)**: Moves HTML/CSS composition (a presentation concern)
  out of Application handlers (`RegisterCommandHandler`, `RequestEmailChangeCommandHandler`) and
  into Infrastructure, where the two existing background jobs' email composition already lives.
  **PASS** — this is a net improvement over the current state, not a new violation.
- **§8 Security**: No secrets introduced; no new external dependency; FR-010 (no tracking)
  directly satisfies the "least surprising/no unnecessary data collection" posture of this
  section even though no formal threat changes. **PASS.**
- **§10 Testing Standards**: New renderer behavior gets unit tests in
  `AskLucy.Infrastructure.Tests`; updated call sites get their existing
  `AskLucy.Application.Tests` handler tests updated for the new content-model construction.
  **PASS** (tracked in tasks.md).
- **§7 UI Principles**: Not applicable — no in-app UI changes; this section governs the React/MUI
  frontend, and rendered emails are consumed by third-party mail clients, not the app's own theme
  provider. Noted as N/A rather than silently skipped, per §16 gate rules.

No violations requiring justification. Complexity Tracking section below is empty.

## Project Structure

### Documentation (this feature)

```text
specs/061-branded-email-templates/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
│   └── email-template-renderer.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Application/
└── Abstractions/
    ├── IEmailSender.cs                 # extended: add textBody parameter
    └── IEmailTemplateRenderer.cs       # new: renderer contract + AccountEmailContent/EmailAction
    Authentication/Commands/
    ├── Register/RegisterCommandHandler.cs               # updated: build AccountEmailContent
    └── ChangeEmail/RequestEmailChangeCommandHandler.cs  # updated: build AccountEmailContent

src/AskLucy.Infrastructure/Email/
├── BrandedAccountEmailTemplateRenderer.cs  # new: HTML/text shell implementation
├── SmtpEmailSender.cs                      # updated: send multipart (html + text)
├── ConsoleEmailSender.cs                   # updated: log both bodies
├── AccountEmailJob.cs                      # updated: build AccountEmailContent
└── PasswordEmailJob.cs                     # updated: build AccountEmailContent

tests/AskLucy.Application.Tests/Authentication/
├── RequestEmailChangeCommandHandlerTests.cs   # updated for new content-model call
└── (RegisterCommandHandler tests, if present)  # updated for new content-model call

tests/AskLucy.Infrastructure.Tests/Email/
└── BrandedAccountEmailTemplateRendererTests.cs  # new: brand markers, dark-mode CSS, single CTA,
                                                   # no-tracking assertions, plain-text parity
```

**Structure Decision**: No new projects. Changes are confined to `AskLucy.Application` (one new
abstraction, two updated handlers) and `AskLucy.Infrastructure` (one new renderer, two updated
senders, two updated jobs), following the existing `Application/Abstractions` +
`Infrastructure/Email` layering already established by `IEmailSender`/`SmtpEmailSender`.

## Complexity Tracking

*No Constitution Check violations — this section intentionally left empty.*
