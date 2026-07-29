# API Contract: `/api/v1` (Legacy Modernization)

Versioned REST API replacing today's unversioned, mostly-unauthenticated endpoints (FR-020). All responses use RFC 9457 Problem Details on error (FR-021). All AI-invoking endpoints require authentication (FR-015) and are rate-limited (FR-023, `research.md` Topic 3).

## Authentication

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/api/v1/auth/register` | Anonymous | Register; triggers email confirmation. Preserves FR-009. |
| POST | `/api/v1/auth/login` | Anonymous | Email/password login. Returns a 2FA challenge response if TOTP is enabled (FR-011), otherwise a JWT access token + refresh token. |
| POST | `/api/v1/auth/login/2fa` | Anonymous (holds a short-lived pre-auth token from `/login`) | Completes TOTP or recovery-code challenge (FR-011). |
| POST | `/api/v1/auth/refresh` | Anonymous (holds a valid refresh token) | Rotates the refresh token; reuse of an already-rotated token revokes the whole family (`research.md` Topic 1). |
| POST | `/api/v1/auth/logout` | Authenticated | Revokes the current refresh token. |
| GET/POST | `/api/v1/auth/external/{provider}` | Anonymous | Google/Facebook OAuth challenge + callback (FR-010). |
| POST | `/api/v1/auth/2fa/enable`, `/api/v1/auth/2fa/disable`, `/api/v1/auth/2fa/recovery-codes` | Authenticated | TOTP enrollment/recovery-code management (FR-011), same capability as today's scaffolded Identity UI, exposed as API endpoints instead of Razor Pages. |

## Users / Profile

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/v1/users/me` | Authenticated | Returns the caller's own profile **DTO** (never the raw Identity entity — FR-019). |
| PATCH | `/api/v1/users/me` | Authenticated | Updates the caller's own profile fields only (FR-018). |
| PUT | `/api/v1/users/me/avatar` | Authenticated | Uploads a new avatar; stored per `research.md` Topic 6. |
| GET | `/api/v1/users/me/avatar?exp=...&sig=...` | Signed URL (short-lived HMAC) | Serves the avatar file; never exposes the physical path (FR-025). |
| GET | `/api/v1/users` | Authenticated + Administrator/Super User role | Admin user list, **DTO-projected** (no password hash/security stamp — closes the current exposure, FR-017/FR-019). Evolved by SPEC-001 to add search/sort/pagination — see `specs/001-admin-dashboard/contracts/api-v1.md`. |
| PATCH | `/api/v1/users/{id}` | Authenticated + Administrator/Super User role | Admin update via an explicit, validated command — no client-supplied entity overposting (closes current mass-assignment gap). |

**Additive in SPEC-001** (`specs/001-admin-dashboard/contracts/api-v1.md`): dashboard summary endpoint plus per-user lock/unlock/role-change/force-2FA-reset/delete actions — not part of this migration's scope, listed here only for cross-reference.

## Chats

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/v1/chats` | Authenticated | Lists **only the caller's own** chats (closes current cross-user enumeration — FR-018). |
| POST | `/api/v1/chats` | Authenticated | Creates a named chat entry (FR-008). |
| PATCH | `/api/v1/chats/{id}` | Authenticated, owner-only | Renames a chat (FR-033). 403/404 if the chat isn't the caller's. |
| DELETE | `/api/v1/chats/{id}` | Authenticated, owner-only | Soft-deletes a chat (FR-033). 403/404 if the chat isn't the caller's. |

## AI

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/api/v1/ai/chat` (SSE) | Authenticated | Streams a chat completion via SSE (`research.md` Topic 2), replacing today's blocking `/openai/chat`. Rate-limited (FR-023). Retries once on transient provider failure before returning a Problem Details error (FR-032). |
| POST | `/api/v1/ai/translate` | Authenticated | Equivalent to today's `/openai/translate` (FR-003). Rate-limited. |
| POST | `/api/v1/ai/images` | Authenticated | Equivalent to today's `/openai/draw` (FR-002). Rate-limited. |
| POST | `/api/v1/ai/transcriptions` | Authenticated (multipart file upload) | Equivalent to today's `/openai/transcript` (FR-004). Rate-limited. |

## Error format (all endpoints)

```json
{
  "type": "https://asklucy.io/problems/ai-provider-unavailable",
  "title": "AI provider unavailable",
  "status": 502,
  "detail": "The AI service could not process your request. Please try again.",
  "traceId": "00-4bf9...-01"
}
```

No endpoint ever returns a raw exception message, stack trace, or provider-native error payload (FR-021, constitution §8/§29).

## Explicitly not part of this contract

No `/api/v1/conversations/{id}/messages`, `/api/v1/knowledge-bases`, `/api/v1/agents`, `/api/v1/models` (model switching), or `/api/v1/subscriptions` endpoints are introduced — all reserved for the future specifications listed in `spec.md` § Future Specifications.
