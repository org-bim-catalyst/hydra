# Research: Model Deprecation Workflow

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-06

The current code was checked on 2026-10-06. Paths are relative to `src/`.

## Current-state facts that shape the design

| # | Fact | Where |
|---|---|---|
| F1 | `AIModel.SetStatus` allows any transition. Only the admin UI stops a Deprecated model being re-enabled; the API accepts any status. | `AskLucy.Domain/Ai/AIModel.cs:139`, `UpdateAiModelStatusCommandValidator.cs` |
| F2 | The sync apply sets removed-from-vendor rows to **Unavailable**, applying them per row and saving once for the whole batch. | `ApplyProviderModelSyncCommandHandler.cs:83,87` |
| F3 | `RemovedModelDto(Id, ModelKey, DisplayName)` lists only Available models. | `ProviderModelSyncDiffDto.cs:6`, `GetProviderModelSyncDiffQueryHandler.cs:34-37` |
| F4 | Platform defaults are `AIProvider.DefaultModelId` and `AiCapabilityAssignment.ModelId`. A null assignment means "use the provider default". Only `ImageGeneration` must name a model. | `AIProvider.cs:55`, `AiCapabilityAssignment.cs:26,65`, `AiCapability.cs` |
| F5 | Editable user settings are `UserChat.ProviderId/ModelId`, `Agent.ModelProviderId/ModelId` (the draft), `Prompt.PreferredModelKey`, the `providerId`/`modelId` in a workflow draft's AiPrompt node `ConfigurationJson`, and `UserAiPreference.DefaultModelId` (never written in practice). | entity files |
| F6 | **Published versions can't be changed.** Agents run from `AgentVersion`, workflows from `WorkflowVersion` (pinned on the execution), and `PromptVersion`/`Message` are history. | `AgentExecutionOrchestrator.cs:91`, `WorkflowExecutionOrchestrator.cs:155` |
| F7 | Only chat send checks whether a model can be used. Agents, workflow AiPrompt steps and prompt execution call whatever model they are given. | `SendChatMessageCommandValidator.cs:40,47`; `AgentExecutionOrchestrator.cs:133-136`; `PromptNodeExecutor.cs:78-83`; `ExecutePromptCommandHandler.cs:58-61` |
| F8 | `DefaultProviderResolver` falls back silently and logs nothing. `AiCapabilityProviderResolver` logs its fallbacks. | resolvers |
| F9 | The notification hub (067) has in-app delivery working. `Publish` adds an outbox row to the caller's own unit of work, and accepts at most 100 users per request. | `NotificationPublisher.cs:58`, `INotificationPublisher.cs:40` |
| F10 | **Until 067's email channel is built, email deliveries are recorded as Skipped (channel unavailable), not queued.** | `Infrastructure/Notifications/NotificationChannelRegistry.cs` |
| F11 | Admins are found through the permission catalogue: `IRoleRepository.ListByPermissionAsync` and then `IRoleAssignmentRepository.ListUserIdsByRoleAsync`. Built-in roles are named Administrator and Super User. | `SuperUserControlledPermissionGuard.cs:122-143`, `PrivilegedRoleNames.cs` |
| F12 | `IUnitOfWork` has no transaction API. There is no domain-event dispatcher. Set-based writes inside one explicit transaction live in Persistence stores (precedent: `OperationalFailureStore`). | `IUnitOfWork.cs`, `OperationalFailureStore.cs:409` |
| F13 | Spec 074's failure audit is built: `IOperationalFailureRecorder.Record(...)` never throws, user messages are kept generic, and engines include `Agent`, `Workflow` and `Chat`. | `OperationalFailures/Abstractions/IOperationalFailureRecorder.cs` |

---

## D1: Choosing the replacement ("same tier")

**Decision**: Add a pure Domain service, `ReplacementModelSelector`. Given the retired model and the provider's other models, it returns them ranked, best first:

1. **Filter**: same provider; `Status == Available`; not in the current confirmation's set of models being deprecated; supports **every** capability flag the retired model has (out of the 9 `Supports*` flags).
2. **Price distance**: `|ΔInputPerMillion| + |ΔOutputPerMillion|`, smaller first. Candidates with unknown price rank after every priced candidate. If the retired model has no price, price is ignored.
3. **Context window**: `ContextWindowTokens ≥ retired` ranks first. If either value is unknown, the candidate ranks after the known ones.
4. **Newest**: `ReleaseDate` descending, unknown last.
5. **Tie-break**: `ModelKey` ordinal, so the result is deterministic.

