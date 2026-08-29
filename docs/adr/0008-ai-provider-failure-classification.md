# ADR 0008: AI Provider Failure Classification

**Status:** Accepted

**Date:** 2026-08-29

**Feature:** [specs/043-provider-error-classification](../../specs/043-provider-error-classification/spec.md)

## Context

Provider-side failures reached administrators as the API's unmapped-exception fallback — *"An unexpected error occurred. Please try again."* Two independent causes:

1. The model-catalog listing path ran outside the retry/translate wrapper the chat path used. `CryptographicException` (a Data Protection key ring replaced by a deployment), `TaskCanceledException` (a request timeout), and `JsonException`/`KeyNotFoundException` (an unexpected response shape) all escaped untranslated and hit the generic 500. `KeyNotFoundException` was worse than generic — it mapped to a confident 404 "Not found".

2. Each of the four provider adapters derived its own classification from the HTTP status alone, in four divergent hand-written `EnsureSuccessAsync` bodies. Google returns `403` for an invalid key, a disabled API, **and** disabled billing; status alone cannot separate them, so a billing-disabled project was reported as *"check the provider's API key"* when the key was perfectly valid.

Provider health compounded both: a bare boolean whose recorded reason was never exposed to the admin DTO, so a quota problem, a wrong key, a disabled billing account and a momentary blip all rendered as the same red "Unhealthy" chip — with a timestamp that could be days old while still reading as current fact.

## Decision

### 1. An abstract exception base carrying `Kind`, not a rewrite

`AiProviderException` exposes an `AiProviderFailureKind` and an optional `RetryAfter`. The four pre-existing sealed types were **re-parented** onto it rather than replaced, and five new subtypes added for the classifications that had none.

Re-parenting is source-compatible for `catch` clauses, so every existing catch-by-type site — inside the providers' own retry filters, plus `TextToSpeechStreamer`, `PromptNodeExecutor` and `CreateSpeechToTextSessionCommandHandler` — kept compiling and behaving identically. At the same time `ProblemDetailsMiddleware` collapsed five near-identical arms into one that switches on `Kind`. Capability up, branching down.

*Rejected:* one exception type with a `Kind` property, deleting the four. It breaks every catch site across six files for no gain, and the type identity is load-bearing in the providers' retry filters.

### 2. The enum lives in `Domain`, the classifier in `Infrastructure`

`AiProviderFailureKind` is persisted by `AIProvider` and `ProviderHealthCheck`. Domain may reference nothing (constitution §3), so the vocabulary a Domain entity stores has to be owned there. Parsing an `HttpResponseMessage` into it is vendor I/O detail and lives in `Infrastructure/Ai/AiProviderResponseClassifier`; Application may not reference `HttpClient`.

This was corrected during implementation — the enum was first placed in `Application/Abstractions`, which does not compile once Domain must persist it.

### 3. Vendor reason first, HTTP status second

The classifier reads `error.status` and `error.details[]` (Google) or `error.type`/`error.code` (OpenAI-shaped envelopes, which Anthropic and OpenRouter also use) **before** falling back to the status code, with a documented precedence for ties.

Where a vendor does not distinguish a short-term rate limit from an exhausted allowance, the broader `RateLimited` is reported; `QuotaExhausted` is raised only on positive evidence (a `google.rpc.QuotaFailure` detail, `insufficient_quota`, HTTP 402). Guessing the more alarming of two readings would send administrators chasing a quota that is fine.

### 4. Disclosure is gated on the administrator role, in the middleware

An administrator receives the classifier's own prose as `detail` plus a machine-readable `providerFailure` extension. Everyone else keeps the pre-existing cause-free message and gets no extension at all.

Without this gate, any end user could read `"kind": "QuotaExhausted"` out of a chat response in devtools. An exhausted commercial allowance and a disabled billing account are tenant operational state, not something to broadcast to every user of the workspace. Gating once in the middleware — which holds `HttpContext` directly — beats duplicating the check per endpoint.

### 5. The vendor body never travels in an exception message

The classifier reads the body to classify, then discards it; the exception carries prose built from the classification alone. The body is recorded server-side, truncated, in a structured log.

This became load-bearing once `AiProviderException.Message` started feeding the administrator-visible `detail`: two ElevenLabs call sites that interpolated their raw response body into an `AiProviderUnavailableException` had to be cleaned up in the same change, because a message that was previously replaced by a fixed string would otherwise have started reaching the client.

## Consequences

**Good.** Adding a tenth classification needs one enum member, one row in the classifier table, and one row in `MapProviderFailure` — no change to any adapter. Adding a fifth provider needs one vendor-reason reader. Health, Problem Details, and logging all read the same `Kind`, so they cannot drift apart.

**Cost.** Nine classifications is more vocabulary than four, and a wrong classification is now a more confident wrong answer than "something failed". The table-driven tests over every documented vendor case exist for exactly that reason.

**Behavioural changes** deliberately introduced, each covered by an updated test that previously asserted the old behaviour:

- "No credential configured" now raises `NotConfigured`, not `CredentialRejected` — a different administrator action.
- A Google `403` with no vendor reason now classifies as `UsageRestricted`, not `CredentialRejected`.
- A vendor's own error text no longer appears in any exception message.

**Not addressed.** OpenAI reads its key from configuration while the other three read the encrypted per-provider credential, so `CredentialUnreadable` cannot arise for it. That inconsistency predates this work.
