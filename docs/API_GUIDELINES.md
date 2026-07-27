# API_GUIDELINES.md

> **Project:** Ask Lucy AI Workspace
>
> **Version:** 2.0
>
> **Architecture:** REST API + Streaming + Clean Architecture
>
> **Framework:** ASP.NET Core (.NET 10)
>
> **Last Updated:** July 2026

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

Conversations

```
GET /conversations

POST /conversations

GET /conversations/{id}

PATCH /conversations/{id}

DELETE /conversations/{id}
```

Messages

```
GET /conversations/{id}/messages

POST /conversations/{id}/messages

POST /chat/stream
```

Regenerate

```
POST /messages/{id}/regenerate
```

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

```
GET /knowledge-bases

POST /knowledge-bases

GET /knowledge-bases/{id}

PATCH /knowledge-bases/{id}

DELETE /knowledge-bases/{id}
```

Documents

```
POST /knowledge-bases/{id}/documents

GET /knowledge-bases/{id}/documents
```

Re-index

```
POST /documents/{id}/reindex
```

---

# 24. Prompt Library

```
GET /prompts

POST /prompts

PATCH /prompts/{id}

DELETE /prompts/{id}
```

---

# 25. Agent Endpoints

```
GET /agents

POST /agents

PATCH /agents/{id}

DELETE /agents/{id}

POST /agents/{id}/execute
```

---

# 26. MCP Endpoints

```
GET /mcp/servers

GET /mcp/tools

POST /mcp/tools/{id}/execute
```

Tool execution must validate permissions before invocation.

---

# 27. User Profile

```
GET /profile

PATCH /profile

POST /profile/avatar

DELETE /profile/avatar
```

---

# 28. Authentication Endpoints

```
POST /auth/register

POST /auth/login

POST /auth/logout

POST /auth/refresh

POST /auth/forgot-password

POST /auth/reset-password

POST /auth/verify-email

POST /auth/2fa/enable

POST /auth/2fa/verify
```

---

# 29. Billing

```
GET /subscriptions

POST /subscriptions

GET /usage

GET /payments
```

---

# 30. Administration

```
GET /admin/users

GET /admin/logs

GET /admin/system

GET /admin/providers

GET /admin/feature-flags
```

All administrative endpoints require elevated authorization.

---

# 31. HTTP Status Codes

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

429 Too Many Requests

500 Internal Server Error

503 Service Unavailable
```

---

# 32. Rate Limiting

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

# 33. OpenAPI Standards

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

# 34. API Security

Validate

* JWT
* Ownership
* File permissions
* Knowledge Base permissions
* Agent permissions

Never trust client-provided IDs without verifying ownership.

---

# 35. API Observability

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

# 36. Error Codes

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

# 37. API Evolution

Breaking changes require a new API version.

Non-breaking additions include:

* New optional properties
* New endpoints
* New query parameters
* New response metadata

Never remove or repurpose existing fields within the same API version.

---

# 38. Client SDK Compatibility

The API should be designed so SDKs can be generated for:

* C#
* TypeScript
* Python
* Java
* Go

Avoid ambiguous payloads and polymorphic responses unless clearly documented.

---

# 39. REST vs. Real-Time

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

# 40. API Design Checklist

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
