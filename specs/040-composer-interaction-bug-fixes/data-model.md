# Data Model: Composer Interaction Bug Fixes

No persisted/database entities are introduced or changed by this feature — every fix is
presentational (control ordering/positioning), control-flow (a retried capture-start), or
error-classification (exception → Problem Details mapping). The two "entities" below are the
in-memory/contract shapes spec.md's Key Entities section refers to.

## Composer Control Row (existing `ChatComposer.tsx` states, layout corrected)

Unchanged input: `composerVisualState: 'recording' | 'typing' | 'continuous' | 'empty'` (derived
exactly as today — this feature does not change the derivation, only what renders for each value
and how it's positioned).

| State | Leading group (pinned left) | Middle | Trailing group (pinned right) |
|---|---|---|---|
| `empty` | Attachment | — | Mic (click/hold-to-talk), Continuous-conversation entry |
| `typing` | Attachment, Mic (click/hold-to-talk) | — | Send |
| `recording` (awaiting tap review) | Cancel | Live waveform | Finish |
| `recording` (actively holding, no review yet) | — | Live waveform | Mic (`mic-fill`, non-interactive indicator) — unchanged from today, already correct |
| `continuous` (idle-listening) | — | Live waveform | Mute, Exit |

Notes:
- The `recording` row has two sub-cases (holding vs. awaiting-tap-review) that already differ in
  today's code and are not changed in shape by this feature — only the awaiting-tap-review
  sub-case's ordering changes (US3).
- The `typing` row's Mic control is a live, functional element (US2), not a static icon — pressing
  it behaves identically to the `empty` state's Mic control, per the extended single-persistent-
  element requirement (research.md Decision 2).

## Transcription Failure Classification (existing `AiProvider*Exception` hierarchy, one gap closed)

| Underlying cause | Exception thrown | Problem Details status/type (existing) |
|---|---|---|
| Missing/blank `OpenAI:ApiKey` configuration | `AiProviderAuthenticationException` (now thrown proactively in `CreateClient()` — new) | 502 `ai-provider-authentication-failed` (existing mapping, now reachable from this cause) |
| OpenAI returns 401/403 | `AiProviderAuthenticationException` (existing) | 502 `ai-provider-authentication-failed` (existing) |
| OpenAI returns 429 | `AiProviderRateLimitedException` (existing) | 429 `ai-provider-rate-limited` (existing) |
| OpenAI returns another 4xx | `AiProviderRequestInvalidException` (existing) | 400 `ai-provider-request-invalid` (existing) |
| OpenAI returns 5xx / times out, survives retry | `AiProviderUnavailableException` (existing, via `WithRetryAsync`) | 502 `ai-provider-unavailable` (existing) |
| A raw `HttpRequestException` reaches the middleware unclassified (defense-in-depth case) | *(no new exception type)* | 502 `ai-provider-unavailable` (new mapping — Decision 6) |
| Anything else (truly unknown) | *(unchanged)* | 500 generic (unchanged — this feature does not claim to eliminate every possible unknown failure, only the two confirmed gaps above) |
