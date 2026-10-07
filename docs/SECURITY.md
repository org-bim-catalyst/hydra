# SECURITY.md

> **Project:** Ask Lucy AI Workspace
> **Version:** 1.0
> **Status:** Mandatory Security Standard
> **Classification:** Internal Engineering Standard
> **Last Updated:** July 2026

---

# 1. Purpose

This document defines the mandatory security requirements for the Ask Lucy platform.

Security is a shared responsibility across every layer of the application.

Every feature, specification, architectural decision, and pull request MUST comply with this document.

---

# 2. Security Principles

The platform SHALL follow these principles:

* Defense in Depth
* Least Privilege
* Secure by Default
* Zero Trust
* Fail Securely
* Minimize Attack Surface
* Principle of Explicit Access
* Separation of Duties

Security MUST be considered during design—not after implementation.

---

# 3. Compliance Objectives

The platform should align with:

* OWASP ASVS
* OWASP Top 10
* OWASP API Security Top 10
* OWASP Secure Coding Practices

Where applicable, design should also facilitate future alignment with enterprise standards such as ISO/IEC 27001 and SOC 2.

---

# 4. Security Architecture

Security is implemented across multiple layers:

```text
Client
    │
HTTPS/TLS
    │
Web API
    │
Authentication
    │
Authorization
    │
Application Layer
    │
Domain Layer
    │
Infrastructure
    │
SQL Server
```

Every layer validates its own inputs and enforces its own responsibilities.

---

# 5. Authentication

Authentication SHALL use:

* ASP.NET Identity
* JWT Access Tokens
* Refresh Token Rotation
* Secure Password Hashing
* Email Verification
* TOTP Two-Factor Authentication
* Session Management

Passwords must never be stored or transmitted in plain text.

---

# 6. Password Policy

The policy in force is configured once, in `AskLucy.Persistence/DependencyInjection.cs`, and applies
identically at registration, at password reset and at change-password:

| Rule | Value |
|---|---|
| Minimum length | 8 characters |
| Uppercase letter | required |
| Lowercase letter | required |
| Digit | required |
| Non-alphanumeric character | required |
| Confirmed email before sign-in | required |
| Failed attempts before lockout | **3** |
| Lockout duration | 5 minutes |

The lockout threshold was tightened from 5 to 3 once the sign-in page began naming the lockout
explicitly and offering a way out (contact an administrator, or reset the password). A tighter
threshold is only defensible when a locked-out user is told what happened — see
[ADR 0010](adr/0010-naming-sign-in-refusals.md).

Because the rules are enforced by a single Identity options block, the reset and registration
screens can render the same live checklist from the same source of truth rather than restating it.

Still future work:

* Password history
* Configurable expiration policies (enterprise option)

Passwords are always hashed using the framework's recommended algorithms.

---

# 7. Multi-Factor Authentication

Supported factors:

* Authenticator App (TOTP)

Future support may include:

* Passkeys/WebAuthn
* Hardware security keys

MFA secrets must be encrypted at rest.

---

# 8. Session Security

Sessions SHALL support:

* Refresh Token Rotation
* Token Revocation
* Device Tracking (future)
* Session Expiration
* Logout from All Devices

Refresh token reuse must invalidate the session.

## Revocation must be enforced, not only recorded

An access token is a self-contained JWT: stateless validation checks its signature and its expiry
and nothing else. Revoking server-side state therefore has **no effect on a token already issued**
unless something checks it per request. Marking a refresh-token family revoked stops the client
obtaining a *new* access token while the one it holds keeps working until it expires.

That gap was real and user-visible: signing in on two browsers and changing the password from one
left the other with full API access for the rest of its access-token lifetime. It looked fixed only
because a page reload routes through the session endpoint, which does read the revoked cookie.

Accordingly:

* Every access token carries a `sid` claim naming the refresh-token family — the session — it was
  issued for. `TokenIssuer` mints the refresh token first so that id exists to claim.
* `ActiveSessionTokenValidator`, wired to `JwtBearerEvents.OnTokenValidated`, calls `context.Fail`
  when that family is no longer active. `IClaimsTransformation` cannot serve this purpose: it can
  alter a principal's claims but never refuse a request.
