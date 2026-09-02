# Phase 1 Data Model: AI Provider Failure Classification & Accurate Health Reporting

**Feature**: `043-provider-error-classification` | **Date**: 2026-08-29

Three persisted changes, all additive or relaxing. One migration. No table is created and none is dropped.

---

## 1. `AiProviderFailureKind` — new enum (Application vocabulary, persisted as string)

The nine classifications of FR-001. Lives in `src/AskLucy.Application/Abstractions/IAIProvider.cs` beside the exception types it labels.

| Member | Meaning | Administrator can act? |
|---|---|---|
| `CredentialRejected` | Vendor refused the configured key | Yes — replace the key |
| `CredentialUnreadable` | Stored ciphertext could not be decrypted (key ring changed) | Yes — re-enter the key |
| `NotConfigured` | No credential is set for this provider | Yes — set one |
| `QuotaExhausted` | Project/account usage allowance is spent | Wait, or raise the quota |
| `RateLimited` | Short-term throughput limit | Wait |
| `UsageRestricted` | Billing disabled, or API not enabled for the project | Yes — at the vendor console |
| `Unavailable` | Vendor outage, network failure, or timeout | Wait |
| `RequestInvalid` | Vendor rejected this specific request as malformed | Sometimes |
| `ResponseNotUnderstood` | 2xx or error body the adapter could not parse | No — investigate |

`InternalError` is deliberately **not** a member: FR-007 reserves that condition for failures originating inside Ask Lucy, which are represented by the *absence* of an `AiProviderException` and continue to map to the 500 fallback.

**Persistence**: stored via `.HasConversion<string>().HasMaxLength(40)`, matching the existing `AIModelStatus` / `ProviderHealthStatus` convention. Never stored as an ordinal — see the enum-serialization gotcha this repo already learned.

---

## 2. `AIProvider` — current health state (2 new columns)

`src/AskLucy.Domain/Ai/AIProvider.cs`

| Field | Type | Null? | Notes |
|---|---|---|---|
| `HealthStatus` | `ProviderHealthStatus` | no | **Unchanged** — `Unknown` / `Healthy` / `Unhealthy` remains the coarse signal (clarification Q1: augment, not replace) |
| `HealthStatusCheckedAtUtc` | `DateTime?` | yes | **Unchanged** |
| `HealthFailureKind` | `AiProviderFailureKind?` | **yes** | NEW. Non-null only when `HealthStatus == Unhealthy` |
| `HealthFailureReason` | `string?` (≤500) | **yes** | NEW. Administrator-facing prose. Never the raw vendor body |

**Invariant**: `HealthFailureKind` is non-null **iff** `HealthStatus == Unhealthy`. A successful check clears both new fields; this is what stops a stale reason surviving a recovery.

**Mutator change**:

```
UpdateHealthStatus(bool isHealthy, DateTime checkedAtUtc)
  → UpdateHealthStatus(bool isHealthy, DateTime checkedAtUtc, AiProviderFailureKind? kind, string? reason)
```

The method enforces the invariant itself (clearing kind/reason on success) rather than trusting callers — constitution §I, rules belong on the model.

---

## 3. `ProviderHealthCheck` — append-only history (2 new columns)

`src/AskLucy.Domain/Ai/ProviderHealthCheck.cs`. Remains append-only: no soft delete, no mutators, no query filter (its existing EF configuration comment records why).

| Field | Type | Null? | Notes |
|---|---|---|---|
| `IsHealthy` | `bool` | no | **Unchanged** |
| `Detail` | `string?` (≤500) | yes | **Unchanged** — retained for continuity with existing rows |
| `FailureKind` | `AiProviderFailureKind?` | **yes** | NEW |
| `FailureReason` | `string?` (≤500) | **yes** | NEW |

Existing rows keep `FailureKind = NULL`; they are not back-classified (spec Assumptions). The existing `(ProviderId, CheckedAtUtc)` index is unchanged and still serves the "latest check per provider" read.

---

## 4. `AIModel` — token limits become optional

`src/AskLucy.Domain/Ai/AIModel.cs`

| Field | Before | After |
|---|---|---|
| `ContextWindowTokens` | `int`, required, rejected `<= 0` | `int?`, optional; rejected only when **supplied** and `<= 0` |
| `MaxOutputTokens` | `int`, required, rejected `<= 0` | `int?`, optional; rejected only when **supplied** and `<= 0` |

**Validation rule change** in `AIModel.Create`:

```
// before
if (contextWindowTokens <= 0) throw new DomainRuleViolationException("Context window must be greater than zero.");

// after
if (contextWindowTokens is <= 0) throw new DomainRuleViolationException("Context window must be greater than zero when supplied.");
```

`is <= 0` is null-safe: a `null` does not match, so absence passes and a supplied `0` or negative still fails.

**Precedent**: this mirrors the optional `Pricing` owned type in `AIModelConfiguration`, whose own comment states *"null on the entity means 'pricing unknown' — EF maps that as both columns being NULL, never a fabricated 0."*

