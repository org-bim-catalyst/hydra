# Tasks: Multi-Provider AI Engine

**Input**: Design documents from `/specs/005-multi-provider-ai-engine/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included — the spec's own Testing section explicitly requires Unit, Integration,
Provider Mock, Streaming, Performance, and Playwright E2E tests.

**Organization**: Tasks are grouped by user story (spec.md priorities P1–P4) so each story
can be implemented and demoed independently once Setup + Foundational are done.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unmet dependencies)
- **[Story]**: Which user story this task belongs to (US1–US7)
- Every task names an exact file path

## Path Conventions

Existing Clean Architecture solution — see plan.md's Project Structure:
`src/AskLucy.Domain`, `src/AskLucy.Application`, `src/AskLucy.Infrastructure`,
`src/AskLucy.Persistence`, `src/AskLucy.Web` (+ `ClientApp` React SPA), `tests/AskLucy.*.Tests`.

---

## Phase 1: Setup

**Purpose**: New vendor plumbing that has no behavior of its own yet — pure scaffolding.

- [X] T001 [P] Create `AnthropicOptions.cs` (mirrors `OpenAIOptions.cs`'s shape: `ApiKey`, model defaults, `BaseUrl`) in `src/AskLucy.Infrastructure/Ai/AnthropicOptions.cs`
- [X] T002 [P] Create `GoogleGeminiOptions.cs` in `src/AskLucy.Infrastructure/Ai/GoogleGeminiOptions.cs`
- [X] T003 [P] Create `OpenRouterOptions.cs` in `src/AskLucy.Infrastructure/Ai/OpenRouterOptions.cs`
- [X] T004 Register named `HttpClient`s (`"Anthropic"`, `"GoogleGemini"`, `"OpenRouter"`, mirroring the existing `"OpenAI"` registration) and bind the three new options classes (`AddOptions<T>().ValidateDataAnnotations().ValidateOnStart()`) in `src/AskLucy.Infrastructure/DependencyInjection.cs`
- [X] T005 [P] Add empty placeholder config sections (`Anthropic`, `GoogleGemini`, `OpenRouter` — no real keys committed, constitution §8) to `src/AskLucy.Web/appsettings.json` and `appsettings.Development.json`
- [X] T006 [P] Create `ProviderHealthCheckOptions.cs` (check interval, default 2 minutes) in `src/AskLucy.Infrastructure/Ai/ProviderHealthCheckOptions.cs`, bound alongside the others in `src/AskLucy.Infrastructure/DependencyInjection.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The provider/model catalog, revised `IAIProvider` contract, and persistence
layer every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain

