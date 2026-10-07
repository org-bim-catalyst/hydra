# API_GUIDELINES.md

> **Project:** Ask Lucy AI Workspace
>
> **Version:** 2.0
>
> **Architecture:** REST API + Streaming + Clean Architecture
>
> **Framework:** ASP.NET Core (.NET 10)
>
> **Last Updated:** October 2026 (added notification hub, announcements and localization endpoints, specs/067)

---

# 1. Purpose

This document defines the API standards for the Ask Lucy platform.

The goals are:

* Consistent API design
* Predictable request/response contracts
* Strong typing
* Versioning
* Security
* Extensibility
* OpenAPI compatibility
* Easy SDK generation

Every endpoint in the application must follow these standards.

---

# 2. API Architecture

The application exposes one public API.

```
React SPA
        │
        ▼
REST API
        │
        ▼
Application Layer
        │
        ▼
Domain
```

Long-running AI responses are streamed.

---

# 3. API Versioning

Always version the API.

```
/api/v1
```

Examples

```
/api/v1/auth/login

/api/v1/chats

/api/v1/messages

/api/v1/models
```

Never expose unversioned endpoints.

Future versions:

```
v2

v3
```

must run alongside previous versions when necessary.

---

# 4. REST Naming

Use nouns.

Correct

```
GET /chats

GET /messages

POST /knowledge-bases
```

Avoid verbs.

Incorrect

```
/createChat

/deleteChat

/sendPrompt
```

---

# 5. HTTP Methods

GET

Read data

POST

Create resources

PUT

Replace resources

PATCH

Partial update

DELETE

Soft delete

---

# 6. Resource Naming

Plural nouns.

```
users

conversations

messages

documents

knowledge-bases

providers

models

agents

subscriptions
```

---

# 7. URI Design

Examples

```
GET /api/v1/conversations

GET /api/v1/conversations/{id}

POST /api/v1/conversations

PATCH /api/v1/conversations/{id}

DELETE /api/v1/conversations/{id}
```

Nested resources

```
GET /api/v1/conversations/{id}/messages

POST /api/v1/conversations/{id}/messages

GET /api/v1/knowledge-bases/{id}/documents

POST /api/v1/documents/{id}/reindex
```

Keep nesting shallow (generally no more than two levels).

---

# 8. Standard Response Format

Successful responses should follow a consistent envelope.

```json
{
  "success": true,
  "data": {},
  "meta": {
    "timestamp": "2026-07-27T12:00:00Z",
    "correlationId": "..."
  }
}
```

Errors use RFC 9457 Problem Details.

Example

```json
{
  "type": "https://asklucy.io/problems/validation",
  "title": "Validation Failed",
  "status": 400,
  "detail": "Temperature must be between 0 and 2.",
  "instance": "/api/v1/settings",
  "correlationId": "..."
}
```

Never expose stack traces.

---

# 9. Pagination

Use cursor-based pagination for large collections.

Request

```
GET /messages?cursor=abc123&pageSize=50
```

Response

```json
{
  "items": [],
  "nextCursor": "def456",
  "hasMore": true
}
```

For administrative grids, offset pagination may be used.

```
?page=1&pageSize=25
```

Maximum page size: **100**.

---

# 10. Filtering

Use query parameters.

Example

```
GET /documents?status=indexed
```

Multiple filters

```
GET /messages?role=user&provider=openai
```

Date filters

```
createdAfter

createdBefore
```

---

# 11. Sorting

Example

```
GET /messages?sort=createdAt

GET /messages?sort=-createdAt
```

"-" indicates descending.

---

# 12. Searching

Example

```
GET /conversations/search?q=revit
```

or

```
GET /conversations?q=revit
```

Full-text search should be delegated to the appropriate backend implementation.

---

# 13. Authentication

JWT Bearer Token

```
Authorization:

Bearer <token>
```

Refresh Tokens

```
POST /auth/refresh
```

Logout

```
POST /auth/logout
```

All protected endpoints require authentication unless explicitly marked public.

## Password recovery and management (specs/058-password-recovery)

| Endpoint | Auth | Success | Notes |
|---|---|---|---|
| `POST /auth/password/forgot` | anonymous | `202 Accepted` | Body and latency are identical for every input — an account that exists, one that does not, an unconfirmed address and a locked-out account are indistinguishable. Rate limited by IP, never by email address. |
| `POST /auth/password/reset/validate` | anonymous | `204 No Content` | `{ userId, token }`. Asks whether a link is still redeemable **without consuming it**, so the reset page can say "this link is no longer valid" on load rather than after the user has typed and confirmed a new password. `POST`, not `GET`: a token in a query string is recorded in full by proxies and server logs. |
| `POST /auth/password/reset` | anonymous | `204 No Content` | `{ userId, token, newPassword }`. Never returns a session, so two-factor enrolment still applies on the next sign-in. |
| `POST /auth/change-password` | bearer | `204 No Content` | `{ currentPassword?, newPassword }`. The acting session is identified by the httpOnly refresh cookie, never by the body. |
| `GET /auth/password/status` | bearer | `200 OK` | `{ hasPassword }` — tells the client whether to ask for a current password. |

