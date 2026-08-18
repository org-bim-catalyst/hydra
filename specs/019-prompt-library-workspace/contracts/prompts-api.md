# API Contract: Prompts (Library, Editor, Versions, Organization, Import/Export)

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `PromptsController` (`/api/v1/prompts`) and `PromptFoldersController`
(`/api/v1/prompt-folders`). Rate-limited via a new `prompt-endpoints` policy (matching every other
feature's named-policy convention). `[Authorize]` by default; every response is scoped to the
caller's own prompts via a new `PromptOwnershipGuard` (mirrors `MemoryOwnershipGuard`/
`ChatOwnershipGuard`) — a request naming a prompt the caller does not own returns `404`, never `403`
(matches FR-090 and this codebase's existence-disclosure convention). All list/search endpoints are
cursor-paginated (constitution §6).

## Create a prompt

`POST /api/v1/prompts`

```json
{
  "name": "Summarize a technical document",
  "description": "Produces a length- and language-controlled summary",
  "promptType": "Summarization",
  "systemInstructions": "You are a precise technical summarizer.",
  "userInstructions": "Summarize {{document}} in {{target_language}} at {{summary_length}} length.",
  "contextText": null,
  "examplesText": null,
  "outputInstructions": "Return plain prose, no headings.",
  "constraints": "Never invent facts not present in the source document.",
  "categoryId": "...",
  "folderId": null,
  "requiredCapabilities": { "requiresJsonMode": false, "requiresVision": false, "requiresStreaming": false, "requiresFunctionCalling": false, "requiresReasoning": false, "requiresEmbeddings": false, "requiresImageInput": false, "requiresImageOutput": false, "requiresAudio": false },
  "variables": [
    { "name": "document", "type": "File", "isRequired": true, "description": "The source document" },
    { "name": "target_language", "type": "String", "isRequired": true, "defaultValue": "English" },
    { "name": "summary_length", "type": "String", "isRequired": false, "defaultValue": "medium" }
  ]
}
```

(FR-001–FR-005, FR-010–FR-012, User Story 1). Creates `Prompt` + its first `PromptVersion` (version
1) + `PromptVariable` rows. `requiredCapabilities` is a nested DTO object for API ergonomics; it
maps to `Prompt`'s 9 flat `Requires*` boolean columns (data-model.md), not an owned-type column —
`PromptMappingProfile` (tasks.md T035) is responsible for the flatten/nest transform in both
directions. `201 Created` → `PromptDetailDto` (shape below). `409 Conflict`
(`duplicate-resource`) if `name` collides with an existing, non-deleted prompt for this owner
(FR-006). `400 Bad Request` if `userInstructions` references a `{{placeholder}}` with no matching
entry in `variables`, or a variable is defined but never referenced (FR-014).

## Get a prompt

`GET /api/v1/prompts/{id}` → `PromptDetailDto`:

```json
{
  "id": "...",
  "name": "Summarize a technical document",
  "description": "...",
  "promptType": "Summarization",
  "status": "Active",
  "systemInstructions": "...",
  "developerInstructions": null,
  "userInstructions": "...",
  "contextText": null,
  "examplesText": null,
  "outputInstructions": "...",
  "constraints": "...",
  "categoryId": "...",
  "folderId": null,
  "isFavorite": false,
  "isPinned": false,
  "requiredCapabilities": { "requiresJsonMode": false, "requiresVision": false, "requiresStreaming": false, "requiresFunctionCalling": false, "requiresReasoning": false, "requiresEmbeddings": false, "requiresImageInput": false, "requiresImageOutput": false, "requiresAudio": false },
  "preferredModelKey": null,
  "currentVersion": { "id": "...", "versionNumber": 3 },
  "variables": [ { "name": "document", "type": "File", "isRequired": true } ],
  "tags": ["technical-writing", "summarization"],
  "usageCount": 12,
  "lastSuccessfulUseAtUtc": "2026-08-09T10:00:00Z",
  "createdAtUtc": "...",
  "modifiedAtUtc": "..."
}
```

(User Story 1 AC1/AC2, FR-005).

## Update a prompt (creates a new version)

`PUT /api/v1/prompts/{id}`

Same body shape as create, plus an optional `"changeDescription": "Tightened output constraints"`
(FR-031). `200 OK` → updated `PromptDetailDto`. `409 Conflict` (`concurrency-conflict`) if the
caller's copy is stale (FR-007, research.md Decision 8 — standard `RowVersion`/
`DbUpdateConcurrencyException` handling, no request-body change needed to opt in). `409 Conflict`
(`duplicate-resource`) if the update includes a rename that collides with another prompt (FR-006).
`400 Bad Request` for undeclared/unreferenced variables (FR-014), same as create.