- [X] T007 [P] Create `ModelPricing` value object (record: `InputPerMillionTokensUsd`, `OutputPerMillionTokensUsd`) in `src/AskLucy.Domain/Ai/ModelPricing.cs`
- [X] T008 [P] Create `AIProvider` entity (data-model.md: `ProviderKey`, `DisplayName`, `IsEnabled`, `CredentialCiphertext`, `CredentialLastRotatedAtUtc`, `DefaultModelId`, `HealthStatus`, `HealthStatusCheckedAtUtc`; enable/disable + set/clear-credential mutation methods; validation: cannot enable without a credential) in `src/AskLucy.Domain/Ai/AIProvider.cs`
- [X] T009 [P] Create `AIModel` entity (data-model.md fields incl. capability flags, owned `ModelPricing`, `Status` enum with the Available/Deprecated/Unavailable transition rules from Clarifications Q2) in `src/AskLucy.Domain/Ai/AIModel.cs`
- [X] T010 [P] Create `ProviderHealthCheck` entity (append-only, no soft delete) in `src/AskLucy.Domain/Ai/ProviderHealthCheck.cs`
- [X] T011 [P] Create `UserAiPreference` entity in `src/AskLucy.Domain/Ai/UserAiPreference.cs`
- [X] T012 Extend `Message` with `CachedTokenCount`, `ReasoningTokenCount`, `LatencyMs`, `EstimatedCostUsd`, `ComparisonGroupId`, `IsIncludedInContext` (data-model.md) and update `Message.Create(...)`'s signature/doc comment in `src/AskLucy.Domain/Chats/Message.cs`
- [X] T013 Extend `MessageDto` with `latencyMs`, `estimatedCostUsd`, `cachedTokenCount`, `reasoningTokenCount` and update its mapping from `Message` in `src/AskLucy.Application/Chats/MessageDto.cs` (depends on T012) — *(analysis finding G4: without this, the new columns T012 persists are never returned by `GET /chats/{id}/messages`, leaving User Story 5's usage display with no data)*
- [X] T014 Extend `UserChat` with `ProviderId`, `ModelId`, `GenerationParametersJson` and a new `SetModelSelection(providerId, modelId, generationParametersJson, actor)` method (matching the existing `Rename`/`Archive` mutation pattern) in `src/AskLucy.Domain/Chats/UserChat.cs`

### Application abstractions

- [X] T015 Revise `IAIProvider` (remove fixed `ChatModel`/`ImageModel` properties; `ChatAsync`/`StreamChatAsync`/`GenerateImageAsync` take a model identifier + generation parameters; add `CheckHealthAsync()` and `ListAvailableModelsAsync()`; rewrite the doc comment to remove the old FR-022 "single implementation" note) in `src/AskLucy.Application/Abstractions/IAIProvider.cs`
- [X] T016 [P] Create `IAIProviderResolver` (`IAIProvider Resolve(string providerKey)`, research.md Decision 3) in `src/AskLucy.Application/Abstractions/IAIProviderResolver.cs`
- [X] T017 [P] Add `AiProviderAuthenticationException` and `AiProviderRateLimitedException` (research.md Decision 9) alongside the existing `AiProviderUnavailableException` in `src/AskLucy.Application/Abstractions/IAIProvider.cs`
- [X] T018 [P] Create `GenerationParametersDto` (temperature, topP, topK, presencePenalty, frequencyPenalty, maxTokens, stopSequences, seed, reasoningLevel, responseFormat/jsonMode, streaming, systemPrompt, developerPrompt) in `src/AskLucy.Application/Ai/GenerationParametersDto.cs`
- [X] T019 [P] Create `IAIProviderRepository` and `IAIModelRepository` in `src/AskLucy.Application/Abstractions/IAIProviderRepository.cs` and `IAIModelRepository.cs`
- [X] T020 [P] Create `IProviderHealthCheckRepository` and `IUserAiPreferenceRepository` in `src/AskLucy.Application/Abstractions/IProviderHealthCheckRepository.cs` and `IUserAiPreferenceRepository.cs`

### Infrastructure & Persistence

- [X] T021 Revise `OpenAIProvider` to the new `IAIProvider` signature (per-call model/parameters, `CheckHealthAsync`, `ListAvailableModelsAsync`) in `src/AskLucy.Infrastructure/Ai/OpenAIProvider.cs`
- [X] T022 Create `AiProviderResolver` (`IServiceProvider.GetRequiredKeyedService<IAIProvider>(providerKey)`) in `src/AskLucy.Infrastructure/Ai/AiProviderResolver.cs`
- [X] T023 Register `OpenAIProvider` as `AddKeyedScoped<IAIProvider, OpenAIProvider>("openai")` and register `IAIProviderResolver` in `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T021, T022)
- [X] T024 Write `docs/adr/0004-multi-provider-ai-engine.md` — records the keyed-DI provider-resolution pattern just implemented (T021–T023) and the `Message`/`UserChat` entity-reuse decision (T012, T014; plan.md Complexity Tracking), alternatives considered, consequences (constitution §17) (depends on T012, T014, T021–T023) — *(analysis finding I2: previously listed after the Cross-cutting tasks it postdated, contradicting the "Task ID = execution order" convention; now positioned where it's actually written)*
- [X] T025 [P] Create `AIProviderConfiguration` (Fluent API; `CredentialCiphertext` excluded from default projections) in `src/AskLucy.Persistence/Configurations/AIProviderConfiguration.cs`
- [X] T026 [P] Create `AIModelConfiguration` (owned `ModelPricing`; unique index on `ProviderId`+`ModelKey`) in `src/AskLucy.Persistence/Configurations/AIModelConfiguration.cs`
- [X] T027 [P] Create `ProviderHealthCheckConfiguration` (index `ProviderId`+`CheckedAtUtc`, no soft-delete query filter) in `src/AskLucy.Persistence/Configurations/ProviderHealthCheckConfiguration.cs`
- [X] T028 [P] Create `UserAiPreferenceConfiguration` (unique index on `UserId`) in `src/AskLucy.Persistence/Configurations/UserAiPreferenceConfiguration.cs`
- [X] T029 Update `MessageConfiguration` for the six new columns in `src/AskLucy.Persistence/Configurations/MessageConfiguration.cs`
- [X] T030 Update `UserChatConfiguration` for `ProviderId`/`ModelId` FKs (indexed) + `GenerationParametersJson` in `src/AskLucy.Persistence/Configurations/UserChatConfiguration.cs`
- [X] T031 Add `DbSet<AIProvider>`, `DbSet<AIModel>`, `DbSet<ProviderHealthCheck>`, `DbSet<UserAiPreference>` in `src/AskLucy.Persistence/AskLucyDbContext.cs` (depends on T007–T011)
- [X] T032 [P] Implement `AIProviderRepository` and `AIModelRepository` in `src/AskLucy.Persistence/Repositories/AIProviderRepository.cs` and `AIModelRepository.cs`
- [X] T033 [P] Implement `ProviderHealthCheckRepository` and `UserAiPreferenceRepository` in `src/AskLucy.Persistence/Repositories/ProviderHealthCheckRepository.cs` and `UserAiPreferenceRepository.cs`
- [X] T034 Register the four new repositories in `src/AskLucy.Persistence/DependencyInjection.cs`
- [X] T035 Generate EF Core migration `AddMultiProviderAiEngine` (`dotnet ef migrations add`) with a `HasData()` seed for 4 `AIProviders` (all `IsEnabled = false`) and the baseline `AIModel` catalog (research.md Decision 5) in `src/AskLucy.Persistence/Migrations/`

### Cross-cutting

- [X] T036 Map `AiProviderAuthenticationException` (→ 502, `ai-provider-authentication-failed`) and `AiProviderRateLimitedException` (→ 429, `ai-provider-rate-limited`, `Retry-After` when the vendor supplies one) into `Map(...)` in `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs`
- [X] T037 Add the `"ai-catalog-endpoints"` rate-limit policy (research.md Decision 6) in `src/AskLucy.Web/Program.cs`
- [X] T038 Create `ProviderHealthCheckHostedService` (mirrors `WhisperWarmupHostedService`; on `ProviderHealthCheckOptions.Interval`, calls `IAIProviderResolver` + `IAIProvider.CheckHealthAsync()` per enabled `AIProvider`, writes a `ProviderHealthCheck` row, updates `AIProvider.HealthStatus`) in `src/AskLucy.Infrastructure/Ai/ProviderHealthCheckHostedService.cs`
- [X] T039 Register `ProviderHealthCheckHostedService` as a hosted service in `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T038)
- [X] T040 [P] Unit tests for `AIProvider`/`AIModel` domain validation (cannot enable without credential; status transitions) in `tests/AskLucy.Domain.Tests/Ai/AIProviderTests.cs` and `AIModelTests.cs`
- [X] T041 [P] Unit tests for the `Message`/`UserChat` entity extensions in `tests/AskLucy.Domain.Tests/Chats/MessageTests.cs` and `UserChatTests.cs`