Failure shapes:

* Reset rejection returns **one** `400` — *"Reset link is no longer valid"* — for all six causes
  (unknown, expired, consumed, superseded, wrong user, tampered). The specific cause is logged, not
  returned. Distinguishing them to the caller would reintroduce the oracle the 202 above closes.
* A password that fails the policy returns a `validation-failed` `400` whose `errors` bag carries
  one message per broken rule, so the client can list them rather than guess. A policy failure does
  **not** consume the reset link.
* Change-password failures return distinct titles — *"Current password is incorrect"*, *"Current
  password is required"*, *"New password must be different"* — because the caller is already
  authenticated and nothing is leaked by being specific.
* `password/reset/validate` returns that same single `400` for a link that is no longer redeemable,
  with the detail *"This password reset link has expired or has already been used. Request a new one
  to continue."* It reports on the link, never on the account behind it.

## Sign-in failure disclosure

`POST /auth/login` distinguishes three refusals by status code, so the client can say what is
actually wrong instead of showing "invalid username or password" for every case:

| Status | Title | Client behaviour |
|---|---|---|
| `401 Unauthorized` | `Invalid credentials` | Generic message. Covers an unknown address and a wrong password alike — these two stay indistinguishable. |
| `403 Forbidden` | `Email not confirmed` | Tells the user to check inbox/junk, and offers `POST /auth/confirm-email/resend`. |
| `423 Locked` | `Account locked out` | Names the lockout and opens the contact-an-administrator form backed by `POST /auth/account-support`. |

The `403` and `423` are a deliberate, bounded enumeration trade-off — see
[ADR 0010](adr/0010-naming-sign-in-refusals.md). They are reachable only by someone who already holds
the correct password, which is why the anonymous flows above (`password/forgot`,
`confirm-email/resend`) stay uniformly silent.

## Account recovery helpers

| Endpoint | Auth | Success | Notes |
|---|---|---|---|
| `POST /auth/confirm-email/resend` | anonymous | `202 Accepted` | `{ email }`. Re-issues a confirmation link. Neutral response on exactly the same terms as `password/forgot` — identical body and latency whether or not the address exists. |
| `POST /auth/account-support` | anonymous | `202 Accepted` | `{ email, message }`. Relays a locked-out user's message to the support mailbox. The destination is server-side configuration and never appears in the contract, so the sign-in page can offer "contact an administrator" without publishing an address. |

Both carry `[EnableRateLimiting("auth-endpoints")]`, like every other anonymous auth endpoint.

---

# 14. Authorization

Role-based authorization.

Examples

```
Admin

User

Support
```

Future

Policy-based authorization.

Resource ownership must always be enforced.

## Reading for everyone, changing for administrators (specs/080)

Some settings are read by every signed-in user but changed only by administrators. The presence sphere's look is one: `GET /api/v1/appearance/presence-sphere` needs only authentication and returns the defaults when nothing has been saved; `PUT` on the same route needs the `admin.appearance.manage` permission and replaces all three values (dot size multiplier 0.25 to 2.0, fill percent 40 to 95, zoom on/off). An out-of-range value is a 400 Problem Details naming the allowed range, and nothing is saved. Contract: `specs/080-presence-sphere-admin-controls/contracts/presence-sphere-api.md`.

---

# 15. Idempotency

Support idempotency for non-idempotent operations that may be retried by clients.

Examples

```
POST /payments

POST /subscriptions
```

Header

```
Idempotency-Key
```

The server should safely return the original result when the same key is reused within the configured window.

---

# 16. Validation

Validation occurs before business logic.

Use

FluentValidation

Return

400

with Problem Details.

---

# 17. Correlation IDs

Every request receives a correlation ID.

Header

```
X-Correlation-ID
```

Returned in:

* Response headers
* Logs
* Problem Details
* AI provider requests

---

# 18. Streaming Responses

AI responses should stream tokens.

Preferred transport:

Server-Sent Events (SSE)

Fallback:

SignalR

SSE endpoint example

```
POST /api/v1/chat/stream
```

Events

```
message

token

done

error
```

The frontend should render streamed tokens progressively.

---

# 19. File Upload

