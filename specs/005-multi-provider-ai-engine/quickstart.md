# Quickstart: Validating the Multi-Provider AI Engine

**Feature**: [spec.md](./spec.md) | **Data model**: [data-model.md](./data-model.md) |
**Contracts**: [contracts/](./contracts/)

Manual/scripted validation scenarios proving the feature works end-to-end, mapped to the
spec's user stories and success criteria. Run after implementation, before marking the
feature done (constitution §19 Definition of Done).

## Prerequisites

- Solution built and running locally (`dotnet run` for `AskLucy.Web`), against a local SQL
  Server instance with this feature's migration applied (`dotnet ef database update`).
- A logged-in test user, and a second user with the `Administrator` role (existing auth
  flow — see `docs/TESTING.md`).
- Real (or sandbox/test-tier) API keys for at least two of the four providers — OpenAI and
  one other (e.g., Anthropic) — to exercise genuine cross-vendor behavior rather than a
  single-vendor happy path. A third, deliberately invalid key is useful for Scenario 6.

## Scenario 1 — Administrator enables providers (User Story 1)

1. As the admin user, `GET /api/v1/admin/ai/providers` (contracts/admin.md) — confirm all
   four seeded providers appear, `isEnabled: false`, `hasCredential: false`.
2. `PUT /api/v1/admin/ai/providers/{openAiId}/credential` with a valid key → `204`.
3. `PATCH /api/v1/admin/ai/providers/{openAiId}` with `{ "isEnabled": true }` → `200`,
   `isEnabled: true`.
4. Attempt to enable a second provider **without** first setting its credential → confirm
   `400` (data-model.md validation rule).
5. `GET /api/v1/admin/ai/providers/{openAiId}` again — confirm the credential value never
   appears anywhere in the response body.

**Pass condition**: matches spec.md User Story 1's four acceptance scenarios exactly.

## Scenario 2 — Multi-provider chat with attribution (User Story 2 / SC-001, SC-002)

1. As the test user, `GET /api/v1/ai/providers` → confirm only the enabled provider(s)
   from Scenario 1 appear.
2. Start a new conversation; send a message selecting OpenAI + one of its models
   (contracts/chat.md `POST /api/v1/ai/chat`). Confirm the reply streams in token-by-token
   and the resulting message is labeled with that exact provider/model.
3. `PATCH /api/v1/chats/{id}/model-selection` to switch to the second enabled provider;
   send another message. Confirm the new reply is attributed to the new provider/model,
   and the **first** message's attribution is unchanged.
4. Disable the first provider (as admin); as the user, confirm it no longer appears in the
   picker (`GET /api/v1/ai/providers`), while the earlier message from Scenario 2 step 2
   still displays its original provider/model label in history (FR-011).

**Pass condition**: every acceptance scenario in spec.md User Story 2 holds; SC-001's
labeled-response timing and SC-002's 100%-attribution claim both verifiable from this run.

## Scenario 3 — Defaults and fallback (User Story 3)

1. As the user, `GET /api/v1/ai/preferences` with no prior save → confirm it returns the
   platform default with `isPlatformDefault: true`.
2. `PUT /api/v1/ai/preferences` setting a specific default provider/model → start a new
   conversation → confirm it opens pre-selected with that default.
3. As admin, disable the user's saved default provider. Start another new conversation as
   the user → confirm it falls back to a different enabled provider/model with a visible
   notice, rather than failing (FR-018).

**Pass condition**: matches User Story 3's three acceptance scenarios.

## Scenario 4 — Generation parameters (User Story 4)

1. Open the parameter panel for a conversation using a model with `SupportsReasoning:
   false`; confirm the reasoning-level control is hidden/disabled (FR-015).
2. Set `temperature` to an out-of-range value (e.g., `5.0`); confirm `400` with a specific,
   actionable message (FR-016).
3. Set `temperature` very low, send a message, then very high, send the same prompt again;
   confirm the two replies visibly differ in determinism (qualitative check).

