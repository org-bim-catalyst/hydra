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

Passwords SHOULD satisfy configurable requirements, such as:

* Minimum length
* Mixed character classes (if enabled)
* Password history (future)
* Account lockout after repeated failures
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