* The check is a single indexed `EXISTS` behind a 30-second `IMemoryCache`. Each revocation path
  evicts the affected keys immediately, *after* the database commit, so the cache is never cleared
  for a revocation that then fails to commit and the 30 seconds is only a safety net.
* A token with no `sid` is allowed through — tokens minted before the check existed carry none, and
  refusing them would sign every active user out on deploy. That window closes on its own within one
  access-token lifetime, and the case is logged rather than silently tolerated. A `sid` that will
  not parse fails closed.
* The acting session is deliberately exempt on a password *change*: the user is signing their other
  devices out, not themselves.

Any new way to end a session MUST evict alongside its database write, or it will be honoured up to
a whole access-token lifetime late. The decision, and why Identity's `SecurityStamp` was rejected
for this purpose, is recorded in
[ADR 0012](adr/0012-enforcing-session-revocation-on-the-access-token.md).

**Limitation**: the eviction cache is per-instance. On a multi-instance deployment only the instance
that handled the revocation evicts, and the others fall back to the 30-second expiry. A distributed
cache is required before scaling out.

## The Hangfire dashboard cookie is deliberately narrower than the app's session cookies

`askLucyHangfireSession` is a separate, purpose-bound cookie (`Path=/hangfire`, `SameSite=Lax`,
`HttpOnly`, `Secure`, 30-minute lifetime) minted only for Administrator/Super User callers via
`POST /api/v1/admin/hangfire/session`, distinct from both `RefreshTokenCookie` and
`AccessTokenCookie`. Its token carries a dedicated `purpose=hangfire-dashboard` claim that the
`/hangfire`-scoped `OnTokenValidated` check requires — an otherwise validly signed, currently
active ordinary session token is refused. `Path=/hangfire` means the browser never attaches it to
any bearer-authenticated API call, so it cannot widen the attack surface of the rest of the app
even if read or replayed. `SameSite=Lax` (not `None`, unlike the refresh/access cookies, which
need `None` for a genuinely cross-site `fetch()` case in dev) is sufficient because this cookie is
only ever sent via a top-level browser navigation, and still blocks a forged cross-site POST
against Hangfire's own job-mutating endpoints. See
[ADR 0013](adr/0013-hangfire-dashboard-scoped-session-and-theming-gap.md).

---

# 9. Authorization

Use policy-based authorization.

Never hardcode authorization logic.

Permissions must be centralized.

Every endpoint must verify authorization explicitly.

---

# 10. Role Management

Roles should represent broad responsibilities.

Permissions should control individual capabilities.

Avoid using roles directly in business logic.

---

# 11. Input Validation

All external input is untrusted.

Validate:

* Requests
* Query parameters
* Route parameters
* JSON payloads
* Uploaded files
* AI prompts
* Document metadata

Use FluentValidation for application-level validation.

---

# 12. Output Encoding

Encode user-generated content before rendering.

Protect against:

* XSS
* HTML Injection
* JavaScript Injection

Never trust rendered Markdown without sanitization.

---

# 13. SQL Injection

Always use parameterized queries.

Prefer Entity Framework Core.

Never concatenate SQL strings using user input.

Review raw SQL carefully.

---

# 14. Cross-Site Scripting (XSS)

Protect against:

* Stored XSS
* Reflected XSS
* DOM XSS

Sanitize all rendered Markdown and HTML.

Use a strict Content Security Policy where practical.

---

# 15. Cross-Site Request Forgery (CSRF)

Evaluate CSRF protections based on the authentication mechanism.

If cookies are used, implement anti-forgery protections.

For JWT-based APIs, avoid mixing authentication patterns that reintroduce CSRF risk.

---

# 16. Prompt Injection

Because Ask Lucy interacts with LLMs, prompt injection is a first-class security concern.

Mitigations include:

* Separating system prompts from user prompts
* Restricting tool access
* Validating tool execution requests
* Limiting context exposure
* Sanitizing retrieved content where appropriate
* Logging suspicious prompt behavior

Never assume AI output is trustworthy.

---

# 17. AI Tool Security

AI tools must execute through controlled interfaces.

Tools must:

* Validate inputs
* Enforce permissions
* Log execution
* Apply timeouts
* Restrict filesystem access
* Restrict network access unless explicitly allowed

---

# 18. RAG Security