**Verified blast radius**: these two properties are read by `AdminAiModelDto` and `ModelSummaryDto` and by nothing else. No chat, context-assembly, retrieval, or token-budgeting path consumes them, so optionality constrains no behaviour (FR-030).

**Not changed**: `AIModel.Create` still starts a model `Available`, and `ApplyProviderModelSyncCommandHandler` still immediately sets `Unavailable`. A model with absent limits is therefore added disabled and enabled through the existing status control — no new edit action is required (clarification Q3).

---

## 5. `ProviderModelInfo` — adapter DTO

`src/AskLucy.Application/Abstractions/IAIProvider.cs`

`ContextWindowTokens` and `MaxOutputTokens` become `int?`. Providers stop fabricating `0`:

- **OpenAI** — currently hardcodes `ContextWindowTokens: 0, MaxOutputTokens: 0` for every model; becomes `null, null`.
- **Google Gemini** — currently coerces a JSON `null` limit to `0` via a `ValueKind` guard; the guard stays (it is what stops the `GetInt32()` throw) but now yields `null`.
- **Anthropic / OpenRouter** — same treatment where their lists omit the figures.

---

## 6. `ProviderHealthResult` — new adapter result record

`ProviderHealthResult(bool IsHealthy, AiProviderFailureKind? Kind, string? Reason)`, returned by `IAIProvider.CheckHealthAsync` in place of `bool` (research.md Decision 7).

---

## 7. DTO surface

| DTO | Change |
|---|---|
| `AdminAiProviderDto` | + `HealthFailureKind` (`AiProviderFailureKind?`), + `HealthFailureReason` (`string?`), + `HealthStaleAfterUtc` (`DateTime?`, computed = `checkedAt + 3 × interval`) |
| `AdminAiModelDto` | `ContextWindowTokens`/`MaxOutputTokens` → `int?` |
| `ModelSummaryDto` | same |
| `CheckAiProviderHealthResultDto` | NEW — `HealthStatus`, `HealthFailureKind?`, `HealthFailureReason?`, `CheckedAtUtc`, `HealthStaleAfterUtc` |

`AdminAiProviderDto.FromEntity` currently takes only the entity; it gains an `IProviderHealthFreshnessPolicy` argument (or the precomputed horizon) so the staleness horizon is derived server-side per FR-019.

---

## 8. Migration

**One migration**, `AddProviderFailureClassificationAndOptionalModelLimits`.

**Up**
1. `AIProviders`: add `HealthFailureKind` `nvarchar(40) NULL`, `HealthFailureReason` `nvarchar(500) NULL`.
2. `ProviderHealthChecks`: add `FailureKind` `nvarchar(40) NULL`, `FailureReason` `nvarchar(500) NULL`.
3. `AIModels`: alter `ContextWindowTokens` and `MaxOutputTokens` to `int NULL`.

**Down** (reversible, per constitution §5)
1. `AIModels`: `UPDATE AIModels SET ContextWindowTokens = 0 WHERE ContextWindowTokens IS NULL` (and likewise for `MaxOutputTokens`) **before** altering back to `NOT NULL` — without the backfill the alter fails on any row added after this feature ships. Note in the migration that the round-trip is lossy: rows added with absent limits come back as `0`, which the pre-feature domain rule would itself have rejected.
2. Drop the four added columns.

**No two-step deploy required** — nothing is dropped and no column stops being read. Step 3 is a widening (`NOT NULL` → `NULL`), which is safe against a running old build: the old build never writes `NULL` and never reads one, because rows with absent limits can only be created by the new build.

**No backfill on `Up`**: no `AIModels` row can currently hold `0`, because the pre-feature `Create` rejected it. Verified by inspection of the only construction path.

**Index changes**: none. No new column appears in a `WHERE`, `JOIN`, or `ORDER BY`.

**CI note**: this repository's shared persistence-test database requires the migration to be applied manually before the persistence suite will pass against it.

---

## State transitions

**Provider health**, per check cycle or on-demand probe:

```
                 ┌──────────────────────────────────────────┐
                 │                                          │
    Unknown ─────┼──► Healthy  (kind/reason cleared) ◄───────┤
   (never        │        │                                 │
    checked)     │        ▼                                 │
                 └──► Unhealthy (kind + reason set) ─────────┘
```

- Every transition also writes one immutable `ProviderHealthCheck` row.
- `Unknown` is only ever an initial state; nothing transitions back into it.
- A mechanism failure (database unreachable, scope creation failure) writes **nothing** and leaves the prior state intact — FR-023. The status then ages past `HealthStaleAfterUtc` and presents as possibly out of date, which is the correct signal.

**Catalog model**, unchanged by this feature and shown for completeness: `Create` → `Available` → immediately `Unavailable` by the sync handler → administrator sets `Available` when ready. Absent token limits gate none of these transitions.