Use multipart/form-data.

Example

```
POST /documents/upload
```

Server returns

```json
{
  "fileId": "...",
  "status": "Uploaded"
}
```

Maximum upload size is configurable.

Virus scanning can be added in future.

---

# 20. File Download

Never expose physical paths.

Use signed URLs.

Example

```
GET /files/{id}/download
```

The server validates access before generating a temporary download URL.

---

# 21. AI Chat Endpoints

> **As shipped** (SPEC-000 + SPEC-002, `specs/002-chat-history-management/contracts/chats-api.md`):
> the resource is `/api/v1/chats`, not `/conversations` (SPEC-000 established this first;
> SPEC-002 extended it rather than renaming — research.md Topic 1). Non-CRUD state changes
> are sub-resource actions (`/actions/{verb}`) per constitution §6, not separate top-level
> resources. There is no message-level `/regenerate`; streaming goes through `/ai/chat`.

Conversations

```
GET /chats                          (search/filter/sort/paginate — FR-019–FR-024)

POST /chats

PATCH /chats/{id}                   (rename)

DELETE /chats/{id}                  (soft delete → Recently Deleted)

POST /chats/{id}/actions/archive
POST /chats/{id}/actions/restore
POST /chats/{id}/actions/pin
POST /chats/{id}/actions/unpin
POST /chats/{id}/actions/favorite
POST /chats/{id}/actions/unfavorite
POST /chats/{id}/actions/duplicate
POST /chats/{id}/actions/clear      (requires { "confirm": true })
DELETE /chats/{id}/actions/purge    (permanent delete — requires { "confirm": true })

GET /chats/{id}/export
```

Messages

```
GET /chats/{id}/messages            (cursor-paginated)

POST /ai/chat                       (SSE streaming; persists via AppendMessageCommand)
```

`POST /ai/chat` carries at most one dispatch instruction alongside the message list. Both are
optional and **mutually exclusive** — a request carrying both is a 400, because there is no
defensible order between them and picking one silently is the guessing these features exist to
remove:

| Member | Meaning |
|---|---|
| `selectedAction` | The user picked a row on the newest unanswered offer card (specs/045) |
| `retry` | `{ "failedMessageId": "<guid>" }` — re-run the action that turn recorded as failed (specs/068) |

`retry` carries **a message id and nothing else**. The capability, its arguments and its target are
read server-side from that turn's recorded outcome, never from the request. Accepting them from the
client would make this a general capability-invocation route that merely looks like a retry. It
resolves to Problem Details before the stream opens: 400 when the id names no recorded failure, 409
when the target no longer resolves, 404 (never 403) when the id belongs to another user's chat.

Trailing SSE events are sentinel-prefixed `data:` lines, each a single JSON payload:
`__RAG__`, `__MEMORY__`, `__LOCATION__`, `__ZOOM__`, `__ACTIONS__`, `__SITE_BOUNDARY__`,
`__VIEWER_CONTENT__`, `__SOLAR_ANALYSIS__`, `__MESSAGE_BREAK__`, `__TURN_OUTCOME__`.

`__TURN_OUTCOME__` (specs/068) reports what the turn actually did — a verdict plus one entry per
attempted action, each with its own `succeeded` and `failureReason`. There is no aggregate pass/fail
flag by design: a turn can succeed at one part and fail at another, and collapsing that into one
boolean is how a half-failure becomes a claim of success. Server-resolved arguments are never
included. The same payload is returned on the persisted message when the conversation is reopened;
its **absence** means the outcome is unknown, which is never read as success.

---

# 22. AI Provider Endpoints

```
GET /providers

GET /providers/{id}

GET /models

GET /models/{id}
```

User settings

```
GET /settings/ai

PUT /settings/ai
```

---

# 23. Knowledge Base Endpoints

> **As shipped** (SPEC-014, `specs/014-knowledge-base-management/contracts/`): organization/
> lifecycle only — no embedding/indexing/RAG-retrieval endpoints exist yet (a future spec), so
> there is no `/documents/{id}/reindex`. Categories/tags are a separate sibling controller
> (`KnowledgeBaseTaxonomyController`), not nested under one knowledge base, since they're
> caller-scoped lists referenced *by* many knowledge bases, not sub-resources of one.

