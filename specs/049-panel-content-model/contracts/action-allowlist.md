# Contract: Action Allowlist

**Version**: 1 | **Feature**: `049-panel-content-model`

The closed set of viewer operations that panel content may invoke. This is a **security boundary**, not a convenience list: panel content is composed by a language model, and §8 of the constitution treats model output as untrusted data that must never become instruction.

## Enforcement rules

1. **Closed set.** `viewer/panels/actions/allowlist.ts` maps each command name to an explicit invoker and an argument schema. A command not in the map is not reachable.
2. **No dynamic dispatch.** Never index `viewerEngine` by a content-supplied string. Each invoker is written out, so the reachable operations are readable in one file.
3. **Validated at render, not activation.** An action whose command is unknown, or whose args fail their schema, makes its entry render **inert** — not styled as activatable, not keyboard-reachable, no handler attached. Refusing on click would still have advertised an operation that was never permitted (spec FR-013, FR-014).
4. **Refusals are recorded.** Every rejected action is logged for diagnosis. Silent rejection would hide a model consistently attempting something it should not.
5. **Failures after invocation are surfaced.** The engine's commands return a result rather than throwing. A command that reports failure — a target that no longer exists — tells the user, rather than appearing to succeed (spec FR-015).

## Commands (v1)

Every entry is idempotent and changes only what is shown or selected.

| Command | Arguments | Effect |
|---|---|---|
| `select` | `{ layerId: string, elementId: string }` | Selects the element. Fails visibly if the element is not registered as selectable. |
| `clearSelection` | `{}` | Clears the current selection. |
| `zoomToLocation` | `{ latitude: number, longitude: number, zoom?: number }` | Latitude −90…90, longitude −180…180. Frames the location. |
| `setLayerVisibility` | `{ layerId: string, visible: boolean }` | Shows or hides a layer. Fails visibly for an unknown layer. |
| `setViewMode` | `{ mode: 'isometric' \| 'plan' }` | Switches camera perspective. |
| `setMapStyle` | `{ mapStyle: 'roadmap' \| 'satellite' \| 'hybrid' \| 'buildings-only' }` | Switches base map style. |

`fitBounds` is intentionally not included, despite existing on the concrete viewer engine: it was never published on the viewer's public `IViewerEngine` surface, and widening that published interface is the kind of viewer-command change this feature keeps out of scope. `zoomToLocation` covers framing for v1.

## Deliberately excluded

`addLayer`, `removeLayer`, `displayContent`, `createOverlay` are **not** allowlisted, though they exist on the engine today. They mutate what the viewer contains rather than how it is shown, and content composed by a model should not be able to add or destroy viewer content as a side effect of the user clicking a table row.

Content commands are specs/051's subject, where they can be designed with the right guarantees — identity, lifecycle, resource release — and only then considered for this list.

## Adding a command

A command joins this list only when all of these hold:

- It changes what is shown or selected, not what the viewer contains.
- It is safe to invoke repeatedly, including by accident.
- It cannot cause a request to any address the content chooses.
- It cannot reveal anything the user is not already entitled to see.
- Its failure mode is a visible message, not an exception.

Adding one means: a map entry, an argument schema, a unit test proving a malformed call is refused, and an update to this contract. specs/051 will add the commands it defines that satisfy these conditions.
