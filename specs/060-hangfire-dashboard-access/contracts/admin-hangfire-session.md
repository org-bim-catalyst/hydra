# Contract: Admin Hangfire Dashboard Session

## `POST /api/v1/admin/hangfire/session`

Mints a short-lived dashboard-access cookie for the calling administrator and sets it on the
response. No request body.

**Authorization**: `[Authorize(Policy = "AdministratorOrSuperUser")]` (existing policy).

**Rate limiting**: `admin-endpoints` policy (existing).

### Response `204 No Content`

- Sets `Set-Cookie: askLucyHangfireSession=<jwt>; Path=/hangfire; HttpOnly; Secure; SameSite=Lax; Max-Age=1800`.
- Empty body — the client doesn't need the token value, only the fact that the cookie is now
  set, before it opens the new tab.

### Response `401 Unauthorized` / `403 Forbidden`

- Standard Problem Details (`application/problem+json`), same shape as every other protected
  endpoint (§6). No dashboard-specific error shape.

### Response `429 Too Many Requests`

- Standard Problem Details from the `admin-endpoints` rate-limit policy, unchanged from its
  existing behavior on other admin endpoints.

### Client contract

- The caller MUST have already opened the target tab (`window.open('', '_blank')`) *before*
  awaiting this call, to preserve the browser's user-activation window for the pop-up (see
  research.md Decision 1 / spec edge case on pop-up blocking).
- On success (`204`), the caller sets the pre-opened tab's `location.href` to
  `/hangfire?theme=light|dark` (theme value read from the caller's current theme state at click
  time, not fetched from the server).
- On any non-2xx response, the caller MUST close the pre-opened blank tab (avoid leaving a dead
  `about:blank` tab open) and surface a visible error (FR-008) — never a silent failure.

## `GET /hangfire` (existing Hangfire-owned route, unchanged in shape)

Not a new contract — documented here only to record the one behavioral addition: this route now
also authenticates via the `askLucyHangfireSession` cookie (in addition to the pre-existing
`Authorization` header path), per research.md Decision 1. Its authorization outcome
(`HangfireDashboardAuthorizationFilter`) and its own internal contract are unchanged.