```
GET /knowledge-bases                          (search/filter/sort/paginate — FR-022–FR-024)
GET /knowledge-bases/dashboard-summary        (cached per-user, 60s TTL — FR-029)

POST /knowledge-bases
GET /knowledge-bases/{id}
PATCH /knowledge-bases/{id}                   (full-replace update — FR-003)
DELETE /knowledge-bases/{id}                  (soft delete → Deleted view)

POST /knowledge-bases/{id}/actions/activate   (Draft → Active)
POST /knowledge-bases/{id}/actions/archive
POST /knowledge-bases/{id}/actions/restore
POST /knowledge-bases/{id}/actions/favorite
POST /knowledge-bases/{id}/actions/unfavorite
POST /knowledge-bases/{id}/actions/pin
POST /knowledge-bases/{id}/actions/unpin
POST /knowledge-bases/{id}/actions/duplicate  (deep copy — independent folder tree + files)
DELETE /knowledge-bases/{id}/actions/purge    (permanent delete — requires { "confirm": true })

GET /knowledge-bases/{id}/export              (JSON metadata download)
```

Folders

```
GET /knowledge-bases/{knowledgeBaseId}/folders                              (full tree)
POST /knowledge-bases/{knowledgeBaseId}/folders
PATCH /knowledge-bases/{knowledgeBaseId}/folders/{folderId}                 (rename)
POST /knowledge-bases/{knowledgeBaseId}/folders/{folderId}/actions/move
DELETE /knowledge-bases/{knowledgeBaseId}/folders/{folderId}                (requires { "confirm": true } if non-empty)
```

Documents

```
GET /knowledge-bases/{knowledgeBaseId}/documents                            (?folderId=... optional)
POST /knowledge-bases/{knowledgeBaseId}/documents                           (multipart/form-data)
POST /knowledge-bases/{knowledgeBaseId}/documents/{documentId}/actions/move
DELETE /knowledge-bases/{knowledgeBaseId}/documents/{documentId}
```

Categories & tags (`KnowledgeBaseTaxonomyController`)

```
GET /knowledge-bases/categories               (predefined + caller's own custom categories)
POST /knowledge-bases/categories
DELETE /knowledge-bases/categories/{id}        (referencing knowledge bases fall back to Uncategorized)

GET /knowledge-bases/tags                     (?q=prefix optional)
```

---

# 24. Document Intelligence Endpoints

> **As shipped** (SPEC-015, `specs/015-document-intelligence-pipeline/contracts/`): a
> user's own document workspace — upload, processing, folders, versions, search, dashboard,
> and notifications. Distinct from the Knowledge Base Engine's `/knowledge-bases/{id}/documents`
> (§23), which are files scoped to one knowledge base for future RAG retrieval; these are a
> user's general document library with its own processing pipeline and no KB attachment.

```
GET /documents                                          (?view=Active|Archived|Deleted, cursor-paginated — FR-034)
GET /documents/{id}
PATCH /documents/{id}                                    (rename)
DELETE /documents/{id}
POST /documents/{id}/actions/archive
POST /documents/{id}/actions/restore
POST /documents/{id}/actions/duplicate
GET /documents/{id}/download                             (mints a signed URL)
GET /documents/{id}/preview                              (FR-043/FR-044 — Unavailable is a valid, non-error result)
```

Upload (single-request and resumable/chunked)

```
POST /documents/uploads/simple                            (multipart/form-data — small files)
POST /documents/uploads                                   (?documentId=... to target a version replacement — US5)
PUT /documents/uploads/{uploadSessionId}/chunks/{chunkIndex}
POST /documents/uploads/{uploadSessionId}/complete
POST /documents/uploads/{uploadSessionId}/complete-as-version
POST /documents/uploads/{uploadSessionId}/complete-as-new
DELETE /documents/uploads/{uploadSessionId}
```

Processing

```
GET /documents/{id}/processing
GET /documents/{id}/processing/history
POST /documents/{id}/processing/actions/retry
```

Metadata, classification, tags

```
PATCH /documents/{id}/metadata                            (optimistic concurrency via rowVersion)
PUT /documents/{id}/classification
GET /documents/tags
POST /documents/{id}/tags
DELETE /documents/{id}/tags/{name}
GET /documents/categories
```

Folders

```
GET /documents/folders/tree
POST /documents/folders
PATCH /documents/folders/{id}                             (rename)
PATCH /documents/folders/{id}/parent                      (move)
DELETE /documents/folders/{id}                            (?onContainedDocuments=MoveToParent|ArchiveAll|DeleteAll)
PATCH /documents/{id}/folder                              (move a document)
```

Versions (US5)

```
GET /documents/{id}/versions
GET /documents/{id}/versions/compare                      (?fromVersionId=...&toVersionId=...)
POST /documents/{id}/versions                              (replace — new version from an upload session)
POST /documents/{id}/versions/{versionId}/actions/restore
```

Dashboard & notifications (US6)

