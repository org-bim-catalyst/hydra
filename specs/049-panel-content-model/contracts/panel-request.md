# Contract: Panel Request

**Feature**: `049-panel-content-model`

The payload that opens a panel. Delivered over the existing `PanelRequested` hub event, or passed directly to the store. Replaces the flat `{typeKey, title, data}` shape from specs/028.

## Shape

Discriminated on `kind` (research D5).

### Content panel

```jsonc
{
  "requestId": "01J8…",
  "kind": "content",
  "title": "Al Safa Park 2",
  "content": { "version": 1, "blocks": [ /* … */ ] },
  "chrome": { "titleBar": true, "resizable": true },
  "position": null,
  "contextAssociation": { "layerId": "gis-current-location", "elementId": null }
}
```

### Live panel

```jsonc
{
  "requestId": "01J8…",
  "kind": "live",
  "title": "Sun position",
  "typeKey": "solar-controls",
  "data": { /* validated against the registered kind's own schema */ },
  "chrome": null,
  "position": null,
  "contextAssociation": null
}
```

## Fields

| Field | Applies to | Notes |
|---|---|---|
| `requestId` | both | Becomes the panel id. Two requests with the same id replace rather than duplicate, as today. |
| `kind` | both | `content` or `live`. |
| `title` | both | Required, non-empty. |
| `content` | content | Validated against the vocabulary — server-side at the capability gate, client-side per block. |
| `typeKey` | live | Must be a currently registered live kind. If not, the existing unknown-type fallback panel is shown. |
| `data` | live | Opaque on the wire; validated client-side against the registered kind's schema. |
| `chrome` | both | Optional override. Content panels default to `{titleBar: true, resizable: true, 400×300}`; live kinds declare chrome at registration. |
| `position` | both | Absent or null means cascade placement. Unchanged from specs/028. |
| `contextAssociation` | both | Unchanged from specs/028, including the stale and invalid transitions driven by viewer events. |

## Transport

Unchanged from specs/028. `IPanelNotifier.PanelRequestedAsync` pushes to the caller's own user group over `PanelHub`; delivery stays private per user, and panel state stays session-scoped with no persistence. Only the payload's shape changes.

## Capabilities

The single `open_visual_panel` capability is replaced by two, reflecting that Lucy now has two distinct affordances (spec FR-024):

| Capability | Availability | Declares |
|---|---|---|
| `present_panel_content` | Always available | The content vocabulary as its `InputSchemaJson`, so `CapabilityExecutor` validates composed content before anything is pushed |
| `open_live_panel` | Only while at least one live kind is registered | A `typeKey` plus that kind's data |

`open_live_panel` has no consumer in this feature — no live kind ships here. It exists so the affordance is defined before specs/050 gives extensions something to register.

The hardcoded `RenderableTypeKeys` set in the retired `OpenVisualPanelCapability` is removed, along with the comment acknowledging its drift risk. Nothing server-side enumerates panel kinds any more.

## Breaking change

The four type keys `chart`, `table`, `parameters` and `summary` stop being valid `typeKey` values. There is no compatibility shim — there are no external consumers, and the spec prefers a clean break. A request using one of them is handled as an unregistered live kind: the existing fallback panel, visibly.
