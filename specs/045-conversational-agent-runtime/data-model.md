# Phase 1 Data Model: Conversational Agent Runtime

**Feature**: 045-conversational-agent-runtime | **Date**: 2026-09-08

One EF Core migration. Every schema change is additive or widening — no column is dropped, no existing row is invalidated, and a conversation that predates this feature reads back unchanged (FR-049).

---

## 1. `SuggestedAction` — value object (Domain/Conversations)

Not a table. Serialized as a JSON array into `Message.SuggestedActionsJson`.

| Field | Type | Rules |
|---|---|---|
| `Kind` | enum | `FlowVariant` \| `Capability` \| `FollowUp` \| `Decline` (FR-021a). Determines which grounding rule applies and which dispatch path runs. |
| `Key` | string (≤120) | Required for `FlowVariant` (`flow:variant`) and `Capability`; **null** for `FollowUp` and `Decline`. Must resolve to a registered, available entry at both offer and dispatch time (FR-024.1/FR-028). |
| `Text` | string (≤300) | `FollowUp` only — the composed instruction, echoed back on selection. Null for every other kind. Composed per situation, never registry-backed (FR-021b, research.md D20). |
| `Label` | string (≤80) | Required. User-facing, spoken aloud (FR-044). |
| `Description` | string (≤160) | Required. One line, rendered under the label, **never spoken** (FR-044). |
| `ArgumentsJson` | string | Required for `FlowVariant`/`Capability`; must validate against the capability's `InputSchemaJson`. `{}` when none needed; null for `FollowUp`/`Decline`. |
| `IsDecline` | bool | True only on the single `Decline` row, appended server-side and always last (FR-022). |

**Validation**: an offer is 2–`MaxSuggestedActions` (default 5) entries inclusive — so at most 4 substantive rows plus the decline (FR-023) — containing exactly one decline, with distinct `Kind` + `Key` + `ArgumentsJson` triples. An offer that fails validation after grounding is dropped **entirely** rather than shown partially (FR-025). There is no free-text row: the composer is live throughout (FR-031).

**Grounding differs by kind** (FR-024): `FlowVariant`/`Capability` rows are checked absolutely against the registry; `FollowUp` rows have no key to check and are guaranteed instead by dispatch — selecting one can never invoke a capability (FR-021c, SC-002a).

---

## 2. `Message` — modified (Domain/Chats)

Append-only aggregate; all three columns are set at creation and never updated (research.md D6).

| New column | Type | Null | Meaning |
|---|---|---|---|
| `SuggestedActionsJson` | `nvarchar(max)` | yes | The offer this **assistant** message made. Null on user messages and on assistant messages that offered nothing. |
| `SelectedActionKind` | `nvarchar(20)` | yes | On a **user** message created by selecting a row: which kind was chosen (`FlowVariant`, `Capability`, `FollowUp`, `Decline`). Null for typed messages. |
| `SelectedActionKey` | `nvarchar(120)` | yes | The flow-variant or capability key chosen; null for `FollowUp` and `Decline`, whose identity is carried by the row's text and label. |
| `SelectedActionArgumentsJson` | `nvarchar(max)` | yes | The arguments bound to that selection. Non-null exactly when `SelectedActionKey` is non-null. |

**Invariants**
- `SuggestedActionsJson` is non-null only when `Role = Assistant`.
- `SelectedActionKind` is non-null only when `Role = User`. `SelectedActionKey`/`SelectedActionArgumentsJson` are non-null only alongside a `FlowVariant` or `Capability` kind, and are both null or both non-null.
- The **live** offer is the newest assistant message in the chat with non-null `SuggestedActionsJson` that no later user message has already answered. All earlier offers render inert (FR-029).

**No index added.** The live offer is found from the last page of messages already loaded for the conversation; an index would serve no query that exists.

---

## 3. `Agent` — modified (Domain/Agents)

| New column | Type | Null | Meaning |
|---|---|---|---|
| `SystemKey` | `nvarchar(64)` | yes | Stable identity of a platform-provisioned agent (`lucy.orchestrator`, `lucy.site`, `lucy.knowledge`, `lucy.memory`, `lucy.viewer`). Null for user-created agents. **Unique filtered index** where non-null. |
| `IsSystemOwned` | `bit` | no, default `0` | `true` blocks every user mutation path (rename, edit, publish, archive, delete) — FR-034. |
| `ModelCapability` | `int` | yes | The `AiCapability` a null-model version resolves through at run time. Non-null exactly when `IsSystemOwned`. |

`OwnerId` for system agents is the sentinel string `"system"`, matching the existing `SystemActor` convention (`"system:agent-runtime"`) used by the background runtime.

**State transitions**: unchanged for user agents. A system agent is created `Published` by the provisioner and never transitions; it is replaced by a new `AgentVersion`, not by a status change.

---

## 4. `AgentVersion` — widened (Domain/Agents)

| Column | Change | Meaning |
|---|---|---|
| `ModelProviderId` | `uniqueidentifier` → **nullable** | Null = resolve at run time from `Agent.ModelCapability` (research.md D9). |
| `ModelId` | `uniqueidentifier` → **nullable** | As above. Both are null together or both non-null. |
| `DefinitionHash` | **new** `nvarchar(64)`, nullable | SHA-256 of the `SystemAgentDefinitions` entry that produced this version. Null for user-published versions. Gates re-publishing (FR-035). |

**Invariant**: a version with null model columns is creatable only by `SystemAgentProvisioner`; `Agent.Publish` continues to require both for user agents, so no existing behaviour changes.

