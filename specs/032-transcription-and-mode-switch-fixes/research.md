# Research: Transcription 500 Fix & Mode-Switch Simplification

## Decision 1 — New `AiProviderRequestInvalidException`, classified in `OpenAIProvider`

**Finding** (verified by reading the full chain): `OpenAIProvider.EnsureSuccessAsync`
(`src/AskLucy.Infrastructure/Ai/OpenAIProvider.cs:334-358`) only special-cases 401/403 →
`AiProviderAuthenticationException` and 429 → `AiProviderRateLimitedException`; every other
non-2xx response (including a 400, the most plausible response for a rejected transcription
upload) becomes a bare `HttpRequestException`. `IsTransient` (`:393-400`) classifies that as
non-transient (only `>= 500` or a null status code counts), so `WithRetryAsync` (`:360-391`)
doesn't catch it — it propagates straight to `ProblemDetailsMiddleware.Map()`
(`src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs:113-258`), which has no case for
`HttpRequestException` and falls into the generic `_ => 500` default (`:253-258`) — exactly the
observed "Transcription failed with 500."

**Decision**: Add `AiProviderRequestInvalidException(string message, Exception? innerException =
null)` to `src/AskLucy.Application/Abstractions/IAIProvider.cs`, immediately after the three
existing `AiProvider*Exception` types, following their exact shape. In
`OpenAIProvider.EnsureSuccessAsync`, after the existing 401/403 and 429 checks, add: any other
4xx status (`>= 400` and `< 500`) throws `AiProviderRequestInvalidException` carrying the
response body (already captured at `:341` as `body`) in its **exception `Message`** (internal —
never sent to the client as-is; see the correction below) — e.g. `$"OpenAI rejected the request
with {status}: {body}"`. Anything else (a genuine 5xx or unparseable status) still falls through
to the existing bare `HttpRequestException` → `IsTransient` → retry →
`AiProviderUnavailableException` path, unchanged. In `ProblemDetailsMiddleware.Map()`, add a case
for `AiProviderRequestInvalidException` mapping to **400 Bad Request** (not 502 — this reflects a
genuinely invalid client request, not an upstream service failure), positioned next to the two
existing `AiProvider*Exception` cases.