```
GET /documents/dashboard                                  (per-user, 5s poll fallback alongside SignalR push)
GET /documents/dashboard/organization                     (admin-only)
GET /documents/notifications                              (?unreadOnly=..., cursor-paginated)
POST /documents/notifications/{id}/actions/mark-read
```

---

# 25. Prompt Library

```
GET /prompts

POST /prompts

PATCH /prompts/{id}

DELETE /prompts/{id}
```

---

# 26. Agent Endpoints

```
GET /agents

POST /agents

PATCH /agents/{id}

DELETE /agents/{id}

POST /agents/{id}/execute
```

---

# 27. MCP Endpoints

Shipped in specs/021-mcp-integration as two controllers — see `specs/021-mcp-integration/contracts/mcp-api.md`
for full request/response shapes. There is no "execute a tool" endpoint: an MCP tool is invoked
only as part of an agent execution, through the existing `POST /agents/{id}/execute`-driven
runtime (§26), never a direct HTTP call.

```
# McpServersController — Administrator/Super User only, api/v1/admin/mcp/servers

POST   /admin/mcp/servers
GET    /admin/mcp/servers                                          (cursor-paginated; status/transport/enabled filters)
GET    /admin/mcp/servers/{id}
PUT    /admin/mcp/servers/{id}
DELETE /admin/mcp/servers/{id}                                     (422 if any agent tool still references it)
POST   /admin/mcp/servers/{id}/actions/enable
POST   /admin/mcp/servers/{id}/actions/disable
POST   /admin/mcp/servers/{id}/actions/test-connection
POST   /admin/mcp/servers/{id}/actions/refresh-capabilities
POST   /admin/mcp/servers/{id}/actions/rotate-credential            (write-only; never echoes the credential back)
GET    /admin/mcp/servers/{id}/health
GET    /admin/mcp/servers/{id}/references                          (which agents/tools currently use it)
GET    /admin/mcp/servers/{id}/tools                                (admin view — includes PendingReview/Deactivated)
GET    /admin/mcp/servers/{id}/audit-log                            (cursor-paginated)
POST   /admin/mcp/servers/{id}/tools/{toolId}/actions/activate      (the mandatory admin review gate)
POST   /admin/mcp/servers/{id}/tools/{toolId}/actions/deactivate

# McpCatalogController — any authenticated user, api/v1/mcp/catalog

GET    /mcp/catalog/tools                                          (only Active+Available+enabled+healthy — exactly what an agent could call)
GET    /mcp/catalog/tools/{namespacedName}                          (full detail — schema, capabilities, version)
GET    /mcp/catalog/resources
GET    /mcp/catalog/prompts                                         (MCP-sourced only; merge with native prompts client-side)
POST   /mcp/catalog/prompts/{namespacedName}/actions/duplicate      (201 — creates an independent, editable native Prompt)
```

Enabling/disabling an MCP tool for a specific agent is not a separate endpoint either — it's the
existing `PUT /agents/{id}` body (§26) accepting `mcp:{serverId}:{toolName}`-namespaced strings in
its `tools` array alongside native tool names, zero schema change.

---

# 28. User Profile

```
GET /profile

PATCH /profile

POST /profile/avatar

DELETE /profile/avatar
```

---

# 29. Authentication Endpoints

```
POST /auth/register

POST /auth/login

POST /auth/login/2fa

POST /auth/logout

POST /auth/refresh

GET  /auth/session

POST /auth/confirm-email

POST /auth/confirm-email/resend

POST /auth/account-support

POST /auth/password/forgot

POST /auth/password/reset/validate

POST /auth/password/reset

POST /auth/change-password

GET  /auth/password/status

POST /auth/change-email/request

POST /auth/change-email/confirm

POST /auth/2fa/enable

POST /auth/2fa/disable

POST /auth/2fa/recovery-codes

GET  /auth/external/{provider}/challenge

GET  /auth/external/{provider}/link

POST /auth/external/link-ticket

POST /auth/external/complete

GET  /auth/external-logins

DELETE /auth/external-logins/{provider}/{providerKey}
```

---

# 30. Billing

```
GET /subscriptions

POST /subscriptions

GET /usage

GET /payments
```

---

# 31. Administration