## Delete / archive / restore / duplicate

`DELETE /api/v1/prompts/{id}` — soft delete (FR-001). `204 No Content`.

`POST /api/v1/prompts/{id}/actions/archive` / `POST /api/v1/prompts/{id}/actions/restore` (FR-001,
spec.md Edge Cases — an archived prompt stays usable for in-flight references but drops out of
default listings). `204 No Content`.

`POST /api/v1/prompts/{id}/actions/duplicate` → `201 Created` with a new, independent `PromptDetailDto`
(new id, fresh version-1 history, name auto-suffixed on collision per FR-006's rename-or-suggest rule)
(FR-001, spec.md Edge Cases).

## List / search prompts

`GET /api/v1/prompts?query=&categoryId=&tag=&folderId=&favorite=&pinned=&status=&view=recentlyUsed|recentlyModified&cursor=&pageSize=50`

(FR-050–FR-053, User Story 4). `query` searches `name`/`description`/`systemInstructions`/
`userInstructions` via the `FULLTEXT INDEX` (research.md Decision 12) plus a separate variable-name
match; results rank best-match-first. `view=recentlyUsed` orders by
`PromptUsageStatistics.LastSuccessfulUseAtUtc` (successful executions only — spec.md Clarifications);
`view=recentlyModified` orders by `ModifiedAtUtc`. Response → `PromptListItemDto[]` (a lighter
projection than `PromptDetailDto`: id, name, description, promptType, status, categoryId, tags,
isFavorite, isPinned, usageCount, lastSuccessfulUseAtUtc, modifiedAtUtc), cursor-paginated.

## Favorite / pin

`PUT /api/v1/prompts/{id}/favorite` `{ "isFavorite": true }` — `204 No Content`.
`PUT /api/v1/prompts/{id}/pinned` `{ "isPinned": true }` — `204 No Content`.

(FR-050, User Story 4 AC3).

## Tags

`POST /api/v1/prompts/{id}/tags` `{ "value": "technical-writing" }` — `201 Created`.
`DELETE /api/v1/prompts/{id}/tags/{tagId}` — `204 No Content`.
`GET /api/v1/prompts/tags` → the caller's distinct tag values (owner-scoped, mirrors
`KnowledgeBaseTagConfiguration`'s indexed `(OwnerId, Value)` query) — populates tag-filter
autocomplete.

## Categories

`GET /api/v1/prompts/categories` → predefined (shared) + the caller's custom categories.
`POST /api/v1/prompts/categories` `{ "name": "BIM Documentation" }` → `201 Created`. `409 Conflict`
on a name collision within the caller's own custom categories (mirrors
`CreateCustomCategoryCommandHandler`'s existing knowledge-base behavior).

## Preview

`POST /api/v1/prompts/{id}/preview` — body: variable values (any subset; missing required variables
fall back to their `ExampleValue`/`DefaultValue`, never blocked). Response: the fully resolved
prompt text, **no AI provider call is made** (FR-005).

## Folders

`PromptFoldersController` (`/api/v1/prompt-folders`) — mirrors `KnowledgeBasesController`'s existing
folder sub-resource shape exactly (research.md Decision 5):

- `GET /api/v1/prompt-folders` → the caller's full folder tree.
- `POST /api/v1/prompt-folders` `{ "name": "...", "parentFolderId": null }` → `201 Created`. `400
  Bad Request` (`domain-rule-violation`) past `MaxNestingDepth`.
- `PUT /api/v1/prompt-folders/{id}` (rename) — `204 No Content`.
- `PUT /api/v1/prompt-folders/{id}/move` `{ "newParentFolderId": "..." }` — `204 No Content`. `400
  Bad Request` if the move would create a cycle (spec.md Edge Cases) or exceed `MaxNestingDepth`.
- `DELETE /api/v1/prompt-folders/{id}` — `204 No Content`; prompts inside become unfiled
  (`FolderId = null`), matching `KnowledgeBaseFolder`'s delete behavior.

## Versions

`GET /api/v1/prompts/{id}/versions` → `PromptVersionSummaryDto[]` (versionNumber, changeDescription,
author, createdAtUtc), newest first (FR-032).

`GET /api/v1/prompts/{id}/versions/{versionNumber}` → full `PromptVersionDetailDto` (content,
variables, model settings snapshot) (FR-032).

`GET /api/v1/prompts/{id}/versions/compare?from={versionNumber}&to={versionNumber}` → a field-by-field
diff of content, variables, and model settings between the two versions (FR-032, User Story 3 AC2).

`POST /api/v1/prompts/{id}/versions/{versionNumber}/actions/restore` → `200 OK` with the updated
`PromptDetailDto`; creates a **new** version copying the restored content (FR-033 — history is never
deleted or overwritten). `409 Conflict` (`concurrency-conflict`) under the same stale-`RowVersion`
rule as a normal edit.

`POST /api/v1/prompts/{id}/versions/{versionNumber}/actions/duplicate` → `201 Created`, a new,
independent `Prompt` seeded from that version's content, own fresh version-1 history (FR-032).

## Export / Import

`POST /api/v1/prompts/export` `{ "promptIds": ["...", "..."] }` (one or more; research.md
Decision 13, spec.md Clarifications — bulk export supported) → `200 OK`, `Content-Type:
application/json`, body:

```json
{
  "schemaVersion": 1,
  "prompts": [
    {
      "name": "...",
      "description": "...",
      "promptType": "Summarization",
      "systemInstructions": "...",
      "userInstructions": "...",
      "variables": [ { "name": "document", "type": "File", "isRequired": true } ],
      "currentVersion": { "content": "...", "modelSettings": { "providerKey": null, "modelKey": null } },
      "tags": ["technical-writing"]
    }
  ]
}
```

(FR-070). A single-prompt export is simply a one-element `prompts` array — no separate file shape.

`POST /api/v1/prompts/import` — body: the same file shape. `201 Created` → the list of newly created
`PromptListItemDto`s, each starting its own independent version-1 history (FR-072). `400 Bad Request`
(`domain-rule-violation`, with a per-entry `errors` extension) if the file or **any** entry fails
schema/content validation — the whole import is rejected, nothing is created (FR-071, research.md
Decision 13). A name collision on any entry is resolved the same way as `POST /api/v1/prompts`
(FR-006/FR-072): auto-suffixed rather than failing the entire import for that reason alone.

## Statistics

`GET /api/v1/prompts/{id}/statistics` → `{ "successfulExecutionCount": 12, "lastSuccessfulUseAtUtc":
"...", "ratingBreakdown": { "good": 8, "needsImprovement": 3, "failed": 1 } }` (FR-062, spec.md
"Prompt Statistics" API requirement).

## Error format

Every error response is RFC 9457 Problem Details (constitution §6), matching every other controller
in this codebase — see `ProblemDetailsMiddleware.cs`'s existing type/status mapping. This feature adds
no new Problem Details `type` beyond the ones already registered
(`duplicate-resource`, `concurrency-conflict`, `domain-rule-violation`, `not-found`, `forbidden`,
`validation-failed`) — no new exception type is introduced.