Knowledge Bases must be isolated per tenant or user.

Documents should never be retrieved across authorization boundaries.

Embedding data is subject to the same authorization rules as source documents.

---

# 19. File Upload Security

Validate:

* File extension
* MIME type
* File size
* Upload limits

Reject:

* Executables
* Scripts
* Unsupported archive formats (unless explicitly supported)

Store uploads outside the web root.

Generate random file names.

## Content-based validation applies equally to provider-sourced files, not just direct user uploads

A file does not need to arrive via a multipart form to need content validation — a URL supplied
in a third party's OAuth claim (e.g. a Google/Facebook profile-picture URL) is still external
input driving a server-side fetch and write. `IImageContentValidator` (magic-byte signature
check: JPEG/PNG/GIF/WebP) is shared between `UploadAvatarCommandHandler` (manual upload) and
`ExternalProfilePictureSyncJob` (provider-sourced sync) so neither path trusts an extension or
`Content-Type` header alone, and the outbound fetch itself is restricted to each provider's known
image-CDN hosts before any request is made, closing the SSRF surface a malformed or compromised
claim value would otherwise open. See
[ADR 0014](adr/0014-external-profile-picture-fetch-hardening.md).

---

# 20. File Download Security

Never expose physical paths.

Downloads must be authorized.

Use signed URLs or secure download endpoints.

Log download events where appropriate.

---

# 21. Malware Scanning

The architecture should support pluggable malware scanning for uploaded files.

Scanning should occur before files become available to downstream AI processing.

---

# 22. Secrets Management

Secrets MUST NOT be stored:

* In Git
* In source code
* In configuration files committed to the repository
* In frontend bundles
* In logs

Use environment-specific secure configuration.

---

# 23. Encryption

Encrypt sensitive data in transit using TLS.

Encrypt sensitive data at rest where appropriate.

Sensitive values include:

* Refresh tokens
* MFA secrets
* API keys
* Provider credentials

---

# 24. API Keys

Provider keys must be stored securely.

Never expose provider keys to the browser.

Rotate keys periodically.

Support key replacement without redeployment.

## The database is the source of truth; configuration is fallback only

An AI provider key lives encrypted in `AiProvider.CredentialCiphertext` and is read from there
first. A configuration value (`OpenAI:ApiKey` and its siblings) is consulted only when no stored
credential exists, and the platform is moving toward removing those settings entirely.

This ordering is a security property, not just plumbing. It is what makes "rotate a key without a
redeployment" real: an administrator replacing a key in the admin UI must take effect for *every*
consumer of that provider at once. When chat read configuration and embeddings read the database,
rotating the stored key left chat running on the old one — a revoked credential still in active
use, with nothing in the UI to indicate it.

A stored credential that fails to decrypt is surfaced as *credential unreadable*. It must never
fall back to a configuration key, because that silently substitutes a different credential for the
one the administrator selected.

---

# 25. Logging

Log:

* Authentication events
* Authorization failures
* Exceptions
* AI provider failures
* Administrative actions
* Security events

Never log:

* Passwords
* Tokens
* Secrets
* API keys
* Personally sensitive content unless explicitly required and protected

---

# 26. Audit Trail

Record important security events including:

* Login
* Logout
* Password changes
* Email verification
* MFA changes
* Role changes
* AI provider changes
* Billing changes (future)

Audit records should be immutable where practical.

---

# 27. Rate Limiting

Implement rate limiting for:

* Authentication endpoints
* AI requests
* File uploads
* Image generation
* Password reset
* Email verification

Limits should be configurable.

---

# 28. Denial-of-Service Protection

Protect against:

* Excessive requests
* Large uploads
* Expensive AI requests
* Excessive concurrent sessions

Introduce back-pressure where appropriate.

---

# 29. Error Handling

Errors should never reveal:

* Stack traces
* SQL statements
* Connection strings
* Internal implementation details

Return standardized Problem Details responses.

---

# 30. Browser Security

Apply secure HTTP headers where appropriate, including:

* Content-Security-Policy
* X-Content-Type-Options
* Referrer-Policy
* X-Frame-Options (or CSP equivalent)
* Permissions-Policy

Review header configuration regularly.

---

# 31. CORS

Allow only trusted origins.

Avoid wildcard origins in production.

