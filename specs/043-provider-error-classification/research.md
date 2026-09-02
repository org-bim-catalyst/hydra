# Phase 0 Research: AI Provider Failure Classification & Accurate Health Reporting

**Feature**: `043-provider-error-classification` | **Date**: 2026-08-29

No `NEEDS CLARIFICATION` markers entered this phase — the five open decisions were settled in `/speckit-clarify` (see spec.md § Clarifications). What follows resolves the *technical* unknowns those answers created.

---

## Decision 1 — Classification vocabulary lives in `Application`, response parsing in `Infrastructure`

**Decision**: Add `AiProviderFailureKind` (a plain enum) to `Application/Abstractions/IAIProvider.cs` beside the exception types that already live there. Parsing an `HttpResponseMessage` into that enum happens in a new `Infrastructure/Ai/AiProviderResponseClassifier`.

**Rationale**: Constitution §3 forbids `Application → HttpClient`. The vocabulary is a domain-of-discourse concern the Application layer must speak (handlers, DTOs, health records); the parsing is vendor I/O detail. Splitting at that line keeps "swap a provider = add an Infrastructure class + DI registration" true (§3 Infrastructure isolation).

**Alternatives considered**:
- *Classify in the Application layer from a status code + reason string passed up* — would require every provider to pre-parse anyway, duplicating the work and leaking HTTP semantics into Application.
- *One classifier per provider, no shared helper* — four divergent implementations of the same nine-way decision; the divergence between the existing four `EnsureSuccessAsync` bodies is exactly how this bug arose. Violates DRY (§III).

---

## Decision 2 — An abstract exception base carrying `Kind`, not a rewrite of the four existing types

**Decision**: Introduce `public abstract class AiProviderException(string message, AiProviderFailureKind kind, Exception? inner) : Exception` exposing `Kind` and a nullable `RetryAfter`. Re-parent the four existing sealed types (`AiProviderUnavailableException`, `AiProviderAuthenticationException`, `AiProviderRateLimitedException`, `AiProviderRequestInvalidException`) onto it, each fixing its own `Kind`. Add five new sealed subtypes for the classifications that have none today:

| New type | `Kind` |
|---|---|
| `AiProviderQuotaExhaustedException` | `QuotaExhausted` |
| `AiProviderUsageRestrictedException` | `UsageRestricted` (billing disabled, API not enabled for project) |
| `AiProviderCredentialUnreadableException` | `CredentialUnreadable` |
| `AiProviderNotConfiguredException` | `NotConfigured` |
| `AiProviderResponseInvalidException` | `ResponseNotUnderstood` |

**Rationale**: Every existing `catch (AiProviderAuthenticationException)` site — there are several inside the providers' own retry logic, plus `TextToSpeechStreamer`, `PromptNodeExecutor`, `CreateSpeechToTextSessionCommandHandler` — keeps compiling and behaving identically, because re-parenting a type is source-compatible for catch clauses. Simultaneously, `ProblemDetailsMiddleware` gains one `AiProviderException ex` arm that switches on `ex.Kind`, replacing five near-identical arms. Smallest verified change that unlocks the whole feature.

**Alternatives considered**:
- *One exception type with a `Kind` property, deleting the four* — breaks every catch-by-type site across six files for no gain; the type identity is load-bearing in the providers' retry filters.
- *Keep types flat, add a `Kind` to each independently* — no single place to read the classification; middleware and health service each re-derive it from the type. Rejected as a DRY violation.

---

## Decision 3 — Vendor reason codes: which field carries the truth, per provider

**Decision**: Classify from the vendor's machine-readable reason first, HTTP status second (FR-002).