**Checkpoint**: Foundation ready — catalog, resolver, persistence, and error/rate-limit
plumbing all exist. User story implementation can begin.

---

## Phase 3: User Story 1 - Administrator enables providers and configures credentials (Priority: P1)

**Goal**: An administrator can enable a provider, set its credential, and see it reach a
healthy status.

**Independent Test**: Enable one provider with a valid credential and confirm it reports
healthy — no end-user chat flow required (spec.md).

### Tests for User Story 1

- [ ] T042 [P] [US1] Contract tests for `GET/PATCH /api/v1/admin/ai/providers`, `PUT/DELETE .../credential` (contracts/admin.md) in `tests/AskLucy.Web.Tests/Controllers/AdminAiProvidersControllerTests.cs`
- [ ] T043 [P] [US1] Integration test: a credential round-trips through Data Protection encryption and is never present in any read DTO in `tests/AskLucy.Infrastructure.Tests/Ai/AiProviderCredentialTests.cs`

### Implementation for User Story 1

- [ ] T044 [P] [US1] Create `GetAdminAiProvidersQuery`/Handler in `src/AskLucy.Application/Ai/Queries/GetAdminAiProviders/`
- [ ] T045 [P] [US1] Create `UpdateAiProviderCommand`/Handler/Validator (`isEnabled`/`defaultModelId`; rejects enabling without a credential) in `src/AskLucy.Application/Ai/Commands/UpdateAiProvider/`
- [ ] T046 [P] [US1] Create `SetAiProviderCredentialCommand`/Handler/Validator (encrypts via `IDataProtectionProvider`, research.md Decision 4) in `src/AskLucy.Application/Ai/Commands/SetAiProviderCredential/`
- [ ] T047 [P] [US1] Create `ClearAiProviderCredentialCommand`/Handler in `src/AskLucy.Application/Ai/Commands/ClearAiProviderCredential/`
- [ ] T048 [US1] Create `AdminAiProvidersController` (`GET /api/v1/admin/ai/providers`, `PATCH {id}`, `PUT/DELETE {id}/credential`) with `[Authorize(Policy = "AdministratorOrSuperUser")]` + `[EnableRateLimiting("admin-endpoints")]` in `src/AskLucy.Web/Controllers/v1/AdminAiProvidersController.cs` (depends on T044–T047)
- [ ] T049 [P] [US1] Contract test for `GET /api/v1/admin/ai/providers/{providerId}/models`, `PATCH /api/v1/admin/ai/models/{id}`, `POST .../models/actions/sync`, `.../sync/apply` (contracts/admin.md) in `tests/AskLucy.Web.Tests/Controllers/AdminAiProvidersControllerTests.cs` — *(analysis finding G1)*
- [ ] T050 [P] [US1] Create `GetAdminAiModelsQuery`/Handler (any status, per-provider) in `src/AskLucy.Application/Ai/Queries/GetAdminAiModels/` — *(analysis finding G1)*
- [ ] T051 [P] [US1] Create `UpdateAiModelStatusCommand`/Handler/Validator (Available/Deprecated/Unavailable, any transition allowed per data-model.md) in `src/AskLucy.Application/Ai/Commands/UpdateAiModelStatus/` — *(analysis finding G1)*
- [ ] T052 [US1] Create `SyncProviderModelsCommand`/Handler (calls `IAIProvider.ListAvailableModelsAsync()`, returns an added/removed diff, writes nothing) and `ApplyProviderModelsSyncCommand`/Handler (writes the confirmed diff — research.md Decision 5's two-step confirm) in `src/AskLucy.Application/Ai/Commands/SyncProviderModels/` — *(analysis finding G1)*
- [ ] T053 [US1] Add `GET {providerId}/models`, `PATCH /admin/ai/models/{id}`, `POST {providerId}/models/actions/sync`, `POST .../sync/apply` to `src/AskLucy.Web/Controllers/v1/AdminAiProvidersController.cs` (depends on T048, T050–T052) — *(analysis finding G1)*
- [ ] T054 [US1] Add audit-trail logging (constitution §8 — who/when/before-after `isEnabled`, credential set/cleared without ever logging the key value, and model status changes) to the T045–T047 and T051–T052 handlers
- [ ] T055 [P] [US1] Create `adminAiApi.ts` (`getProviders`, `updateProvider`, `setCredential`, `clearCredential`, `getModels`, `updateModelStatus`, `syncModels`) in `src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiApi.ts`
- [ ] T056 [US1] Create `AdminAiProvidersPage.tsx` (provider list, enable/disable toggle, credential entry dialog) in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx`
- [ ] T057 [US1] Extend `AdminAiProvidersPage.tsx` with a per-provider model list, status controls, and a "Sync from provider" action (depends on T053, T055, T056) — *(analysis finding G1)*
- [ ] T058 [US1] Add the route and admin-nav entry for the new page in the router config and admin navigation component under `src/AskLucy.Web/ClientApp/src/`

**Checkpoint**: User Story 1 is fully functional and independently testable (health status
reflects T038's hosted service, already built in Foundational).

---

## Phase 4: User Story 2 - User chats using a chosen provider and model (Priority: P1) 🎯 MVP

**Goal**: A user can pick any enabled provider/model, chat, and see per-message attribution.

**Independent Test**: Start a conversation, pick a specific provider/model, send a message,
confirm the reply is labeled with that exact provider/model (spec.md).

### Tests for User Story 2

- [ ] T059 [P] [US2] Contract test for `POST /api/v1/ai/chat` with `providerId`/`modelId` (contracts/chat.md) in `tests/AskLucy.Web.Tests/Controllers/AiControllerTests.cs`
- [ ] T060 [P] [US2] Contract tests for `GET /api/v1/ai/providers`, `GET /api/v1/ai/providers/{id}/models`, `GET /api/v1/ai/models` (contracts/providers.md) in `tests/AskLucy.Web.Tests/Controllers/AiProvidersControllerTests.cs`
- [ ] T061 [P] [US2] Provider mock tests: `OpenAIProvider` and `AnthropicProvider` each satisfy `IAIProvider` against mocked/recorded HTTP responses in `tests/AskLucy.Infrastructure.Tests/Ai/OpenAIProviderTests.cs` and `AnthropicProviderTests.cs`
- [ ] T062 [P] [US2] Unit test (handler, `IAIProviderResolver` faked): switching provider mid-conversation leaves earlier messages' attribution unchanged (FR-011) in `tests/AskLucy.Application.Tests/Ai/SendChatMessageCommandHandlerTests.cs` — *(analysis finding C1: explicitly a faked-dependency handler unit test, not a real-infrastructure integration test)*
- [ ] T063 [P] [US2] Streaming test: SSE chunk framing is identical from the client's perspective across two different providers in `tests/AskLucy.Web.Tests/Controllers/AiControllerTests.cs`

### Implementation for User Story 2

- [ ] T064 [P] [US2] Implement `AnthropicProvider` (top-level `system` field, `user`/`assistant` messages, `content_block_delta` SSE framing; error mapping per research.md Decision 9) in `src/AskLucy.Infrastructure/Ai/AnthropicProvider.cs`
- [ ] T065 [P] [US2] Implement `GoogleGeminiProvider` (`contents`/`parts` structure, `user`/`model` roles) in `src/AskLucy.Infrastructure/Ai/GoogleGeminiProvider.cs`
- [ ] T066 [P] [US2] Implement `OpenRouterProvider` (OpenAI-compatible wire format, `"vendor/model"` routing) in `src/AskLucy.Infrastructure/Ai/OpenRouterProvider.cs`
- [ ] T067 [US2] Register Anthropic/GoogleGemini/OpenRouter as keyed `IAIProvider` services (`"anthropic"`, `"google-gemini"`, `"openrouter"`) in `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T064–T066)
- [ ] T068 [P] [US2] Create `GetEnabledAiProvidersQuery`/Handler in `src/AskLucy.Application/Ai/Queries/GetEnabledAiProviders/`
- [ ] T069 [P] [US2] Create `GetAiModelsQuery`/Handler (per-provider and flat cross-provider variants) in `src/AskLucy.Application/Ai/Queries/GetAiModels/`
- [ ] T070 [US2] Create `AiProvidersController` (`GET providers`, `GET providers/{id}/models`, `GET models`) with `[EnableRateLimiting("ai-catalog-endpoints")]` in `src/AskLucy.Web/Controllers/v1/AiProvidersController.cs` (depends on T068, T069)
- [ ] T071 [US2] Create `CostEstimator` (computes `EstimatedCostUsd` from `AIModel.ModelPricing` + token counts; returns `null` when pricing is missing, FR-022) in `src/AskLucy.Application/Ai/CostEstimator.cs`
- [ ] T072 [US2] Revise `SendChatMessageCommand`/Handler to accept `providerId`/`modelId`/`GenerationParametersDto`, resolve via `IAIProviderResolver`, and call `CostEstimator` in `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommand.cs` and `SendChatMessageCommandHandler.cs` (depends on T016, T071)
- [ ] T073 [US2] Revise `SendChatMessageCommandValidator` to reject a generation parameter unsupported by the selected model, naming the specific parameter (FR-015/FR-016) in `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandValidator.cs`
- [ ] T074 [US2] Revise `AiController.Chat` to pass `providerId`/`modelId`/`generationParameters` through and persist `LatencyMs`/`EstimatedCostUsd`/`CachedTokenCount`/`ReasoningTokenCount` via `AppendMessageCommand`; also update the class-level doc comment, whose "FR-015"/"FR-023" citations refer to specs/000-legacy-modernization's numbering, to disambiguate them from this spec's own FR-015/FR-023 (unrelated requirements) in `src/AskLucy.Web/Controllers/v1/AiController.cs` (depends on T072) — *(analysis finding I3)*
- [ ] T075 [US2] Create `UpdateChatModelSelectionCommand`/Handler/Validator (FR-009) in `src/AskLucy.Application/Chats/Commands/UpdateChatModelSelection/`
- [ ] T076 [US2] Add `PATCH /api/v1/chats/{id}/model-selection` to `src/AskLucy.Web/Controllers/v1/ChatsController.cs` (depends on T075)
- [ ] T077 [P] [US2] Create `aiProvidersApi.ts` (`getProviders`, `getModels`) in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiProvidersApi.ts`
- [ ] T078 [US2] Add a provider/model picker to `ChatComposer.tsx`, wired to `aiProvidersApi`, feeding `providerId`/`modelId` into `streamChat` in `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`
- [ ] T079 [US2] Update `streamChat()`/`ChatMessage` to send `providerId`/`modelId`, surface returned cost/usage metadata, and parse the RFC 7807 Problem Details body on a non-OK response (`title`/`detail` per FR-028) instead of throwing a generic status-code error, in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts` — *(analysis finding G3: previously only threw "Chat request failed with {status}", never surfacing the translated provider error)*
- [ ] T080 [US2] Add a provider/model attribution `Chip` to `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx`
- [ ] T081 [US2] Update `useChatStream.ts` to track the conversation's current provider/model selection and call the model-selection `PATCH` endpoint on change in `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts`
- [ ] T082 [US2] Update `useChatStream.ts`'s `send()` catch handler to keep the partial assistant message (mark it `isIncomplete: true`) instead of reverting to `history`, per FR-030 in `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts` (depends on T081) — *(analysis finding G2: the existing handler currently discards any partial content already streamed in, contradicting FR-030)*

