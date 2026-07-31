# Data Model: Admin AI Provider Configuration UI

No new database entities, migrations, or backend DTOs are introduced by this feature — it
is a UI-only consumer of the `AIProvider` aggregate and `AdminAiProviderDto` already
delivered under `005-multi-provider-ai-engine`. This document maps the spec's Key
Entities onto what already exists and defines the one new artifact this feature adds: a
frontend TypeScript view-model mirroring the existing backend DTO.

## Existing backend shape this feature reads (no changes)

`AdminAiProviderDto` (`src/AskLucy.Application/Ai/AdminAiProviderDto.cs`), returned by
`GET /api/v1/admin/ai/providers`:

| Field | Type | Notes |
|---|---|---|
| `id` | guid | |
| `providerKey` | string | Stable machine key (`"openai"`, `"anthropic"`, ...) — display purposes only in this UI. |
| `displayName` | string | e.g. "OpenAI" — what the table shows. |
| `isEnabled` | bool | Drives the enable/disable action's current state and label. |
| `hasCredential` | bool | Whether a credential is configured — never the value itself (spec FR-002/SC-002). Drives whether "Enable" is offered or explained-as-blocked (FR-003). |
| `credentialLastRotatedAtUtc` | datetime? | Shown so an admin can tell how recently a credential was set/rotated. |
| `defaultModelId` | guid? | Not surfaced in this feature's UI — model-catalog management is out of scope (spec Assumptions). |
| `healthStatus` | `"Unknown" \| "Healthy" \| "Unhealthy"` | User Story 2. |
| `healthStatusCheckedAtUtc` | datetime? | User Story 2 — "how current is this." |

## New frontend view-model

`AdminAiProvider` (in the new `adminAiProvidersApi.ts`) — a direct 1:1 TypeScript mirror
of the DTO above (camelCase, matching the API's default JSON casing); no additional
client-side fields or derived state are stored beyond what each dialog needs transiently
(the in-progress credential string, the currently-open confirmation's target action).

## State transitions (already enforced server-side; this UI only triggers them)

These transitions live in the `AIProvider` domain entity (`Enable`/`Disable`/
`SetCredential`/`ClearCredential`) and are unchanged by this feature — restated here only
so the UI's confirmation copy (Decision 2, research.md) can describe them accurately:

- `Disabled, no credential` → *(set credential)* → `Disabled, has credential` → *(enable)* → `Enabled`
- `Enabled` → *(disable)* → `Disabled, has credential` (credential is retained)
- `Enabled` or `Disabled, has credential` → *(clear credential)* → `Disabled, no credential` (always forces disabled — FR-005)
- `Disabled, has credential` → *(set a new credential)* → `Disabled, has credential` (replaces in place, per FR-006; if it was `Enabled`, it stays `Enabled` under the new credential — replacing does not disable)
- `Disabled, no credential` → *(enable, attempted)* → rejected, no transition (FR-003) — this UI must present the credential dialog instead of a bare enable action for a provider with `hasCredential: false`.
