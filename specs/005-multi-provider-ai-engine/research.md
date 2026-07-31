# Phase 0 Research: Multi-Provider AI Engine

All Technical Context unknowns are resolved below; no `NEEDS CLARIFICATION` markers remain.

## Decision 1: Provider abstraction shape

**Decision**: Redefine `IAIProvider` so `ChatModel`/`ImageModel` are no longer fixed
properties of the implementation — every call takes a model identifier and a generation
parameters object as arguments. Keep one implementation class per vendor
(`OpenAIProvider`, `AnthropicProvider`, `GoogleGeminiProvider`, `OpenRouterProvider`), each
responsible only for translating the shared `ChatMessage`/parameters shape into that
vendor's wire format and translating the response back — no shared base class.

**Rationale**: The four initial vendors have materially different request/response shapes:
- **OpenAI** (and **OpenRouter**, which is wire-compatible with OpenAI's `chat/completions`
  format, routing via a `"vendor/model"`-style model string): flat `messages` array with
  `role`/`content`, SSE `data:` chunks with a `delta.content` field — this is exactly what
  the existing `OpenAIProvider` already implements.
- **Anthropic**: a separate top-level `system` field (not a `system`-role message in the
  array), a `messages` array of `user`/`assistant` turns only, and a different SSE event
  shape (`event: content_block_delta` framed events, not bare `data:` chunks).
- **Google Gemini**: `contents`/`parts` structure with roles `user`/`model` (not
  `assistant`), and a distinct streaming response shape.