Review CORS configuration for every deployment environment.

---

# 32. Dependency Security

All dependencies must:

* Be actively maintained
* Be reviewed before adoption
* Receive security updates promptly

Run dependency vulnerability scans in CI.

---

# 33. Secure Configuration

Production defaults should disable:

* Debug mode
* Detailed exception pages
* Test endpoints
* Development credentials

---

# 34. Database Security

Use least-privilege database accounts.

Protect backups.

Encrypt backups where appropriate.

Review database permissions regularly.

---

# 35. Infrastructure Security

Servers should:

* Receive security updates
* Use minimal installed software
* Disable unused services
* Restrict administrative access

Separate development, staging, and production environments.

---

# 36. Email Security

SMTP connections must use STARTTLS.

Verify sender domains where possible.

Never expose email credentials.

Rate limit email-triggering endpoints.

## Notification hub (specs/067)

Every email and in-app message now goes through the notification hub (see ARCHITECTURE.md §36 and [ADR 0018](adr/0018-transactional-notification-outbox.md)). The controls below are part of its design, not options.

### One-time links are minted at send time and never stored

A password reset, email confirmation or email change link is created by `IAccountLinkIssuer` at the moment the delivery worker sends, and goes only to the renderer. The token is not in an outbox row, a notification, a delivery, an audit row or a log line. The token's own record is saved before the call returns, so a link in a sent email always works. A refused link (unconfirmed, locked out, throttled) cancels the delivery with a fixed safe reason, and the real reason goes to the security log. A retried delivery mints a fresh link and is never throttled as a new request.

### Anti-enumeration

A password reset request publishes one outbox event addressed to an *address lookup*, and the lookup happens in the background. The response and the single outbox insert are the same whether or not the address has an account, so the response cannot reveal it. When no account matches, the outcome is `NoRecipient` and no delivery exists. An address that may belong to no account is logged only as a lowercase SHA-256 hash of the trimmed, lower-cased address (`NotificationAddressHash`), never in clear. Account emails are never shown in the notification center (`ShowInCenter = false`), so a request leaves no trace a user could probe.

### Content minimization

Types in the Security category (password changed, two-factor enabled or disabled, recovery codes regenerated) set `MinimizeSensitiveContent`. Their email says what happened, when, and to sign in and review. It does not carry device or location detail beyond the declared variables, and never a code, a token or a credential. A support request is sent only to the support mailbox, whose address comes from server configuration and is never stored on a delivery or returned by an API.

### Masking in admin views

Administrators see a recipient as the first letter and the domain (`m•••@bimcatalyst.com`) and a name as initials. Support-mailbox deliveries have no address at all. Admin delivery views contain no rendered body, token or link, and omit the message body for Security and Account notifications. Failure text is fixed wording chosen by `SmtpFailureClassifier`, and the stored provider response is the numeric SMTP status code only, because exception messages routinely carry the server banner, a recipient address or a login name.

### Logic-free templates and encoding

Templates cannot run code: the only syntax is `{{ name }}` against the type's declared variables, and any other brace sequence, HTML, raw URL or unknown variable is rejected on save (422). The renderer substitutes first and encodes after. In-app output is plain text that the client renders as text, never HTML, so a variable holding markup shows up literally. In the email HTML part every value is HTML-encoded for its context, so a variable cannot inject markup; the plain-text part carries the same values raw. Links in a notification are built from the type's own route template with each value URL-encoded, and a template that would leave the app is rejected. Only an administrator holding the manage permission can create or publish a template, a published version is immutable, and every change is audited.

### Subject and header injection

Every value that reaches a header, the subject above all, has CR, LF, every control character and the Unicode line and paragraph separators removed and runs of whitespace collapsed. The `Message-ID` is built from the delivery id, never from input.

### Sandboxed template preview

The preview HTML is produced by the production renderer and shown in an `<iframe sandbox="" srcdoc=...>`: the empty `sandbox` attribute disables scripts, forms, popups and same-origin access. The client never injects the returned HTML into the page. A sensitive link renders as the fixed sample `https://example.invalid/sample-link`, so a preview cannot expose a real token. A test send goes only to the calling administrator's own verified address, with sample values and the sample link, and is capped by the `notifications-test-send` policy (10 per hour).

