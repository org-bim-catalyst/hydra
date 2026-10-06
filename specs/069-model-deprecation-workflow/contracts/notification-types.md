# Contract: Notification Types

Four new entries in `NotificationTypeCatalog` (`AskLucy.Domain/Notifications`), category **System**, published through `INotificationPublisher` (spec 067, [module-integration](../../067-notifications-communication-hub/contracts/module-integration.md)). This feature never delivers on a channel itself (FR-020).

| Key | Recipient | In-app | Email | Priority |
|---|---|---|---|---|
| `system.ai-model.deprecated` | each affected owner (`User`) | Mandatory | On | Normal |
| `system.ai-model.replacement-changed` | each owner whose items moved again (`User`) | Mandatory | On | Normal |
| `system.ai-model.deprecation-summary` | AI-provider admins other than Super User-only holders (`Users`, chunked to 100) | Mandatory | **Mandatory** | High |
| `system.ai-model.deprecation-summary.super-user` | users whose access comes only from the Super User role (`Users`, chunked to 100) | Mandatory | On | High |

The two summary types render the same content from one payload builder (research D7). The second exists only so a Super User can turn the email off (FR-020b).

## Declared variables

Plain text only, never HTML or URLs (067 rule 2). Links come from each type's route template.

| Type | Variables |
|---|---|
| `deprecated` | `modelNames`, `itemSummary` (for example "2 conversations, 1 agent"), `replacementSummary` (per model: "GPT-4 Turbo → GPT-5", or "no replacement available") |
| `replacement-changed` | `modelNames`, `itemSummary`, `replacementSummary` |
| `deprecation-summary` (both) | `providerName`, `modelNames`, `reason` ("No longer listed by the vendor"), `confirmedBy`, `confirmedAt`, `reassignedSummary`, `flaggedSummary`, `validationRequest`, `notifiedUserCount` |

`notifiedUserCount` is not known when the summary is published, because the user job runs afterwards. The summary therefore states "affected users are being notified" and the final count appears on the deprecation record (FR-002).

## Routes (in-app action)

| Type | Route template |
|---|---|
| `deprecated` | `/settings/chat` for a user with a single affected conversation, otherwise the notification detail view; the notice lists each item by name |
| `replacement-changed` | the same as `deprecated` |
| both summary types | `/admin/ai-providers?deprecation={deprecationId}`, opening the deprecation record with its accept and change actions |

`RelatedItem` is `("ai-model-deprecation", deprecationId)` for the summaries and `("ai-model-deprecation-batch", batchId)` for user notices.

## Event keys (de-duplication, FR-008)

- User notices: `ai-model-deprecation:{batchId}:{userId}`, so a restarted job never double-notifies.
- Summaries: `ai-model-deprecation-summary:{batchId}:{userId}`.
- Replacement change: `ai-model-replacement-changed:{batchId}:{userId}`.

## Templates

Seeded in English under `AskLucy.Infrastructure/Notifications/Templates/Seed/en/{key}.{inapp|email}.json` for all four keys. Arabic versions follow 067's localization work and are not part of this feature.

## Delivery note

Until 067's email channel exists, the email half of every type is recorded as Skipped and is not retried later (research D8). In-app delivery works today.
