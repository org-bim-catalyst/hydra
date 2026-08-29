# Contract: Provider Failure Classification

**Feature**: `043-provider-error-classification`

This is the internal contract between the four provider adapters, the Problem Details boundary, and the health subsystem. It is not itself an HTTP surface — see [admin-provider-health-api.md](./admin-provider-health-api.md) for that.

---

## 1. Classification input → `AiProviderFailureKind`

The shared classifier receives the HTTP status, the response body, and any `Retry-After` header, and yields exactly one kind. Vendor reason first, status second (FR-002).

### Google Gemini

| Status | Vendor signal | Kind |
|---|---|---|
| 400 | `error.status = INVALID_ARGUMENT` with `API_KEY_INVALID` in details | `CredentialRejected` |
| 401 | `error.status = UNAUTHENTICATED` | `CredentialRejected` |
| 403 | `error.details[].reason = SERVICE_DISABLED` | `UsageRestricted` |
| 403 | `error.details[].reason = BILLING_DISABLED` | `UsageRestricted` |
| 403 | `error.status = PERMISSION_DENIED`, no more specific reason | `UsageRestricted` |
| 429 | `error.details[]` contains a `google.rpc.QuotaFailure` | `QuotaExhausted` |
| 429 | `error.status = RESOURCE_EXHAUSTED`, no `QuotaFailure` | `RateLimited` |
| 500/503/504 | `INTERNAL` / `UNAVAILABLE` / `DEADLINE_EXCEEDED` | `Unavailable` |
| other 4xx | — | `RequestInvalid` |
| any | body unparseable, or expected `models` array absent | `ResponseNotUnderstood` |

### OpenAI

| Status | Vendor signal | Kind |
|---|---|---|
| 401 | `error.code = invalid_api_key` | `CredentialRejected` |
| 403 | — | `UsageRestricted` |
| 429 | `error.type = insufficient_quota` | `QuotaExhausted` |
| 429 | `error.code = rate_limit_exceeded` (or absent) | `RateLimited` |
| 400 | `error.type = invalid_request_error` | `RequestInvalid` |
| 5xx | — | `Unavailable` |

### Anthropic

| Status | `error.type` | Kind |
|---|---|---|
| 401 | `authentication_error` | `CredentialRejected` |
| 403 | `permission_error` | `UsageRestricted` |
| 429 | `rate_limit_error` | `RateLimited` |
| 400 | `invalid_request_error` | `RequestInvalid` |
| 529 / 5xx | `overloaded_error`, `api_error` | `Unavailable` |

### OpenRouter

OpenAI-compatible bodies, with one distinctive case: **HTTP 402 → `QuotaExhausted`** (account credits exhausted). Otherwise the OpenAI table applies.

### Non-HTTP failures (all providers)

| Condition | Kind |
|---|---|
| `CryptographicException` from credential decryption | `CredentialUnreadable` |
| No provider row, or `CredentialCiphertext is null` | `NotConfigured` |
| `TaskCanceledException`/`OperationCanceledException` **and** the caller's token did not fire | `Unavailable` |
| `TaskCanceledException`/`OperationCanceledException` **and** the caller's token fired | *not a failure* — rethrow (FR-035) |
| `HttpRequestException` (DNS, TLS, connection reset) | `Unavailable` |
| `JsonException`, or an expected property missing/mistyped | `ResponseNotUnderstood` |

### Precedence

When more than one rule could match, apply top-down (FR-003):

```
CredentialUnreadable > NotConfigured > CredentialRejected > UsageRestricted
  > QuotaExhausted > RateLimited > RequestInvalid > Unavailable > ResponseNotUnderstood
```

Where a vendor does not distinguish a rate limit from an exhausted quota, classify as `RateLimited`; escalate to `QuotaExhausted` only on positive evidence.

---

## 2. Kind → exception type

