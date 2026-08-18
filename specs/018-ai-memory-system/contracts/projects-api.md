# API Contract: Projects

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `ProjectsController` (`/api/v1/projects`), plus one action sub-resource on the existing
`ChatsController`. Rate-limited via `memory-endpoints` (research.md Decision 17 — Projects is a
lightweight grouping construct introduced for Memory scoping, not a separate cost-tiered surface).
`[Authorize]` by default; scoped to the caller's own Projects (FR-002a).

## List Projects

`GET /api/v1/projects?cursor=&pageSize=50` → paginated `ProjectDto[]` (`id, name, createdAtUtc`),
newest-first (FR-002a).

## Create a Project

`POST /api/v1/projects`

```json
{ "name": "Riverside Tower — Mechanical Coordination" }
```

→ `201 Created` with the new `ProjectDto`. (FR-002a).

## Rename a Project

`PUT /api/v1/projects/{id}`

```json
{ "name": "Riverside Tower — MEP Coordination" }
```

→ `204 No Content`. (FR-002a).

## Delete a Project

`DELETE /api/v1/projects/{id}`

(FR-002a, User Story 5 AC3). Soft-deletes the Project; `ProjectDeletedDomainEvent` (research.md
Decision 15) archives its scoped memories (never immediately deletes them) and leaves conversation
history intact. `204 No Content`.

## Assign a conversation to a Project

`PUT /api/v1/chats/{chatId}/project`

```json
{ "projectId": "..." }
```

(FR-002a — "a conversation MAY belong to at most one Project at a time"). Pass `"projectId": null`
to remove a conversation from its Project (back to general scope). `204 No Content`. Mirrors the
existing `PUT /api/v1/chats/{id}/knowledge-bases` sub-resource-action shape from specs/016's
`ConversationKnowledgeBasesController`.