**Checkpoint**: User Stories 1 and 2 both work end-to-end — real multi-vendor chat with
per-message attribution. **This is the feature's MVP** (both are P1).

---

## Phase 5: User Story 3 - User sets default provider/model preferences (Priority: P2)

**Goal**: A user's saved default provider/model pre-fills every new conversation.

**Independent Test**: Set a default in settings, start a new conversation, confirm it opens
pre-selected (spec.md).

### Tests for User Story 3

- [ ] T083 [P] [US3] Contract tests for `GET/PUT /api/v1/ai/preferences` (contracts/preferences.md) in `tests/AskLucy.Web.Tests/Controllers/AiProvidersControllerTests.cs`
- [ ] T084 [P] [US3] Unit test (handler, repositories faked): saved default falls back with a visible notice when its provider is disabled (FR-018) in `tests/AskLucy.Application.Tests/Ai/UserAiPreferenceFallbackTests.cs` — *(analysis finding C1)*

### Implementation for User Story 3

- [ ] T085 [P] [US3] Create `GetUserAiPreferenceQuery`/Handler (returns platform default + `isPlatformDefault: true` when unset) in `src/AskLucy.Application/Ai/Queries/GetUserAiPreference/`
- [ ] T086 [P] [US3] Create `SaveUserAiPreferenceCommand`/Handler/Validator (cross-field check: `defaultModelId` belongs to `defaultProviderId`) in `src/AskLucy.Application/Ai/Commands/SaveUserAiPreference/`
- [ ] T087 [US3] Add `GET/PUT /api/v1/ai/preferences` to `src/AskLucy.Web/Controllers/v1/AiProvidersController.cs` (depends on T085, T086)
- [ ] T088 [US3] Add fallback-resolution logic (user preference → platform default → first enabled provider/model, with a notice flag) used when starting a new conversation, in `src/AskLucy.Application/Ai/DefaultProviderResolver.cs`
- [ ] T089 [P] [US3] Create `aiPreferencesApi.ts` (`getPreferences`, `savePreferences`) in `src/AskLucy.Web/ClientApp/src/features/settings/api/aiPreferencesApi.ts`
- [ ] T090 [US3] Add an "AI Providers" tab to `src/AskLucy.Web/ClientApp/src/features/settings/pages/SettingsPage.tsx` (default provider/model pickers)
- [ ] T091 [US3] Update the new-conversation flow to apply the resolved default (T088) in `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`

