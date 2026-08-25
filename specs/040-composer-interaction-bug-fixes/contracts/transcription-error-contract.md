# Contract: Transcription Error Classification (US6)

## `OpenAIProvider.CreateClient()` (behavior change, no signature change)

```csharp
private HttpClient CreateClient()
{
    if (string.IsNullOrWhiteSpace(_options.ApiKey))
    {
        throw new AiProviderAuthenticationException(
            "The OpenAI provider is not configured with an API key.");
    }

    var client = httpClientFactory.CreateClient("OpenAI");
    client.BaseAddress = new Uri(_options.BaseUrl);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    return client;
}
```

- Every call site (`ChatAsync`, `StreamChatAsync`, `GenerateImageAsync`, `TranscribeAudioAsync`,
  `CheckHealthAsync`, `ListAvailableModelsAsync`) already goes through `CreateClient()`, so this
  fix applies uniformly, not just to transcription — but transcription is the confirmed
  reported symptom.
- `AiProviderAuthenticationException` is already caught specially by `WithRetryAsync` (never
  retried — research.md notes retrying a bad credential wastes the retry budget) and already
  mapped by `ProblemDetailsMiddleware` to 502 `ai-provider-authentication-failed`. No changes
  needed in either of those two places for this part of the fix.

## `ProblemDetailsMiddleware` (new mapped case)

```csharp
HttpRequestException => (
    StatusCodes.Status502BadGateway,
    "https://hydra.bimcatalyst.com/problems/ai-provider-unavailable",
    "AI provider unavailable",
    "The AI provider could not process your request. Please try again."),
```

- Added alongside the existing `AiProviderUnavailableException` case (same status/type/title/detail
  shape — a raw `HttpRequestException` that escapes provider-level classification is
  indistinguishable from "provider unavailable" to the end user).
- Placed as a specific `case` before the generic `_ => 500` fallback, so it only changes behavior
  for exceptions that were previously falling through unclassified — every other existing mapped
  exception type is unaffected (switch expression pattern matching is unambiguous by type).

## Response contract (unchanged shape, newly-reachable content)

No change to the Problem Details response *shape* consumed by `aiApi.ts`'s `transcribeAudio()` —
it already reads `problem.detail ?? problem.title`. This fix only changes which `detail`/`title`
values become reachable for the two closed gaps; the frontend needs no changes for US6.
