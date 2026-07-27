# Phase 0 Research: Legacy Application Modernization & Technology Stack Migration

Each topic below was either an open technical decision not fixed by `spec.md`, or a "NEEDS CLARIFICATION"-shaped question the specification deliberately deferred to planning (rate-limit thresholds). No unresolved unknowns remain after this phase.

## 1. JWT access tokens + refresh token rotation on top of ASP.NET Core Identity

**Decision**: Keep ASP.NET Core Identity (`UserManager`/`SignInManager`) exactly as-is for credential validation, email confirmation, TOTP 2FA, and external-login linking. On successful sign-in, issue a short-lived JWT access token (15 minutes) plus a long-lived, single-use refresh token (14 days) stored server-side (hashed, with a `TokenFamily` id). Refresh MUST rotate the token (old one invalidated, new one issued) and reused-token detection MUST revoke the entire token family, per constitution §8/`docs/SECURITY.md` §8.

**Rationale**: This preserves every existing Identity capability (FR-009–FR-011) untouched — 2FA secrets and external-login claims live in the same `AspNetUserTokens`/`AspNetUserLogins` tables regardless of what's issued after sign-in — while replacing only the session transport, which is what FR-016 requires.

**Alternatives considered**: (a) Cookie auth retained alongside JWT for a transition period — rejected, adds a second auth code path to maintain and test for no real benefit given the small user base (<100 users) and the one-time-relogin edge case already accepted in `spec.md`. (b) Long-lived JWT with no refresh token — rejected, contradicts FR-016 and constitution §8 explicitly.

## 2. Chat response streaming transport

**Decision**: Server-Sent Events (SSE) via a plain `text/event-stream` response written incrementally from the WebAPI layer as the `IAIProvider.Stream()` call yields tokens. No SignalR.

**Rationale**: `docs/API_GUIDELINES.md`/target stack explicitly prefer SSE for this exact use case (one-way server→client token stream); SignalR is reserved (per the target stack) for scenarios needing real two-way real-time messaging, which chat streaming is not. SSE requires no new client library beyond the browser's native `EventSource` (or an `fetch` + `ReadableStream` reader, since `EventSource` doesn't support custom auth headers — the frontend will use a `fetch`-based SSE reader passing the JWT as a header).

**Alternatives considered**: SignalR — rejected as unnecessary complexity (WebSocket negotiation, hub infrastructure) for a one-directional stream; long-polling — rejected, strictly worse latency than SSE for the same effort.

## 3. Rate-limit thresholds (FR-023)

**Decision**: Use ASP.NET Core's built-in `Microsoft.AspNetCore.RateLimiting` middleware with a partitioned fixed-window limiter keyed by user id. Regular users: **20 requests/minute** across chat+translate+image+transcribe combined. Administrator/Super User accounts: **100 requests/minute** (generous, but not unlimited — an admin account compromise or a buggy admin script still shouldn't have zero ceiling, per constitution's Defense-in-Depth/Least-Privilege principles).

**Rationale**: `spec.md`'s Clarifications session confirmed a **tiered-by-role** policy shape but deferred exact numbers to planning. 20/min comfortably covers normal interactive chat use (a burst of messages plus occasional image/translation/transcription calls) for the confirmed <100-user, low-concurrency scale, while still bounding worst-case cost exposure from a single compromised or scripted account. 100/min for admins avoids friction for legitimate administrative/testing use without being literally unbounded.

**Alternatives considered**: A single flat limit for all roles — rejected, doesn't reflect that admin accounts have legitimate higher-volume needs (e.g., verifying a fix across many test messages). A token-bucket limiter — considered equivalent in effect for this scale; fixed-window is simpler to reason about and sufficient given the low concurrency.

## 4. AI provider failure handling (FR-032)

**Decision**: `OpenAIProvider` wraps each outbound call in a single retry with exponential backoff (e.g., 500ms then give up) for transient failures (timeouts, 5xx, 429 rate-limit responses from OpenAI itself). On final failure, the WebAPI layer returns an RFC 9457 Problem Details response with a stable error code (e.g., `ai-provider-unavailable`) and a user-friendly `detail`, never the raw provider exception or stack trace.

**Rationale**: Directly implements the clarification answer recorded in `spec.md`. A single retry balances resilience against not compounding latency on top of the 2-second SC-006 performance target — a second retry would risk blowing the P95 budget on an already-degraded provider.

