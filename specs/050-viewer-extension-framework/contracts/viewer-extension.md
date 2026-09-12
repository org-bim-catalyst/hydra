# Contract: Viewer Extension

**Feature**: `050-viewer-extension-framework`

What a viewer capability implements to be loaded as an extension. This is the contract specs/051 extends and specs/052 is the first real consumer of — it is meant to be stable.

## Shape

```ts
interface ViewerExtension {
  id: string
  manifest: ExtensionManifest
  start(context: ExtensionContext): void | Promise<void>
  stop(): void | Promise<void>
  activate?(mode?: string): void      // only when manifest.toggleable
  deactivate?(): void                 // only when manifest.toggleable
}

interface ExtensionManifest {
  displayName: string
  description: string
  toggleable?: boolean        // default false
  startsWithViewer?: boolean  // default true
}
```

An extension is produced by a factory — `createPanelsExtension()` — not by extending a base class. Nothing here needs an is-a relationship, and the reference model's class-inheritance shape is an artefact of its age, not a requirement.

## Registering and declaring

```ts
// At module load — once, by the extension's own module.
viewerExtensionRegistry.register(createPoiMarkerExtension())

// In declared.ts — the list the viewer starts.
export const DECLARED_EXTENSIONS = ['viewer.panels', 'viewer.poi-marker', /* … */]
```

Registering two extensions under one id is a **configuration error**, thrown in development — never a silent replacement. Declaring an id that was never registered is surfaced visibly at start, and does not stop the other extensions starting.

## Lifecycle rules

| Rule | Behaviour |
|---|---|
| Start when already started | No-op. No duplicate contributions. |
| Stop when not started | No-op. No error. |
| Stop while still starting | The stop is honoured; anything the in-flight start contributes is discarded. |
| `start()` throws or times out | That extension is marked failed and named to the user; every other extension still starts. |
| `stop()` throws | Surfaced and recorded; every other extension still stops. |
| Viewer closes | Every running extension is stopped and every contribution withdrawn. |
| Activate a non-toggleable extension | Rejected visibly, not silently ignored. |

`start()` may be async. The loader does not await the declared set as a group, so a slow extension cannot delay the viewer or the others.

## What an extension must not do

- **Import `viewerEngine` directly.** Reach the viewer only through the context — that is what makes the extension testable without a viewer, and what lets the framework guarantee teardown.
- **Assume another extension has started.** There is no dependency mechanism, by design. An extension that needs something another provides should read shared state, not the other extension.
- **Assume a contribution host already exists.** Contribute whenever; the host renders it when it mounts. A contribution is never discarded for being early.
- **Hold anything it does not withdraw itself that the context did not give it.** The framework withdraws every contribution made through the context; anything else is the extension's own responsibility in `stop()`.

## Writing one

```ts
export function createPoiMarkerExtension(): ViewerExtension {
  return {
    id: 'viewer.poi-marker',
    manifest: {
      displayName: 'Point-of-interest marker',
      description: 'Marks the agent-confirmed location on the map.',
    },
    start(context) {
      context.contributeOverlay(POIMarkerOverlay)
    },
    stop() {
      // Nothing: the overlay was contributed through the context, so the framework
      // withdraws it. A stop body is only needed for what the context never saw.
    },
  }
}
```

That is the whole shape of three of the four migrated capabilities. The existing component file is untouched.