### Secrets

SMTP host, credentials and the support mailbox address are read only from server configuration (`Smtp`, user secrets, the untracked `appsettings.Production.json`). They are not in the database, a response, a template, a delivery row, the audit log or a log line. The hub adds no new secret.

### Ownership and real-time access

A user can read, mark read or delete only their own notifications. Another user's id and an owner-deleted id both return 404, so existence is not revealed. The notification hub joins the connection to a group derived from the authenticated user id, never a client-supplied one, and has no client-invocable methods. Types that point at an item re-check at dispatch that the recipient can still see it.

### Audit coverage

`NotificationAuditLog` is append-only and retention never deletes it. It records every state-changing administrator action: `TemplateDraftSaved`, `TemplateVersionPublished`, `TemplateVersionArchived`, `TemplateTestSent`, `DeliveryRetried`, `DeliveriesBulkRetried`, `AnnouncementPublished` and `LocalizationSettingChanged`, plus the approval history (`ApprovalNotificationCreated`, `...Delivered`, `...Read`). Read-only admin views of delivery data are audited too (`StatisticsViewed`, `ChannelsViewed`, `DeliveriesViewed`, `DeliveryViewed`, `AuditViewed`, `TemplatesViewed`, `TemplateViewed`, `TemplateVersionViewed`), at most once per administrator per resource per hour. These rows are written by a MediatR pipeline behavior in its own scope, and if the audit write fails the view fails too: an unaudited view of delivery data is not returned. Details hold a safe before and after summary, never a token, credential or rendered body.

### Permissions

`admin.notifications.view` allows the read-only screens (statistics, channels, deliveries, templates, announcements, localization setting, audit). `admin.notifications.manage` allows retrying deliveries, creating, editing, publishing and archiving templates, sending a template test, publishing announcements and changing the localization setting. The server checks each endpoint independently; holding manage does not imply view on the server. Both are catalogue permissions assigned through role management, and the admin endpoints are also covered by the `admin-endpoints` rate limit.

### Delivery safety

Email is sent at most once per delivery: a delivery whose outcome is unknown after a crash is recorded as `AmbiguousOutcome` and never resent automatically. A send limiter caps outbound mail at the mail host's allowance and reserves capacity for account emails, so a bulk announcement cannot delay a password reset. Announcements are plain text, never HTML, and only an administrator can publish one.

---

# 37. AI Provider Security

Never expose provider credentials.

Monitor provider usage.

Track:

* Token usage
* Failures
* Costs
* Abuse patterns

Support rapid provider credential rotation.

---

# 38. Privacy

Collect only data necessary for platform functionality.

Avoid retaining unnecessary prompts, files, or metadata.

Support future data export and deletion capabilities.

---

# 39. Security Testing

Security verification should include:

* Static analysis
* Dependency scanning
* Secret scanning
* Authentication testing
* Authorization testing
* File upload testing
* Prompt injection testing
* Penetration testing (where appropriate)

---

# 40. Incident Response

Prepare procedures for:

* Credential compromise
* Data exposure
* Account takeover
* Malicious uploads
* AI abuse
* Service outages

Maintain an incident log.

---

# 41. AI Coding Agent Security Rules

AI coding assistants MUST:

* Never bypass authentication.
* Never bypass authorization.
* Never disable validation.
* Never hardcode secrets.
* Never expose provider keys.
* Never log confidential information.
* Never weaken security for convenience.
* Recommend secure defaults.
* Flag security trade-offs before implementation.

---

# 42. Security Review Checklist

Every pull request should verify:

* Authentication reviewed
* Authorization reviewed
* Input validation complete
* Output encoding reviewed
* Logging appropriate
* Secrets protected
* Error handling secure
* Dependencies reviewed
* Tests updated
* Documentation updated

---

# 43. Definition of Secure

A feature is considered secure only when:

* Authentication requirements are satisfied.
* Authorization is enforced.
* Inputs are validated.
* Outputs are safely rendered.
* Sensitive data is protected.
* Secrets are never exposed.
* Logging follows policy.
* Automated security checks pass.
* Security review is complete.
* No known critical vulnerabilities remain.

Security is not a one-time milestone—it is a continuous engineering practice that applies to every specification, every implementation, and every release of the Ask Lucy platform.
