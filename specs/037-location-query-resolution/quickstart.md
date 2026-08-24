# Quickstart: Validating Location Query Resolution

**Branch**: `037-location-query-resolution` | **Date**: 2026-08-23

This is a manual/scripted end-to-end validation guide, not a substitute for the unit/
integration tests listed in plan.md's Project Structure. It exercises the feature exactly
as the frontend already consumes it (specs 035/036) — the `POST /api/v1/ai/chat` SSE
stream — without needing the React app running, since no frontend code changes with this
feature.

## Prerequisites

- Backend running locally (`dotnet run` in `src/AskLucy.Web`), reachable at
  `https://localhost:<port>`.
- A valid auth token for an existing user (the endpoint is `[Authorize]`) and an existing
  `chatId` (create one via the normal chat-creation flow, or reuse one from the UI).
- A configured, enabled AI provider/model (any of OpenAI/Anthropic/Gemini/OpenRouter) —
  used both for the main reply and, via `DefaultProviderResolver`, for the classification
  call (research.md Decision 3).
- Outbound network access to `nominatim.openstreetmap.org` (no API key required).

## Scenario 1 — Confident Single Match (User Story 1)

```
curl -N -X POST https://localhost:<port>/api/v1/ai/chat \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
        "chatId": "<existing-chat-id>",
        "messages": [{"role":"user","content":"Show me Al Safa 2 Park"}],
        "providerId": "<provider-id>",
        "modelId": "<model-id>"
      }'
```

**Expected**:
- Ordinary `data: ...` content-delta lines stream first, unaffected in timing by location
  resolution (FR-008) — the first delta should arrive at the same latency as any other
  chat message.
- After the model's own text, one or more additional `data: ...` lines carry a
  deterministic confirmation sentence naming the resolved place (research.md Decision 1).
- A trailing `data: __LOCATION__{"latitude":...,"longitude":...,"locationName":"Al Safa 2
  Park...","confidence":...,"source":"nominatim"}` line appears before `data: [DONE]`,
  matching `specs/036-startup-geolocation/contracts/location-sse-event.md`'s existing wire
  format exactly (unchanged by this feature).
- Re-fetch the chat: the `UserChat` row's `ActiveLocationLatitude`/`ActiveLocationLongitude`/
  `ActiveLocationName`/`ActiveLocationConfidence` columns are now populated
  (`RecordActiveLocationCommand` ran).

## Scenario 2 — Passing Mention Does Not Resolve (User Story 2)

Send: `"I read that Al Safa Park was renovated last year."`

**Expected**: No `__LOCATION__` trailing event; no deterministic confirmation sentence
appended; only the model's own ordinary reply streams. `UserChat.ActiveLocation` is
unchanged from before this call (verifies `LocationResolutionOutcomeType.NoIntent`).

## Scenario 3 — Ambiguous Place (User Story 3)

Send: `"Show me Springfield"` (or another name known to match multiple unrelated real
places).

**Expected**: No `__LOCATION__` event. A deterministic ambiguity sentence appears in the
stream telling the user the request was ambiguous (data-model.md `Ambiguous` row).
`UserChat.ActiveLocation` unchanged.

## Scenario 4 — Not Found

Send: `"Show me Xyzzyplorp Nonexistent Place"` (or any string guaranteed not to geocode).

**Expected**: No `__LOCATION__` event. A deterministic not-found sentence appears.
`UserChat.ActiveLocation` unchanged.

## Scenario 5 — Back-Reference (FR-014, Clarification Session 2026-08-23 Q2)

1. Run Scenario 1 first (establishes an active location).
2. Send a second message on the same `chatId`: `"Zoom in on it"`.

**Expected**: A `__LOCATION__` trailing event reappears, with the **same**
latitude/longitude/locationName/confidence as Scenario 1 — and no live geocoding call is
made for this turn (verify via logs: no `GeocodingCandidate`/Nominatim log entry for this
message, only the classification call). This is the fast path SC-007 measures.

## Scenario 6 — Geocoding Unavailable

Temporarily point `GeocodingOptions.SearchBaseUrl` at an unreachable host (or block
outbound access to `nominatim.openstreetmap.org`), then repeat Scenario 1.

**Expected**: No `__LOCATION__` event. A deterministic "couldn't look that up right now"
sentence appears within FR-013's 15 s ceiling — the response must still complete (`data:
[DONE]` is written), never hang. Check logs for a `GeocodingProviderUnavailableException`
warning (contracts/geocoding-provider-contract.md).

## Verifying FR-008 (No Delay to First Byte)

Compare time-to-first-byte between a location-bearing message (Scenario 1) and an
unrelated message ("What's 2+2?") sent immediately after in the same session — they should
be statistically indistinguishable, since classification+geocoding run concurrently with
generation rather than before it (research.md Decision 1).

## Cleanup

No teardown required — all state is ordinary chat history (`Messages`) plus the four new
nullable `UserChat` columns; deleting the test chat via the existing chat-delete flow
removes everything this feature wrote.