**Append-only preserved**: an upgrade publishes version N+1; version N and every execution referencing it stay intact (FR-035).

---

## 5. `UserConversationPreference` — new table (Domain/Chats)

Mirrors `UserPanelPreference` exactly (same shape, same repository/command/query pattern).

| Column | Type | Null | Meaning |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK, `Guid.CreateVersion7()`. |
| `UserId` | `nvarchar(450)` | no | **Unique index.** Owner. |
| `SuggestedActionsEnabled` | `bit` | no, default `1` | FR-032. |
| `CreatedAtUtc` / `CreatedBy` / `UpdatedAtUtc` / `UpdatedBy` | — | — | `BaseEntity` audit fields. |

**Absent row = defaults**, so no backfill and no migration data step.

---

## 6. `AiCapability` — extended enum (Domain/Ai)

One new member, `TurnOrchestration`, appended after `BoundaryVision` so no existing persisted integer value shifts. Unassigned by default; `AiCapabilityProviderResolver` falls back to the platform default with a logged warning, so it works with no administrator action (research.md D4).

---

## 7. Reused without change — the turn record

A turn that invokes at least one capability writes into the existing tables (research.md D8). No schema change.

| Table | Row per turn | Carries |
|---|---|---|
| `AgentExecutions` | 1 | `AgentId` = Lucy orchestrator, `AgentVersionId` = its live version, `UserChatId`, `RunByUserId`, `Objective` = the user's message, `PlanJson` = the decision document, `Status`, `TerminationReason`, `Usage`, `Cost` (FR-020, FR-038). |
| `AgentExecutionSteps` | 1 per beat | Beat type, the sub-agent that ran it, its outcome. |
| `AgentToolCalls` | 1 per capability invocation | `ToolName`, `ValidatedInputJson`, `ValidatedOutputJson`, duration — the input to `AgentDuplicateToolCallDetector` (FR-019). |
| `AgentExecutionErrors` | 1 per failure | Category + detail (FR-039–FR-041). |
| `AgentAuditLogs` | 1 per invocation and per discarded suggestion | FR-024's discard reason lands here. |

**Terminal states** map onto the existing `AgentExecutionStatus`: `Completed`, `Failed`, and `Cancelled` for an interrupted turn (FR-042). A degraded turn — decision step failed, plain reply delivered (FR-039) — is `Completed` with a `TerminationReason` naming the degradation, not `Failed`, because the user did receive an answer.

---

## 8. Transient types (no persistence)

### `TurnContext` (Application/Conversations/Capabilities)

Snapshot assembled once per turn and passed to every `IConversationCapability.IsAvailable` (FR-011).

| Field | Source |
|---|---|
| `UserId`, `UserChatId` | `ICurrentUserAccessor`, command |
| `ActiveLocation` | `UserChat.ActiveLocation` |
| `ActiveBoundary` | `UserChat.ActiveBoundary` |
| `AttachedKnowledgeBaseIds` | `IConversationKnowledgeBaseRepository` |
| `HasAttachedDocuments` | conversation attachments |
| `IsMemoryAvailable` | `IMemoryService` availability |
| `OpenPanelTypeKeys` | panel registry state supplied by the client |
| `GrantedPermissions` | user's `AgentToolPermission` set |
| `SubscriptionTier` | billing tier, for entitlement filtering |

Immutable record; captured before the decision call so availability cannot shift mid-turn (dispatch re-evaluates against a fresh context per FR-028).

### `TurnDecision` (Application/Conversations/Runtime)

Parsed decision document. Shape and validation live in [contracts/turn-stream.md](./contracts/turn-stream.md).

| Field | Meaning |
|---|---|
| `Intent` | `answer` \| `act` — `answer` takes the fast path (FR-006) |
| `Acknowledgement` | the sentence shown before work starts (FR-003); null when `Intent = answer` |
| `Slices` | 0–`MaxDelegationsPerTurn` sub-agent slices, each with `SubAgentKey`, `CapabilityKey`, `ArgumentsJson`, `PendingLabel`, optional `DependsOn` index |

---

## Entity relationships

```text
UserChat 1──* Message
                ├── SuggestedActionsJson   (assistant message: the offer)
                └── SelectedActionKey      (user message: the answer to the previous offer)

UserChat 1──* AgentExecution               (one per capability-invoking turn)
                 ├──* AgentExecutionStep   (one per beat)
                 ├──* AgentToolCall        (one per capability invocation)
                 └──* AgentExecutionError

Agent (SystemKey, IsSystemOwned, ModelCapability)
   └──* AgentVersion (nullable model binding, DefinitionHash)

User 1──1 UserConversationPreference
```

---

## Migration notes

Single migration, `AddConversationalAgentRuntime`:

1. `Messages`: add three nullable columns.
2. `Agents`: add `SystemKey`, `IsSystemOwned` (default `0`), `ModelCapability`; create a unique filtered index `IX_Agents_SystemKey` where `SystemKey IS NOT NULL`.
3. `AgentVersions`: alter `ModelProviderId`/`ModelId` to nullable; add `DefinitionHash`.
4. Create `UserConversationPreferences` with a unique index on `UserId`.

No data migration. Existing agents get `IsSystemOwned = 0` from the column default and keep their non-null model bindings, so the background runtime is unaffected.

Per the repository's CI conventions, the generated migration file must be checked for a BOM and for `System.*` usings ordered first before commit.
