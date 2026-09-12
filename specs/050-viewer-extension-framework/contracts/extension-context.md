# Contract: Extension Context

**Feature**: `050-viewer-extension-framework`

What a starting extension receives, and the **only** route it has to the viewer (spec FR-011). Every contribution made through it is recorded against the calling extension, which is what makes "stopping an extension withdraws all of it" a property of the framework rather than a request to the author (spec FR-014, FR-015).

## Shape

```ts
interface ExtensionContext {
  readonly engine: IViewerEngine
  on<E extends ViewerEventType>(type: E, handler: ViewerEventHandler<E>): void
  contributeOverlay(component: ComponentType): void
  contributeToolbarEntry(entry: ToolbarEntry): void
  registerLivePanelKind(definition: PanelTypeDefinition): void
  openPanel(request: PanelRequest): void
}
```

The context is per-extension. An extension never passes its own id to anything — every call already knows who made it.

## Members

| Member | Notes |
|---|---|
| `engine` | The existing published `IViewerEngine`, passed through unchanged. This feature adds no command and changes no command's meaning (spec FR-012); specs/051 is what grows this surface. |
| `on(type, handler)` | Subscribes to a viewer event. The unsubscribe is recorded, so it happens on stop whether or not the extension remembers. A handler that throws is contained and surfaced — it does not stop other subscribers receiving the event (spec FR-017). |
| `contributeOverlay(component)` | Contributes a component rendered over the viewer surface. Contribute whenever — before the host exists is fine (research D3). |
| `contributeToolbarEntry(entry)` | Contributes an entry to the **viewer-embedded** toolbar — the surface this feature builds inside the viewer, not the workspace overlay outside it (research D6). Entries from multiple extensions appear in a defined, stable order (spec FR-022), and the toolbar is absent or empty rather than broken when nothing has contributed (FR-023). |
| `registerLivePanelKind(definition)` | Registers a live panel kind into specs/049's panel registry. Withdrawn on stop; an open panel of that kind is closed or shown as unavailable rather than left rendering against a capability that is gone (spec FR-036). |
| `openPanel(request)` | Opens a panel. Not tracked for teardown — a panel the user can close is theirs, not the extension's, and closing a user's panel because a capability stopped would be surprising. |

## Why tracking is framework-side

The reference model this feature takes its shape from leaves teardown to the extension author, and its own published samples get it wrong — one leaks its panel and a camera-change listener on unload, another never removes four event listeners. That is not carelessness so much as evidence: teardown-by-discipline fails even for the people who wrote the framework.

So every context helper records what it did, and `stop()` exists for what the context never saw rather than for the routine case. Three of the four capabilities migrated by this feature have an empty `stop()` as a result.

## What is deliberately absent

- **No `getExtension(id)` / no inter-extension handles.** Extensions do not reach each other; there is no dependency mechanism to get wrong. Shared state is shared through stores, which already exist.
- **No raw DOM container.** Contributions are React component types (research D1) — a container would strand contributed UI outside React's tree, losing theme, store subscriptions and every existing testing approach.
- **No scene, renderer or frame access.** That is specs/051, and adding it early would mean designing the four constraints that spec exists to settle — single anchor, ENU convention, framework-owned redraw, declared renderer state — a feature ahead of schedule.