Given these differences, a shared base class would spend more code fighting the differences
than it would save (constitution §IV: inheritance only for genuine is-a relationships with
*stable, shared invariants* — these vendors don't share one). Each provider maps into the
same `ChatMessage`/`ChatRole` DTOs already defined in `IAIProvider.cs` on the way in, and
yields the same `string` chunks on the way out, so `Application` and the frontend stay
provider-agnostic without a shared implementation base.

**Alternatives considered**:
- *Single generic HTTP-templated provider driven by per-vendor config* — rejected: request/
  response JSON shapes differ too much (nested `parts` vs. flat `messages`, differing
  streaming event framing) for config-driven templating to stay simpler than four small
  classes.
- *One `IAIProvider` per capability instead of per vendor* — rejected: FR-005's capability
  flags (vision, JSON mode, reasoning, etc.) already model per-model capability variation;
  splitting by capability would fragment a single vendor's implementation across many
  interfaces for no benefit.

## Decision 2: Reuse `Message`/`UserChat` instead of new `MessageUsage`/`ConversationModelSettings` tables

**Decision**: The spec's Key Entities list "Message Usage" and "Conversation Model
Settings" as concepts. Implement them as additional columns on the existing `Message` and
`UserChat` entities (both already carry `Provider`/`Model` since SPEC-002), not as new
tables.

- `Message` gains: `CachedTokenCount` (int?), `ReasoningTokenCount` (int?), `LatencyMs`
  (int?), `EstimatedCostUsd` (decimal(18,6)?).
- `UserChat` gains: `ProviderId` (Guid?, FK to `AIProvider`), `ModelId` (Guid?, FK to
  `AIModel`), `GenerationParametersJson` (string?) — the conversation-level defaults new
  messages inherit unless overridden per-send.

**Rationale**: A `MessageUsage` table joined 1:1 on `Message.Id` (or `ConversationModelSettings`
joined 1:1 on `UserChat.Id`) has no independent lifecycle, ownership, or query pattern —
every read of a message already needs its usage, every read of a chat already needs its
model settings. Constitution §III (Simplicity First) explicitly rejects abstraction/
duplication introduced for a hypothetical future need rather than a present, specified one;
nothing in this spec requires usage data to be created, queried, or deleted independently
of the message it belongs to.

**Alternatives considered**:
- *Separate `MessageUsage`/`ConversationModelSettings` tables, matching the spec's Key
  Entities literally* — rejected: adds two always-joined tables and two repositories for
  data that is 1:1 and co-created with its parent row every time; contradicts DRY/YAGNI.
- *Store usage/settings as owned-entity JSON columns instead of scalar columns* — rejected
  for `Message`/`UserChat` usage fields specifically, since `InputTokenCount`/
  `OutputTokenCount` already exist as scalar columns (SPEC-002) — mixing scalar and JSON
  for logically identical data would be inconsistent; `GenerationParametersJson` stays JSON
  (as it already is on `Message`) because its shape varies per provider/model and isn't
  queried column-by-column.

## Decision 3: Keyed dependency injection for provider resolution

**Decision**: Register each `IAIProvider` implementation as a *keyed* scoped service
(`.NET`'s `AddKeyedScoped<IAIProvider, OpenAIProvider>("openai")`, etc., keyed by each
`AIProvider` entity's stable `ProviderKey` string). Add `IAIProviderResolver` to
`Application/Abstractions`:

```csharp
public interface IAIProviderResolver
{
    IAIProvider Resolve(string providerKey);
}
```

implemented in `Infrastructure.Ai` using `IServiceProvider.GetRequiredKeyedService<IAIProvider>(providerKey)`.

**Rationale**: The provider to use is only known at request time (from the conversation's
or message's stored `ProviderId` → `AIProvider.ProviderKey`), not at compile time. .NET's
built-in keyed-service DI (available since .NET 8; this solution targets .NET 10) is the
first-class mechanism for exactly this "select one of N registered implementations by a
runtime key" scenario, and keeps `Application` depending only on an interface it owns
(constitution §V, DIP) — the keyed-lookup mechanics stay inside `Infrastructure`.

**Alternatives considered**:
- *Hand-rolled `Dictionary<string, IAIProvider>` built in a factory* — rejected: reimplements
  what keyed DI already validates at container-build time (a typo'd key fails fast at
  startup with keyed DI's `GetRequiredKeyedService`, vs. a silent `KeyNotFoundException` at
  request time with a hand-rolled dictionary).
- *`IEnumerable<IAIProvider>` injected everywhere, filtered by `ProviderName` at each call
  site* — rejected: pushes provider-selection logic into every consumer instead of one
  resolver; violates DRY.

This is a new pattern for this codebase (no prior keyed-service usage) — flagged in
plan.md's Complexity Tracking as requiring an ADR before/alongside implementation.

## Decision 4: Credential encryption

**Decision**: Encrypt `AIProvider.CredentialCiphertext` using the already-registered
`IDataProtectionProvider` (`services.AddDataProtection()`, `Infrastructure/
DependencyInjection.cs:43`), following the exact pattern `SignedUrlService` already uses:
`provider.CreateProtector("AskLucy.AiProviderCredentials")`, `.Protect(rawKey)` /
`.Unprotect(ciphertext)`. No credential value is ever included in any read DTO — admin
"view" endpoints return only a masked indicator (e.g., `hasCredential: true`,
`lastRotatedAtUtc`), never the plaintext or ciphertext.

**Rationale**: Data Protection is already a registered, tested dependency in this exact
project for the exact same class of problem (protecting a secret server-side, never
exposing it to the client) — reusing it is Convention Over Configuration (constitution §VII)
and avoids introducing a second secrets mechanism (e.g., a raw `EncryptedColumn` attribute
or a new library) for the same job.

**Alternatives considered**:
- *Azure Key Vault / external secret manager per credential* — rejected for this spec:
  constitution §8 reserves Key Vault for production secrets/config (connection strings,
  the app's own signing keys), not per-tenant admin-entered third-party API keys that must
  be readable by the running application on every AI call; Data Protection is the correct
  tier for this class of secret and is already proven in this codebase.
- *Store credentials in plaintext, relying on DB-level encryption at rest only* — rejected:
  fails FR-004 explicitly ("MUST never display a stored credential in plain text") and
  constitution §8's column/field-level encryption requirement for sensitive data.

## Decision 5: Model catalog curation

**Decision**: Seed a baseline `AIModel` catalog per provider via an EF Core migration
`HasData()` seed (name, display name, context window, output limits, capability flags,
pricing, release date, initial status = `Available`). Administrators can change a model's
`Status` (Available/Deprecated/Unavailable) via an admin endpoint; they cannot free-form
create arbitrary models in this spec's scope. Each `IAIProvider` implementation additionally
exposes a `ListAvailableModelsAsync()` capability (matching the "Model Discovery"
responsibility called out in the original request) that an admin "Sync from provider"
action can call to detect new/removed models — but this sync surfaces a diff for the admin
to review, it does not silently add or remove selectable models.

**Rationale**: Matches the spec's Assumption ("Model catalog is administrator-curated...
rather than being fetched live from each provider on every request") while still honoring
the "Model Discovery" capability from the original request as an explicit, admin-triggered
action rather than an implicit background process — keeps the catalog stable and
predictable for end users (a model a user picked mid-conversation doesn't silently vanish
because a background sync ran).

**Alternatives considered**:
- *Fully dynamic, live-fetched catalog on every page load* — rejected: contradicts the
  spec's own Performance section ("cache model lists... avoid requesting model lists on
  every request") and the Assumptions section.
- *No discovery capability at all, catalog is 100% hand-maintained forever* — rejected:
  wastes the "Model Discovery" capability the spec explicitly calls for per provider, and
  makes keeping the catalog current entirely manual/error-prone as vendors ship new models.

## Decision 6: Rate limiting — no new policy required

**Decision**: New AI-invoking endpoints (`/api/v1/ai/chat` [existing, revised],
`/api/v1/ai/compare` [new]) stay under the existing `ai-endpoints` rate-limit policy
already registered in `Program.cs` and already applied via `[EnableRateLimiting
("ai-endpoints")]` on `AiController`. New admin endpoints reuse the existing `admin-endpoints`
policy (same pattern as `AdminDashboardController`). New read-only catalog/preferences/usage
endpoints (not AI-invoking) get their own lightweight policy mirroring the existing
`chat-endpoints` policy shape (generous limit, no cost tiering), consistent with the
existing precedent of giving every new controller *some* rate-limit policy per constitution
§6, even when it isn't cost-sensitive.

**Rationale**: Confirms Clarification Q1's answer — this spec introduces no new token/cost-
based throttling requirement; baseline request-count limiting already exists exactly where
the constitution requires it (every public endpoint) and already differentiates AI-invoking
endpoints from ordinary ones. FR-033 is satisfied by attaching the existing policies to new
routes, not by building new rate-limiting infrastructure.

**Alternatives considered**:
- *A new `ai-compare-endpoints` policy with a lower limit, since comparison fans out to
  N models per call* — considered reasonable, deferred: the spec's own Assumptions defer
  cost/usage-based enforcement to the future Billing Engine; a request-count policy doesn't
  meaningfully protect against comparison's higher *cost* per request. Flagging for
  `tasks.md`/Billing Engine follow-up rather than solving it as a rate-limit workaround here.

## Decision 7: Provider health checks

**Decision**: A new `ProviderHealthCheckHostedService` (`IHostedService`, mirroring the
existing `WhisperWarmupHostedService` pattern already in `Infrastructure.Ai`) runs on a
fixed interval (default 2 minutes, configurable), calling each enabled provider's
lightweight health-check operation (`IAIProvider.CheckHealthAsync()`, a new interface
member — typically a minimal, cheap request such as a models-list call rather than a full
chat completion) and writing one `ProviderHealthCheck` row per provider per check.

**Rationale**: Matches the spec's Assumption ("Health checks run on a recurring schedule...
to avoid adding latency to user-facing chat requests") and reuses an established
background-service pattern already proven in this codebase for exactly this kind of
periodic, provider-adjacent background work.

**Alternatives considered**:
- *Check health lazily, on the first chat request after some staleness threshold* —
  rejected: would add latency to a real user-facing request (violates the Assumption) and
  makes "detect an outage within one health-check interval" (SC-006) unpredictable.

## Decision 8: Scale/Scope

**Decision**: No new scale target beyond what the existing chat feature already handles —
this feature adds provider/model dimensions to an already-working chat and conversation
system (SPEC-002), not a new high-throughput subsystem. Health checks and catalog reads are
cached/interval-based (Decisions 5, 7) specifically so they don't scale linearly with chat
volume.

**Rationale**: The spec's Assumptions and Success Criteria don't state concurrent-user or
request-volume numbers, and the constitution's enterprise vision (§1) doesn't set a specific
target either — treating this as "scale with the existing platform, not a new ceiling" avoids
inventing an unfounded requirement (constitution §III, YAGNI: don't build for hypothetical
scale not present in the spec).

## Decision 9: Provider error translation

**Decision**: Extend the existing `AiProviderUnavailableException` pattern (already used by
`OpenAIProvider`, already mapped to Problem Details at the API boundary) with two siblings:
`AiProviderAuthenticationException` (credential invalid/expired — maps to a distinct
Problem Details `type` so an admin, not the user, can be pointed at the fix) and
`AiProviderRateLimitedException` (maps to 429 with a `Retry-After` hint when the vendor
supplies one). Every provider implementation's `IsTransient`/error-mapping logic (already
present in `OpenAIProvider`) is replicated per vendor — not centralized into a shared
helper, per Decision 1's "no shared base class" reasoning — but all four must produce only
these three exception types (plus success), so `Application` and the API layer stay
provider-agnostic.

**Rationale**: Directly satisfies FR-028 ("translate provider-specific error conditions...
into a consistent set of user-facing error messages") and constitution §9's "translate
provider-specific errors into standardized application exceptions" without inventing a new
mechanism beyond what `OpenAIProvider` already does today.

**Alternatives considered**:
- *One generic `AiProviderException` with an error-code enum* — considered, not chosen:
  three purpose-named exception types map more directly onto ASP.NET Core's existing
  exception-handling-middleware-to-Problem-Details pattern (each type → one `type` URI) than
  a single exception with a discriminant would.

## Decision 10: Model comparison execution

**Decision**: `POST /api/v1/ai/compare` accepts a prompt and 2+ `{providerId, modelId}`
pairs, resolves each via `IAIProviderResolver`, runs all `ChatAsync` (non-streaming) calls
concurrently (`Task.WhenAll`-style, each independently try/caught so one failure doesn't
fault the others per FR-026), and returns once all have settled (succeeded or failed).

**Rationale**: See plan.md Complexity Tracking — streaming N concurrent responses over one
SSE connection to the client has no precedent in this codebase and is unjustified complexity
for a P4 story; a single non-streaming response per model, returned together, satisfies
every acceptance scenario in User Story 7 without it.

**Alternatives considered**:
- *N independent SSE connections, one per compared model, multiplexed client-side* —
  rejected for this spec as premature complexity (see Complexity Tracking); revisit if
  comparison usage data justifies the investment.

## Decision 11: Frontend state split

**Decision**: Server state (provider/model catalog, health, usage, preferences) is fetched
via TanStack Query hooks (`useProviders`, `useModels`, `useAiPreferences`, `useUsage`,
mirroring the existing `useChats`/`useProfile` hook naming convention). Transient UI state
(which provider/model is currently selected in the composer before a message is sent, the
open/closed state of the parameter panel) lives in a small feature-local Zustand store or
component state — never duplicated into TanStack Query's cache, per constitution §7.

**Rationale**: Directly matches the constitution's explicit state-management rule and the
existing `useAuthStore` (Zustand)/`useChatStream` (local state) + TanStack Query split
already used elsewhere in `ClientApp`.

**Alternatives considered**: None materially different — this is simply applying the
project's already-settled convention to new features, not a new decision.
