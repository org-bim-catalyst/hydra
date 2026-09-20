# Data Model: Branded Transactional Email Templates

No database schema changes. This feature is purely presentational: it introduces one in-memory
content model passed from each Application call site into the template renderer, and no entity is
persisted, queried, or migrated.

## AccountEmailContent (Application-owned model)

Supplied by the caller (Application handler or Infrastructure job) to `IEmailTemplateRenderer`;
fully describes one email's variable content while the renderer owns the shared visual shell.

| Field | Type | Required | Notes |
|---|---|---|---|
| `Subject` | string | yes | Email subject line; unchanged wording from today's five emails (FR-008). |
| `PreheaderText` | string | yes | Short hidden preview text shown in inbox list views before the email is opened; summarizes the email's purpose in one line. |
| `Heading` | string | yes | Short on-brand headline shown at the top of the message body (e.g., "Confirm your account"). |
| `Greeting` | string? | no | Personalized opener (e.g., "Hi {name},"); omitted when no display name is available, falling back to no greeting line rather than an empty/awkward one. |
| `BodyParagraphs` | IReadOnlyList\<string\> | yes | One or more plain-language paragraphs explaining the email's purpose (HTML-encoded values only — no raw markup from user-controlled input, matching today's `WebUtility.HtmlEncode` usage). |
| `PrimaryAction` | `EmailAction`? | no | The single emphasized call-to-action (FR-004); null only for the password-changed notice, which is informational with no action. |
| `SafetyNote` | string | yes | The "didn't request this?" reassurance line (FR-005), present on every email. |
| `FooterNote` | string? | no | Optional extra footer line beyond the shared sender/support footer (FR-006); most emails leave this unset. |

## EmailAction (value object)

| Field | Type | Required | Notes |
|---|---|---|---|
| `Label` | string | yes | Descriptive link/button text (e.g., "Confirm my email") — never generic "click here" (Edge Cases: screen-reader link text). |
| `Url` | string | yes | Direct destination URL, exactly as already constructed by the caller today (confirmation/reset/change link). Never wrapped, shortened, or redirected through a tracking endpoint (FR-010). |

## Relationship to the five existing Account Email Types

Each existing call site maps its current ad hoc HTML into one `AccountEmailContent` instance; no
new email types are introduced (spec Assumptions).

| Account Email Type | Current call site | `PrimaryAction` |
|---|---|---|
| Registration confirmation | `RegisterCommandHandler` | Confirm my email → confirmation link |
| Confirmation-link resend | `AccountEmailJob.ResendConfirmationAsync` | Confirm my email → confirmation link |
| New-email confirmation | `RequestEmailChangeCommandHandler` | Confirm email change → change-confirmation link |
| Password reset link | `PasswordEmailJob.SendResetLinkAsync` | Reset my password → reset link |
| Password-changed notice | `PasswordEmailJob.SendPasswordChangedNoticeAsync` | none (informational only) |

## Rendered output (renderer's return shape)

`IEmailTemplateRenderer.Render(AccountEmailContent)` returns a simple `(string HtmlBody, string
TextBody)` pair — the multipart bodies `IEmailSender.SendAsync` now accepts (Research Topic 4).
Neither value is persisted; both are constructed fresh per send, matching every existing job's
"build in-memory per send" pattern.