| Vendor | Where the reason lives | Signals that matter |
|---|---|---|
| **Google Gemini** | `error.status`, plus `error.details[]` typed entries | `UNAUTHENTICATED` / `API_KEY_INVALID` → CredentialRejected. `PERMISSION_DENIED` with `SERVICE_DISABLED` or `BILLING_DISABLED` → UsageRestricted. `RESOURCE_EXHAUSTED` + a `google.rpc.QuotaFailure` detail → QuotaExhausted. `RESOURCE_EXHAUSTED` without it → RateLimited. `UNAVAILABLE`/`INTERNAL`/`DEADLINE_EXCEEDED` → Unavailable. |
| **OpenAI** | `error.type`, `error.code` | `insufficient_quota` / `billing_hard_limit_reached` → QuotaExhausted. `invalid_api_key` → CredentialRejected. `rate_limit_exceeded` → RateLimited. |
| **Anthropic** | `error.type` | `authentication_error` → CredentialRejected. `permission_error` → UsageRestricted. `rate_limit_error` → RateLimited. `overloaded_error`/`api_error` → Unavailable. `invalid_request_error` → RequestInvalid. |
| **OpenRouter** | OpenAI-compatible body; HTTP 402 is distinctive | `402` → QuotaExhausted (credits exhausted). Otherwise the OpenAI table. |

**Rationale**: This is the finding that makes the current behaviour actively misleading — a billing-disabled Google project returns `403`, which today maps to "the provider rejected the configured credential. An administrator needs to check the provider's API key." The key is fine; billing is off. Status alone cannot separate them.

**Tie-break precedence** (FR-003), applied top-down so identical responses always classify identically:

```
CredentialUnreadable > NotConfigured > CredentialRejected > UsageRestricted
  > QuotaExhausted > RateLimited > RequestInvalid > Unavailable > ResponseNotUnderstood
```

**Ambiguity rule**: where a vendor does not separate a short-term rate limit from an exhausted longer-term quota, classify as `RateLimited` — the broader, less alarming condition — per the spec's standing assumption. Escalate to `QuotaExhausted` only on positive evidence (a `QuotaFailure` detail, `insufficient_quota`, HTTP 402).

**Alternatives considered**: *Status-code-only mapping* — the status quo, and the cause of the misleading message. *Regex over the body text* — brittle across vendor copy changes; the structured reason fields are stable contract.

---

## Decision 4 — Close the three unmapped-exception holes on the catalog path

**Decision**: Route `ListAvailableModelsAsync` through the same retry/translate wrapper the chat path uses, and convert three currently-escaping exception types at their source:

1. `credentialProtector.Unprotect(...)` → wrap in try/catch for `CryptographicException` → `AiProviderCredentialUnreadableException`. **This is the most likely cause of the reported symptom**: it explains the generic 500 on sync *and* the red Unhealthy chip on a provider whose credential shows Configured, simultaneously. It occurs when the Data Protection key ring changes — e.g. a deploy that replaced the key directory.
2. `TaskCanceledException` / `OperationCanceledException` where the **caller's** token did not fire → `AiProviderUnavailableException`. Where it did fire, rethrow: a user cancellation is not a provider failure (FR-035).
3. `JsonException`, and a missing/misshapen expected property (`models`, `data`), → `AiProviderResponseInvalidException`. Note `GetProperty` throws `KeyNotFoundException`, which today maps to **404 "Not found"** — a wrong answer that looks deliberate.

**Rationale**: FR-004/005/006/007 require these three to stop being "internal application error". Each is a one-line conversion at a known site.

**Alternatives considered**: *Add `CryptographicException`/`JsonException` arms to `ProblemDetailsMiddleware`* — would fix the message but classify at the wrong layer, and the middleware cannot know which provider was involved for the health record or the log. Rejected.

---

## Decision 5 — Disclosure is gated on the administrator role, in the middleware

**Decision**: `ProblemDetailsMiddleware` emits the specific classified `detail` plus a `providerFailure` extension member **only** when the principal is in `Administrator` or `Super User` (the same test `Program.cs:146` already applies). Every other principal receives today's generic detail and no extension.

**Rationale**: FR-015a requires the classification to reach administrators only, and an end user could otherwise read `"providerFailure": "quota-exhausted"` from network devtools. Gating in the one cross-cutting place beats duplicating the check per endpoint (constitution §3). The role names are already established; no new policy.

**Alternatives considered**:
- *Gate by route prefix (`/api/v1/admin/...`)* — brittle, and the chat path can raise the same exceptions.
- *Always emit the code, vary only the prose* — still discloses the condition to non-administrators.

---

## Decision 6 — Health records the `Kind`; staleness is computed, not stored

