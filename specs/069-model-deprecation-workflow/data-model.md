# Data Model: Model Deprecation Workflow

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

Every new entity inherits `BaseEntity`, so it has a `Guid` id, created/modified audit columns stamped by the interceptor, soft-delete columns and a `RowVersion`. Persistence mapping lives in Fluent configuration classes in `AskLucy.Persistence` (§3, "Domain purity"). One migration, `AddModelDeprecationWorkflow`, adds every table below and is reversible.

## Changed: `AIModel` (`AskLucy.Domain/Ai/AIModel.cs`)

No new columns.

| Member | Change |
|---|---|
| `SetStatus(AIModelStatus)` | Throws `DomainRuleViolationException` when the current **or** target status is `Deprecated` (FR-021, FR-022; research D6). |
| `Deprecate()` *(new)* | Sets `Deprecated` when the current status is `Available`; otherwise throws. It is the only way into Deprecated, and only `ModelDeprecationService` calls it. |

**State transitions**:

```text
Available ⇄ Unavailable      (simple toggle, specs/008 FR-002)
Available → Deprecated       (Deprecate(), this feature only)
Deprecated → (none)          (terminal, FR-021)
```

## New: `ModelDeprecationBatch` (aggregate root)

One confirmed admin action: a sync confirmation (Kind `Deprecation`), or a replacement change during validation (Kind `ReplacementChange`).

| Field | Type | Notes |
|---|---|---|
| `ProviderId` | Guid, FK → `AIProviders`, Restrict | |
| `Kind` | enum `Deprecation` / `ReplacementChange` | stored as a string |
| `ConfirmedByUserId` | string | the acting admin |
| `ConfirmedAtUtc` | DateTime | |
| `UserImpactStatus` | enum `Pending` / `Completed` | research D4; indexed together with `ConfirmedAtUtc` for the sweep |
| `UserImpactCompletedAtUtc` | DateTime? | |
| `NotifiedUserCount` | int | set when the job completes |
| `Deprecations` | `List<ModelDeprecation>` | children; Kind `Deprecation` only |
| `ReplacementChangeOfDeprecationId` | Guid? | Kind `ReplacementChange` only; FK → `ModelDeprecations` |

Methods: `MarkUserImpactCompleted(int notifiedUsers)` succeeds only from `Pending`.

## New: `ModelDeprecation` (child of the batch)

One per deprecated model. This is the spec's **Deprecation Record** (FR-002).

| Field | Type | Notes |
|---|---|---|
| `BatchId` | Guid, FK, Cascade | |
| `ModelId` | Guid, FK → `AIModels`, Restrict | **unique**, because a model is deprecated once |
| `Reason` | enum `RemovedFromVendor` | stored as a string; only this value is used in this feature |
| `ReplacementModelId` | Guid?, FK → `AIModels`, Restrict | null means no suitable replacement; indexed (successor chain, D3) |
| `ReplacementSource` | enum `Proposed` / `AdminChosen` / `None` | |
| `ValidationStatus` | enum `AwaitingValidation` / `Accepted` / `Changed` / `NotApplicable` | `NotApplicable` when there is no replacement (FR-011a) |
| `ValidatedByUserId`, `ValidatedAtUtc` | string?, DateTime? | |
| `PlatformDefaultChanges` | `List<PlatformDefaultChange>` | children |

**Rules**:
- `ReplacementModelId` must be on the same provider and must not equal `ModelId`. It must be Available at confirmation time (D5).
- `Accept()` and `ChangeReplacement(newId)` are allowed only from `AwaitingValidation`. `ChangeReplacement` moves to `Changed` and replaces `ReplacementModelId`. The previous value is kept on the `ReplacementChange` batch's item switches and default changes.

## New: `PlatformDefaultChange` (child of `ModelDeprecation`)

One per platform default the retired model filled. It also serves as the spec's **Default Attention Flag** when `Outcome = Flagged`.

| Field | Type | Notes |
|---|---|---|
| `DeprecationId` | Guid, FK, Cascade | |
| `TargetKind` | enum `ProviderDefault` / `CapabilityAssignment` | |
| `ProviderId` | Guid | the provider whose default this was |
| `Capability` | `AiCapability`? | set when `TargetKind = CapabilityAssignment` |
| `PreviousModelId` | Guid | the retired model |
| `Outcome` | enum `Reassigned` / `Flagged` | |
| `NewModelId` | Guid? | set when `Reassigned` |
| `FlagReason` | enum `NoEligibleReplacement` / `MissingRequiredCapability` / `AdminChoseNone` | set when `Flagged` |
| `ServingProviderId`, `ServingModelId` | Guid? | what the default rules now serve, captured at flag time (FR-009) |
| `ResolvedByUserId`, `ResolvedAtUtc`, `ResolutionModelId` | | FR-010; null means the flag is open |

Index: `(Outcome, ResolvedAtUtc)`, filtered to open flags, for the AI Providers page banner.

**Clearing a flagged default** (FR-009): a flagged `ProviderDefault` sets `AIProvider.DefaultModelId = null`. A flagged `CapabilityAssignment` sets its `ModelId = null`. For `ImageGeneration`, which can't be null (F4), the assignment row is **removed**, and the capability then reports "not configured" (FR-011).

## New: `ItemSwitch`

One end-user item moved off a retired model. It is written by the user-impact job (D4), and it is its own table because a batch can produce thousands of rows. It is reached only through `IModelDeprecationRepository` query methods.

| Field | Type | Notes |
|---|---|---|
| `BatchId` | Guid, FK, Cascade | |
| `DeprecationId` | Guid, FK, Restrict | which retired model |
| `OwnerUserId` | string | indexed together with `BatchId` (notice building) |
| `ItemKind` | enum `Conversation` / `Agent` / `Prompt` / `WorkflowStep` / `UserDefault` | |
| `ItemId` | Guid | for `WorkflowStep`, the workflow id |
| `NodeId` | string? | `WorkflowStep` only |
| `ItemLabel` | string(200) | the item's name when switched, used in the notice |
| `PreviousModelId`, `NewModelId` | Guid | |

**Rule (FR-011b)**: a re-point moves an item only if its **current** model still equals `NewModelId`. If the owner has changed it since, the item is left alone.

## Unchanged, but read or written

| Entity | Use |
|---|---|
| `AIProvider.DefaultModelId` | reassigned or cleared at confirm time |
| `AiCapabilityAssignment.ModelId` | reassigned, nulled, or removed at confirm time |
| `UserChat`, `Agent` (draft), `Prompt.PreferredModelKey`, `Workflow` draft node config, `UserAiPreference` | rewritten by the job |
| `AgentVersion`, `WorkflowVersion`, `PromptVersion`, `PromptTestCase`, `Message` | **never** written; resolved at run time (D3.2) |
| `NotificationOutboxEvent` (067) | appended through `INotificationPublisher` |

## Application-layer value types (not persisted)

- `ReplacementChoice(Kind: Proposed | Model | None, ModelId?)`: the per-row request input.
- `DeprecationImpact`: the per-model preview (contract §1).
- `ReplacementCandidate(ModelId, DisplayName, MissingCapabilities)`: one entry in the D1 ranking.
- `ExecutableModel(ModelId, ProviderId, ModelKey, WasRedirected, RetiredModelId?)`: the result of `ExecutableModelResolver`.
