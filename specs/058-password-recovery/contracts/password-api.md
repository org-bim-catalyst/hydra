# Contract: Password Recovery & Management API

**Feature**: 058-password-recovery | **Base**: `/api/v1/auth` | **Controller**: `AuthController`

All five endpoints carry `[EnableRateLimiting("auth-endpoints")]`. Failures return RFC 7807
Problem Details from the global exception middleware, per constitution §6.

---

## POST /auth/password/forgot — `[AllowAnonymous]`

Request a reset link.

```jsonc
// Request
{ "email": "user@example.com" }
```

**Response — always `202 Accepted`, empty body.** Identical for a valid account, an unknown
address, an unconfirmed account, a locked-out account and an email-throttled caller (FR-003).

| Condition | Status | Body |
|-----------|--------|------|
| Any syntactically valid email | 202 | *(empty)* |
| Missing or malformed `email` | 400 | Problem Details, `title: "Validation failed"` |
| Per-IP rate limit exceeded | 429 | Problem Details, `title: "Too many requests"` |

Note the asymmetry: the per-IP limiter may return 429, the per-email throttle never does — see
[research.md](../research.md) Topic 3.

The 202 is written **without awaiting email dispatch**; the reset message is enqueued as a
background job. A caller therefore cannot infer account existence from response latency either.

---

## POST /auth/password/reset/validate — `[AllowAnonymous]`

*(Added 2026-09-19 — post-release amendment, FR-019.)*

Ask whether a link is still redeemable, **without consuming it**.

```jsonc
// Request
{
  "userId": "…",   // from the link
  "token":  "…"    // from the link, URL-decoded by the client
}
```

| Condition | Status | Body |
|-----------|--------|------|
| Link still redeemable | 204 | *(empty)* |
| Token unknown, consumed, superseded, expired, or email changed since issue | 400 | Problem Details, `title: "Reset link is no longer valid"`, `detail: "This password reset link has expired or has already been used. Request a new one to continue."` |
| Malformed request | 400 | Problem Details, `title: "Validation failed"` |

Called by the reset page on mount, so a stale link is refused on arrival rather than after the user
has chosen and confirmed a new password. Same single indistinguishable 400 as the redemption
endpoint below — it reports on the link, never on the account behind it, so it is no more of an
oracle than clicking the link itself would be.

`POST`, not `GET`: a `GET` would put the token in a query string, which proxies, browser history and
server access logs all record in full.

---

## POST /auth/password/reset — `[AllowAnonymous]`

Redeem a link and set a new password.

```jsonc
// Request
{
  "userId": "…",              // from the link
  "token":  "…",              // from the link, URL-decoded by the client
  "newPassword": "…"
}
```

| Condition | Status | Body |
|-----------|--------|------|
| Success | 204 | *(empty)* |
| Token unknown, consumed, superseded, expired, or email changed since issue | 400 | Problem Details, `title: "Reset link is no longer valid"`, `detail` explains how to request a new one. One indistinguishable response for all six causes — the specific cause goes to the log, not the client |
| New password violates policy | 400 | Problem Details, `title: "Password does not meet requirements"`, `errors` carries the per-rule Identity failures so the UI can show which rule failed (FR-007) |
| Malformed request | 400 | Problem Details, `title: "Validation failed"` |

On success: password updated, token consumed, every refresh token for the account revoked
(FR-009), notification email sent (FR-011). Two-factor enrolment is untouched (FR-012) — the
response never returns tokens, so the client must sign in afresh and satisfy 2FA there.

---

## POST /auth/change-password — `[Authorize]` *(exists; behaviour extended)*

```jsonc
// Request — `currentPassword` is now optional
{ "currentPassword": "…", "newPassword": "…" }
```

| Condition | Status | Body |
|-----------|--------|------|
| Success | 204 | *(empty)* |
| Wrong `currentPassword` | 400 | Problem Details, `title: "Current password is incorrect"` |
| `currentPassword` omitted and the account **has** a password | 400 | Problem Details, `title: "Current password is required"` |
| New password violates policy | 400 | as `/password/reset` above |
| New password equals current | 400 | Problem Details, `title: "New password must differ from the current one"` |

Setting a first password (account has no password, `currentPassword` omitted) succeeds with 204 —
this is US4's settings path (FR-014).

**Behaviour change:** on success the handler now revokes every refresh token for the account
*except* the calling session's token family (FR-010) and sends the notification email (FR-011).
The response shape is unchanged, so existing clients keep working.

---

## GET /auth/password/status — `[Authorize]`

Lets the settings UI choose between "change password" and "set password" without guessing.

```jsonc
// 200 OK
{ "hasPassword": true }
```

---

## Frontend contract

New client functions in `features/auth/api/authApi.ts`, all going through `apiFetch`
(`isAuthFlow: true` for the two anonymous ones, matching `login`/`confirmEmail`):

```ts
requestPasswordReset(email: string): Promise<void>
resetPassword(userId: string, token: string, newPassword: string): Promise<void>
getPasswordStatus(): Promise<{ hasPassword: boolean }>
changePassword(currentPassword: string | undefined, newPassword: string): Promise<void>  // signature widened
```

New routes in `routes/router.tsx`, both lazy and both with `errorElement: <ErrorPage />`, matching
the existing `/confirm-email` entry:

| Path | Page | Notes |
|------|------|-------|
| `/forgot-password` | `ForgotPasswordPage` | Linked from `LoginPage` via `FormField`'s existing right-aligned label slot |
| `/reset-password` | `ResetPasswordPage` | Reads `userId` and `token` from the query string |

Every one of these calls surfaces failure as visible UI — inline `Alert` or field error, never a
console log alone (FR-017, constitution §II.VIII).

---

## Email contract

Both messages are rendered in-memory per request and HTML-encode all user-supplied values, per
`RegisterCommandHandler`'s pattern.

| Message | Trigger | Link |
|---------|---------|------|
| "Reset your Ask Lucy password" | Eligible forgot-password request | `{FrontendBaseUrl}/reset-password?userId=…&token=…`, both values `Uri.EscapeDataString`-encoded |
| "Your Ask Lucy password was changed" | Any successful reset or change | No link; states the UTC time and tells the reader to contact support if it was not them |