**Decision**: Add nullable `HealthFailureKind` + `HealthFailureReason` to both `AIProvider` (current state) and `ProviderHealthCheck` (append-only history), alongside the existing tri-state and `IsHealthy`/`Detail` — the augment shape settled in clarification Q1. The admin DTO additionally returns a computed `healthStaleAfterUtc` = `checkedAtUtc + 3 × interval`.

**Rationale**: Returning an absolute horizon rather than a boolean lets the client re-evaluate staleness as the page sits open, with no polling and no server/client clock-skew arithmetic on an interval. It also keeps FR-019's "derived from the interval, never a fixed absolute" property on the server where the interval is configured.

The interval lives in `ProviderHealthCheckOptions` (Infrastructure), which Application may not read. Introduce `IProviderHealthFreshnessPolicy` in `Application/Abstractions`, implemented in Infrastructure over those options — the same pattern the codebase already uses to expose infrastructure configuration to handlers.

**Alternatives considered**: *Store an `IsStale` flag* — wrong by construction; staleness is a function of *now*, not of write time. *Return the interval and let the client multiply* — pushes the 3× policy into two codebases.

---

## Decision 7 — `CheckHealthAsync` returns a result, not a bool

**Decision**: Change `IAIProvider.CheckHealthAsync` to `Task<ProviderHealthResult>` where `ProviderHealthResult(bool IsHealthy, AiProviderFailureKind? Kind, string? Reason)`. Providers implement it by letting the shared classifier throw and catching `AiProviderException` to build the result.

**Rationale**: The bool is what discards the reason. There is exactly **one** caller (`ProviderHealthCheckHostedService:75`), so the signature change is contained; the on-demand command becomes the second. Returning a value rather than throwing keeps the hosted service's per-provider loop straightforward and preserves FR-023 (a mechanism failure — DB unreachable — is distinguishable from a provider failure, because it surfaces as an exception from the loop rather than an unhealthy result).

**Alternatives considered**: *Keep `Task<bool>` and have callers re-probe for a reason* — a second live API call per check. *Let `CheckHealthAsync` throw the classified exception* — forces the caller into exception-driven control flow for an expected, routine outcome.

---

## Decision 8 — On-demand re-check: existing rate limiting is the concurrency guard

**Decision**: `POST /api/v1/admin/ai/providers/{id}/actions/check-health`, a MediatR command that performs one live probe, writes one `ProviderHealthCheck` row, updates the provider's current state, and returns the classified result. No new throttling mechanism: the controller already carries `[EnableRateLimiting("admin-endpoints")]` and `[Authorize(Policy = "AdministratorOrSuperUser")]`. The UI disables the trigger while the mutation is pending.

**Rationale**: This resolves the item `/speckit-clarify` left Deferred. FR-025 requires that probes not be *unbounded*; a per-user rate-limit policy already bounds them. Adding a distributed per-provider lock would be new machinery for a hazard the existing policy covers — YAGNI (§III).

**Alternatives considered**: *A per-provider in-flight semaphore* — single-instance-only correctness, and this app is deployed as a single instance today; the rate limiter is the honest bound. Revisit if the app is ever scaled out, at which point the semaphore would be wrong anyway.

---

## Decision 9 — Optional token limits follow the existing `Pricing` precedent

**Decision**: `AIModel.ContextWindowTokens` and `MaxOutputTokens` become `int?`. The `<= 0` rejection in `AIModel.Create` applies only to a *supplied* value. `ProviderModelInfo`'s two fields become `int?` and the providers stop substituting `0`. `AdminAiModelDto` / `ModelSummaryDto` carry `int?`.

**Rationale**: The codebase already solved this exact problem one field over — `AIModelConfiguration` maps the optional `Pricing` owned type with the comment *"null on the entity means 'pricing unknown' (FR-022) — EF maps that as both columns being NULL, never a fabricated 0."* Token limits get the same treatment, for the same reason. Verified blast radius: both fields are read by **two display DTOs and nothing else** — no chat, context-assembly, or token-budgeting path consumes them — so making them optional constrains no behaviour (FR-030).