**Checkpoint**: Users 1–3 stories all independently functional.

---

## Phase 6: User Story 4 - User configures generation parameters (Priority: P2)

**Goal**: A user can tune temperature/top-P/penalties/system prompt/etc. per conversation,
gated by the selected model's declared capabilities.

**Independent Test**: Change a parameter, send a message, confirm the response reflects it;
confirm an unsupported parameter's control is hidden for an incapable model (spec.md).

### Tests for User Story 4

- [ ] T092 [P] [US4] Unit tests for `GenerationParametersDto` validator range/format checks (FR-016) in `tests/AskLucy.Application.Tests/Ai/GenerationParametersValidatorTests.cs`
- [ ] T093 [P] [US4] Unit test (handler, `IAIProviderResolver`/repositories faked): a parameter unsupported by the selected model is rejected server-side, naming the parameter (FR-015) in `tests/AskLucy.Application.Tests/Ai/SendChatMessageCommandHandlerTests.cs` — *(analysis finding C1)*

### Implementation for User Story 4

- [ ] T094 [US4] Expand `GenerationParametersDto`'s validator to the full parameter set (topK, presence/frequency penalty, stopSequences, seed, reasoningLevel, responseFormat, developerPrompt) with per-field range/format rules in `src/AskLucy.Application/Ai/GenerationParametersDtoValidator.cs`
- [ ] T095 [US4] Wire the parameter-inheritance chain (per-send override → `UserChat.GenerationParametersJson` → `UserAiPreference.DefaultGenerationParametersJson` → model defaults) into `SendChatMessageCommandHandler` in `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs`
- [ ] T096 [US4] Create a capability-aware generation-parameter panel component (hides/disables controls the selected model doesn't support) in `src/AskLucy.Web/ClientApp/src/features/chat/components/GenerationParametersPanel.tsx`
- [ ] T097 [US4] Wire the parameter panel into `ChatComposer.tsx`, saving via the model-selection `PATCH` endpoint, in `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`

**Checkpoint**: Users 1–4 stories all independently functional.

---

## Phase 7: User Story 5 - User sees token usage and estimated cost (Priority: P3)

**Goal**: Per-message and per-conversation token usage/cost are visible to the user.

**Independent Test**: Send a message, confirm the message shows token counts and estimated
cost, matching provider/model (spec.md).

### Tests for User Story 5

- [ ] T098 [P] [US5] Contract test for `GET /api/v1/chats/{id}/usage` (contracts/usage.md) in `tests/AskLucy.Web.Tests/Controllers/ChatsControllerTests.cs`
- [ ] T099 [P] [US5] Unit test (handler, repositories faked): usage aggregation totals match the sum of the conversation's messages, and `costIncomplete` is set when any message lacks pricing (FR-022) in `tests/AskLucy.Application.Tests/Chats/GetChatUsageQueryTests.cs` — *(analysis finding C1)*

### Implementation for User Story 5

- [ ] T100 [US5] Create `GetChatUsageQuery`/Handler (per-provider/model breakdown + totals + `costIncomplete`) in `src/AskLucy.Application/Chats/Queries/GetChatUsage/`
- [ ] T101 [US5] Add `GET /api/v1/chats/{id}/usage` to `src/AskLucy.Web/Controllers/v1/ChatsController.cs` (depends on T100)
- [ ] T102 [US5] Add a per-message usage/cost display and a conversation usage-summary panel in `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx` and a new `UsageSummaryPanel.tsx`

**Checkpoint**: Users 1–5 stories all independently functional.

---

## Phase 8: User Story 6 - Administrator monitors provider health and usage (Priority: P3)

**Goal**: Admins can see provider health history and aggregate usage/cost across all users.

**Independent Test**: Simulate a provider outage; confirm the admin health view reflects it
within one check interval (spec.md).

### Tests for User Story 6

- [ ] T103 [P] [US6] Integration test: `ProviderHealthCheckHostedService` flips a provider's status healthy → unhealthy → healthy across simulated check cycles (SC-006) in `tests/AskLucy.Infrastructure.Tests/Ai/ProviderHealthCheckHostedServiceTests.cs`
- [ ] T104 [P] [US6] Contract test for `GET /api/v1/admin/ai/providers/{id}/health` and `GET /api/v1/admin/ai/usage` (contracts/admin.md) in `tests/AskLucy.Web.Tests/Controllers/AdminAiProvidersControllerTests.cs`

### Implementation for User Story 6

- [ ] T105 [US6] Create `GetProviderHealthHistoryQuery`/Handler in `src/AskLucy.Application/Ai/Queries/GetProviderHealthHistory/`
- [ ] T106 [US6] Create `GetAdminAiUsageQuery`/Handler (date-range validated, `groupBy=provider|model`, `costIncomplete` flag) in `src/AskLucy.Application/Ai/Queries/GetAdminAiUsage/`
- [ ] T107 [US6] Add `GET {id}/health` and `GET /api/v1/admin/ai/usage` to `src/AskLucy.Web/Controllers/v1/AdminAiProvidersController.cs` (depends on T105, T106)
- [ ] T108 [US6] Add a health-history view and an admin usage/cost report to `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx`

**Checkpoint**: Users 1–6 stories all independently functional.

---

## Phase 9: User Story 7 - User compares responses from multiple models (Priority: P4)

**Goal**: A user can fan a prompt out to 2+ models, view results side by side, and continue
from one.

**Independent Test**: Select 2+ models, submit a prompt, confirm side-by-side attributed
results; pick one to continue (spec.md).

### Tests for User Story 7

- [ ] T109 [P] [US7] Contract tests for `POST /api/v1/ai/compare` and `POST /api/v1/ai/compare/{id}/actions/continue` (contracts/chat.md) in `tests/AskLucy.Web.Tests/Controllers/AiControllerTests.cs`
- [ ] T110 [P] [US7] Unit test (handler, `IAIProviderResolver` faked): one failing model in a comparison doesn't block or hide the others' results (FR-026) in `tests/AskLucy.Application.Tests/Ai/CompareModelsCommandHandlerTests.cs` — *(analysis finding C1)*
- [ ] T111 [P] [US7] Unit test (handler, repositories faked): after `continue`, a follow-up send's context includes only the chosen candidate (`IsIncludedInContext` filtering) in `tests/AskLucy.Application.Tests/Ai/SendChatMessageCommandHandlerTests.cs` — *(analysis finding C1)*

### Implementation for User Story 7

- [ ] T112 [US7] Create `CompareModelsCommand`/Handler/Validator (2–5 selections, all enabled/available; concurrent fan-out, each selection independently try/caught per FR-026) in `src/AskLucy.Application/Ai/Commands/CompareModels/`
- [ ] T113 [US7] Create `ContinueFromComparisonCommand`/Handler (persists the prompt + one `Message` per successful candidate, `ComparisonGroupId` set, `IsIncludedInContext` true only for the chosen one; updates `UserChat.ProviderId`/`ModelId`) in `src/AskLucy.Application/Ai/Commands/ContinueFromComparison/`
- [ ] T114 [US7] Update context-assembly in `SendChatMessageCommandHandler` to filter conversation history on `IsIncludedInContext = true` in `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs` (depends on T012, T113)
- [ ] T115 [US7] Add `POST /api/v1/ai/compare` and `POST /api/v1/ai/compare/{id}/actions/continue` to `src/AskLucy.Web/Controllers/v1/AiController.cs` (depends on T112, T113)
- [ ] T116 [US7] Create a model-comparison UI (multi-model select, side-by-side response view, "continue with this" action) in new components under `src/AskLucy.Web/ClientApp/src/features/chat/components/ModelComparison/`

**Checkpoint**: All seven user stories independently functional.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [ ] T117 [P] Playwright E2E: multi-provider chat with attribution (quickstart.md Scenario 2) in `tests/AskLucy.E2E.Tests/multi-provider-chat.spec.ts`
- [ ] T118 [P] Playwright E2E: admin enable/disable + credential flow (quickstart.md Scenario 1) in `tests/AskLucy.E2E.Tests/admin-provider-management.spec.ts`
- [ ] T119 [P] Playwright E2E: model comparison flow (quickstart.md Scenario 7) in `tests/AskLucy.E2E.Tests/model-comparison.spec.ts`
- [ ] T120 [P] Performance test: `/api/v1/ai/chat` non-model latency overhead stays within SC-001's budget under load in `tests/AskLucy.Web.Tests/Performance/AiChatLatencyTests.cs`
- [ ] T121 Update `docs/ARCHITECTURE.md` §9 (AI provider abstraction) to describe the multi-provider design, superseding the old "single implementation" note
- [ ] T122 Security review pass: confirm no endpoint response ever contains a credential value, every new route carries a rate-limit policy, and every admin mutation is audit-logged (constitution §16 gate 6)
- [ ] T123 Run all 8 quickstart.md scenarios end-to-end and record results

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–9)**: All depend on Foundational. US1 and US2 (both P1) have no
  dependency on each other and can proceed in parallel; US3–US7 build on US2's revised
  `SendChatMessageCommand`/catalog endpoints existing, so are easiest done after US2 even
  though nothing prevents starting them earlier with a team split.
