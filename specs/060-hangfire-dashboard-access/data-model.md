# Data Model: Hangfire Dashboard Access from Admin Panel

No new persisted entities, no migration. The one conceptual entity from the spec (Key
Entities: "Dashboard access session") is realized as a stateless, signed token — not a database
row — so there is nothing to store, index, or garbage-collect.

## Dashboard access session (conceptual — not persisted)

Carried as the value of the `askLucyHangfireSession` cookie (name finalized alongside
`RefreshTokenCookie` in implementation).

| Field | Type | Notes |
|---|---|---|
| `sub` | string (user id) | Same claim shape `ITokenService.GenerateAccessToken` already emits. |
| `role` | string | The caller's role at mint time (`Administrator` or `Super User`) — already required to reach the minting endpoint's `[Authorize]` policy in the first place. |
| `purpose` | string, fixed value `hangfire-dashboard` | Rejected by `OnMessageReceived` if absent or mismatched — see research.md Decision 2. Prevents replay as a general bearer token. |
| `exp` | JWT standard claim | 30 minutes from mint (research.md Decision 4). |
| signature | HMAC-SHA256 | Same signing key already used for the SPA's normal access tokens — no new secret. |

**Validation rules**:
- Signature and `exp` validated by the existing `TokenValidationParameters` already configured
  for the default JWT Bearer scheme — no new validation logic.
- `purpose` claim checked in `OnMessageReceived`, scoped to requests under `/hangfire` only.
- Role membership (`Administrator` or `Super User`) re-checked by the existing, unmodified
  `HangfireDashboardAuthorizationFilter` against the resulting `HttpContext.User` — the token's
  `role` claim is informational/auditing; authorization is still enforced independently, not
  trusted blindly from the token payload.

**State transitions**: None — the token is immutable once minted and simply expires. No
revocation list; the 30-minute window plus `Path=/hangfire` scoping is the accepted blast-radius
bound (documented in research.md).