**Rationale**: This uses only data the catalog already has (spec Q1 → A). It can be unit-tested with no I/O, and the same function serves both the preview and the confirm-time recheck. That keeps one rule in one place (§2 III).

**Alternatives considered**: An admin-maintained tier label was rejected in Q1. Ranking by context window first was rejected: two models of very different cost can have the same window, and price is the closest proxy for "performance class" in the data we have.

## D2: Capabilities a default's function requires (FR-007 / FR-008 exception)

**Decision**: Add a Domain map, `PlatformFunctionRequirements`, giving the capability flags each default target needs:

| Target | Required flags |
|---|---|
| `AIProvider.DefaultModelId` (feeds Chat and the fallback for every other capability) | `SupportsStreaming` |
| `AiCapability.Chat` | `SupportsStreaming` |
| `AiCapability.ImageGeneration` | `SupportsImageOutput` |
| `AiCapability.BoundaryVision` | `SupportsVision` |
| all other capabilities | none |

A candidate from D1 already supports a superset of the retired model's flags. So this map only matters when an admin overrides the proposal (FR-007): the review lists the lost flags, and a target whose required flag is lost is **flagged**, not reassigned.

**Rationale**: Without this map, an admin override could quietly assign a model that can't produce images to `ImageGeneration`.

## D3: What gets switched, and what is resolved at run time

**Decision**: There are two mechanisms. Each covers the place the other can't reach.

1. **Rewrite every editable setting** (FR-014, Q2 → A): `UserChat`, the `Agent` draft, `Prompt.PreferredModelKey`, the AiPrompt node config in a workflow draft, `UserAiPreference`, `AIProvider.DefaultModelId` and `AiCapabilityAssignment.ModelId`. Each user-item rewrite is recorded as an `ItemSwitch` row.
2. **Resolve the successor at run time for immutable snapshots**: published agent and workflow versions (F6), and paused or queued executions pinned to them. A new Application service, `ExecutableModelResolver`, takes `(modelId)` and returns:
   - the model itself, if it is not Deprecated (Unavailable behavior is unchanged, per the spec assumptions);
   - otherwise the end of its replacement chain, if that model is Available. The chain is followed through `ModelDeprecation.ReplacementModelId`, at most 10 hops, with cycles detected;
   - otherwise a `ModelRetiredException`. Its user-safe message names the retired model and tells the user how to choose another (FR-015).

   It is called by the agent orchestrator (L133), `PromptNodeExecutor` (L78), `ExecutePromptCommandHandler` (L58) and the chat send validator. The model that actually ran is the one recorded on the output (FR-016).

**Rationale**: Published versions are audit artifacts, and an execution is pinned to the version it started on. Rewriting them would make history lie. A run that resumes after a deprecation still has to work, though (FR-013: the change applies "from the next request or step"). Resolving at run time covers that without republishing anything. Mechanism 1 keeps every model picker and settings screen honest. Mechanism 2 means nothing breaks while the user-item job (D4) is still running.

**Alternatives considered**:
- *Clone the published version with only the model changed (a system republish)*. Rejected. It interacts with publish validation and approval policies, adds system-authored entries to the user's version history, and still misses executions pinned to the old version.
- *Run-time redirect only*. Rejected by Q2: pickers and settings would keep showing the retired model.

**Not silent**: a redirect at run time logs at Information (`ModelSuccessorResolved`), and the output's model attribution shows the successor, so it is never invisible (§2 VIII). A `ModelRetiredException` from an agent or workflow step is also reported to `IOperationalFailureRecorder` (Engine `Agent`/`Workflow`, Kind `NotConfigured`), following spec 074's convention.

## D4: What commits at confirm time, and what is fanned out afterwards

**Decision**:

- **Confirm (one `SaveChangesAsync`, inside the existing sync-apply request)**: for each selected removed row:
  - `AIModel.Deprecate(...)`;
  - a `ModelDeprecation` record;
  - every platform-default reassignment or flag (`PlatformDefaultChange`);
  - the admin summary `Publish` calls (they join the same unit of work, per F9).

  One `ModelDeprecationBatch` per confirmation records `UserImpactStatus = Pending`. This makes FR-005 (all-or-nothing per model) and FR-019 (durable alongside the deprecation) hold.
