# Feature Specification: Legacy Application Modernization & Technology Stack Migration

**Feature Branch**: `000-legacy-modernization`

**Created**: 2026-07-27

**Status**: Draft

**Input**: User description: "SPEC-000: Legacy Application Modernization & Technology Stack Migration — migrate the existing Ask Lucy application to the enterprise architecture defined in `.specify/memory/constitution.md` and `docs/*.md`, preserving all existing user-facing functionality. This is not a rewrite and not a feature-development effort; no new business capability is introduced. Future specifications (SPEC-001 through SPEC-010) build on top of this migration."

## Framing

Ask Lucy is an existing, production AI assistant application (originally built as "ChatGPT Client"). This specification defines **how the existing application is modernized onto the approved architecture and technology stack without changing what a user can already do.**

- This is **not** a rewrite for its own sake and **not** a feature-development project.
- No existing user-facing capability is removed without explicit approval (captured in this document).
- No new business capability (chat history, multiple conversations, RAG, AI memory, multiple providers, agents, MCP, knowledge bases, prompt library, billing, team collaboration) is introduced here — those are reserved for the future specifications listed in [§ Future Specifications](#future-specifications).
- Where an existing behavior cannot be migrated exactly as-is, the limitation and the chosen alternative are documented explicitly rather than silently changed.

This specification is the prerequisite gate for every subsequent Ask Lucy specification.

## Clarifications

### Session 2026-07-27

- Q: SC-006 currently says users should perceive chat responses "at least as quickly" as before — too vague to test. What concrete performance target should replace it? → A: P95 time-to-first-visible-content under 2 seconds for a standard chat message under normal load.
- Q: When the OpenAI API itself times out, rate-limits, or errors, what should the migrated system do (today it just surfaces whatever raw error occurs)? → A: Attempt one automatic retry with backoff for transient failures, then surface a clear, user-friendly Problem Details error if it still fails.
- Q: FR-023 requires rate limiting on AI endpoints but doesn't specify a number — what per-user threshold should apply? → A: Tiered by role (regular users get a standard limit; Administrator/Super User accounts get a higher or no limit); exact numeric thresholds are configurable and set during planning rather than fixed in this specification.
- Q: The legacy API scaffolding supports editing/deleting a saved chat entry, but no rename/delete UI was found wired up today (only "create chat" is observably functional). Should rename/delete be preserved, or is "create only" the actual behavior to preserve? → A: Add user-facing rename and delete for chat entries as part of this migration, even though it goes slightly beyond strict 1:1 UI preservation.
- Q: No concurrent-user or scale assumption is stated anywhere in the spec, making the rate limits and performance target hard to size. What's a reasonable current/near-term scale assumption? → A: Small team, fewer than 100 total registered users, with low concurrency — consistent with the current single shared-hosting instance and no load-balancing.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Existing user experience is unchanged (Priority: P1)

An existing, already-registered user logs in and uses AI chat, image generation, translation, voice input/output, PDF-based chat, and their saved chat list exactly as they could before the migration — they should not notice anything different in what they can do, only how it feels (faster, more reliable).

**Why this priority**: This is the entire point of the migration. If existing users lose any capability, the migration has failed regardless of how much the technology stack improved underneath.

**Independent Test**: Run the full legacy-feature regression matrix (chat, voice, speech-to-text, PDF upload/extraction, translation, image generation, authentication, user settings — per `docs/TESTING.md` §36) against the migrated application and confirm every item behaves identically to the pre-migration baseline.

**Acceptance Scenarios**:

1. **Given** a user with an existing account and existing chat entries, **When** they log in after migration, **Then** they see the same chats, the same profile information, and can continue chatting exactly as before.
2. **Given** an authenticated user, **When** they send a chat message, generate an image, request a translation, or upload an audio file for transcription, **Then** they receive the same category of result they received before migration.
3. **Given** an authenticated user with 2FA enabled before migration, **When** they log in after migration, **Then** they can still complete their TOTP challenge without re-enrolling.

---

### User Story 2 - Anonymous AI access is closed (Priority: P1)

A visitor who is not logged in can no longer call the AI chat, image-generation, translation, or transcription endpoints and consume the organization's AI provider quota.

**Why this priority**: This is a live cost-abuse and security exposure in the current production system (see [§ Gap Analysis](#gap-analysis)). Per the approved decision for this migration, it is closed as part of this phase rather than carried forward.

**Independent Test**: Call each AI endpoint without an authenticated session and confirm each is rejected with an authorization error; call the same endpoints with a valid authenticated session and confirm they succeed.

**Acceptance Scenarios**:

1. **Given** no authenticated session, **When** a request is made to any AI-invoking endpoint, **Then** the system rejects it with an authentication-required response.
2. **Given** a valid authenticated session, **When** the same request is made, **Then** the system processes it normally.

---

### User Story 3 - Cross-user data access is closed (Priority: P2)

An authenticated user can no longer read, modify, or delete another user's chats, and cannot view or alter another user's account record (including fields such as password hash that must never be exposed at all).

**Why this priority**: This is a concrete, currently-exploitable data-isolation gap (see [§ Gap Analysis](#gap-analysis)). It does not change any legitimate user's own experience — it only removes access that was never an intended capability.

**Independent Test**: As User A, attempt to read/update/delete a chat or account record belonging to User B, using only User A's authenticated session; confirm every attempt is rejected. Confirm no API response ever includes password hash, security stamp, or concurrency stamp fields.

**Acceptance Scenarios**:

1. **Given** User A is authenticated, **When** User A requests User B's chat by ID, **Then** the system denies access.
2. **Given** any authenticated user, **When** user account data is retrieved through any endpoint, **Then** the response never includes password hash, security stamp, or other Identity secrets.

---

### User Story 4 - Administrative access is properly gated (Priority: P2)

A user in the "Super User"/"Administrator" role can still reach the Control Panel and user-management screens exactly as before; a user who is **not** in that role can no longer reach those screens by navigating to their URLs directly.

**Why this priority**: Today, the role check only hides a link in the UI — the underlying pages have no server-side role enforcement. This closes that gap without changing what an actual administrator can do.

**Independent Test**: As a non-admin authenticated user, navigate directly to each Control Panel/user-management route and confirm access is denied. As an admin user, confirm all the same routes work exactly as before.

**Acceptance Scenarios**:

1. **Given** an authenticated user without the Administrator/Super User role, **When** they request a Control Panel route, **Then** the system denies access.
2. **Given** an authenticated user with the Administrator/Super User role, **When** they request the same route, **Then** the system serves it as before.

---

### User Story 5 - Automated build, test, and deploy (Priority: P3)

An engineer pushes a change and it is automatically built, linted, tested, and deployed to the existing hosting target without manual steps.

**Why this priority**: Enables every future specification to ship safely and quickly; lower priority than functional/security parity because it does not affect end users directly.

**Independent Test**: Push a change to the repository and confirm a GitHub Actions pipeline runs build, lint, and automated tests, and that a merge to the main branch results in an automated deployment to the existing hosting target.

**Acceptance Scenarios**:

1. **Given** a pull request with a failing test, **When** CI runs, **Then** the merge is blocked.
2. **Given** a merged, passing change, **When** the pipeline completes, **Then** the application is deployed to the existing hosting target without a manual publish step.

---

### Edge Cases

- An anonymous visitor who previously could chat without logging in is now redirected to sign in; since no server-side conversation content is ever persisted for anonymous use today (only client-memory), no data is lost by this change — only access is gated earlier.
- A user with TOTP 2FA already enrolled must be able to complete login after the underlying auth transport changes from cookie-only to JWT + refresh tokens, without being forced to re-enroll their authenticator app.
- All users are signed out exactly once at the moment authentication is cut over from cookie sessions to JWT (a one-time re-login), since existing cookie sessions have no equivalent in the new token model.
- A `UserChats` row's existing integer identifier is never referenced outside the application (confirmed: no bookmarked/external links depend on it), so changing its key type during migration carries no external-reference risk.
- The already-exposed secrets and hardcoded seed-admin credential (see [§ Risks](#risks)) are explicitly not rotated by this specification; anyone with repository access retains the exposure that exists today until a separate remediation is performed.

## Current System Assessment

*(Deliverable 1 — see also the full technical inventory this section summarizes.)*

**Architecture.** A single ASP.NET Core MVC project (no layering, no CQRS, no service abstractions) targeting **.NET 7** (out of support). Server-rendered Razor views and Razor Pages (scaffolded ASP.NET Identity UI) plus a client-side TypeScript/webpack/jQuery bundle — there is no SPA framework and no React code today.

**Technologies & dependencies.** ASP.NET Core Identity (cookie-based), EF Core 7 / SQL Server, Google/Facebook OAuth (Microsoft/Twitter wired but commented out), SendGrid for outbound email, Google APIs client libraries, HtmlAgilityPack. Frontend build is webpack + Babel over a large, eclectic dependency set spanning several UI eras at once (jQuery, Bootstrap 5, MDB admin theme, DataTables, KaTeX **and** MathJax, `@mui/material` present but unused, `knockout` present but unused, `tinymce` imported but commented out, `mark.js` present but unused, `moment`, deprecated `request`) — evidence of accumulated, uncleaned experimentation rather than an intentional multi-library strategy.

**Modules.** `ChatGPTController` (chat/translate/image-generation/transcription, all `[AllowAnonymous]`, calling the OpenAI REST API directly and synchronously with no abstraction), `HomeController` (main chat view), `PolicyController` (static policy pages), `Controllers/api/UserChatsController` (CRUD over chat titles), `Areas/ControlPanel` (admin shell, role-checked only in the view), `Areas/UsersManager/Controllers/api/UsersController` (user CRUD, returns raw Identity entities), `Areas/Identity` (stock scaffolded Identity UI, including a fully implemented TOTP 2FA flow). `Services/` contains only an email sender and two dead/half-finished pieces (`IProfileManager`/`ProfileManager`, never invoked).

**Database.** One custom table (`UserChats`: int identity PK, `Title`, `SessionId`, two nullable local-time timestamps, FK to `AspNetUsers`) plus the stock ASP.NET Identity schema extended with `FirstName`, `LastName`, `BirthDate`, and an inline `byte[]` `ProfilePicture`. **There is no message/conversation-content table** — conversation content lives only in browser memory and is lost on refresh; only a chat's title/timestamps persist server-side. Schema has not evolved since July 2023 (12 migrations, all within a 3.5-month window).

**APIs.** Two unversioned attribute-routed API controllers plus four raw, unauthenticated, non-streaming POST endpoints under `/openai/*`. No Problem Details, no pagination, no OpenAPI/Swagger, no rate limiting.

**Frontend.** ~2,100-line single-file chat application (`app.ts`) combining AJAX calls, Web Speech recognition/synthesis, a Web Audio visualizer, KaTeX rendering with an accessible spoken-math renderer, and file-type dispatch for PDF/audio/CSV uploads. PDF text extraction, speech-to-text (live voice), and text-to-speech all run **client-side in the browser**, not on the server (only uploaded-audio-file transcription goes through the server, via OpenAI Whisper).

**Authentication.** ASP.NET Identity with email confirmation required, Google + Facebook OAuth, a fully functional TOTP 2FA flow (enrollment, recovery codes, login challenge), 5-minute sliding cookie expiration, a 10-second session-cache idle timeout (looks like a leftover debug value), and wildcard (`*`) CORS. Role checks ("Super User"/"Administrator") exist only in Razor views, never enforced server-side.

**Deployment.** FTP and MSDeploy publish profiles targeting a third-party shared host (`site4now.net`) and an orphaned Azure App Service still named `chatgpt-client`. No Docker, no CI/CD pipeline, no automated tests anywhere in the repository.

## Migration Strategy

*(Deliverable 2)*

**Approaches considered:**

| Option | Description | Trade-off |
|---|---|---|
| A. Big-bang rewrite | Rebuild everything on the new stack and cut over in one release. | Rejected. Highest risk: every capability must work correctly on day one, rollback means reverting the entire application, and the user has explicitly ruled this out. |
| **B. Incremental, module-by-module (Recommended)** | Introduce the new solution structure and CI first with zero behavior change, then migrate one module at a time behind the same regression matrix, keeping the legacy project deployable throughout. | Slower to reach 100% completion, but each increment ships independently, is independently testable, and is independently revertible. Matches the constitution's incremental-delivery expectations. |
| C. Parallel-run / blue-green hosting cutover | Stand up the new deployment target alongside the old and shift traffic gradually. | Not applicable this phase — the approved decision keeps the current `site4now.net` hosting target unchanged (see [§ Assumptions](#assumptions)); this option is deferred to whichever future spec revisits hosting. |

**Recommended sequence (each step gated by CI and the regression matrix before the next begins):**

1. **Foundation** — introduce the `Domain`/`Application`/`Infrastructure`/`Persistence`/`WebAPI` solution structure and the GitHub Actions pipeline (build, lint, test) with no behavior change yet; the legacy project keeps running and deploying unchanged.
2. **Authentication** — migrate ASP.NET Identity to issue JWT access tokens + rotating refresh tokens, preserving email confirmation, social login, and TOTP 2FA; add role-based server-side authorization.
3. **AI endpoints** — introduce `IAIProvider`/`OpenAIProvider` behind CQRS commands/queries for chat, translate, image generation, and transcription, one endpoint at a time, each requiring authentication and each verified against the regression matrix before moving to the next.
4. **Data** — migrate `UserChats` to the project's standard entity conventions (surrogate key, audit columns, soft delete, concurrency token) and move the profile picture from an inline database BLOB to file storage served via a signed URL.
5. **Frontend** — replace the Razor/jQuery/webpack UI with React 19 + Vite + MUI, screen by screen, each screen verified against the regression matrix before the next begins.
6. **Decommission** — remove the legacy ASP.NET MVC/Razor project only after every capability has reached parity in production.

**Rollback considerations.** Database migrations in steps 3–4 are additive/backward-compatible wherever possible so the legacy and new code can coexist against the same database during the transition; no destructive schema change is applied until parity is confirmed. Because the legacy project remains deployable through step 5, rollback at any point is a redeploy of the last known-good published artifact through the existing (now CI-driven) publish pipeline — no separate rollback tooling is required for this phase.

## Target Architecture

*(Deliverable 3 — full detail lives in the referenced documents; this section states what applies to this migration specifically.)*

This migration adopts the architecture already approved in `.specify/memory/constitution.md` and `docs/ARCHITECTURE.md`, `docs/DATABASE.md`, `docs/ENTITY_MODEL.md`, `docs/API_GUIDELINES.md`, `docs/SECURITY.md`, `docs/DOMAIN_SERVICES.md`, `docs/DESIGN_SYSTEM.md`, `docs/UI_GUIDELINES.md`, and `docs/TESTING.md` — but scoped to only what Phase 0 (`docs/ROADMAP.md` § "Phase 0 – Platform Modernization") requires:

- **Backend**: Clean Architecture solution (`Domain`, `Application`, `Infrastructure`, `Persistence`, `WebAPI`) on ASP.NET Core (.NET 10), CQRS via MediatR, FluentValidation, AutoMapper, Serilog structured logging, OpenAPI/Swagger.
- **AI integration**: a single `IAIProvider` abstraction with **one** implementation (`OpenAIProvider`) covering `Chat`, `Stream`, `GenerateImage`, and `SpeechToText` — matching today's four capabilities exactly. Multi-provider support, model switching, and additional providers are explicitly **not** introduced here (see [§ Future Specifications](#future-specifications), SPEC-003/SPEC-004).
- **Streaming**: chat responses are delivered via Server-Sent Events instead of today's blocking request/response, per the approved target stack's stated preference for SSE. This changes only delivery cadence, not the final content the user receives, so it is treated as in-scope for this migration rather than a new capability.
- **Text-to-speech**: remains a **client-side** capability (browser `SpeechSynthesis` API), unchanged — no server-side `TextToSpeech()` capability is introduced in this phase.
- **Authentication**: ASP.NET Identity + JWT access tokens + rotating refresh tokens + TOTP 2FA, per `docs/DATABASE.md` § "Identity Context" — limited to the `Users`, `RefreshTokens`, `ExternalLogins`, and `TwoFactorDevices` concepts already needed to preserve current auth capability. `UserSessions`/device tracking beyond what exists today is not introduced.
- **Data**: SQL Server via EF Core Code-First, with the `UserChats` table (only) brought onto the standard entity conventions (GUID surrogate key, `CreatedAtUtc`/`CreatedBy`/`ModifiedAtUtc`/`ModifiedBy`/`DeletedAtUtc`/`DeletedBy`, concurrency token). The full `Conversations`/`Messages`/`KnowledgeBase`/`Agent`/`Payment` schema described in `docs/DATABASE.md` is future scope, not part of this migration.
- **API**: versioned REST (`/api/v1/...`), RFC 9457 Problem Details error responses, per `docs/API_GUIDELINES.md`.
- **Frontend**: React 19 + TypeScript + Vite + Material UI, feature-based folder structure per `docs/UI_GUIDELINES.md`/`docs/DESIGN_SYSTEM.md`, limited to the single `chat` feature area (plus authentication/profile screens) — the `rag`, `agents`, `billing`, `admin`-beyond-user-management, etc. feature folders described in those documents are not built here.
- **Deployment**: GitHub Actions CI/CD deploying to the existing `site4now.net` shared-hosting target via the existing publish mechanism. **Docker containerization and an Azure App Service cutover are explicitly deferred** past this migration per the approved scope decision — this is a deliberate, documented divergence from the org's longer-term Azure-ready aspiration in `docs/ARCHITECTURE.md`, scoped to this phase only.

## Gap Analysis

*(Deliverable 4)*

**Technical debt**
- .NET 7 is out of support; no automated tests exist anywhere in the repository; front-end build artifacts (`.js` alongside `.ts`) are committed to source control; several dependencies are present but entirely unused (`@mui/material`, `knockout`, `mark.js`, `tinymce`, `mathjax`) or deprecated (`moment`, `request`); dead/half-finished code exists (`IProfileManager`/`ProfileManager`, `UserProfileFoldersTemplate`, the `ChatMessages`/`Author`/`Content` POCOs left over from the app's original "ChatGPT Client" export-format origin); several scaffolded folders were never filled in; the npm package is still named `chatgpt-client`.

**Deprecated / unsupported libraries**
- .NET 7 (end of support), `moment` (deprecated, in maintenance mode only), `request` (deprecated by its own maintainers), duplicate math-rendering libraries (KaTeX **and** MathJax both present, only KaTeX wired up).

**Security risks** *(full detail in `docs/SECURITY.md` terms; see also the clarified scope decisions above)*
- Anonymous, unauthenticated access to all four AI endpoints (cost-abuse exposure) — **closed by this migration** (User Story 2).
- Missing authorization on the chat-CRUD and user-management API controllers, enabling cross-user data access and administrative-data exposure (IDOR) — **closed by this migration** (User Story 3).
- Raw `ApplicationUser` entities (including password hash, security stamp) returned directly by the user-management API and rendered into an admin HTML table — **closed by this migration** (User Story 3).
- Overposting/mass-assignment on the user-update endpoint (a client-supplied entity is persisted almost as-is) — **closed by this migration** as part of introducing DTO-based, validated commands.
- UI-only ("Super User") role enforcement with no server-side check on Control Panel routes — **closed by this migration** (User Story 4).
- Hardcoded plaintext secrets committed in `appsettings.json` (OAuth secrets, three OpenAI API keys, SendGrid key, SQL Server connection string with credentials) and three credentialed `*.PublishSettings` files at the repository root, plus a hardcoded seed-admin plaintext password baked into the EF Core migrations — **explicitly not remediated by this migration**, per the approved scope decision; carried forward as an accepted, documented risk (see [§ Risks](#risks)).
- Wildcard (`*`) CORS and an inconsistent 10-second session idle timeout — flagged for correction as part of the authentication/API modernization work, since fixing them does not change any legitimate user's behavior.
- A registration-flow bug that mutates the shared email-confirmation template file on disk for every registration (a race condition under concurrent registrations, and permanent template corruption after the first send) — fixed as part of the email-sending modernization, since this is an implementation defect, not a designed behavior.

**Performance bottlenecks**
- The chat endpoint blocks until the full AI response is received before returning anything to the browser (no streaming) — addressed by the SSE-based Chat Engine described in [§ Target Architecture](#target-architecture).
- The user's profile picture is re-transmitted as a base64 data URI inline in every page load rather than served as a cacheable image — addressed by moving to file storage + signed URLs.

**Maintainability issues**
- All business logic lives inline in MVC controllers with no service/abstraction layer, no CQRS, and no tests, making any change high-risk and hard to verify — addressed by the Clean Architecture/CQRS migration itself.
- A single ~2,100-line frontend file mixes AJAX, speech, visualization, and math-rendering concerns — addressed by the feature-based React frontend restructuring.

## Functional Requirements

### Feature parity (preserve exactly)

- **FR-001**: System MUST allow an authenticated user to send a chat message and receive an AI-generated response, equivalent in capability to today's default chat model behavior.
- **FR-002**: System MUST allow an authenticated user to generate an image from a text prompt, equivalent to today's image-generation capability.
- **FR-003**: System MUST allow an authenticated user to translate content via AI assistance, preserving today's multi-language, direction-aware (LTR/RTL) rendering behavior.
- **FR-004**: System MUST allow an authenticated user to upload an audio file and receive a text transcription, equivalent to today's capability.
- **FR-005**: System MUST continue to perform PDF text extraction client-side in the browser, with extracted text available for the user to send as a chat message, unchanged from today.
- **FR-006**: System MUST continue to support client-side voice input (speech recognition) and client-side voice output (speech synthesis) via the browser's native Web Speech APIs, unchanged from today.
- **FR-007**: System MUST continue to support math/equation rendering, including an accessible spoken/read-aloud rendering of equations, unchanged from today.
- **FR-008**: System MUST allow a user to create a named chat entry that persists across sessions, equivalent to today's `UserChats` capability.
- **FR-033**: System MUST allow a user to rename and delete their own saved chat entries. This completes CRUD support that already exists at the API-scaffolding level today but has no wired-up UI; it is treated as an approved, minor completion of an existing entity's lifecycle rather than a new business capability (see Assumptions).
- **FR-009**: System MUST continue to support user registration, email confirmation, login, logout, password reset, and account management via ASP.NET Identity.
- **FR-010**: System MUST continue to support Google and Facebook social sign-in, preserving the profile claims currently captured (e.g., profile picture).
- **FR-011**: System MUST continue to support TOTP-based two-factor authentication, including enrollment, recovery codes, and 2FA login challenge.
- **FR-012**: System MUST continue to support light and dark UI themes and a responsive layout across desktop and mobile breakpoints.
- **FR-013**: System MUST continue to support the target-language selector used to drive AI-assisted translation (as today — this selects the translation's target language; it is not a localized-UI/chrome language switch).
- **FR-014**: System MUST preserve every existing user's account, profile, chat entries, and role assignments through the migration with zero data loss.

### Authentication & authorization modernization

- **FR-015**: System MUST require an authenticated session for every AI-invoking endpoint (chat, image generation, translation, transcription).
- **FR-016**: System MUST issue JWT access tokens plus rotating, revocable refresh tokens for authenticated sessions, replacing the current cookie-only session model, while preserving the login/2FA/social-login capabilities in FR-009 through FR-011.
- **FR-017**: System MUST enforce role-based authorization on every Control Panel and user-management action, so only a user holding the appropriate role can perform it.
- **FR-018**: System MUST scope every user-owned resource (chats, profile) so a user can only read, modify, or delete their own data.
- **FR-019**: System MUST NOT return password hashes, security stamps, or other Identity secrets in any API response.

### API & backend modernization

- **FR-020**: System MUST expose chat, translation, image-generation, and transcription functionality through a versioned REST API, replacing today's unversioned raw endpoints.
- **FR-021**: System MUST return API errors in RFC 9457 Problem Details format.
- **FR-022**: System MUST route all AI-provider calls through a single server-side provider abstraction rather than inline controller code, without introducing additional providers or a provider/model-switching capability.
- **FR-023**: System MUST apply rate limiting to AI-invoking endpoints, tiered by role — regular users receive a standard per-minute request limit across chat/translate/image/transcribe combined, while Administrator/Super User accounts receive a higher (or no) limit. Exact numeric thresholds are a configurable value determined during planning, not fixed by this specification.
- **FR-032**: When the underlying AI provider call fails transiently (timeout, 5xx, rate-limit), the system MUST retry automatically once with backoff before giving up; if the retry also fails, the system MUST return a clear, user-friendly Problem Details error rather than a raw provider error.

### Data modernization

- **FR-024**: System MUST migrate the existing chat-entry data to the project's standard entity conventions (surrogate key, audit columns, soft delete, concurrency token) without altering its currently observable fields or losing existing rows.
- **FR-025**: System MUST store the user profile picture as a file served via a signed download URL, replacing the current inline database BLOB, with every existing user's picture migrated without loss.
- **FR-026**: System MUST NOT introduce a persisted message/conversation-content table, knowledge base, RAG/embeddings, AI memory, agents, MCP, prompt library, or billing changes as part of this migration.

### Frontend modernization

- **FR-027**: System MUST replace the current server-rendered UI with a React 19 + TypeScript + Vite + Material UI implementation that is behaviorally and visually equivalent, for every capability in FR-001 through FR-013, to the current UI.
- **FR-028**: System MUST preserve the chat UI's Markdown/math rendering, language selection, file-attach flows (PDF/audio/CSV), and chat-creation interaction in the new frontend.

### CI/CD & quality

- **FR-029**: System MUST run an automated pipeline (build, lint, automated tests) on every change and block merges on failure.
- **FR-030**: System MUST deploy to the existing `site4now.net` hosting target via the existing publish mechanism; this migration MUST NOT introduce containerization or an Azure hosting cutover.
- **FR-031**: System MUST include automated unit and integration test coverage for the migrated authentication, authorization, and AI-endpoint behavior described above, where none exists today.

## Non-Functional Requirements

The migrated application MUST improve the following, without changing the user-facing behavior defined in the Functional Requirements above:

- **Maintainability** — Clean Architecture layering, CQRS, and automated tests replace today's untested, unlayered controller logic.
- **Performance** — chat responses stream to the user instead of blocking; profile images are served via cacheable URLs instead of inline BLOBs.
- **Security** — anonymous AI access, cross-user data access, password-hash exposure, mass assignment, UI-only role checks, wildcard CORS, and the email-template race condition are all closed (except the two items explicitly deferred per [§ Assumptions](#assumptions)).
- **Testability** — every migrated module has unit and/or integration test coverage; none exists today.
- **Scalability** — the API layer is stateless (JWT-based) rather than relying on server-side session/cookie state. Sized for the current scale assumption of fewer than 100 total registered users with low concurrency (see Assumptions) — this migration is not required to support significantly higher scale.
- **Observability** — structured logging, correlation IDs, and health checks are introduced per `docs/ARCHITECTURE.md` §21–22.
- **Accessibility** — the new React/MUI frontend meets WCAG 2.1 AA, matching or exceeding today's accessibility posture.

## Key Entities

- **ApplicationUser** (existing, retained) — the Identity user; existing custom fields (`FirstName`, `LastName`, `BirthDate`) are preserved as-is; `ProfilePicture` moves from an inline BLOB to a stored-file reference served via signed URL.
- **UserChat** (existing, migrated) — the existing chat-title entity; migrated onto standard conventions (GUID key, audit columns, soft delete, concurrency token); no new fields (message content, attachments, etc.) are added — that is explicitly future scope.
- **RefreshToken** (new) — supports JWT refresh-token rotation; purely an authentication-infrastructure entity, not a user-facing capability.
- **Role / UserRole** (existing, unchanged data — newly enforced) — the existing "Super User"/"Administrator" roles; no new roles are introduced, but they are now checked server-side (FR-017).

Explicitly **not** introduced in this migration: `Conversations`, `Messages`, `MessageAttachments`, `KnowledgeBases`, `Documents`, `Embeddings`, `Agents`, `MCPServers`, `PromptTemplates`, `UserSubscriptions` — all reserved for the future specifications in [§ Future Specifications](#future-specifications).

## Success Criteria *(mandatory)*

- **SC-001**: 100% of the legacy capability regression matrix (chat, voice, speech-to-text, PDF upload/extraction, translation, image generation, authentication, user settings, theming, responsive layout) passes against the migrated application.
- **SC-002**: Every user who could log in before migration can log in after migration without re-registering, and sees the same chat entries and profile information they had before.
- **SC-003**: Anonymous requests to any AI-invoking capability are rejected; only authenticated requests succeed.
- **SC-004**: No user can view or modify another user's chats or account data through any interface, and no interface ever discloses a password hash or equivalent secret.
- **SC-005**: A non-administrator can no longer reach any Control Panel or user-management screen by direct navigation; an administrator can reach every one of them exactly as before.
- **SC-006**: For a standard chat message under normal load, the first visible content of the AI response appears within 2 seconds (P95), given the move to streamed delivery.
- **SC-007**: Every code change is automatically built, linted, and tested before merge, and every merge to the main branch deploys to the existing hosting target without a manual publish step.
- **SC-008**: The migrated codebase passes an architecture-compliance review against `.specify/memory/constitution.md` with zero unresolved Clean Architecture boundary violations.
- **SC-009**: Zero production data (accounts, chat entries, role assignments) is lost or corrupted during the migration cutover.

## Acceptance Criteria

*(Deliverable 5 — restates the Success Criteria and selected Functional Requirements as a mergeable checklist.)*

- [ ] Every capability in FR-001 through FR-013 functions identically to the pre-migration baseline (SC-001).
- [ ] Users can rename and delete their own saved chat entries (FR-033).
- [ ] The solution builds successfully targeting .NET 10 with the approved Clean Architecture solution structure.
- [ ] The React 19 + TypeScript + Vite + Material UI frontend is in place and serves every preserved capability (FR-027, FR-028).
- [ ] Authentication runs on ASP.NET Identity + JWT + refresh-token rotation + TOTP 2FA (FR-016).
- [ ] All AI-invoking endpoints require authentication and are rate-limited (FR-015, FR-023).
- [ ] Cross-user data access is impossible and no Identity secret is ever returned by an API (FR-018, FR-019).
- [ ] Control Panel / user-management actions are enforced server-side by role (FR-017).
- [ ] APIs are versioned and return Problem Details errors (FR-020, FR-021).
- [ ] The `UserChats` data is migrated onto standard entity conventions with zero data loss (FR-024).
- [ ] Automated tests exist and pass for the migrated authentication, authorization, and AI-endpoint behavior (FR-031).
- [ ] CI (GitHub Actions) builds, lints, and tests every change and deploys merges to the existing hosting target (FR-029, FR-030).
- [ ] No critical regressions exist against the legacy capability regression matrix.
- [ ] Documentation (this specification, referenced `docs/*.md`, and any ADRs raised during implementation) is up to date.

## Risks

*(Deliverable 6)*

| Risk | Category | Notes |
|---|---|---|
| Forced one-time re-login for every existing user at auth cutover | Compatibility / breaking change | Unavoidable consequence of moving from cookie sessions to JWT; communicate to users ahead of the cutover. |
| `UserChats` primary-key type change (int → GUID) during data migration | Data migration | No external references to the existing integer ID were found in the current codebase, but the production migration script must be tested against a copy of production data before running against the live database. |
| Anonymous-access closure changes behavior for any external tool or script relying on the current unauthenticated endpoints | Breaking change | No such consumer is known to exist; flagged here in case one is discovered before cutover. |
| Third-party dependency risk: continued dependence on OpenAI's API shape (even behind an abstraction) | Third-party dependency | Mitigated by the `IAIProvider` abstraction — provider-side changes are isolated to one implementation. |
| Browser compatibility of the Web Speech API (speech recognition/synthesis) is inherently browser- and OS-dependent | Browser compatibility | Pre-existing condition, unchanged by this migration; not a new risk introduced here. |
| Deployment risk: CI/CD is added on top of the existing FTP/MSDeploy shared-hosting publish path rather than replacing it | Deployment | Per the approved scope decision; revisit if `site4now.net` proves to be a long-term constraint. |
| **Accepted risk, not remediated by this specification**: hardcoded seed-admin plaintext password in EF migrations, and live OAuth/OpenAI/SendGrid/database secrets committed in `appsettings.json` and root-level `*.PublishSettings` files | Security | Explicitly deferred per stakeholder decision (see [§ Assumptions](#assumptions)); these credentials remain exposed to anyone with repository access until a separate remediation is performed. Recorded here so the risk is visible, not silently dropped. |

## Assumptions

*(Deliverable 7)*

- **Anonymous AI access is closed.** Per the approved decision, every AI-invoking endpoint requires authentication after this migration (FR-015); this is a deliberate, approved change to today's literal behavior, justified as closing an unintended cost-abuse exposure rather than a designed feature.
- **Hosting stays on `site4now.net`; Docker/Azure are deferred.** Per the approved decision, this migration adds a GitHub Actions CI/CD pipeline but keeps deploying to the existing shared-hosting target through the existing publish profiles. Containerization and an Azure App Service cutover are out of scope for this specification and reserved for a future phase.
- **Credential rotation and hardcoded seed-admin removal are out of scope.** Per the approved decision, the already-exposed secrets and hardcoded seed-admin account are left unchanged by this specification and carried forward as a documented, accepted risk rather than an acceptance criterion.
- Existing production data (user accounts, chat entries, role assignments) is migrated in place through additive EF Core migrations; no production data is deleted or recreated from scratch.
- The application currently serves and is expected to continue serving fewer than 100 total registered users with low concurrency; rate limits (FR-023) and the performance target (SC-006) are sized against this scale, not against a large-scale multi-tenant load.
- Dead/never-invoked legacy code (`IProfileManager`/`ProfileManager`, `UserProfileFoldersTemplate`, the `ChatMessages`/`Author`/`Content` POCOs, unused front-end dependencies) implements no functioning capability today and is removed rather than ported, since porting non-functioning code would violate simplicity/YAGNI without preserving any real behavior.
- Conversation content itself is not persisted server-side today (only a chat's title/timestamps); this migration preserves that exact behavior. True persistent conversation history is explicitly future scope (SPEC-002).
- Introducing SSE-based streaming for chat responses is treated as in-scope transport modernization, not a new capability, since the final content delivered to the user is unchanged — only its delivery cadence improves, matching the approved target stack's stated preference for SSE.
- The orphaned `chatgpt-client.azurewebsites.net` Azure App Service is out of scope for this specification; its decommissioning, if desired, is an operational housekeeping task for a separate effort.
- **Chat rename/delete (FR-033) is an approved, minor exception to strict 1:1 preservation.** The underlying `UserChats` API already scaffolds edit/delete; only the UI was never wired up. Completing that UI is treated as finishing an existing entity's basic lifecycle, not as introducing a new business capability, and does not conflict with the Out-of-Scope list (chat history/multiple conversations/message content remain out of scope — only the existing chat-title entry's rename/delete is added).
- "Migrating" the frontend means a behaviorally and visually equivalent replatform (Razor/jQuery/webpack → React 19/Vite/MUI), not a visual redesign.

## Dependencies

*(Deliverable 8)*

- Every future specification (SPEC-001 through SPEC-010, see below) depends on this specification's Clean Architecture, CQRS, JWT-authentication, and `IAIProvider` foundation being in place first — none of them should begin implementation before this specification's Acceptance Criteria are met.
- A maintenance window and user-facing communication are needed for the one-time forced re-login at authentication cutover.
- A tested rollback/backup plan for the production SQL Server database is required before the `UserChats` migration (int → GUID key) is applied to production data.
- The three scope decisions captured in this specification (anonymous-access closure, hosting target, credential remediation) are treated as already resolved for planning purposes; revisiting any of them requires updating this specification, not silently overriding it during implementation.

## Future Specifications

*(Deliverable 9)*

This specification (**SPEC-000**) is the prerequisite foundation for all of the following, none of which may begin implementation before this specification's Acceptance Criteria are satisfied:

- **SPEC-001** — Authentication Improvements (if further needed beyond this migration's JWT/2FA baseline)
- **SPEC-002** — Conversation Management (persisted message history, multiple conversations, chat history)
- **SPEC-003** — AI Provider Abstraction (additional providers beyond the single `OpenAIProvider` introduced here)
- **SPEC-004** — Model Switching
- **SPEC-005** — Knowledge Bases
- **SPEC-006** — RAG
- **SPEC-007** — AI Memory
- **SPEC-008** — AI Agents
- **SPEC-009** — Prompt Library
- **SPEC-010** — Billing

## Migration Complete Definition

The migration defined by this specification is complete when:

- All existing functionality (FR-001 through FR-013) is preserved and verified against the regression matrix.
- The application runs on the approved technology stack (.NET 10, Clean Architecture, CQRS, React 19/Vite/MUI, JWT auth).
- The architecture complies with `.specify/memory/constitution.md` with no unresolved violations.
- Automated tests pass in CI.
- Performance is equal to or better than the legacy application (see SC-006).
- Documentation (this specification and the referenced `docs/*.md`) is complete and accurate.
- The codebase is ready for SPEC-001 through SPEC-010 to begin.