- **Polish (Phase 10)**: Depends on whichever stories are in scope for the release being
  polished.

### User Story Dependencies

- **US1 (P1)**: Foundational only. Fully independent of US2–US7.
- **US2 (P1)**: Foundational only. Independent of US1 for its own testability, though a
  real demo benefits from US1 existing first (something has to enable a provider).
- **US3 (P2)**: Foundational + reads the catalog/validation shape US2 establishes (T068–T073).
- **US4 (P2)**: Foundational + extends the `GenerationParametersDto`/validator US2 creates.
- **US5 (P3)**: Foundational + reads the usage columns US2 starts populating.
- **US6 (P3)**: Foundational + reads the health mechanism US1 builds (T038) and the usage
  data US2/US5 populate.
- **US7 (P4)**: Foundational + reuses `IAIProviderResolver`/providers from US2 and the
  `IsIncludedInContext` context-assembly it extends.

### Within Each User Story

- Tests written first, confirmed failing, then implementation.
- Domain/Application (commands/queries) before controllers before frontend.
- Story complete and independently checkpointed before moving to the next priority.

### Parallel Opportunities

- All `[P]` Setup tasks (T001–T003, T005–T006) run in parallel.
- Within Foundational, all `[P]` Domain tasks (T007–T011), all `[P]` Configuration tasks
  (T025–T028), and both repository-implementation tasks (T032–T033) run in parallel; T035's
  migration must wait for all Configuration/DbSet tasks (T025–T031) to land first.
