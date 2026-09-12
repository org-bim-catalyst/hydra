# Contract: Action Allowlist Extension

**Feature**: `051-viewer-scene-content-api`

Extends specs/049's `contracts/action-allowlist.md` (implemented in
`viewer/panels/actions/allowlist.ts`). FR-032 requires this feature's *safe* commands to be added
— safe meaning "reads/re-frames existing content," never "creates or destroys content."

## Added

| Command | Args schema | Why safe |
|---|---|---|
| `selectAndFrame` | `{ layerId: string; elementId: string }` | Only selects and re-frames an already-loaded element — identical risk profile to the existing `select` entry, which this effectively supersets with framing. |

## Deliberately still excluded

`loadContent`, `replaceContent`, `unloadContent` — these create/destroy viewer content, exactly
the same reason `addLayer`/`removeLayer`/`displayContent`/`createOverlay` are already excluded
(specs/049's own allowlist doc comment). Content composed by a model must not be able to load or
destroy viewer content as a side effect of a user clicking something inside a panel. Lucy loads
content through the capability mechanism (research D8), which is a deliberate, user-request-driven
act with its own authorization path — not a click inside already-rendered, untrusted panel
content.

`getElementInfo`, `getCameraState`, `getReferencePoint`, `listContent` — read-only queries with no
viewer-visible side effect. Not added to the allowlist because the allowlist governs *actions* a
rendered block can trigger (a button, a table-row click) — these are not actions, they have no
`ActionAffordance` use case; a panel presents element information by having Lucy compose it into
the panel's own content at request time, not by the panel querying the viewer live.
