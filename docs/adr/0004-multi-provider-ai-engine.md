# ADR-0004: Multi-Provider AI Engine — keyed-DI provider resolution and entity reuse

**Status**: Accepted
**Date**: 2026-07-30
**Deciders**: Engineering (via `/speckit-plan`/`/speckit-analyze` for SPEC-005)

## Context

`specs/005-multi-provider-ai-engine` replaces the single, hardcoded `IAIProvider`/
`OpenAIProvider` pairing (frozen at one vendor by the legacy-modernization spec's FR-022)
with support for four vendors (OpenAI, Anthropic, Google Gemini, OpenRouter), selectable
per conversation/message. Two decisions here are the kind constitution §17 requires an ADR
for: a new architectural pattern with no prior use in this codebase, and a choice that is
expensive to reverse once conversations start being written against it.

## Decision 1: Keyed dependency injection for provider resolution

The provider to call is only known at request time (from a conversation's or message's
stored `ProviderId` → `AIProvider.ProviderKey`), not at compile time. Each `IAIProvider`
implementation is registered as a **keyed** scoped service
(`AddKeyedScoped<IAIProvider, OpenAIProvider>("openai")`, etc.,
`src/AskLucy.Infrastructure/DependencyInjection.cs`), and `IAIProviderResolver`
(`src/AskLucy.Application/Abstractions/IAIProviderResolver.cs`) is the single seam
Application code depends on to look one up — `Resolve(string providerKey)`, implemented in
`Infrastructure` via `IServiceProvider.GetRequiredKeyedService<IAIProvider>`.

**Alternatives considered**:
- *Hand-rolled `Dictionary<string, IAIProvider>` factory* — rejected: reimplements what
  keyed DI already validates at container-build time; a typo'd key fails loudly at
  `GetRequiredKeyedService` instead of silently at request time.
- *`IEnumerable<IAIProvider>` filtered by `ProviderName` at each call site* — rejected:
  pushes provider-selection logic into every consumer instead of one resolver.

This is the first use of keyed DI in this codebase. No prior pattern was available to
follow; this ADR is the record of the decision, per constitution §17.

**Backward compatibility.** Four pre-existing call sites (`TranslateCommandHandler`,
`GenerateImageCommandHandler`, `TranscribeAudioCommandHandler`,
`AppendMessageCommandHandler`) predate per-request provider selection and are out of this
spec's scope. Rather than forcing them onto the resolver, `OpenAIProvider` is registered
**both** as the keyed `"openai"` service **and** as the plain, unkeyed `IAIProvider` — those
four handlers keep injecting `IAIProvider` directly and stay wired to OpenAI, unchanged.
`IAIProvider` itself keeps its original single-model members (`ChatModel`, `ImageModel`,
and the two-argument `ChatAsync`/`StreamChatAsync`/`GenerateImageAsync` overloads) alongside
new model/parameter-aware overloads, so those four files needed zero code changes.

## Decision 2: Reuse `Message`/`UserChat` instead of new `MessageUsage`/`ConversationModelSettings` tables

The spec's Key Entities list describes "Message Usage" and "Conversation Model Settings"
as concepts. They are implemented as additional columns on the existing `Message` and
`UserChat` entities (both already carried `Provider`/`Model` since SPEC-002), not as new
tables — see `data-model.md` and `research.md` Decision 2 for the full column list and
rationale.

**Why this is expensive to reverse once accepted**: once messages are being written with
`Message.EstimatedCostUsd`/`ComparisonGroupId`/etc. as first-class columns on the existing,
already-indexed `Messages` table, splitting them out into a separate `MessageUsage` table
later would require a data migration touching every historical row, not just a schema
addition. Accepting this now, deliberately, avoids discovering the tradeoff mid-migration
later.

**Alternatives considered**: see research.md Decision 2 (separate 1:1 tables; JSON-blob
storage for the new scalar fields). Both rejected for the reasons detailed there
(DRY/YAGNI — no independent lifecycle or query pattern justifies the split).

## Consequences

- Adding a fifth AI provider is a new `IAIProvider` implementation class, one
  `AddKeyedScoped` registration line, and one `AIProvider`/`AIModel` seed row — zero changes
  to `Application`, `Domain`, or any existing provider class (verified by quickstart.md
  Scenario 8 / spec.md SC-003).
- `IAIProvider` carries more surface area (both legacy and multi-provider-aware overloads)
  than a from-scratch design would — an accepted cost of not touching four working,
  out-of-scope files.
- If a future spec ever needs `Message`/`UserChat`'s usage or model-selection data to have
  an independent lifecycle from their parent row (e.g., editable/re-computable cost after
  the fact), that will require a real migration, not just a new nullable column — flagged
  here so it isn't a surprise.