**Alternatives considered**: *`0` as sentinel* — the sentinel that caused this bug; every read site must then honour a meaning the type does not express. *Require entry at apply time* — would force hand-typing two numbers for ~97 rows before any could be added.

**UI wording note**: the model list already renders `Unknown` for absent pricing. FR-029a forbids reusing that word here, so token limits render as **"Not published by the vendor"**. Pricing's existing wording is out of scope and stays as-is.

---

## Decision 10 — The vision budget is a linked CTS in the analyzer, not an HttpClient timeout

**Decision**: Add `BoundaryScoringOptions.VisionTimeoutSeconds` (default 30). `GeminiBoundaryVisionAnalyzer` creates a `CancellationTokenSource.CreateLinkedTokenSource(callerToken)` with that timeout and passes the linked token to the HTTP call. On completion, distinguish the two cancellation causes: caller token fired → rethrow `OperationCanceledException` (FR-035); only the budget fired → return `BoundaryVisionAnalysis.NotConfigured("… timed out after 30s")`.

**Rationale**: The shared `GoogleGemini` named client is configured with a 2-minute timeout and is used by both the chat provider and the analyzer; lowering it would wrongly cap chat. A linked CTS scopes the budget to this one call site — precisely the pattern `DependencyInjection.cs` already documents for the `Mcp` client ("HttpClient.Timeout here is only an outer safety net; the precise per-call bound is enforced via a linked CancellationToken at the call site").

**On the 30s value**: chosen in clarification over 15s because this host has twice produced false "unavailable" results from 15s timeouts — Overpass and Geocoding were both widened to 30s after exactly that failure. A multimodal call carrying a base64 satellite image is heavier than either.

**Alternatives considered**: *A dedicated named HttpClient with a 30s timeout* — works, but `HttpClient.Timeout` surfaces as `TaskCanceledException` indistinguishable from caller cancellation, which is precisely the distinction FR-035 requires.

---

## Decision 11 — Follow the vendor's own list pagination before computing a diff

**Decision**: `ListAvailableModelsAsync` must retrieve every page of the vendor's model list before a sync diff is computed (FR-028a), requesting the largest page size each vendor permits and looping on its continuation field, with a page cap that raises `AiProviderResponseInvalidException` rather than looping forever.

**Rationale**: Verified by inspection — all four adapters issue a bare `GET models` and read only the first payload; there is no `pageSize`, `pageToken`, `after_id`, or `has_more` handling anywhere in `src/AskLucy.Infrastructure/Ai/`. Gemini and Anthropic both paginate their model lists; OpenAI and OpenRouter return theirs complete. A truncated list is worse than an error, because the diff looks successful while silently omitting models — and it directly undermines SC-006, whose measurable claim is that a sync adds *every* selected row.

Corroborating evidence from the reported session: the OpenAI dialog listed 97 models (consistent with an unpaginated endpoint returning everything) and Anthropic listed 12 (plausibly under its page limit). Gemini's dialog never rendered a diff — it failed with the generic 500 — so **no observation exists for the one provider most likely to be truncated**. That absence of evidence is the reason this is tasked as verify-then-implement rather than implement-from-memory.

**Implementation note**: T020a must confirm each vendor's *current* pagination contract — parameter name, default and maximum page size, continuation field — against live API documentation, and record the findings back into this decision. Page-size defaults are exactly the kind of detail that drifts, and a remembered value silently producing a short list would reproduce this very bug.

**Alternatives considered**:
- *Request one large page and assume completeness* — relies on a maximum that can change, and fails silently when it does.
- *Leave pagination unhandled and document the limit* — rejected: the spec's edge case now commits to retrieving every page, and US4's entire value is that a full vendor catalog becomes addable in one action.

---

## Cross-cutting: what is deliberately **not** changed

- **End-user chat messaging** — unchanged by FR-015a. The classifier runs on the chat path, but only to improve server-side diagnostics and health data.
- **Pricing's "Unknown" wording** in the model list — pre-existing, out of scope.
- **The site-boundary fallback logic itself** — verified already correct; this feature adds the time budget and regression tests only.
- **The 2-minute background health interval** — unchanged; only what it records changes.
- **Historical `ProviderHealthCheck` rows** — not back-classified; they age out of the freshness window naturally.
