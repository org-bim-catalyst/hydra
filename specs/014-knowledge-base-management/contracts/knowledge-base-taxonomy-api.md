# API Contract: Categories & Tags

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `KnowledgeBaseTaxonomyController`, `[Authorize]`,
`[EnableRateLimiting("knowledge-base-endpoints")]`. Route base: `/api/v1/knowledge-bases`.
Split from `KnowledgeBasesController` because categories/tags are not sub-resources of one
knowledge base — they're caller-scoped lists referenced *by* many knowledge bases
(FR-017–FR-021, FR-038).

## List categories

`GET /api/v1/knowledge-bases/categories`

Returns the 8 predefined (shared, `ownerId: null`) categories plus the caller's own private
custom categories (FR-017/FR-018/FR-038) — never another user's custom categories:

```json
[
  { "id": "...", "name": "Engineering", "isPredefined": true },
  { "id": "...", "name": "Vendor Docs", "isPredefined": false }
]
```

`isPredefined` is computed from `OwnerId == null` (data-model.md) — not a stored column.

## Create a custom category

`POST /api/v1/knowledge-bases/categories`

Body: `{ "name": "Vendor Docs" }`. `201 Created`. `409` if the caller already has a category
with that name (case-insensitive, data-model.md validation rule). Private to the creating
user (FR-038) — never visible in another user's `GET` response above.

## Delete a custom category

`DELETE /api/v1/knowledge-bases/categories/{id}`

Owner-scoped; `404` if the category doesn't exist, isn't owned by the caller, or is one of
the 8 predefined categories (predefined categories are never deletable). `204` on success —
every knowledge base that referenced it has `categoryId` cleared to `null` (Uncategorized) in
the same transaction (FR-021, data-model.md lifecycle note), not left dangling.

## List the caller's tags

`GET /api/v1/knowledge-bases/tags?q={prefix?}`

Returns the caller's distinct tag values across all their knowledge bases (for the filter
dropdown / autocomplete, FR-020), optionally prefix-filtered by `q`:

```json
["revit", "standards", "vendor-2026"]
```

There is no separate "create a tag" endpoint — tags are created implicitly by including a new
value in a knowledge base's `tags` array via `PATCH /api/v1/knowledge-bases/{id}` (see
[knowledge-bases-api.md](./knowledge-bases-api.md)), consistent with data-model.md's decision
not to model a master tag-catalog table.
