# Feature Specification: Admin Operational Failure Audit Trail

**Feature Branch**: `074-operational-failure-audit`
**Created**: 2026-09-25
**Status**: Draft
**Input**: User description: "Admin operational failure audit trail. When something fails for a user — a chat reply that stops partway, an AI provider rejecting its credential or running out of quota, a voice engine failing over, an embedding/RAG indexing failure, an agent or workflow step failing, a background job failing — the user must keep seeing a calm, generic message (they panic at "The AI provider rejected the credential"), while an administrator gets a precise, actionable record of the same event." (full text, including codebase context and constraints, in the `/speckit-specify` invocation of 2026-09-25)

## Overview

Today a failure has two audiences and serves neither well. End users are sometimes shown the
cause ("The AI provider rejected the configured credential. An administrator needs to check the
provider's API key.") and panic; administrators get nothing unless they happen to be the one who
hit the failure, or go reading the server log. This feature splits the two cleanly:

- **The user** always sees a short, calm, cause-free message — the same few sentences whatever
  went wrong behind the scenes.
- **The administrator** gets one precise, sanitised, grouped record per incident — what failed,
  for whom, where, why, and exactly which admin page fixes it — on a dedicated admin page, with an
  unacknowledged-critical count on the admin navigation.

## Clarifications

### Session 2026-09-25

- Q: What may an administrator see when following an incident's link to another user's chat, workflow or document? → A: Role-split. Administrators get metadata only (option A); the built-in Super User role also gets the full read-only content (option B), with every full-content access written to the immutable audit trail (FR-016–FR-016d). (Initially answered B for everyone; revised the same day.)
- Q: Should full-content access be fixed to the Super User role or delegable? → A: A permission, *View user content in failure investigations*, that only a Super User can grant or revoke (to any role, including Administrator) and that Super Users always hold (FR-016e–FR-016k, US1b).
- Q: How are failures grouped when there is no provider/model (jobs, workflow steps, agent tools, documents)? → A: Option C — key adds operation (job type / step type / tool / call type) and subject (specific workflow, agent or document); chats, users and runs stay per-occurrence (FR-018).
- Q: With per-subject incidents, how should one cause behind many incidents be handled? → A: Option B — badge counts distinct root causes; incidents show "N others share this cause" with Acknowledge all / Resolve all (FR-026a/b).
- Q: Which failures get recorded? → A: Option C — system-side failures plus refused sign-ins and authorisation checks as Warning (Access engine); user validation errors, genuine not-found, own rate limiting and token expiry are not recorded (FR-006–FR-006b, FR-012a).
- Q: What happens to failure records when a user is hard-erased? → A: Option B — anonymise to "erased user" (counts kept); content access events anonymised, never deleted; soft delete leaves records intact (FR-029a).

## Verified Codebase Context (2026-09-25)

Each item from the request was checked against the tree at `18a8970b`. Corrections are marked.

| Claim | Verified | Notes |
|---|---|---|
| `AiController.DescribeTurnFailure` (~L649) maps to three generic sentences, no `AiProviderException` arm | ✅ L649 | Called from the mid-stream `catch` at L342–L400, which records `RecordedTurnOutcome.FailedBeforeCompleting` (L367). **Addition:** that reason is also replayed into later turns' model context via `RecentTurnOutcomeSummary` (L81) and sent to the client in the `__TURN_OUTCOME__` event — so it must stay generic for *both* the user and the model. |
| Providers classify failures via `AiProviderException` subtypes | ✅ | `Application/Abstractions/IAIProvider.cs` — 9 subtypes over the Domain enum `AiProviderFailureKind` (CredentialRejected, CredentialUnreadable, NotConfigured, QuotaExhausted, RateLimited, UsageRestricted, Unavailable, RequestInvalid, ResponseNotUnderstood). |
| Middleware logs `AI provider failure surfaced: kind=..., status=...` | ✅ | `ProblemDetailsMiddleware.cs:509`, Warning, also logs `path`. |
| **Not in the request — found during verification** | ⚠️ | `ProblemDetailsMiddleware.MapProviderFailure` (L414) is where the panic sentence comes from: the **non-administrator** detail for `CredentialRejected` is literally "The AI provider rejected the configured credential. An administrator needs to check the provider's API key." `NotConfigured` ("An administrator needs to enable it.") and `RateLimited` ("…rate-limiting requests…") also name causes. Administrators already get the classified prose + a `providerFailure` extension (specs/043 FR-010/FR-015a) — but only on their *own* requests. |
| Problem Details carry a correlation id | ✅ | `CorrelationIdMiddleware` assigns `X-Correlation-Id`, pushes it into the Serilog `LogContext`, and `ProblemDetailsMiddleware` returns it as `traceId`. Background (Hangfire) jobs have no request and therefore no correlation id today. |
| `AIProviders.HealthStatus/HealthFailureKind/HealthFailureReason/HealthStatusCheckedAtUtc` | ✅ | `Domain/Ai/AIProvider.cs` — *current state* per provider, overwritten on each check. |
| `VoiceProviderFailoverEvents (UserId, OccurredAtUtc, Direction, Reason)` | ✅ | `Domain/Ai/VoiceProviderFailoverEvent.cs` — append-only, `Reason` already sanitised; `Direction` is `FailedOverToFallback` / `RecoveredToPrimary`. |
| Per-module audit logs (Agent, Mcp, Workflow, Document, KnowledgeBase, Memory, Prompt, Role) | ✅ | All `BaseEntity`, append-only, module-specific action enums (e.g. `McpAuditAction`), `DetailsJson`, not hard-FK'd. |
| Admin UI files | ✅ | `features/admin/{adminNav.tsx, adminPermissions.ts, pages/AdminAiProvidersPage.tsx, pages/AdminVoicePage.tsx}`. Permissions mirror `Domain/Authorization/AdminPermissionCatalog.cs` by hand. **Gap:** no admin page accepts a deep-link selection parameter today, and there is no admin user-detail or admin chat view. |
| Retention precedent | ✅ | `PasswordResetTokenCleanupJob` — 90-day retention via a Hangfire recurring job registered in `Program.cs`. |

## Design Decision: one cross-cutting store behind a recorder abstraction

**Decision.** Introduce **one new cross-cutting operational-failure store** with a single
Application-layer recording abstraction (working name `IOperationalFailureRecorder`) that every
engine calls at the point it already knows a failure happened. Do **not** extend or union the
existing per-module audit logs; **link** to the existing partial trails rather than copying them.

**Why one store, not eight unions:**

1. **Different question, different shape.** The per-module audit logs answer *"who changed what,
   and was it allowed?"* (actions like `ServerRegistered`, `CredentialRotated`,
   `UnauthorizedAccessAttempted`). This feature answers *"what broke, for whom, and what do I do
   about it?"* Forcing failure fields (severity, kind, provider/model, corrective action,
   acknowledgement) into eight action-audit tables would bend all eight and still miss the
   engines that have no audit log at all — chat turns, voice, embeddings/RAG indexing and
   background jobs.