**Pass condition**: matches User Story 4's three acceptance scenarios.

## Scenario 5 — Usage and cost (User Story 5 / SC-005)

1. Send a message via a provider known to report token usage; `GET /api/v1/chats/{id}/
   messages` → confirm `inputTokenCount`/`outputTokenCount`/`estimatedCostUsd` are all
   populated on the response message.
2. `GET /api/v1/chats/{id}/usage` (contracts/usage.md) → confirm `byProviderModel` totals
   match the sum of that conversation's messages.
3. If any model in the catalog has no pricing metadata configured, send a message with it
   and confirm `estimatedCostUsd` is `null` (not `0`) and `costIncomplete: true` on the
   usage summary (FR-022).

**Pass condition**: matches User Story 5's three acceptance scenarios; SC-005's 95%-
availability claim spot-checked across several providers/models.

## Scenario 6 — Provider failure handling (User Story 6 / SC-004, SC-006, Edge Cases)

1. Configure a provider with a deliberately invalid credential (or temporarily block
   network egress to one vendor). Send a chat message through it.
2. Confirm the user sees a clear, specific error (not a generic failure or an
   indefinitely-spinning composer) and can retry or switch provider (FR-029).
3. Within one health-check interval, `GET /api/v1/admin/ai/providers/{id}/health` (as
   admin) → confirm a new `ProviderHealthCheck` row shows `isHealthy: false` (SC-006), and
   `GET /api/v1/admin/ai/providers` shows the provider's denormalized `healthStatus` flipped
   to `Unhealthy`.
4. Restore the credential/network; confirm the next health check flips status back to
   `Healthy` without manual intervention (User Story 6, Acceptance Scenario 2).
5. If feasible to simulate, drop the connection mid-stream during a normal (working)
   chat call; confirm the partial content already rendered stays on screen, clearly marked
   incomplete, rather than vanishing (FR-030, Edge Cases).

**Pass condition**: matches User Story 6's three acceptance scenarios and the two related
Edge Cases entries.

## Scenario 7 — Model comparison (User Story 7)

1. `POST /api/v1/ai/compare` with a prompt and 2 valid `{providerId, modelId}` selections
   from different vendors (contracts/chat.md). Confirm both results return, each labeled
   with its own provider/model.
2. Repeat with one selection deliberately pointing at a provider you disable *after*
   sending the request but conceptually representing a mid-flight failure — or simpler,
   substitute an invalid `modelId` for one selection — confirm that slot returns an
   `error` object while the other slot still returns its successful content (FR-026).
3. `POST /api/v1/ai/compare/{comparisonId}/actions/continue` choosing one successful
   result; `GET /api/v1/chats/{chatId}/messages` → confirm **all** successful candidates
   now appear as messages in history (both labeled with their own provider/model), but a
   follow-up message in the same conversation only receives the chosen one as prior
   context (verify indirectly: the follow-up reply doesn't reference content unique to the
   non-chosen candidate).

**Pass condition**: matches User Story 7's three acceptance scenarios; confirms
data-model.md's `ComparisonGroupId`/`IsIncludedInContext` behavior end-to-end.

## Scenario 8 — New provider added without touching existing code (SC-003)

1. Without modifying any file under `Application`, `Domain`, or any existing
   `Infrastructure.Ai` provider class, add a fifth `IAIProvider` implementation (even a
   throwaway test double pointed at a mock HTTP endpoint) plus its keyed DI registration
   and one `AIProvider`/`AIModel` seed row.
2. As admin, enable it via the existing `PUT .../credential` + `PATCH .../isEnabled`
   endpoints — no new endpoint, no schema change.
3. As the user, confirm it appears in `GET /api/v1/ai/providers` and a chat through it
   works identically to the original four.

**Pass condition**: directly verifies SC-003 — zero changes to existing conversations,
providers, or features already in production.