```
GET /admin/users

GET /admin/logs

GET /admin/system

GET /admin/providers

GET /admin/feature-flags

# AdminHangfireController — Administrator/Super User only, api/v1/admin/hangfire
POST /admin/hangfire/session                                       (specs/060-hangfire-dashboard-access; mints the /hangfire-scoped cookie, no request body, 204 No Content)

# AdminCustomModelsController — api/v1/admin/custom-models (specs/072); GETs need admin.custom-models.view, the rest .manage
GET    /admin/custom-models?page=&pageSize=                          (paged CustomModelSummaryDto, newest first)
GET    /admin/custom-models/{id}?overwrittenPage=&overwrittenPageSize= (summary plus the paged overwritten-file report)
GET    /admin/custom-models/deployment-status                        (isConfigured, transport, size cap, allowed destination prefixes; never host, root path or credentials)
POST   /admin/custom-models/source-preview                           (parses a Hugging Face URL, no outbound call; returns repository, revision, derived name and whether it is free)
POST   /admin/custom-models                                          (202 Accepted; saves the record and queues the job before any transfer starts)
POST   /admin/custom-models/{id}/actions/cancel                      (202 Accepted; a queued deployment cancels at once, a running one reports Cancelled over the hub)
PUT    /admin/custom-models/{id}/availability                        (Available/Unavailable; completed models only; 409 if another model of the same repository is Available)
DELETE /admin/custom-models/{id}                                     (204; soft-deletes a failed or cancelled model; files already on the target are left in place)

# SignalR hub /hubs/custom-model-deployments — admin.custom-models.view; pushes per-file and overall progress and state changes
```

All administrative endpoints require elevated authorization.

## Notifications, announcements and localization (specs/067)

Contracts: `specs/067-notifications-communication-hub/contracts/`. Every controller below is marked `[LocalizedSurface]`, so Problem Details `title`, `detail` and `errors` follow the caller's effective language (see Localization below). Notification ids that belong to another user, or that the owner deleted, return **404**, never 403.

### User endpoints

All require a signed-in user and the `notifications-endpoints` rate-limit policy (per user, 120 per minute).

```
# NotificationsController — api/v1/notifications
GET    /notifications?cursor=&limit=&category=&state=         (keyset page, newest first; limit 1-100, default 25; category may repeat; state all|unread|read)
GET    /notifications/unread-count                            ({ count })
GET    /notifications/{id}                                    (the item plus non-sensitive metadata; 404 if absent, not owned or deleted)
POST   /notifications/{id}/actions/mark-read                  (204, idempotent)
POST   /notifications/actions/mark-all-read                   (optional { category }; 200 { updated })
DELETE /notifications/{id}                                    (204; owner soft delete, any category)

# NotificationPreferencesController — api/v1/users/me/notification-preferences
GET    /users/me/notification-preferences                     (effective state: catalogue defaults merged with overrides, locked = true on mandatory pairs)
PUT    /users/me/notification-preferences                     ({ changes: [{ category, channel, enabled }] }, 1-40, atomic; 422 if any change disables a mandatory pair)

# UserLocalizationController — api/v1/users/me/localization
GET    /users/me/localization                                 (localizationEnabled, supportedLanguages, preferredLanguage, effectiveLanguage, direction)
PUT    /users/me/localization                                 ({ preferredLanguage }; 422 when localization is off or the code is unsupported)
```

`title` and `message` of a notification are **plain text**: a client must never render them as HTML. `action` is `null` for a type without a route, and `relatedItem.available` is `false` once the item was deleted, so the client shows "no longer available" instead of navigating. A notification's text keeps the language it was produced in; changing the language never retranslates it.

### Admin endpoints

All use the `admin-endpoints` policy (60 per minute per user). `V` is `admin.notifications.view`, `M` is `admin.notifications.manage`; the server checks each endpoint on its own.

