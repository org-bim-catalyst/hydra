# Contract: Location Intent Classification (Structured LLM Call)

**Spec**: 037-location-query-resolution (research.md Decisions 1/3) | **Date**: 2026-08-23

## Purpose

Before any geocoding happens, `LocationResolutionService` must decide, per spec.md FR-001/
FR-009/FR-014: does this message express navigational intent toward a real-world place
(User Story 1, as distinct from User Story 2's passing mention), and if so, is it a fresh
named query or a back-reference to the session's already-active location? This one
structured, non-streaming LLM call answers both questions and extracts the place name(s)
in a single round trip, following the exact `ChatAsync` + `JsonSerializer.Deserialize`
pattern `MemoryExtractionJob`/`MemoryConflictDetectionService` already use for background
classification/extraction.

## Call Shape

```csharp
var resolved = await defaultProviderResolver.ResolveAsync(preference: null, cancellationToken);
var provider = await aiProviderRepository.GetByIdAsync(resolved.ProviderId, cancellationToken)!;
var model = await aiModelRepository.GetByIdAsync(resolved.ModelId, cancellationToken)!;
var aiProvider = aiProviderResolver.Resolve(provider.ProviderKey);

var messages = new List<ChatMessage>
{
    new(ChatRole.System, LocationIntentClassificationPromptV1),
    new(ChatRole.User, $"User message:\n{latestUserMessage}"),
};

var completion = await aiProvider.ChatAsync(messages, model.ModelKey, parameters: null, cancellationToken);
```

Uses `DefaultProviderResolver` (research.md Decision 3) — never the turn's own
user-selected provider/model.

## System Prompt (`LocationIntentClassificationPromptV1`)

Versioned per constitution §9 ("prompt engineering... versioned artifacts... reviewed like
code"). Content requirements (exact wording is an implementation detail for
`/speckit-tasks`, not fixed here):

- Distinguish navigational intent ("show me X", "where is X", "take me to X", "center on
  X", "let's look at X") from incidental mention (fact, comparison, past-tense
  recollection, "how does X compare to Y") — spec.md User Story 2.
- Recognize a simple back-reference to a place already established in this conversation
  ("zoom in on it", "center on that place", "go there") without requiring the model to
  know *what* "it" refers to — that resolution happens after classification, in
  `LocationResolutionService`, against the passed-in `activeLocation` (data-model.md).
- When a message names more than one distinct place with no single clear navigational
  target, return all of them in `placeQueries` rather than silently picking one — the
  caller treats 2+ entries as `Ambiguous` per FR-009, never auto-selecting.
- Instruct the model to respond with **only** a single JSON object, no markdown/commentary
  — the same defensive instruction `ExtractionSystemPromptV1` already uses for Memory's
  extraction call.

## Response Contract

```json
{
  "intent": "none" | "new_query" | "back_reference",
  "placeQueries": ["Al Safa 2 Park"]
}
```

| Field | Type | Notes |
|---|---|---|
| `intent` | `string` | `"none"` → `LocationResolutionOutcomeType.NoIntent`, no further processing. `"new_query"` → geocode `placeQueries`. `"back_reference"` → resolve against `activeLocation`, ignore `placeQueries`. |
| `placeQueries` | `string[]` | Empty when `intent` is `"none"` or `"back_reference"`. One entry for an ordinary single-place query. 2+ entries when the message named multiple distinct places (FR-009) — the caller does not geocode any of them individually; it goes straight to `Ambiguous`. |

Deserialized into a private `LocationIntentPayload` record
(`[property: JsonPropertyName("intent")] string Intent`, `[property:
JsonPropertyName("placeQueries")] IReadOnlyList<string> PlaceQueries`), mirroring
`MemoryExtractionJob.ExtractedCandidatePayload`'s shape convention.

## Error Handling

- `JsonException` (malformed/non-JSON response) → treated as `NoIntent` is **not**
  correct here (that would silently drop a possibly-real location request) — instead
  mapped to `LocationResolutionOutcomeType.Unavailable` with a generic "couldn't check
  that location right now" `ConfirmationText`, logged at Warning (FR-012). This differs
  deliberately from `MemoryExtractionJob`'s "malformed → nothing found" convention:
  Memory's extraction is a best-effort background sweep with no user-visible failure mode,
  while this call sits in FR-005's no-silent-failure path — a parse failure must still
  produce a user-visible outcome.
- Any other exception from the classification call (provider outage, auth failure, rate
  limit) → also mapped to `Unavailable`, never rethrown into the stream (constitution
  §2.VIII) — unlike `MemoryExtractionJob`'s background-job context, there is no Hangfire
  retry to defer to here; the turn must still complete.
- An unrecognized `intent` value (neither `"none"`, `"new_query"`, nor
  `"back_reference"`) is treated the same as a parse failure — `Unavailable`, not silently
  ignored.

## Out of Scope

- Multi-turn conversational context beyond the single already-active location
  (spec.md Assumptions: back-references resolve only against the session's current
  active location, never an earlier, since-replaced one, and never full coreference
  resolution across the transcript).
- Language/locale-specific phrasing tuning — the prompt is written for general natural
  language; per-locale evaluation is not gated by this contract.