**Correction made during implementation**: `ProblemDetailsMiddleware.cs`'s own doc comment states
it "never exposes a stack trace or raw exception message to the client," and its two sibling
`AiProvider*Exception` cases (`AiProviderAuthenticationException`, `AiProviderRateLimitedException`)
both use a **fixed, safe, friendly detail string** in `Map()` — neither uses the exception's own
`Message`. The original plan (surfacing the raw OpenAI response body as `detail`) would have
broken that established convention and potentially leaked upstream diagnostic content to the
client. Corrected to: `Map()` uses a fixed string ("The AI provider could not process this
request. Please try again.") for the client-facing `detail`, matching the sibling cases exactly.
Since `ProblemDetailsMiddleware`'s own logging only fires for `statusCode >= 500` (this is 400),
the raw body would otherwise be lost entirely for diagnostics — so `OpenAIProvider.EnsureSuccessAsync`
now logs it server-side at Warning via a new `OpenAIProviderLog.RequestRejectedByProvider`
structured log entry before throwing, satisfying constitution §14 (Observability) without
violating the middleware's no-raw-message-to-client rule. This required threading `ILogger`
into the previously-static `EnsureSuccessAsync` (and updating its five call sites).

**Rationale**: Reuses the exact established `AiProvider*Exception` → `ProblemDetailsMiddleware`
pattern (three precedents already exist) rather than inventing a new error-handling mechanism
(constitution §3/§7). `WithRetryAsync` requires no change: a thrown `AiProviderRequestInvalidException`
matches neither of its two catch clauses (`AiProviderAuthenticationException` or `IsTransient`),
so it already propagates uncaught and unretried — the correct behavior for a request-level
rejection (spec.md FR-005: don't touch the already-correct unavailable/rate-limited paths).

**Alternatives considered**: Mapping to 502 (treating any provider-originated failure as an
upstream problem, matching `AiProviderUnavailableException`/`AiProviderAuthenticationException`'s
precedent) was considered but rejected — a 400 here reflects that *this specific recording* was
rejected, which is a client-actionable "try recording again" situation, not a "the AI provider is
down" situation; conflating the two would produce a misleading message (spec.md FR-002 requires
the message to describe that *this recording* couldn't be transcribed).

## Decision 2 — Frontend must actually surface the Problem Details `detail`

**Finding**: `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts`'s `transcribeAudio`
(`:194-211`) does `if (!response.ok) throw new Error(\`Transcription failed with ${response.status}\`)`
— it never reads the response body at all, so even after Decision 1's backend fix ships, the
user would still only see "Transcription failed with 400" (an equally opaque status-code-only
message) unless the frontend is also fixed. The rest of the codebase already has an established
convention for this: `src/AskLucy.Web/ClientApp/src/api/httpClient.ts`'s `ApiError` class
(`:5-17`) carries `status`/`message`/`detail`/`errors` and `apiFetch`'s error path (`:42-45`)
already does `const problem = await response.json().catch(() => undefined); throw new
ApiError(response.status, problem?.title ?? 'Request failed', problem?.detail, problem?.errors)`.
`aiApi.ts` already imports `apiFetch` from this same module for its other functions but
`transcribeAudio` uses a raw `fetch` (needed for `FormData`, which `apiFetch` doesn't support) and
never adopted the `ApiError` pattern.

**Decision**: Import `ApiError` into `aiApi.ts` and change `transcribeAudio`'s error path to parse
the Problem Details body. **Correction made during implementation**: `apiFetch`'s own convention
(`new ApiError(status, problem?.title ?? '...', problem?.detail, ...)`) sets `ApiError.message` to
the Problem Details **`title`** (a generic string per-exception-type, e.g. "AI provider rejected
the request"), with `detail` kept as a separate property — but `useVoiceRecorder.ts:132` reads
`err.message` directly for what the user sees, and `detail` (the specific, actionable sentence) is
what FR-002 actually needs the user to see. So `transcribeAudio` deliberately does **not** mirror
`apiFetch` byte-for-byte here: it constructs `new ApiError(response.status, problem?.detail ??
problem?.title ?? 'Transcription failed', problem?.detail)` — preferring `detail` as the
`.message` so `useVoiceRecorder.ts` needs no change at all, while still populating `.detail`
separately for consistency with the `ApiError` shape.

**Rationale**: Directly satisfies spec.md FR-002 ("specific, visible, user-facing error") using
the codebase's own existing convention rather than a new one-off error shape, and closes the gap
the investigation explicitly flagged: fixing only the backend classification without this frontend
fix would still leave the user looking at an opaque number.

## Decision 3 — Fix the hardcoded recording filename

**Finding**: `useVoiceRecorder.ts:127` does `new File([blob], 'recording.webm', { type: blob.type
|| 'audio/webm' })` — the filename is always `'recording.webm'` regardless of what container/codec
`MediaRecorder` actually produced (`recorder.mimeType`, captured correctly into `blob.type` one
line earlier at `:115`, but then ignored when naming the file). OpenAI's transcription endpoint
uses the filename extension to select a decoder; a mismatched extension (e.g. a browser producing
`audio/mp4` but the file still named `.webm`) is a concrete, code-identified way to trigger the
400 this feature classifies in Decision 1.

**Decision**: Derive the filename's extension from `blob.type` via a small mapping (webm→webm,
mp4→mp4, ogg→ogg, wav→wav, mpeg→mp3), falling back to `webm` only if the type is unrecognized —
e.g. `recording.${extensionFor(blob.type)}`.

**Rationale**: This doesn't just make failures better-explained (Decision 1/2 already guarantee
that regardless of cause) — it reduces how often this specific, identified trigger produces a
failure at all, directly improving spec.md SC-001 ("100% of recordings containing genuine speech
transcribe successfully"), not just SC-002 (better error messages when something is still
rejected).

## Decision 4 — Mode-switch: remove the dropdown, toggle directly

**Finding**: `ChatComposer.tsx`'s mode-switch block renders a `Tooltip`+`IconButton` that opens an
MUI `Menu` (`anchorEl={modeMenuAnchor}`) containing one `MenuItem` calling `onToggleMode` —
requiring two clicks (open menu, then click the option) for what is a plain binary toggle between
exactly two modes.

**Decision**: Remove the `Menu`/`MenuItem` JSX and the `modeMenuAnchor` state entirely. Change the
`IconButton`'s `onClick` from `(e) => setModeMenuAnchor(e.currentTarget)` to call `onToggleMode`
directly (the same handler the removed `MenuItem` used to call). Keep the existing `Tooltip`
(already required by specs/030) and the existing `disabled={isModeSwitchBlocked}` guard
unchanged — this is a pure interaction simplification, not a behavior or accessibility regression.

**Rationale**: Directly implements the user's explicit instruction ("no need to show this dropdown
menu, the button should directly enable the continuous talking mode"). Removes a component rather
than adding one (constitution §3 KISS) — `useState` for `modeMenuAnchor` and the `Menu`/`MenuItem`
imports become fully unused and are deleted, not left as dead code.

## Decision 5 — Backend test placement: this repo has ~190 pre-existing, unrelated uncommitted
## test-file changes from a mechanical bulk edit; new coverage must go in new files

**Finding**: Every candidate existing backend test file for this feature's coverage
(`tests/AskLucy.Web.Tests/Middleware/ProblemDetailsMiddlewareTests.cs`,
`tests/AskLucy.Web.Tests/Ai/AiControllerVoiceTests.cs`, `AnonymousAccessTests.cs`, and in fact
~190 other test files repo-wide) already carry a pre-existing, unrelated, uncommitted local
modification (confirmed via `git diff`: a mechanical `cancellationToken:
TestContext.Current.CancellationToken` addition to async xUnit calls, unrelated to this feature
and present since before this entire chat-widget bug-fix session began). Per this session's
established convention (carried through specs/029-031), these files are never staged or
committed — editing one of them for this feature's new coverage would make it impossible to stage
only this feature's lines without also bundling in that unrelated pre-existing change.

**Decision**: Place all new backend test coverage for this feature in brand-new files:
`tests/AskLucy.Infrastructure.Tests/Ai/OpenAIProviderTests.cs` (the first test file for this
provider — confirmed via the investigation that none currently exists) for `EnsureSuccessAsync`'s
new/existing classification behavior, and
`tests/AskLucy.Web.Tests/Middleware/AiProviderRequestInvalidExceptionMappingTests.cs` for the new
`ProblemDetailsMiddleware.Map()` case, mirroring the existing (unedited) file's own test pattern
for its sibling `AiProvider*Exception` cases.

**Rationale**: Keeps this feature's git history clean and reviewable (no unrelated bulk-edit noise
bundled into this PR) without touching or discarding another, evidently in-progress, uncommitted
change elsewhere in the repo — consistent with this session's established handling of the same
constraint throughout specs/029-031.

## Decision 6 — `OpenAIProvider.cs` itself already has one unrelated dirty line; the user
## approved bundling it rather than isolating it

**Finding**: Unlike the ~190-file mechanical test pattern above, `src/AskLucy.Infrastructure/Ai/
OpenAIProvider.cs` — the exact source file Decision 1 must edit — already carries one small,
different, unrelated uncommitted change: `BuildChatPayload`'s return type
(`:253`) changed from `object` to `Dictionary<string, object?>`, unrelated to
`EnsureSuccessAsync`/`IsTransient` (`:334-400`), the region this feature touches.

**Decision**: Confirmed with the user directly (they have final say over their own uncommitted
work) — edit `EnsureSuccessAsync` in place alongside the existing `BuildChatPayload` change, and
stage/commit the whole file (including that pre-existing 1-line change) when this feature's CI/CD
cycle runs, rather than stashing it aside and restoring it afterward.

**Rationale**: Unlike the test-file pattern (Decision 5), this file cannot be avoided — it's where
the feature's core fix lives — so isolating the unrelated line would require a stash/pop dance for
no real benefit once the user confirmed they're fine with it riding along in this feature's
commit.
