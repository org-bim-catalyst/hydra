# Phase 1 Data Model: Panel Content Model

**Feature**: `049-panel-content-model` | **Date**: 2026-09-12

No persistent storage is introduced. Every entity below is either a wire payload or session-scoped client state.

---

## Panel Content

What a content panel shows. The unit the server validates and the client renders.

| Field | Type | Rules |
|---|---|---|
| `version` | integer | The vocabulary version. `1` for this feature. Required, so a future change is identifiable rather than inferred. |
| `blocks` | array, 1–50 entries | Ordered. Rendered in the order supplied. A document with none is refused before a panel opens (research D11). |

**The document envelope is validated loosely; each block is validated strictly, one at a time, at render.** The envelope schema only requires each array entry to be an object carrying a `kind` string — it does not check the rest of a block's shape. That is deliberate: a single schema strict enough to check every block's exact fields would reject the whole document the instant one block failed to match, which is the opposite of the per-block degradation this vocabulary requires (see Validation Summary below). The strict, per-kind shapes documented for each block below are what `ContentRenderer` parses one block at a time.

**Every collection in this vocabulary is bounded.** Constitution §7 requires long lists to be virtualized; bounding the input satisfies the same concern without a virtualization dependency, and an unbounded collection composed by a model is the realistic route to an unusable panel. The caps are `blocks` ≤ 50, `keyValue.items` ≤ 100, `table.rows` ≤ 200, `table.columns` ≤ 20, `chart.series` ≤ 10. Exceeding one is a schema failure at the server gate, not a rendering problem.

---

## Block

One unit of presented content, discriminated by `kind`. Flat — blocks never contain blocks (research D3).

Common to every kind:

| Field | Type | Rules |
|---|---|---|
| `kind` | string | One of the eight below. An unrecognised value renders a visible placeholder and does not invalidate its siblings. |

### `heading`

| Field | Type | Rules |
|---|---|---|
| `text` | string | 1–200 characters. Rendered as text, never markup. |
| `level` | `1 \| 2` | Optional, default `1`. Two levels only — a panel is not a document. |

### `text`

| Field | Type | Rules |
|---|---|---|
| `text` | string | 1–4000 characters. Plain text. Line breaks preserved; no markup, no rich-text syntax interpreted (spec FR-005). |

### `keyValue`

Replaces the retired `parameters` type.

| Field | Type | Rules |
|---|---|---|
| `items` | array | 1–100 entries. |
| `items[].label` | string | 1–100 characters. |
| `items[].value` | string \| number \| null | `null` renders as an explicit "not available" marker, satisfying the spec's requirement that missing detail is shown as missing rather than blank. |
| `items[].action` | `Action?` | Optional. Makes the entry activatable. |

### `table`

Carries over `tableDataSchema` from the retired `table` type, plus row actions.

| Field | Type | Rules |
|---|---|---|
| `columns` | string[] | 1–20. |
| `rows` | array | 0–200. Each row is an object with `cells` and an optional `action`. |
| `rows[].cells` | (string \| number \| null)[] | Length should match `columns`; a mismatch renders what is present and marks the row as malformed rather than failing the block. |
| `rows[].action` | `Action?` | Optional. Makes the row activatable — the element-table case from the spec's User Story 2. |

### `chart`

Carries over `chartDataSchema` from the retired `chart` type, unchanged.

| Field | Type | Rules |
|---|---|---|
| `chartKind` | `'bar' \| 'line'` | Required. |
| `labels` | string[] | Optional. |
| `series` | array | At least one. Each `{ label: string, values: number[] }` with at least one value. |

### `metric`

A single prominent figure.

| Field | Type | Rules |
|---|---|---|
| `label` | string | 1–100 characters. |
| `value` | string \| number | Required. |
| `unit` | string | Optional, ≤20 characters. |
| `action` | `Action?` | Optional. |

### `image`

| Field | Type | Rules |
|---|---|---|
| `fileId` | string | A platform file identifier, never a URL — resolved through the existing file-access mechanism with its entitlement checks. The renderer never treats it as anything but an opaque parameter to that mechanism, which is what prevents content from causing an arbitrary external fetch (research D10). |
| `alt` | string | Required, 1–300 characters. Non-negotiable for §7 accessibility. |

### `divider`

No fields. A structural separator.

---

## Action

