# Contract: Transcription Error Response & Mode-Switch Interaction

## 1. `POST /api/v1/ai/transcriptions` — new error response shape

**Existing, unchanged responses** (for regression clarity — these already work correctly and this
feature must not alter them):

| Condition | Status | `type` | Notes |
|---|---|---|---|
| OpenAI returns 401/403 | 502 | `.../ai-provider-authentication-failed` | Unchanged by this feature. |
| OpenAI returns 429 | 429 | `.../ai-provider-rate-limited` | Unchanged by this feature. `Retry-After` behavior unchanged. |
| OpenAI returns 5xx (after one retry) or connection fails | 502 | `.../ai-provider-unavailable` | Unchanged by this feature. |

**New response** (this feature):

| Condition | Status | `type` | `title` | `detail` |
|---|---|---|---|---|
| OpenAI returns any other 4xx (e.g. 400) | **400** | `https://hydra.bimcatalyst.com/problems/ai-provider-request-invalid` | `"AI provider rejected the request"` | Fixed, safe string: `"The AI provider could not process this request. Please try again."` — matches the sibling `AiProviderAuthenticationException`/`AiProviderRateLimitedException` cases, which also never surface their own exception `Message` to the client. The raw upstream body is logged server-side only (`OpenAIProviderLog.RequestRejectedByProvider`, Warning level), since `ProblemDetailsMiddleware` only logs unhandled exceptions at ≥500. |

**Previous (buggy) response being replaced**: 500, generic `.../internal-server-error` title
`"An unexpected error occurred"`, no actionable detail — this is the exact shape that must no
longer occur for a classifiable 4xx.

Response body shape (RFC 7807, unchanged structure — only a new `type`/`status`/`detail` value
combination):

```json
{
  "type": "https://hydra.bimcatalyst.com/problems/ai-provider-request-invalid",
  "title": "AI provider rejected the request",
  "status": 400,
  "detail": "OpenAI rejected the audio: <upstream detail>"
}
```

## 2. Frontend consumption contract — `transcribeAudio` (`aiApi.ts`)

**Before**: On `!response.ok`, throws a bare `Error("Transcription failed with ${status}")` —
discards the response body entirely.

**After**: On `!response.ok`, parses the JSON body (tolerating a non-JSON or empty body, matching
`apiFetch`'s existing `.catch(() => undefined)` pattern) and throws
`new ApiError(response.status, problem?.detail ?? problem?.title ?? 'Transcription failed',
problem?.detail)` — note this **deliberately does not** mirror `apiFetch`'s own convention of
`problem?.title ?? '...'` as the message; it prefers `detail` since that's what reaches the user
(see Consumer contract below).

**Consumer contract** (`useVoiceRecorder.ts`, unchanged code — verified compatible): its existing
`catch (err) { setError(err instanceof Error ? err.message : String(err)); }` receives an
`ApiError` (which `extends Error`), so `err.message` is `ApiError`'s constructed message —
containing the real `detail` — automatically, no `useVoiceRecorder.ts` change required for this
part.

## 3. Recording upload filename contract — `useVoiceRecorder.ts`

**Before**: `new File([blob], 'recording.webm', { type: blob.type || 'audio/webm' })` — filename
extension is always `.webm` regardless of `blob.type`.

**After**: filename extension is derived from `blob.type`. Any `;codecs=...` parameter is stripped
first (split on `;`, use only the base MIME type) before matching — real `MediaRecorder.mimeType`/
`blob.type` values commonly look like `audio/webm;codecs=opus`, and matching the base type only
was flagged by `/speckit-analyze` (finding U1) as necessary to avoid silently falling through to
the fallback for exactly the real-world case this fix targets:

| `blob.type` base (post `;` strip) | Filename extension |
|---|---|
| `audio/webm` | `.webm` |
| `audio/mp4` | `.mp4` |
| `audio/ogg` | `.ogg` |
| `audio/wav` / `audio/wave` / `audio/x-wav` | `.wav` |
| `audio/mpeg` | `.mp3` |
| anything else / empty | `.webm` (existing fallback preserved) |

The `type` field passed to `new File(...)` is unchanged (`blob.type || 'audio/webm'`) — only the
filename's extension is newly derived to match it.

## 4. Mode-switch UI contract — `ChatComposer.tsx`

**Before**: `IconButton onClick={(e) => setModeMenuAnchor(e.currentTarget)}` → opens `<Menu>` →
one `<MenuItem onClick={handleToggleModeClick}>` → calls `onToggleMode()`.

**After**: `IconButton onClick={onToggleMode}` (or an equivalent single-step handler that still
respects the existing `disabled={isModeSwitchBlocked}` guard) — no `Menu`/`MenuItem`, no
intermediate anchor state.

**Unchanged**:
- `disabled={isModeSwitchBlocked}` — the button remains disabled while a Push-to-Talk recording is
  in progress, exactly as today (spec.md FR-007).
- The `Tooltip` wrapping the button and its accessible label — must continue to describe "switch to
  the other mode" after the click (spec.md FR-008), not merely name the current mode.
- The icon shown reflects current mode, exactly as today (spec.md Acceptance Scenario US2-4).

## 5. Push-to-Talk hold gesture — regression contract (no interface change)

No public interface changes. Existing contract reaffirmed for regression testing:

- `pointerdown` on the mic button (idle, Push-to-Talk mode) → `recording.start()` → phase becomes
  `'recording'`.
- `pointerup` (or, per the established async-swap timing note in this session's prior work,
  `recording.onFinish` firing as the dominant real-world completion trigger) → `recording.finish()`
  → phase becomes `'transcribing'` → on success, transcript text is appended into the composer's
  text field, phase returns to `'idle'`.
- No intermediate "Accept"/"send to transcribe" step (already removed in specs/031 — reaffirmed
  here, not reintroduced).
