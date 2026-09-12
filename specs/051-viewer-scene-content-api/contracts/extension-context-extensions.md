# Contract: Extension Context Extensions

**Feature**: `051-viewer-scene-content-api`

Extends specs/050's `contracts/extension-context.md` `ExtensionContext` shape with drawing/content/
camera access. Additive only — every existing member (`engine`, `on`, `contributeOverlay`,
`contributeToolbarEntry`, `registerLivePanelKind`, `openPanel`) is unchanged.

## New Context Members

```ts
interface ExtensionContext {
  // ...existing members, unchanged...

  /** research D3 (specs/051) — acquires this extension's own isolated Drawing Space. Idempotent
   * per extension: calling it twice returns the same handle, not two groups (mirrors specs/050's
   * start-when-started idempotency posture). Automatically released on stop — never call
   * `release()` yourself; there isn't one exposed here. */
  acquireDrawingSpace(): DrawingSpaceHandle
}
```

`loadContent`/`replaceContent`/`unloadContent`/`getReferencePoint`/`getCameraState`/
`getElementInfo`/`selectAndFrame` are **not** added to the context — they are reached through
`context.engine` exactly like every other published command (`zoomToLocation`, `select`, etc.),
since they are ordinary viewer commands, not contribution-tracked resources the way a Drawing
Space is. Only `acquireDrawingSpace` needs a dedicated context member, because it is the one new
capability that requires framework-side teardown tracking (research D3).

## Updated `Contribution` Union

```ts
type Contribution =
  | { kind: 'overlay'; extensionId: string; component: ComponentType }
  | { kind: 'toolbarEntry'; extensionId: string; entry: ToolbarEntry }
  | { kind: 'livePanelKind'; extensionId: string; typeKey: string }
  | { kind: 'eventSubscription'; extensionId: string; unsubscribe: () => void }
  | { kind: 'drawingSpace'; extensionId: string; release: () => void }        // NEW
  | { kind: 'frameSubscription'; extensionId: string; unsubscribe: () => void } // NEW
```

Both new kinds are withdrawn by specs/050's existing `loader.ts` `withdrawContributions(id)` path
exactly like the four existing kinds — no new withdrawal mechanism, the same loop that already
handles `livePanelKind` (unregister) and `eventSubscription` (unsubscribe) grows two more `case`
branches.

## What Remains Deliberately Absent

Unchanged from specs/050's own list, plus one addition specific to this feature:

- **No raw `THREE.Scene`/`THREE.Camera`/`THREE.WebGLRenderer` access.** The entire point of
  `DrawingSpaceHandle` (contracts/coordinate-frame-and-drawing-space.md) is that a capability never
  receives these — giving them out here would make every isolation guarantee in this feature
  advisory rather than structural.