A declared request to perform one permitted viewer operation. Validated at render time; a failing action renders inert and is never presented as activatable (research D6).

| Field | Type | Rules |
|---|---|---|
| `command` | string | Must be a member of the allowlist. Any other value renders the carrying entry inert and is recorded. |
| `args` | object | Validated against that command's own argument schema. Malformed args render inert exactly as an unknown command does. |

**Allowlist (v1)**: `select`, `clearSelection`, `zoomToLocation`, `setLayerVisibility`, `setViewMode`, `setMapStyle`. See [contracts/action-allowlist.md](./contracts/action-allowlist.md) for each command's arguments and why `fitBounds` is not among them.

There is no dynamic dispatch: each command maps to an explicit invoker. The reachable set is fixed at build time.

---

## Panel Chrome

How a panel is framed (research D7).

| Field | Type | Rules |
|---|---|---|
| `titleBar` | boolean | Default `true`. When `false`, a grip affordance carries movement, focus and the close/minimise controls. |
| `resizable` | boolean | Default `true`. |
| `defaultSize` | `{width, height}` | Default `{400, 300}`. Subject to the existing `MIN_PANEL_WIDTH`/`MIN_PANEL_HEIGHT` floor and to remaining within the viewer. |

Content panels take the defaults unless the request overrides them. Live panel kinds declare chrome at registration.

---

## Panel Request

The wire payload, discriminated by `kind` (research D5). Replaces the current flat `{typeKey, title, data}` shape.

Common:

| Field | Type | Rules |
|---|---|---|
| `requestId` | string | Becomes the panel's id. |
| `kind` | `'content' \| 'live'` | Discriminator. |
| `title` | string | Required, non-empty. |
| `chrome` | `PanelChrome?` | Optional override. |
| `position` | `{x, y}?` | Absent means cascade placement (unchanged from specs/028). |
| `contextAssociation` | `{layerId?, elementId?}?` | Unchanged from specs/028. |

When `kind = 'content'`: adds `content: PanelContent`.
When `kind = 'live'`: adds `typeKey: string` and `data: unknown`, validated against the registered kind's own schema.

---

## Live Panel Kind

A registered panel whose content is code rather than data. None ships in this feature; the registry narrows to this purpose (spec FR-022, FR-026).

| Field | Type | Rules |
|---|---|---|
| `typeKey` | string | Unique. Duplicate registration is a developer error and throws in development, as today. |
| `renderer` | component | Receives the validated data. |
| `schema` | schema | Validates the request's `data`. |
| `chrome` | `PanelChrome` | Declared at registration. |

Registration becomes **dynamic**: the side-effect import barrel that registers four kinds at module load is emptied, and kinds are registered and withdrawn by whatever owns them (extensions, in specs/050).

---

## Floating Panel (session state)

The existing `FloatingPanel` entity, modified. Unchanged fields are omitted.

| Field | Change |
|---|---|
| `typeKey` | Now optional — present only for live panels. |
| `content` | New — the validated block document, for content panels. |
| `chrome` | New — resolved chrome, replacing the standalone `resizable` field. |
| `validationStatus` | Unchanged values, but `unknown-type` now applies only to live panels. |
| `data`, `validationError`, `position`, `size`, `minimized`, `restoreState`, `zOrder`, `lastFocusedAtUtc`, `opacityOverride`, `contextAssociation`, `contextStatus` | Unchanged. |

Everything downstream of panel construction in the store — cascade, z-order, LRU eviction, minimise/restore, viewport clamping, the two viewer-event subscriptions — is untouched (research D9).

---

## Validation Summary

| Failure | Where caught | Outcome |
|---|---|---|
| Content fails the vocabulary schema | Server, at `CapabilityExecutor`'s existing gate | Capability fails; Lucy reports it; no panel opens |
| Content has no blocks | Server, before pushing | No panel opens; outcome visible |
| One block has an unknown `kind` | Client, at render | Visible placeholder for that block; siblings render |
| One block is malformed | Client, at render | Visible error for that block; siblings render |
| Action names a non-allowlisted command | Client, at render | Entry rendered inert, not activatable; recorded |
| Action args fail their schema | Client, at render | Entry rendered inert, not activatable; recorded |
| Action target no longer exists | Client, on invocation | User told the target is unavailable |
| Live panel kind not registered | Client, at open | Existing unknown-type fallback panel |
