# Contract: `IEmailTemplateRenderer`

This feature exposes no HTTP endpoint — it is an internal Application/Infrastructure contract
used by the five existing account-email send sites. Documented here because it is the interface
those call sites (and their unit tests) are written against.

## `AskLucy.Application.Abstractions.IEmailTemplateRenderer`

```csharp
public interface IEmailTemplateRenderer
{
    /// <summary>
    /// Renders the shared branded shell around <paramref name="content"/>, producing both the
    /// HTML body and its plain-text alternative from the same content — the two can never drift
    /// out of sync because both are derived from one model in one call.
    /// </summary>
    (string HtmlBody, string TextBody) Render(AccountEmailContent content);
}
```

**Preconditions**: `content.Subject`, `content.Heading`, `content.BodyParagraphs`, and
`content.SafetyNote` are non-empty; any pre-encoded/user-controlled values (display name, email
address, IP address) have already been HTML-encoded by the caller before being placed into a
`BodyParagraphs` entry, consistent with every existing call site's current
`WebUtility.HtmlEncode` usage.

**Postconditions**: `HtmlBody` is a complete, self-contained HTML document (`<html>`…`</html>`)
including the light/dark-mode style block; `TextBody` is a complete plain-text rendering carrying
the same information (heading, paragraphs, the primary action's label and full URL on its own
line, safety note, footer) with no HTML markup. Neither body contains a tracking pixel, click
redirect, or any URL not supplied verbatim in `content` (FR-010).

**Failure modes**: Implementation MUST NOT throw for any content satisfying the preconditions —
rendering is pure string composition with no I/O; a missing precondition is a caller bug, not a
runtime condition to recover from at this layer.

## `AskLucy.Application.Abstractions.IEmailSender` (extended)

```csharp
public interface IEmailSender
{
    Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken = default);
}
```

Changed from today's three-body-arg signature to accept `textBody` alongside `htmlBody`, so every
send is multipart (Research Topic 4). Both existing implementations (`SmtpEmailSender`,
`ConsoleEmailSender`) and both existing unit test doubles are updated at the same call sites this
feature already touches — no new implementation is introduced.

## Consumers (all five existing send sites, updated to build `AccountEmailContent` then call both interfaces)

- `RegisterCommandHandler` (registration confirmation)
- `AccountEmailJob.ResendConfirmationAsync` (confirmation resend)
- `RequestEmailChangeCommandHandler` (new-email confirmation)
- `PasswordEmailJob.SendResetLinkAsync` (password reset link)
- `PasswordEmailJob.SendPasswordChangedNoticeAsync` (password-changed notice)

`AccountEmailJob.SendAccountSupportRequestAsync` (the internal support-mailbox relay) is
explicitly **not** a consumer — it stays on today's plain body construction, per the spec's
Assumptions (out of scope: staff-facing, not a branded customer touchpoint).