```
# AdminNotificationsController — api/v1/admin/notifications
GET  /admin/notifications/statistics?from=&to=                                  (V; default last 7 days, at most 90; hourly buckets up to 2 days, daily after)
GET  /admin/notifications/channels                                              (V; enabled, provider, health Healthy|Degraded|Unhealthy, safe detail)
GET  /admin/notifications/deliveries?status=&channel=&category=&type=&from=&to=&cursor=&limit=   (V; default Failed and DeadLettered; limit default 50, max 200)
GET  /admin/notifications/deliveries/{deliveryId}                               (V; masked recipient, no body for Security and Account)
POST /admin/notifications/deliveries/{deliveryId}/actions/retry                 (M; 202 { deliveryId, status: Pending }; 409 with reason)
POST /admin/notifications/deliveries/actions/retry                              (M; { deliveryIds } 1-200, or { filter } matching at most 1,000; 200 { requested, retried, skipped[] })
GET  /admin/notifications/audit?action=&targetType=&targetId=&actorUserId=&from=&to=&cursor=&limit=   (V; read-only, keyset)

# AdminNotificationTemplatesController — api/v1/admin/notifications/templates
GET  /admin/notifications/templates?category=&channel=&language=&type=          (V; unpaged, under 200 rows)
GET  /admin/notifications/templates/{templateId}                                (V; versions, declared variables, isShippedDefault)
GET  /admin/notifications/templates/{templateId}/versions/{versionId}           (V)
POST /admin/notifications/templates/{templateId}/versions                       (M; creates a draft, optional copyFromVersionId; 201 with Location)
PUT  /admin/notifications/templates/{templateId}/versions/{versionId}           (M; edits a draft; If-Match required)
POST /admin/notifications/templates/{templateId}/versions/{versionId}/actions/preview     (V; { subject, html, text } or { title, message, actionLabel })
POST /admin/notifications/templates/{templateId}/versions/{versionId}/actions/send-test   (M; 202 { sentTo: masked }; the admin's own verified address only; notifications-test-send)
POST /admin/notifications/templates/{templateId}/versions/{versionId}/actions/publish     (M; If-Match required; archives the previous published version)
POST /admin/notifications/templates/{templateId}/versions/{versionId}/actions/archive     (M; If-Match required)

# AdminAnnouncementsController — api/v1/admin/notifications/announcements
GET  /admin/notifications/announcements?cursor=&limit=                          (V; newest first, with fan-out status and email counts)
POST /admin/notifications/announcements                                         (M; 201 { id, estimatedRecipients, emailEstimatedMinutes })

# AdminLocalizationController — api/v1/admin/notifications/localization
GET  /admin/notifications/localization                                          (V; isEnabled, supportedLanguages, availableLanguages, rowVersion)
PUT  /admin/notifications/localization                                          (M; If-Match required; { isEnabled, supportedLanguages })
```

**Templates**: a draft is edited in place; a published version is immutable and a change is a new draft. A text field may use only `{{ name }}` tokens for the type's declared variables, and may not contain HTML or a raw URL, so these return **422** with the offending token in the field error. `preview` is rendered by the production renderer, and the returned `html` is for a sandboxed iframe only. A sensitive link renders as the fixed sample `https://example.invalid/sample-link`. Archiving the only published version of a shipped default is refused.

**Announcements**: `kind` is `Maintenance`, `ServiceDegradation` or `ImportantAnnouncement`; `audience` is `AllActiveUsers` or `Roles` (`targetRoleIds` required and existing); title is 1-150 and message 1-2,000 characters, plain text; `endsAtUtc` must be in the future. Every announcement reaches its audience in-app, and email is queued only when `isCritical` is true. There is no PUT or DELETE: a published announcement is immutable.

**Localization setting**: `en` must stay in `supportedLanguages`, and every code must be in `availableLanguages` (the languages the platform ships content for); otherwise **422**. The change takes effect without a redeploy, because the cached setting is evicted.

### Status codes and Problem Details extensions

Standard Problem Details with `traceId`. The notification endpoints add a machine-readable `reason` extension on **409**, and `errors` on **422**:

| Status | When | Extension |
|---|---|---|
| 400 | malformed cursor, unknown enum value, a bulk retry filter that matches over 1,000 deliveries (nothing is retried) | `errors` for validation failures |
| 404 | notification not found, not owned or owner-deleted | |
| 409 | delivery retry refused | `reason`: `NotFailed`, `NotificationDeleted`, `NotificationExpired` or `RecipientDeleted` |
| 409 | template action refused | `reason`: `VersionNotDraft`, `VersionArchived`, `ConcurrencyConflict` or `LastPublishedDefault` |
| 409 | stale `If-Match` or `RowVersion` (templates, localization setting) | `reason`: `ConcurrencyConflict` |
| 422 | template text with an unknown variable, malformed token, HTML or raw URL; a test send with no verified address of the admin | |
| 422 | a preference change that disables a mandatory pair (nothing applied) | `errors["changes[i]"]` |
| 422 | a language that is unsupported, or localization is off | |
| 428 | `If-Match` missing on a template edit, publish or archive, or on a localization update | |
| 429 | over `notifications-endpoints`, `admin-endpoints` or `notifications-test-send`; `Retry-After` is set | |

**If-Match convention**: the `rowVersion` returned by a read (base64) goes back in the `If-Match` header of the edit, publish or archive. A missing header is **428**; a stale one is **409** `ConcurrencyConflict`, and the admin UI reloads. The header value may be quoted.

### Rate-limit policies

| Policy | Limit | Used by |
|---|---|---|
| `notifications-endpoints` | 120 per minute, per user | the user notification, preference and localization endpoints |
| `admin-endpoints` | 60 per minute, per user | every admin notification, template, announcement and localization endpoint (existing policy) |
| `notifications-test-send` | 10 per hour, per administrator | the template `send-test` action, on top of `admin-endpoints` |

All three partition on `RateLimitPartitions.UserOrClientKey`, not `Identity.Name`, which our tokens do not carry.