**Alternatives considered**: A circuit breaker (e.g., Polly `CircuitBreakerPolicy`) — deferred as unnecessary for this scale/phase; a simple retry-once policy is sufficient and the circuit-breaker pattern can be added later behind the same `IAIProvider` abstraction without any caller-visible change if provider instability becomes a real operational problem.

## 5. `UserChats` primary key migration (int → GUID)

**Decision**: A single EF Core migration that (1) adds a new `Id` column of type `uniqueidentifier` with a `NEWSEQUENTIALID()` default, (2) backfills it for all existing rows, (3) drops the old `int` identity `Id` and the old `int`-typed FK usage, (4) promotes the new GUID column to primary key, and (5) adds the standard audit columns (`CreatedAtUtc`/`CreatedBy`/`ModifiedAtUtc`/`ModifiedBy`/`DeletedAtUtc`/`DeletedBy`) and a `RowVersion` concurrency token in the same migration. The migration is rehearsed against a restored copy of the production database before being run against production (per `spec.md` § Risks).

**Rationale**: Confirmed via codebase review (see `spec.md` § Edge Cases) that no code or external link references the existing integer id, so this is a self-contained, low-risk structural change. Doing it in one migration (rather than spreading the PK change across several) keeps the change auditable as a single, revertible unit, consistent with Migration Strategy step 4.

**Alternatives considered**: Keep the `int` PK and add a separate GUID "public id" column for external use — rejected as unnecessary complexity; there is no external consumer of the id today, so a dual-key scheme would add maintenance burden for no benefit.

## 6. Profile picture: BLOB → file + signed URL

**Decision**: On first migration touch, existing `ApplicationUser.ProfilePicture` BLOBs are written out to files under a server-side avatars directory (outside the web root, per constitution §8 file-upload guidance) and the column is replaced with an `AvatarFileName` (nullable string) field. Downloads go through a dedicated `GET /api/v1/users/{id}/avatar?exp=...&sig=...` endpoint validated via an HMAC signature (`IDataProtector`-based), short-lived (e.g., 15 minutes), matching the `IFileStorage`/signed-URL pattern in `docs/ARCHITECTURE.md` §17.

**Rationale**: Directly implements FR-025 with the simplest mechanism that satisfies "signed download URL" — no separate `Files`/`SignedDownloads` aggregate is introduced (that full context is future/Phase-3 scope per `docs/DATABASE.md`), keeping this migration's data model minimal per the Simplicity principle.

**Alternatives considered**: A full `Files` entity/table as described in `docs/DATABASE.md` §12 — rejected as scope creep for this phase; a single filename field on `ApplicationUser` is sufficient for the one file type (avatar) this migration handles.

## 7. Fixing the pre-existing CORS/session-timeout bugs

**Decision**: Replace wildcard (`*`) CORS with an explicit allow-list of the frontend's own origin(s) (the new Vite dev origin locally, the production frontend origin in each environment). Remove the 10-second session-cache idle timeout entirely, since server-side session state is no longer used once JWT auth replaces cookie sessions.

**Rationale**: Both are implementation defects (flagged in `spec.md` § Gap Analysis) whose correction changes no legitimate user-facing behavior — they are naturally subsumed by the JWT migration (session state disappears) and the API modernization (CORS must be scoped to the new SPA origin regardless).

**Alternatives considered**: None — there is no scenario in which preserving wildcard CORS or a 10-second session timeout is desirable; these are corrected as a byproduct of the authentication migration, not a separate decision.

## 8. Test infrastructure choices

**Decision**: Follow `docs/TESTING.md` exactly — xUnit + FluentAssertions + NSubstitute for unit tests; Testcontainers-provisioned SQL Server (not EF Core InMemory) for `AskLucy.Persistence.Tests`; `WebApplicationFactory` for API tests; Playwright for the end-to-end legacy-capability regression matrix (`docs/TESTING.md` §36).

**Rationale**: These are already the org's mandated testing stack; no alternative was evaluated since deviating would itself be a constitution violation without any offsetting benefit.

**Alternatives considered**: EF Core InMemory provider for persistence tests — explicitly rejected by `docs/TESTING.md` §13 ("Avoid the EF Core InMemory provider for relational behavior") since it doesn't validate real SQL Server constraint/concurrency behavior needed to verify the `UserChats` PK migration (Topic 5 above).

## Outcome

All Technical Context fields in `plan.md` are resolved with no remaining `NEEDS CLARIFICATION` markers. Proceeding to Phase 1.
