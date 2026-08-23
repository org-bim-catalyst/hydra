# Data Model: Transcription 500 Fix & Mode-Switch Simplification

No persisted entities, database schema, or migrations are introduced by this feature (confirmed in
spec.md's Key Entities section — this is an error-classification and interaction fix, not a data
feature). The only structural addition is a new **in-memory exception type**, documented here
because it is the feature's central "shape."

## `AiProviderRequestInvalidException`

**Layer**: `AskLucy.Application.Abstractions` (`IAIProvider.cs`), alongside its three existing
siblings (`AiProviderUnavailableException`, `AiProviderAuthenticationException`,
`AiProviderRateLimitedException`).

| Field | Type | Notes |
|---|---|---|
| `Message` | `string` (inherited from `Exception`) | Human-readable detail, including the upstream provider's rejection body where available (e.g. `"OpenAI rejected the audio: {body}"`). Becomes the `detail` field of the Problem Details response. |
| `InnerException` | `Exception?` (inherited) | The original `HttpRequestException`, when available, following the existing three siblings' constructor shape exactly. |

**Lifecycle**:
1. Thrown by `OpenAIProvider.EnsureSuccessAsync` when a transcription (or any AI provider) HTTP
   call returns a 4xx status other than 401/403 (already `AiProviderAuthenticationException`) or
   429 (already `AiProviderRateLimitedException`).
2. Propagates unmodified through `WithRetryAsync` — it matches neither of that method's two catch
   clauses, so no retry is attempted, matching the existing behavior for
   `AiProviderAuthenticationException`.
3. Caught by `ProblemDetailsMiddleware.Map()`, mapped to **400 Bad Request** with a
   `https://hydra.bimcatalyst.com/problems/ai-provider-request-invalid` type and a `detail` drawn
   from `Message`.
4. Reaches the frontend as a standard Problem Details JSON body, parsed by `aiApi.ts`'s
   `transcribeAudio` into an `ApiError` (existing frontend type, unmodified) whose `.message`
   becomes the text shown to the user via `useVoiceRecorder`'s existing `error` state.

**Relationships**: Sibling of the three existing `AiProvider*Exception` types — same base class
(`Exception`), same throwing layer, same middleware-mapping mechanism. No relationship to any
persisted entity.

## Non-entities (explicitly out of scope)

- The recording `Blob`/`File` sent for transcription is transient, in-memory, request-scoped data
  — not a persisted entity. Decision 3 (research.md) changes only its filename derivation, not its
  shape.
- The composer's conversation mode (Push-to-Talk / Continuous) is existing client-side UI state
  (already modeled prior to this feature) — Decision 4 changes only how it's toggled, not its
  representation.