2. **One admin view needs one query.** Filtering by time/severity/engine/provider/user/kind,
   grouping bursts, paginating and counting unacknowledged criticals across eight differently
   shaped tables is a slow union that every new module would have to join.
3. **Acknowledge/resolve is mutable state.** The existing audit logs are deliberately append-only
   and tamper-resistant (constitution §8 "immutable audit trail"). Mixing mutable triage state into
   them would weaken that guarantee. Here, individual occurrences stay append-only; only the
   *incident group* carries triage state.
4. **Engines stay decoupled.** Modules depend on the recorder interface only (CLAUDE.md "modules
   communicate through interfaces"); none references another's audit table.

**How the existing trails relate (link, don't duplicate):**

| Existing trail | Relationship |
|---|---|
| `AIProvider` health columns | Remain the *current state*. An incident for a provider shows that provider's current health beside it; a failed scheduled health check records an occurrence like any other failure. |
| `VoiceProviderFailoverEvents` | Remains the voice page's own history. Each `FailedOverToFallback` also records an occurrence; each `RecoveredToPrimary` is a **recovery marker on the open incident**, not a failure row. |
| Per-module audit logs | Unchanged. Where a module's own audit entry exists for the same event (e.g. `McpAuditAction.CapabilityDiscoveryFailed`), the occurrence carries the same correlation id so the two can be cross-referenced; the audit log remains the authoritative security record. |

## User Scenarios & Testing *(mandatory)*

### User Story 1 — The user sees a calm message; the admin sees what actually happened (Priority: P1)

A user's chat reply fails because the provider rejected its credential. The user sees a short,
cause-free sentence and a retry affordance. Within seconds an administrator opening the new
*Operational failures* admin page sees one record: "Critical · Chat · Anthropic / claude-… ·
Credential rejected", the affected user and chat, a sanitised reason, the correlation id, and
"Replace the API key → Admin › AI providers › Anthropic".

**Why this priority**: This is the whole feature in one path: the user stops panicking and the
failure stops being invisible. It is also the currently broken path — a classified provider
failure mid-stream is recorded as "unexpected error", and before the stream it tells the user
about the credential.

**Independent Test**: With a provider configured with a deliberately invalid key, send a chat
message as a non-admin user; assert the user-visible text contains no cause words (credential,
key, quota, rate-limit, administrator, provider name) and that exactly one Critical incident
appears for an admin with the correct kind, provider, user, chat and correlation id.

**Acceptance Scenarios**:

1. **Given** a provider whose credential is rejected, **When** a non-admin user's chat turn fails *before* streaming starts, **Then** the user sees a generic message with no cause and an admin record of kind *Credential rejected* is created.
2. **Given** the same provider, **When** the turn fails *partway through* a streamed reply, **Then** the partial reply is kept, the calm failure notice is appended, the turn outcome still records *failed before completing* with a generic reason, and the admin record carries the classified kind (not "unexpected error").
3. **Given** a failed turn, **When** the user sends a later message in the same chat, **Then** the recent-turn summary supplied to the model contains only the generic reason, never the classified cause.
4. **Given** an administrator is the one whose request failed, **When** they see the response, **Then** they continue to receive the classified detail they get today (specs/043 FR-010), *and* an admin record is created like for any other user.
5. **Given** the admin record, **When** the admin clicks the corrective-action link, **Then** they land on the admin page where that provider's credential is managed with that provider already selected.
6. **Given** the admin record, **When** an Administrator (without *View user content*) clicks the chat link, **Then** they see the chat's metadata and where the failure occurred, the response contains no message text, and a direct request for the full content is refused.
7. **Given** the same record, **When** a Super User — or anyone a Super User has granted *View user content* — clicks the chat link, **Then** they see the transcript read-only with the failed turn highlighted, cannot act on it, and an access event naming them, the chat owner, the chat and the incident is written to the audit trail.

---

### User Story 1b — A Super User controls who may read user content (Priority: P2)

A Super User decides which staff, beyond Super Users, may read the content behind a failure. They
grant *View user content* to a custom "Support lead" role (or to the Administrator role), and
revoke it later. Administrators cannot grant it, revoke it, or hand out a role that carries it.

**Why this priority**: Without it, full-content access is Super-User-only, which is safe; this
story adds controlled delegation.

**Independent Test**: As an Administrator, attempt every path that could grant the permission
(create role, edit role, bulk delete, bulk assign, toggle on Administrator role, assign a role that has it) and
assert each is refused; repeat as a Super User and assert each succeeds and is audited.

**Acceptance Scenarios**:

1. **Given** a Super User, **When** they add *View user content* to a custom role, **Then** holders of that role get the full-content view and the grant is in the role audit trail.
2. **Given** an Administrator, **When** they try to add or remove *View user content* on any role, **Then** the request is refused and the picker shows the permission disabled with "Only a Super User can grant this".
3. **Given** a custom role holding *View user content*, **When** an Administrator edits its other permissions and saves, **Then** *View user content* is still on the role.
4. **Given** a custom role holding *View user content*, **When** an Administrator tries to assign it to a user, **Then** the assignment is refused.
5. **Given** a Super User, **When** they grant *View user content* to the Administrator role, **Then** every Administrator gets the full-content view; **When** they revoke it, every Administrator reverts to metadata only.
6. **Given** the Super User role, **When** anyone tries to remove *View user content* from it, **Then** it is refused.

---

### User Story 2 — Bursts collapse into one incident (Priority: P1)

On 2026-09-22 one user produced 7 ElevenLabs 401 failover/recover pairs within 70 seconds. The
admin page shows **one** incident — "Critical · Voice · ElevenLabs · Credential rejected · 7
occurrences · 1 user · first 14:02:10, last 14:03:20 · recovered 7×" — which expands to show the
individual occurrences, not 14 rows.

**Why this priority**: Without grouping, a single misconfiguration floods the page and hides every
other failure; the nav badge would count 7 criticals for one problem.

**Independent Test**: Record 7 failover + 7 recovery events for the same user/provider/kind within
70 s; assert the list shows 1 incident with occurrence count 7, recovery count 7, affected-user
count 1, and the badge increases by exactly 1.

**Acceptance Scenarios**:

1. **Given** repeated failures sharing the same grouping key (FR-018), **When** they occur while an incident for that combination is open (not resolved), **Then** they are added to that incident as occurrences, updating the count, last-seen time and distinct-affected-user count.
2. **Given** the same failure across several users, **When** it recurs, **Then** it is still one incident, and the affected-users count and list reflect every distinct user.
3. **Given** a voice recovery event, **When** it follows a failover, **Then** it increments the incident's recovery count and never creates a row of its own.
4. **Given** an incident that was **resolved**, **When** the same failure recurs, **Then** a **new** incident opens and indicates that it is a recurrence of the resolved one.
5. **Given** an incident that was **acknowledged** but not resolved, **When** the failure recurs, **Then** it joins the same incident and the incident is **not** re-flagged as unacknowledged.

---

### User Story 3 — Triage: filter, paginate, acknowledge, resolve, badge (Priority: P2)

An administrator filters the page to "last 24 h · Critical · Voice", acknowledges an incident while
they fix the key, and resolves it once fixed. The admin navigation shows a count badge of
unacknowledged Critical incidents so they notice new ones from any admin page.

**Why this priority**: Stories 1–2 make failures visible; this makes them manageable. Useful only
once there is something to triage.

**Independent Test**: Seed incidents across severities/engines/providers/users/kinds/times;
verify each filter and their combination, stable pagination, acknowledge/resolve transitions, and
the badge count.

**Acceptance Scenarios**:

1. **Given** incidents exist, **When** the admin filters by any combination of time range, severity, engine, provider, user and failure kind, **Then** only matching incidents are listed, newest last-seen first, paginated.
2. **Given** an open incident, **When** the admin acknowledges it, **Then** it records who acknowledged it and when, and it no longer counts toward the badge.
3. **Given** an open or acknowledged incident, **When** the admin resolves it (optionally with a short note), **Then** it records who resolved it, when and the note, and it leaves the default "open" view.
4. **Given** unacknowledged Critical incidents exist, **When** an admin with view permission views any admin page, **Then** the nav entry shows the number of distinct root causes among them; with none, no badge is shown.
5. **Given** a rejected embedding key produced 40 per-document Critical incidents, **When** the admin views any admin page, **Then** the badge shows 1; **When** they open one incident, **Then** it says "39 other incidents share this cause"; **When** they choose Resolve all with a note, **Then** all 40 are resolved, each recording the admin, time and note, and the badge clears.
6. **Given** a user without the new view permission, **When** they call the list/detail/count endpoints or open the page, **Then** access is denied (and logged as an access denial) and the nav entry is not shown.
7. **Given** a user with view but not manage permission, **When** they view an incident, **Then** acknowledge/resolve actions are not offered and the corresponding endpoints refuse them.

---

### User Story 4 — Every engine reports, not just chat (Priority: P2)

Failures from voice (TTS/transcription failover), embeddings/RAG indexing, agent tool steps,
workflow steps, MCP calls, image generation and background jobs that exhaust their retries all
produce records in the same place, with the same fields and the same user-facing calm.

**Why this priority**: The chat path proves the model; coverage makes the page trustworthy. An
admin who learns that "the page only shows chat" stops looking at it.

**Independent Test**: For each engine, force one representative failure and assert one record with
the correct engine, kind, severity and link targets.

**Acceptance Scenarios**:

1. **Given** a document's embedding/indexing fails, **When** processing stops, **Then** a record links the document (and knowledge base, where there is one) and its owner.
2. **Given** a workflow step fails, **When** the run is marked failed, **Then** the record links the workflow and the specific run/step.
3. **Given** a background job fails on its **final** retry, **When** it moves to the failed state, **Then** one record is created (not one per retry attempt), with a correlation id that also appears in that job's log lines.
4. **Given** a scheduled provider health check fails, **When** no user is involved, **Then** the record has no user and still links the provider's admin page.

---

### User Story 5 — Records age out on a defined schedule (Priority: P3)

Old incidents and occurrences are purged automatically so the store stays small and personal data
is not kept longer than needed.

**Why this priority**: Required before long-running production use, but not for first value.

**Independent Test**: Seed incidents at various ages and states; run the retention sweep; assert
only those past their retention window were removed, and the sweep's own outcome is logged.

**Acceptance Scenarios**:

1. **Given** a resolved or acknowledged incident whose last occurrence is older than 90 days, **When** the daily sweep runs, **Then** the incident and its occurrences are removed.
2. **Given** an **unacknowledged** incident, **When** its last occurrence is older than 90 days but younger than 180 days, **Then** it is kept; beyond 180 days it is removed regardless.
3. **Given** an open incident with thousands of occurrences, **When** occurrences inside it are older than 90 days, **Then** those individual occurrences are removed while the incident's counts, first-seen time and triage state are preserved.

---

### Edge Cases

- **Recording itself fails** (database unreachable, validation error, timeout): the failure is logged with the original error's correlation id and kind; the user's request continues exactly as it would have; the original exception is neither replaced nor lost.
- **Recording is slow**: the user's request never waits on it (see FR-020).
- **Failure storm** (e.g. a provider outage hitting every chat for an hour): the store takes one occurrence per failure without degrading user requests; the admin sees one incident per grouping key (FR-018), not thousands of rows (see FR-022 for the occurrence cap).
- **Unclassified exception** (no provider classification): recorded with kind *Unexpected error*, severity Error, and the exception *type* (not message) in the reason.
- **Reason text contains a secret** (a vendor echoing the key, an `Authorization` header, a connection string, a JWT, a URL with a `key=` query parameter): redacted before storage; raw vendor response bodies are never stored.
- **User, chat, workflow or document later deleted**: the record survives; the link renders as "deleted" rather than breaking.
- **User hard-erased**: their occurrences and the content access events about them are anonymised to "erased user", not deleted; incident counts do not change (FR-029a). A later failure from a new account with the same email address is a different user.
- **The failing user is an administrator**: recorded like anyone else.
- **Client disconnects / user cancels**: a cancellation the user caused is **not** a failure and is not recorded.
- **Password-guessing burst** (e.g. 500 wrong passwords against one account from 30 addresses in 10 minutes): one Warning incident (Access · Sign-in refused · sign-in), 500 occurrences, 1 distinct account, 30 distinct sources — not 500 rows, and no badge change (badge counts Critical only).
- **Sign-in attempts for non-existent accounts**: recorded, without the attempted identifier (FR-012a).
- **A user repeatedly opening a chat they don't own** (e.g. a stale shared link): Access denied, Warning, grouped like any other failure.
- **Same failure, different model** (e.g. one model retired, another fine): separate incidents, since the fix differs.
- **One provider fault across many subjects** (e.g. a rejected embedding key while 40 documents are indexing): one incident **per document**, by design (FR-018). They share one root cause (FR-026a): the badge counts them once, and Acknowledge all / Resolve all acts on all 40 (FR-026b). Fixing the key does not resolve them automatically.
- **Unrelated jobs or steps with the same kind** (e.g. the memory-extraction job and the email job both "failed after retries"): separate incidents, because the operation differs.
- **Concurrent acknowledge/resolve by two admins**: the second action is rejected with a clear "already changed" message rather than silently overwriting.
- **Voice recovery with no open incident** (e.g. failover happened before this feature shipped): the recovery is ignored for grouping purposes; the existing voice failover history still has it.

## Requirements *(mandatory)*

### Functional Requirements

**User-facing calm**

- **FR-001**: A non-administrator MUST never see a failure's classified cause. User-visible failure text is limited to a small fixed set of calm, cause-free sentences, e.g. "Something went wrong and I couldn't finish. Please try again." and, where retrying cannot help, "This isn't available right now. Please try again later." None may mention credentials, keys, quotas, rate limits, billing, a provider or model name, or that "an administrator needs to" act.
- **FR-002**: This applies to every surface a user can read: Problem Details `detail` for non-administrators (including replacing today's `CredentialRejected`, `NotConfigured` and `RateLimited` details), the streamed failure notice, the recorded turn outcome's reason, and anything replayed into a later turn's model context.
- **FR-003**: Administrators keep today's classified detail on their own failed requests (specs/043 FR-010/FR-015a); this feature does not remove it.
- **FR-004**: The mid-stream chat failure path MUST classify provider failures for the admin record — a classified provider failure MUST NOT be recorded as "unexpected error" — while the user-visible and model-visible reason stays generic.
- **FR-005**: Where retry cannot succeed until an administrator acts (e.g. not configured, credential rejected), the user-visible message MUST NOT encourage an immediate retry loop, consistent with specs/068.

**What is recorded**

- **FR-006**: Every **system-side** failure the system surfaces to a user or abandons in the background MUST produce exactly one occurrence, from these engines at minimum: Chat, AI provider (non-chat calls, health checks), Voice (TTS and transcription, including failover), Embeddings/RAG indexing, Document processing, Image generation, Agent tool execution, Workflow step, MCP tool call, Background job (final failure only). In addition, the **Access** engine MUST record refused sign-ins (wrong password, failed two-factor code, sign-in to a locked or suspended account, refused external-login callback) and refused authorisation checks (permission denied, and the ownership refusals already logged as access denials) (clarified 2026-09-25).
- **FR-006a**: The following are **not** recorded: request validation errors caused by the user's input, genuine not-found responses, requests refused by the platform's own rate limiter, ordinary access-token expiry and refresh, and user-cancelled requests. *Validation failed* (FR-008) applies only when the system rejects data it produced itself (e.g. a workflow step's or tool's output).
- **FR-006b**: Access occurrences are **in addition to**, not a replacement for, the immutable security audit trail the constitution already requires for authentication events and authorisation denials; they carry the same correlation id so the two can be cross-referenced. The role and MCP audit logs gain a correlation id for this; other module audit logs are cross-referenced by actor and time. Authorisation denials are already in the role audit trail. No immutable authentication-event trail exists today — that is a pre-existing gap against the constitution's security section, recorded as a follow-up and not built by this feature.
- **FR-007**: Each occurrence MUST capture: occurrence time (UTC); severity; engine; operation (short, e.g. "Chat reply", "Index document"); provider and model where applicable; classified failure kind; sanitised reason; correlation id; the related user, chat, message, workflow/run/step, document, knowledge base, agent or MCP server where applicable; and, for voice, whether it was a failover.
- **FR-008**: Failure kind MUST reuse the existing provider classification (`AiProviderFailureKind`) for provider failures and extend it only with kinds for non-provider failures (at minimum: *Unexpected error*, *Timed out*, *Dependency unreachable*, *Validation failed*, *Job failed after retries*, *Sign-in refused*, *Two-factor refused*, *Account locked*, *Access denied*).
- **FR-009**: Severity MUST be derived from kind and outcome, not chosen ad hoc by each caller:
  - **Critical** — needs administrator action and affects everyone using that provider/engine: Credential rejected, Credential unreadable, Not configured, Quota exhausted, Usage restricted.
  - **Error** — an operation failed for a user with no automatic recovery: Unexpected error, Request invalid, Response not understood, Unavailable/Timed out without successful fallback, workflow/agent step failed, job failed after retries.
  - **Warning** — degraded but served: a failover that succeeded, a rate limit, a transient failure recovered by retry; and every Access-engine occurrence.
- **FR-010**: The correlation id MUST be the same value that appears on the server log line(s) for the same failure and, for request-scoped failures, the same value returned to the client as `traceId`. Background jobs MUST be given a correlation id that is pushed into their log context for the job's duration.

**Sanitisation**

- **FR-011**: The reason MUST be built from the classification and the system's own prose, never copied from a vendor response body. It MUST NOT contain API keys, secrets, bearer/JWT tokens, `Authorization`/cookie header values, connection strings, passwords, or URL query parameters carrying credentials; any such pattern is redacted before storage.
- **FR-012a**: Access occurrences MUST NOT store the attempted password, two-factor code, or any token. The attempted sign-in identifier is stored only as a reference to the matching account; an identifier matching no account is not stored. Each Access occurrence records the source IP address so repeated attempts can be spotted, and each Access incident shows its distinct-source and distinct-account counts. The source IP is personal data: it follows the same retention (FR-028) and erasure (FR-029a) rules.
- **FR-012**: The reason MUST NOT contain user content: no prompt text, reply text, transcript, document content or file names beyond an identifier. Occurrences carry references to user content, never copies of it.
- **FR-013**: The reason MUST be length-bounded (≤ 500 characters) and single-line.

**Actionable guidance and links**

- **FR-014**: Each incident MUST show a suggested corrective action derived from engine + kind, with a link to the admin page that fixes it, deep-linked to the relevant item — e.g. *Credential rejected → "Replace the API key" → Admin › AI providers › ElevenLabs*; *Quota exhausted → "Raise the quota or switch the default model" → vendor console note + Admin › Default models*; *Not configured → Admin › AI capabilities*; *MCP failure → Admin › MCP servers › {server}*; *Job failed → Admin › Jobs*. Kinds with no admin fix (rate limited, transient unavailable) MUST say so ("No action needed unless this persists") rather than invent one.
- **FR-015**: Admin pages targeted by these links MUST accept a selection so the link opens with the relevant provider/server already selected.
- **FR-016**: Related users MUST be shown by display name and email with a link to the Admin › Users list filtered to that user. Chats, workflows and documents MUST be shown as links whose destination depends on the viewer's role (clarified 2026-09-25):
  - **Without** the *View user content* permission (FR-016e) — a **metadata-only** view: title, owner, created/last-activity times, message or step count, and where in the chat/run the failure occurred (turn number and time, or failed step name). For documents: name, type, size, knowledge base, processing status. **No content**: no message text, no step inputs/outputs, no extracted document text.
  - **With** the *View user content* permission — the same view **plus the full read-only content**: the chat transcript with the failed turn highlighted; the workflow run with step inputs, outputs and the failed step highlighted; the document's extracted text.
- **FR-016a**: Full-content access MUST be enforced server-side: the content is never sent to a client whose user lacks *View user content*, not merely hidden in the UI.
- **FR-016b**: Both views MUST be strictly read-only — no sending messages, re-running, retrying, editing, renaming, deleting or downloading originals — and reachable only through a link from an incident that references the item; there is no general "browse any user's chats" entry point.
- **FR-016c**: Every time anyone opens another user's **full content** MUST be written to the immutable audit trail as an access event: who, whose content, which item, which incident it was reached from, and when. Metadata-only views and viewing one's own content are not recorded as access events.
- **FR-016d**: Content that was deleted (soft or hard) after the failure MUST show as "deleted" in both views and MUST NOT be restored into view by this feature.

**The *View user content* permission (Super-User-controlled)**

- **FR-016e**: A new permission, *View user content in failure investigations*, MUST be added to the admin permission catalogue. It is meaningful only together with *View operational failures* (it grants nothing on its own).
- **FR-016f**: The built-in **Super User** role MUST always hold it; it cannot be removed from that role.
- **FR-016g**: Unlike every other permission, it MUST NOT be included automatically in the built-in **Administrator** role's full permission set. A Super User MAY grant it to the Administrator role (which grants it to every Administrator, since each user holds exactly one role) and MAY revoke it again.
- **FR-016h**: Only a Super User MAY add this permission to, or remove it from, any role — on create, update, bulk assign, or the Administrator role toggle. A non-Super-User attempt MUST be refused server-side with a clear message, and the permission picker MUST show it disabled with "Only a Super User can grant this" for them.
- **FR-016i**: A non-Super-User editing a role that already holds the permission MUST NOT be able to strip it, whether deliberately or as a side effect of saving other changes — the permission is preserved unless a Super User removes it.
- **FR-016j**: Only a Super User MAY assign a role that holds this permission to a user, or remove such a role from a user (mirrors today's rule that only a Super User may assign privileged roles). Deleting such a role likewise requires a Super User.
- **FR-016k**: Every grant and revoke of this permission — on a role, or implicitly by assigning/removing such a role — MUST be recorded in the existing role audit trail with the acting Super User.
- **FR-017**: Each incident MUST show the provider's current health (from the existing provider health state) beside the failure, so the admin can see whether it has already recovered.

**Grouping**

- **FR-018**: Occurrences MUST be grouped into incidents keyed on **engine + provider + model + failure kind + operation + subject** (clarified 2026-09-25), where any part that does not apply is empty:
  - **Operation** — what was being done: for provider calls the call type (chat reply, embedding, image generation, text-to-speech, transcription, health check); otherwise the job type, workflow step type, agent tool, or MCP server + tool.
  - **Subject** — the specific workflow (definition, not run), agent or document involved. Chats, users, workflow runs and background-job instances are **not** part of the key; they are recorded on each occurrence instead.

  A new occurrence joins the open (unresolved) incident for its key; if none exists, a new incident opens. After resolution, recurrences open a new incident marked as a recurrence.
- **FR-019**: Each incident MUST expose: first seen, last seen, occurrence count, distinct affected-user count, highest severity seen, recovery count (voice), and its triage state. The occurrences within an incident MUST be viewable, newest first, paginated.

**Reliability of recording**

- **FR-020**: Recording MUST never fail, block or noticeably slow the user's request or the job being recorded. The user-visible latency added by recording MUST be negligible (see SC-004); persisting MAY complete after the response.
- **FR-021**: If recording fails, the failure to record MUST be written to the server log with the original failure's correlation id, engine and kind, and the original error MUST still reach the user/caller exactly as it would have without this feature. Recording failures MUST NOT recurse into further recording attempts.
- **FR-022**: During a storm, per-incident stored occurrences MAY be capped (default: detailed occurrences kept for the first 1,000 per incident; beyond that, counts, last-seen and affected-user counts keep updating but individual occurrence rows are not stored). The cap MUST be visible on the incident ("showing 1,000 of 12,408").

**Admin page and permissions**

- **FR-023**: A new admin page *Operational failures* MUST list incidents with filters for time range (presets: last hour, 24 h, 7 days, 30 days, custom), severity, engine, provider, user and failure kind; default view is open (not resolved) incidents, last 7 days, newest last-seen first; paginated.
- **FR-024**: Administrators with manage permission MUST be able to acknowledge and resolve incidents (resolve with an optional note ≤ 500 characters), and reopen a resolved incident. Each transition records who and when. Concurrent conflicting transitions MUST be rejected with a visible message.
- **FR-025**: Two new admin permissions MUST be added to the admin permission catalogue: *View operational failures* and *Manage operational failures*. The page, nav entry, badge and all endpoints MUST require them; every endpoint MUST enforce them server-side.
- **FR-026**: The admin navigation entry MUST show a badge with the number of **distinct root causes** (FR-026a) that have at least one unacknowledged Critical incident, refreshed at least every 60 seconds while an admin page is open and immediately after the admin acknowledges/resolves. No badge when the count is zero.
- **FR-026a**: An incident's **root cause** is (clarified 2026-09-25): for provider-classified kinds, provider + model + failure kind, regardless of engine, operation or subject; for all other kinds, the incident's grouping key without its subject (engine + kind + operation). Incidents sharing a root cause stay separate incidents (FR-018) but are linked.
- **FR-026b**: Each incident MUST show how many other open incidents share its root cause ("39 other incidents share this cause"), with a way to list them, and — for holders of *Manage operational failures* — **Acknowledge all** and **Resolve all** actions that apply the transition (and resolve note) to every open incident with that root cause in one step. Each affected incident records the transition individually. Incidents already in the target state are skipped; if some cannot be transitioned (e.g. changed concurrently), the admin sees how many succeeded and which failed.
- **FR-027**: Every failure on this page itself (loading, filtering, acknowledging) MUST surface visibly to the administrator (constitution §2.VIII).

**Retention**

- **FR-028**: A daily retention sweep MUST remove: acknowledged or resolved incidents (with their occurrences) whose last occurrence is older than **90 days**; unacknowledged incidents whose last occurrence is older than **180 days**; and individual occurrences older than 90 days inside incidents that are kept. The sweep's outcome (counts removed, or failure) MUST be logged, and a failed sweep is itself recorded as a Background job failure.
- **FR-029**: Retention periods MUST be configurable, with the values above as defaults.
- **FR-029a**: When a user is **hard-erased** (the explicit, audited erasure command), every occurrence referencing that user MUST be anonymised in the same operation: the user, chat, message and document references are cleared and the occurrence shows "erased user". Occurrence counts and already-computed distinct-affected-user counts are unchanged; the occurrence's time, engine, provider/model, kind, sanitised reason and correlation id are kept. User content access events (FR-016c) about that user's content are anonymised the same way and are **never deleted**. A **soft-deleted** user's records are left intact and their links show "deleted user" (FR-016d). If anonymisation fails, the erasure MUST fail visibly rather than complete with identifying records left behind (clarified 2026-09-25).

**Out of scope (future extensions)**

- **FR-030**: Emailing, paging or push-notifying administrators is out of scope; the design MUST allow a future notifier to subscribe to "new Critical incident opened" without changing the recording callers.
- Also out of scope: showing end users a support reference code; per-tenant admin scoping (single-tenant today); a public status page.

### Key Entities *(include if feature involves data)*

- **Operational failure incident**: A group of occurrences of the same failure (engine + provider + model + kind + operation + subject, FR-018). Carries first/last seen, counts (occurrences, distinct users, recoveries), highest severity, suggested action, and triage state (Open → Acknowledged → Resolved, with who/when/note; reopenable). References the incident it recurs from, if any. The only mutable part of the trail.
- **Operational failure occurrence**: One failure event. Append-only. Carries time, severity, engine, operation, provider/model, kind, sanitised reason, correlation id, and references (never copies) to the user, chat/message, workflow/run/step, document, knowledge base, agent, MCP server or job involved.
- **Failure kind**: The classification vocabulary — the existing provider kinds plus a small set of non-provider kinds (FR-008).
- **Corrective action**: A fixed mapping from engine + kind to a short instruction and the admin destination that fixes it (FR-014). Owned by code, not admin-editable.
- **User content access event**: An immutable audit entry recording that someone holding *View user content* opened another user's full chat, workflow run or document content from an incident (FR-016c).
- **View user content permission**: A catalogue permission that is Super-User-controlled: always held by Super User, not automatically held by Administrator, grantable/revocable only by a Super User (FR-016e–k). Never mutated or purged by this feature's retention sweep.
- **Existing trails referenced, not copied**: provider health state, voice failover history, per-module audit logs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 0 user-visible failure messages (across chat, voice, documents, workflows, agents, image generation) name a credential, key, quota, rate limit, billing condition, provider, model, or "an administrator", verified by a test that forces each failure kind as a non-admin user.
- **SC-002**: 100% of forced failures in the per-engine test matrix (FR-006) produce exactly one occurrence with the correct engine, kind, severity and correlation id, and that correlation id is found on the server log line for the same failure.
- **SC-003**: The 2026-09-22 replay (7 ElevenLabs 401 failover/recover pairs from one user within 70 s) produces exactly 1 incident with 7 occurrences, 7 recoveries and 1 affected user, and raises the badge by exactly 1.
- **SC-004**: With recording enabled, a failed request's user-visible response time is within 5% (or 20 ms, whichever is larger) of the same failure with recording disabled; with the store deliberately unavailable, 100% of forced failures still reach the user with the same calm message and the recording failure appears in the server log.
- **SC-005**: An administrator can go from the nav badge to the admin page that fixes a Credential-rejected incident in at most 3 clicks.
- **SC-006**: An automated scan of stored reasons after the full test matrix — including vendor responses seeded with keys, bearer tokens and `Authorization` headers — finds 0 secrets and 0 raw vendor response bodies.
- **SC-007**: The list view, filtered or not, returns within 2 seconds for 100,000 stored occurrences.
- **SC-008**: After a retention sweep, 0 incidents remain past their retention window and 100% of in-window incidents remain.
- **SC-009**: 100% of full-content openings of another user's item in the test matrix produce exactly one access audit entry; 0 content-view responses to a viewer without *View user content* contain message text, step inputs/outputs or extracted document text; and 0 write actions succeed from either view.
- **SC-010**: 0 of the non-Super-User grant paths in US1b (create, edit, bulk delete, bulk assign, Administrator-role toggle, role assignment, role deletion, strip-on-save) succeed, and 100% of Super User grants/revokes appear in the role audit trail.
- **SC-011**: A single provider fault affecting N subjects raises the badge by exactly 1 and can be fully resolved in one action, for N up to 1,000.
- **SC-012**: After hard-erasing a user who has occurrences and content access events, 0 records identify that user or their chats/documents, and every affected incident's occurrence and affected-user counts are unchanged.
- **SC-013**: In the test matrix, 100% of refused sign-ins and authorisation checks produce one Warning Access occurrence; 0 validation errors, genuine not-found responses, own-rate-limit refusals or token refreshes produce one; 0 stored Access occurrences contain a password, two-factor code, token, or an identifier that matches no account.

## Assumptions

- Single tenant: every administrator with the new permission sees every user's failures.
- "Administrator" for FR-003 means today's privileged-user role check used by `ProblemDetailsMiddleware.IsAdministrator`; access to the new page uses the new permissions (FR-025), so a custom role can be granted it.
- User-cancelled requests and client disconnects are not failures (they are not "surfaced to the user").
- Background-job retries that eventually succeed are not recorded; only the final failure is.
- Voice failovers that succeeded are Warning unless the kind is itself Critical (a rejected ElevenLabs key is Critical even though the fallback served the user — the admin still has to fix it).
- The existing provider health columns, voice failover table and module audit logs keep their current behaviour and consumers; this feature adds to them, it does not migrate them.
- Retention defaults (90 / 180 days) follow the existing 90-day precedent (`PasswordResetTokenCleanupJob`); occurrences hold user references, not content, so a longer window adds little privacy risk but the shorter default is chosen for store size.
- No new vendor dependency; background scheduling reuses the existing job scheduler; migrations follow the repo's existing conventions.
- "Super User" means the existing built-in role (`RoleName`, guarded by `SuperUserSafeguard`); "Administrator" the existing built-in Administrator role. Both already pass today's `IsAdministrator` check for FR-003.
- Today both built-in roles resolve to the full permission catalogue automatically (`EffectivePermissionResolver` → `PermissionSet.Full`), and each user holds exactly one role. FR-016g therefore requires the Administrator role's automatic set to exclude *View user content* unless a Super User has granted it — a deliberate exception to the "built-in = everything" rule, and the only one. Existing Super-User-only precedent: `AssignRoleCommandHandler`'s `PrivilegedRoleRequiresSuperUser`.
- Content owners are not notified when a Super User views their content. The Terms / privacy disclosure should state that authorised staff may view conversation content to diagnose failures — **follow-up for the open legal review of `/terms` (specs/070)**, not implemented here.
- Constitution §2.VIII is satisfied by *capturing* every failure for diagnosability; hiding the cause from end users is compliant.