| Kind | Exception |
|---|---|
| `CredentialRejected` | `AiProviderAuthenticationException` *(exists)* |
| `RateLimited` | `AiProviderRateLimitedException` *(exists, carries `RetryAfter`)* |
| `Unavailable` | `AiProviderUnavailableException` *(exists)* |
| `RequestInvalid` | `AiProviderRequestInvalidException` *(exists)* |
| `QuotaExhausted` | `AiProviderQuotaExhaustedException` *(new)* |
| `UsageRestricted` | `AiProviderUsageRestrictedException` *(new)* |
| `CredentialUnreadable` | `AiProviderCredentialUnreadableException` *(new)* |
| `NotConfigured` | `AiProviderNotConfiguredException` *(new)* |
| `ResponseNotUnderstood` | `AiProviderResponseInvalidException` *(new)* |

All nine derive from `abstract class AiProviderException` exposing `Kind` and `RetryAfter`.

---

## 3. Kind → HTTP response

`ProblemDetailsMiddleware` gains one `AiProviderException` arm replacing five.

| Kind | Status | `type` suffix under `https://hydra.bimcatalyst.com/problems/` |
|---|---|---|
| `CredentialRejected` | 502 | `ai-provider-authentication-failed` *(unchanged)* |
| `CredentialUnreadable` | 502 | `ai-provider-credential-unreadable` |
| `NotConfigured` | 502 | `ai-provider-not-configured` |
| `QuotaExhausted` | 429 | `ai-provider-quota-exhausted` |
| `RateLimited` | 429 | `ai-provider-rate-limited` *(unchanged)* |
| `UsageRestricted` | 502 | `ai-provider-usage-restricted` |
| `Unavailable` | 502 | `ai-provider-unavailable` *(unchanged)* |
| `RequestInvalid` | 400 | `ai-provider-request-invalid` *(unchanged)* |
| `ResponseNotUnderstood` | 502 | `ai-provider-response-invalid` |

`Retry-After` continues to be emitted from `RetryAfter` when the vendor supplied one — now for `QuotaExhausted` as well as `RateLimited`.

### Administrator-gated disclosure (FR-015a)

The principal is an administrator when `User.IsInRole("Administrator") || User.IsInRole("Super User")` — the same test `Program.cs:146` applies.

**Administrator** receives the specific `detail` plus:

```json
{
  "type": "https://hydra.bimcatalyst.com/problems/ai-provider-quota-exhausted",
  "title": "AI provider quota exhausted",
  "status": 429,
  "detail": "Google Gemini is configured correctly but its usage quota is exhausted. The provider will work again once the quota resets or is raised.",
  "traceId": "…",
  "providerFailure": {
    "kind": "QuotaExhausted",
    "canAdministratorAct": true,
    "retryAfterSeconds": 3600
  }
}
```

**Everyone else** receives today's generic body for the same failure — no `providerFailure` member, and the pre-existing generic `detail`:

```json
{
  "type": "https://hydra.bimcatalyst.com/problems/ai-provider-rate-limited",
  "title": "AI provider rate limited",
  "status": 429,
  "detail": "The AI provider is rate-limiting requests right now. Please try again shortly.",
  "traceId": "…"
}
```

### Invariants (SC-008, FR-013)

For **every** kind and **every** principal, the response body must contain no credential, no raw vendor response body, no exception type name, and no stack trace. The vendor body is logged server-side, truncated, via a source-generated `LoggerMessage` delegate carrying provider, kind, and vendor reason code as structured fields (FR-014, constitution §14).

---

## 4. Adapter contract change

```
// before
Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

// after
Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default);

public sealed record ProviderHealthResult(bool IsHealthy, AiProviderFailureKind? Kind, string? Reason);
```

`CheckHealthAsync` must not throw for a *provider* failure — it returns an unhealthy result carrying the kind. It may still throw for a failure of the checking mechanism itself, which the caller treats as "no result", never as unhealthy (FR-023).

```
// before
public sealed record ProviderModelInfo(string ModelKey, string DisplayName, int ContextWindowTokens, int MaxOutputTokens, AIModelCapabilities Capabilities);

// after
public sealed record ProviderModelInfo(string ModelKey, string DisplayName, int? ContextWindowTokens, int? MaxOutputTokens, AIModelCapabilities Capabilities);
```
