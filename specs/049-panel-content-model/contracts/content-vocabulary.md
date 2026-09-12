# Contract: Panel Content Vocabulary

**Version**: 1 | **Feature**: `049-panel-content-model`

The set of content blocks Lucy may compose into a panel. This vocabulary is the stable interface that replaces the four per-feature panel types — it changes deliberately and rarely, and every change increments `version`.

## Authority and generation

The zod schemas in `viewer/panels/content/blocks.ts` are the single source of truth, split into two tiers:

- **The document envelope** (`panelContentSchema`) — `version` plus a `blocks` array of 1–50 entries, each merely required to be an object carrying a `kind` string. This is deliberately loose at the block level. It is what `contracts/panel-content.schema.json` is generated from and what `PresentPanelContentCapability` declares as its `InputSchemaJson`, so `CapabilityExecutor` enforces it — and only it — before the capability runs. A test regenerates and compares; any divergence fails the build (research D2).
- **The per-block schema** (`blockSchema`) — the exact shape of each of the eight kinds below. Validated **one block at a time**, only at render, inside `ContentRenderer`. Never applied to the whole document at once.

The split exists because a single strict schema over the whole array would reject the *entire* document the moment one block fails to match — the opposite of the degradation this vocabulary requires: one bad block shows a placeholder or an error while every sibling still renders (see Degradation below). The envelope gate only ever needs to answer "is this a well-formed, non-empty content document", which is also the only question the server needs answered before it is safe to push.

## Document shape

```jsonc
{
  "version": 1,
  "blocks": [ /* one or more blocks, rendered in order */ ]
}
```

Blocks are flat. A block never contains another block.

**Every collection is bounded**: `blocks` ≤ 50, `keyValue.items` ≤ 100, `table.rows` ≤ 200, `table.columns` ≤ 20, `chart.series` ≤ 10. Constitution §7 requires long lists to be virtualized; bounding the input meets the same concern without a virtualization dependency. Exceeding a bound is a schema failure at the server gate, not a rendering problem — so a model that composes a thousand-row table is refused with an explanation rather than producing a panel that locks the viewer.

## Blocks

### heading
```jsonc
{ "kind": "heading", "text": "Al Safa Park 2", "level": 1 }
```
`text` 1–200 chars. `level` is `1` or `2`, default `1`.

### text
```jsonc
{ "kind": "text", "text": "The site sits on the north side of the park…" }
```
`text` 1–4000 chars. Rendered as plain text with line breaks preserved. Markup is never interpreted (spec FR-005).

### keyValue
```jsonc
{ "kind": "keyValue", "items": [
    { "label": "Address", "value": "Al Wasl Road, Dubai" },
    { "label": "Coordinates", "value": "25.1412, 55.2210" },
    { "label": "Plot number", "value": null }
] }
```
1–100 items. `value` may be `null`, which renders as an explicit "not available" marker rather than an empty cell. Each item may carry an `action`.

### table
```jsonc
{ "kind": "table",
  "columns": ["Element", "Id", "Level"],
  "rows": [
    { "cells": ["Wall", "wall-42", "Level 02"],
      "action": { "command": "select", "args": { "layerId": "model-1", "elementId": "wall-42" } } }
  ] }
```
At least one column. A row whose `cells` length differs from `columns` renders what is present and is marked malformed, without failing the block. Each row may carry an `action` — this is the element-table case from the spec's User Story 2.

### chart
```jsonc
{ "kind": "chart", "chartKind": "bar",
  "labels": ["Jan", "Feb", "Mar"],
  "series": [{ "label": "Sun hours", "values": [4.2, 5.1, 6.8] }] }
```
`chartKind` is `bar` or `line`. At least one series, each with at least one value. Carried over unchanged from the retired `chart` type.

### metric
```jsonc
{ "kind": "metric", "label": "Boundary confidence", "value": "High", "unit": null }
```
`unit` optional, ≤20 chars. May carry an `action`.

### image
```jsonc
{ "kind": "image", "fileId": "01J8…", "alt": "Site aerial photograph" }
```
`fileId` is a platform file identifier, never a URL. The renderer never puts it into an `<img src>` directly — it is always passed as an opaque parameter to the platform's existing, entitlement-checked file-download endpoint, which resolves it to a signed URL only if the requesting user may see that file. **This is what prevents content from making the browser fetch an arbitrary external address** (research D10) — architecturally, not by pattern-matching the string. `alt` is required.

### divider
```jsonc
{ "kind": "divider" }
```

## Degradation

| Condition | Behaviour |
|---|---|
| Unknown `kind` | Visible placeholder for that block; every sibling renders normally |
| Block fails its schema at render | Visible error for that block; every sibling renders normally |
| Document fails the schema at the server | Capability fails; no panel opens; Lucy reports it |
| Document has no blocks | No panel opens; outcome visible to the user |

A whole-document failure is caught server-side, before a panel exists. A single-block failure is caught client-side, after one does. That split is deliberate: FR-030 forbids opening an empty panel, and User Story 4 requires partial rendering.

## Extending the vocabulary

Adding a block kind means: a zod schema, a renderer module, a registry entry, a regenerated JSON Schema artifact, and a `version` increment. It does **not** mean a change to the panel framework, the store, or the capability. Adding a kind is the only reason to touch this contract — presenting new *content* never is.