- Once Foundational is done, **US1 and US2 can be staffed in parallel** — they touch
  disjoint files until T048/T070 (different controllers) and share no handler.
- Within US2, the three new vendor providers (T064–T066) are fully parallel (different
  files, no shared state).
- US3, US4, US5 can be staffed in parallel by different developers once US2's T068–T073
  land, since each touches a distinct vertical slice (preferences / parameters / usage).

---

## Parallel Example: Foundational Phase

```bash
# Domain entities together:
Task: "Create ModelPricing value object in src/AskLucy.Domain/Ai/ModelPricing.cs"
Task: "Create AIProvider entity in src/AskLucy.Domain/Ai/AIProvider.cs"
Task: "Create AIModel entity in src/AskLucy.Domain/Ai/AIModel.cs"
Task: "Create ProviderHealthCheck entity in src/AskLucy.Domain/Ai/ProviderHealthCheck.cs"
Task: "Create UserAiPreference entity in src/AskLucy.Domain/Ai/UserAiPreference.cs"

# EF configurations together (after entities exist):
Task: "Create AIProviderConfiguration in src/AskLucy.Persistence/Configurations/AIProviderConfiguration.cs"
Task: "Create AIModelConfiguration in src/AskLucy.Persistence/Configurations/AIModelConfiguration.cs"
Task: "Create ProviderHealthCheckConfiguration in src/AskLucy.Persistence/Configurations/ProviderHealthCheckConfiguration.cs"
Task: "Create UserAiPreferenceConfiguration in src/AskLucy.Persistence/Configurations/UserAiPreferenceConfiguration.cs"
```