### SignalR hub `/hubs/notifications`

Server to client only; the caller joins the group `user:{userId}` from the authenticated identity, and the hub has no client-invocable methods, so every mutation is a REST call. It uses the same cookie-delivered access token as the other hubs. Events are best effort.

| Event | Payload | Sent when |
|---|---|---|
| `notificationCreated` | the list item shape plus `unreadCount` | a notification with `ShowInCenter = true` commits |
| `notificationUpdated` | `{ id, change: Read\|Deleted\|Expired, unreadCount }` | read or deleted in another session, or expired |
| `unreadCountChanged` | `{ unreadCount }` | after mark-all-read, bulk expiry or retention |

On `onreconnected` the client must refetch `GET /notifications/unread-count` and invalidate the first page, because a missed push is never replayed. There is no admin group: admin screens read REST.

### Removed legacy endpoints and events

Removed in the same release as the hub, because the old and new channels must never both run:

* The document notification list and mark-read actions on `DocumentProcessingController` (`/documents/notifications`), and the memory notification list and mark-read actions on `MemoriesController` (`/memories/notifications`). They now return 404, and `GET /notifications` replaces both.
* The `notificationCreated` push on `DocumentProcessingHub` and the `memoryNotificationCreated` push on the memory hub. `NotificationHub.notificationCreated` replaces both. `DocumentProcessingHub` keeps its stage-progress events.

### Localization

An endpoint localizes its Problem Details text only when marked `[LocalizedSurface]`, from `Messages.resx` and `Messages.ar.resx`; every other endpoint stays English. The `traceId` and the machine-readable `reason` values are never translated. The effective language is the first supported of the caller's explicit language, their saved choice, then `en`, and is always `en` while localization is disabled.

---

# 32. HTTP Status Codes

Use consistent status codes.

```
200 OK

201 Created

202 Accepted

204 No Content

400 Bad Request

401 Unauthorized

403 Forbidden

404 Not Found

409 Conflict

412 Precondition Failed

422 Unprocessable Content

428 Precondition Required

429 Too Many Requests

500 Internal Server Error

503 Service Unavailable
```

---

# 33. Rate Limiting

Apply rate limits by endpoint category.

Examples

Anonymous

```
20 requests/minute
```

Authenticated

```
120 requests/minute
```

AI generation

Token bucket based on subscription tier.

Return

```
429 Too Many Requests
```

Include retry information in response headers where appropriate.

---

# 34. OpenAPI Standards

Every endpoint must include:

* Summary
* Description
* Tags
* Request schema
* Response schema
* Example payloads
* Authorization requirements
* Possible status codes

Swagger should be production quality.

---

# 35. API Security

Validate

* JWT
* Ownership
* File permissions
* Knowledge Base permissions
* Agent permissions

Never trust client-provided IDs without verifying ownership.

---

# 36. API Observability

Every request should log:

* Correlation ID
* Endpoint
* User ID (when authenticated)
* Duration
* Status code
* AI provider (if applicable)
* Token usage (if applicable)

Sensitive request bodies should not be logged.

---

# 37. Error Codes

Every error should include a machine-readable code.

Examples

```
AUTH_001

AUTH_002

CHAT_001

CHAT_002

RAG_001

MODEL_001

FILE_001

PAYMENT_001
```

These codes should remain stable across API versions.

---

# 38. API Evolution

Breaking changes require a new API version.

Non-breaking additions include:

* New optional properties
* New endpoints
* New query parameters
* New response metadata

Never remove or repurpose existing fields within the same API version.

---

# 39. Client SDK Compatibility

The API should be designed so SDKs can be generated for:

* C#
* TypeScript
* Python
* Java
* Go

Avoid ambiguous payloads and polymorphic responses unless clearly documented.

---

# 40. REST vs. Real-Time

Use the appropriate communication style:

**REST**

* CRUD operations
* Settings
* Authentication
* Administration
* Knowledge base management

**Server-Sent Events (Preferred)**

* AI token streaming
* Long-running generation

**SignalR**

* Notifications
* Presence
* Future collaborative editing
* Live dashboards

Choose the simplest transport that satisfies the requirement.

---

# 41. API Design Checklist

Before publishing any endpoint, verify:

* Is the URI resource-oriented?
* Is the HTTP verb appropriate?
* Is the request model strongly typed?
* Are validation rules defined?
* Are authorization rules enforced?
* Does it return the correct status codes?
* Are errors returned as Problem Details?
* Is the endpoint documented in OpenAPI?
* Is it versioned?
* Is it observable (logging, correlation ID, metrics)?
* Is it backward compatible?

Every public endpoint should meet these standards before release.