- **User impact (after the confirm commits)**: a Hangfire job, `ModelDeprecationUserImpactJob(batchId)`, is enqueued right after the commit. A recurring sweep (every minute, `RegisterRecurringJob`) re-enqueues any batch still Pending after 2 minutes, which covers a crash between commit and enqueue. The job walks the affected **owners** in chunks of 100. For each chunk it:
  1. loads that chunk's items that still name any model in the batch;
  2. rewrites them (D3.1) and inserts `ItemSwitch` rows;
  3. publishes one user notice per owner, listing all of that owner's items across every model in the batch (FR-017; `EventKey = ai-model-deprecation:{batchId}:{userId}`);
  4. commits the chunk with one `SaveChangesAsync`.

  When no owners remain, it sets `UserImpactStatus = Completed` and `NotifiedUserCount`.

**Rationale**: SC-007 requires confirming in under 10 s even when thousands of items reference the model. Loading and rewriting thousands of tracked rows inside the admin's request would break that. Each chunk is idempotent: it only touches items that still name a Deprecated model, and the hub de-duplicates by `EventKey`. That makes the job safe to restart (§2 VIII, and the precedent of 067's own workers). Until the job reaches an item, D3.2 keeps that item working.

**Alternatives considered**:
- *Do everything in one request*: fails SC-007.
- *Set-based `ExecuteUpdate` inside one explicit transaction (the `OperationalFailureStore` pattern)*: rejected. Rewriting workflow node JSON can't be done set-based, and per-owner notices still need the per-owner list.

## D5: Hooking into the sync apply

**Decision**: `ApplyProviderModelSyncCommand.RemovedFromVendor` entries gain an optional `ReplacementChoice`: `{ kind: "proposed" | "model" | "none", modelId? }`. Omitting it means "proposed". The handler hands every non-stale removed row to `ModelDeprecationService.Deprecate(batch, model, choice)` instead of `SetStatus(Unavailable)`.

At confirm time the service re-ranks D1, because the preview may be stale (spec edge case):
- if the admin's explicit choice is no longer Available, that row **fails** with a reason (per row, specs/009 FR-007a);
- if the proposed model is no longer Available, the next-ranked candidate is used and reported.

The response DTO gains `deprecations[]` (per model: replacement, reassigned, flagged). Added-side behavior is unchanged.

**Rationale**: This reuses 009's per-row best-effort contract and its single save. FR-005's per-model atomicity holds because a row either passes every check before the save or is excluded from it.

## D6: Locking the Deprecated status in the Domain

**Decision**: `AIModel.SetStatus` throws `DomainRuleViolationException` when the current **or** target status is Deprecated (FR-021, FR-022). A new `AIModel.Deprecate()` is the only way into Deprecated, and it is called only by `ModelDeprecationService`. The status endpoint maps the exception to a `409` Problem Details with a clear `detail`. The admin UI toggle stays disabled (specs/008 US2 scenario 3).

**Rationale**: The client-hidden button is not an authorization control (§8). The invariant belongs on the aggregate (§3 Application services).

## D7: Notification types and recipients

**Decision**: Add four `NotificationTypeCatalog` entries, all in category **System**:

| Key | Recipient | In-app | Email | Purpose |
|---|---|---|---|---|
| `system.ai-model.deprecated` | affected owner | Mandatory | On | FR-017 user notice |
| `system.ai-model.replacement-changed` | affected owner | Mandatory | On | FR-011b follow-up |
| `system.ai-model.deprecation-summary` | AI-provider admins who are **not** Super User-only | Mandatory | **Mandatory** | FR-018, FR-020b |
| `system.ai-model.deprecation-summary.super-user` | users whose admin access comes **only** from the Super User role | Mandatory | On | FR-020b opt-out |

How the recipients are built:
- **Admins**: the union of the Administrator and Super User role members and the users of every role returned by `ListByPermissionAsync("admin.ai-providers.manage")` (F11). Users who hold Super User and no other qualifying role get the `.super-user` type; everyone else gets the mandatory type (FR-020b: holding both roles counts as Administrator). Requests are chunked to at most 100 users (F9).
- **Owners**: each owner is resolved by the owning system, our deprecation job, so 067's caller rule 4 holds.