## Parallel Example: User Story 2

```bash
# The three new vendor providers, together:
Task: "Implement AnthropicProvider in src/AskLucy.Infrastructure/Ai/AnthropicProvider.cs"
Task: "Implement GoogleGeminiProvider in src/AskLucy.Infrastructure/Ai/GoogleGeminiProvider.cs"
Task: "Implement OpenRouterProvider in src/AskLucy.Infrastructure/Ai/OpenRouterProvider.cs"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 — both P1)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (critical — blocks everything).
3. Complete Phase 3 (US1) and Phase 4 (US2) — together these are the feature's actual MVP:
   an admin can turn on real providers, and a user can chat through any of them with
   correct attribution.
4. **STOP and VALIDATE**: run quickstart.md Scenarios 1, 2, and 8.
5. Deploy/demo if ready.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 + US2 → MVP → validate → deploy/demo.
3. US3 (defaults) + US4 (parameters) → validate → deploy/demo.
4. US5 (usage/cost) + US6 (admin monitoring) → validate → deploy/demo.
5. US7 (comparison) → validate → deploy/demo.
6. Polish (Phase 10) → E2E/perf/security/doc pass → final validation.

### Parallel Team Strategy

1. Team completes Setup + Foundational together (Foundational is large — split Domain/
   Application/Persistence/Cross-cutting sub-groups across developers using the `[P]`
   markers).
2. Once Foundational is done:
   - Developer A: US1 (admin)
   - Developer B: US2 (chat) — the other P1, highest-value path
   - Developer C: starts US4/US5 groundwork once US2's `GenerationParametersDto`/usage
     columns exist
3. US7 last — it's the lowest priority and depends on US2's resolver/provider work being
   solid first.

---

## Notes

- `[P]` tasks touch different files with no unmet dependency on an incomplete task.
- `[Story]` labels trace every task back to spec.md for scope/priority audits.
- T024 (ADR) is deliberately sequenced immediately after T021–T023 (the keyed-DI
  implementation it documents) and depends on it explicitly — Task ID order matches
  execution order throughout this document, per the Format section above.
- Commit after each task or logical group; stop at any checkpoint to validate a story
  independently before continuing.
- Avoid: vague tasks, two tasks editing the same file marked `[P]`, cross-story
  dependencies that would break a story's independent testability.
- Tasks/edits marked *(analysis finding G1/G2/G3/G4/C1/I2/I3)* were added or revised by
  `/speckit-analyze`'s remediation pass (see git history / conversation record) — they
  close gaps found between spec.md's FR-006/FR-028/FR-030 and the original task list,
  clarify unit- vs. integration-test intent per constitution §10, fix the ADR's listed
  position to match its actual execution order, and disambiguate a stale FR cross-reference
  in `AiController`'s doc comment. (I1, a plan.md wording fix, has no task-level marker.)