Templates are seeded in English under `Infrastructure/Notifications/Templates/Seed/en/{key}.{inapp|email}.json` for all four keys. They are logic-free, and the variables are plain text (see [contracts/notification-types.md](contracts/notification-types.md)).

**Rationale**: 067's mandatory rule is per type and per channel (F9; `ChannelDefault.Mandatory`), and 067's preferences work (US4) isn't built yet. Splitting the summary into two types gives FR-020b's role-dependent lock with **no change to the hub**. The two summary types share one Application payload builder, so the business logic is not duplicated. The only duplicates are the two template files.

**Revisit when 067 US4 ships**: if 067 adds role-conditional mandatory channels, merge the two summary types into one.

## D8: Email before 067's email channel exists (corrects the spec)

**Finding**: F10. A notice published before 067 US3 ships records its email delivery as **Skipped**, and it is **never** sent later. Spec FR-020a assumed those emails would go out once the channel shipped.

**Decision**: Publish with email requested, as FR-020 says. This feature does nothing more for emails. Deprecations confirmed **after** 067's email channel ships send email. Deprecations confirmed **before** it are in-app only, and the email shows as Skipped in the delivery records.

**Rationale**: Replaying weeks-old deprecation emails would confuse users, and building a replay path would mean changing 067's routing rules from outside 067. Deprecations are rare, admin-triggered events, so the gap is small.

**Spec follow-up**: FR-020a and SC-006 need to be reworded to match this. It was raised with the user at plan hand-off.

## D9: Flags, validation and their resolution

**Decision**:
- **Flags** are `PlatformDefaultChange` rows with `Outcome = Flagged`. There is no separate entity. A flag resolves inside the same unit of work as the admin's fix. `UpdateAiProviderCommandHandler` (set or clear default) and the capability-assignment upsert handler each call a new Application service, `PlatformDefaultFlagResolver.ResolveFor(target, newModelId, actor)`. Resolution is recorded on the row (FR-010).
- **Validation** (FR-011a/b) has two commands:
  - `AcceptModelReplacement(deprecationId)` marks the replacement Accepted.
  - `ChangeModelReplacement(deprecationId, newModelId)` re-points every `PlatformDefaultChange(Reassigned)` target whose current value still equals the old replacement. It records a new `ModelDeprecationBatch` of kind `ReplacementChange` and enqueues the D4 job in "re-point" mode. That job moves only items whose current model equals `ItemSwitch.NewModelId` and sends `system.ai-model.replacement-changed`.

**Rationale**: The codebase has no domain-event dispatcher (F12). A post-commit event would also break "the flag is resolved together with the fix". So a direct call inside the same unit of work is the honest choice. See the plan's Complexity Tracking.

## D10: Impact preview in the sync diff (FR-003)

**Decision**: `RemovedModelDto` gains `impact`, built by one Persistence query per diff call. It is grouped per model and per item type, and the query returns counts only (spec FR-003). The fields are:
- `platformDefaults[]`, each with `{ target, capability? }`;
- `proposedReplacement`, `{ id, displayName, missingCapabilities: [] }` or null;
- `eligibleReplacements[]`, the D1 ranking, used for the override picker;
- `itemCounts`, `{ conversations, agents, prompts, workflowSteps }`;
- `distinctUserCount`.

Workflow steps are counted in draft workflows only. The query pre-filters node `ConfigurationJson` with a `LIKE` on the model id GUID, then parses the matches in memory to confirm an AiPrompt node really names the model.

**Rationale**: The diff runs only when an admin starts a sync, and drafts number in the hundreds at most, so the scan is cheap (§15).

**Alternative considered**: a maintained workflow-to-model reference projection. Rejected as YAGNI (§2 III) at the platform's real scale.

## D11: Reference counting excludes deleted and historical rows (FR-012)

**Decision**: Counts and rewrites go through the global soft-delete query filters, so deleted chats, agents, prompts and workflows are excluded. `Message`, `AgentVersion`, `WorkflowVersion`, `PromptVersion` and `PromptTestCase` are never counted or rewritten. Archived chats **are** included.
