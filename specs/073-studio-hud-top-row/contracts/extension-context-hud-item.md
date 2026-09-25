# Contract Delta: `ExtensionContext.contributeHudItem`

**Amends**: [specs/050-viewer-extension-framework/contracts/extension-context.md](../../050-viewer-extension-framework/contracts/extension-context.md)
**Research**: [research.md](../research.md) D3

## Addition

```ts
interface ExtensionContext {
  // …existing members unchanged…
  /** Contributes a compact, glanceable status item to the workspace's top-left HUD row, after
   * the host's own items (Home, project title, weather). The component should render a
   * `HudCard` (40 px, shared surface) or `null`. Withdrawn automatically on stop/failure. */
  contributeHudItem(component: ComponentType): void
}
```

## Guarantees

| # | Guarantee |
|---|---|
| X1 | Records `{ kind: 'hudItem', extensionId, component }` through `addContribution`, the same way `contributeOverlay` records `overlay`. |
| X2 | `ExtensionHudItemHost` renders from `viewerExtensionStore`. A contribution made before the host mounts appears exactly like one made after (mirrors specs/050 FR-018/FR-020). |
| X3 | `removeContributionsFor(extensionId)` removes the item. After the extension stops or fails, it is no longer rendered (specs/050 FR-014/FR-015). |
| X4 | `ExtensionOverlayHost` ignores `hudItem`, and `ExtensionHudItemHost` ignores every other kind. |
| X5 | Rendering order is contribution order. Instance keys are `extensionId:ordinal`. |
| X6 | The host renders its items as direct flex children of the row (a fragment, no wrapper element), so the row's gap and wrapping apply to each item individually. An error boundary adds no DOM, so X6 still holds with X7 in place. |
| X7 | Each contributed component (hudItem **and** overlay) renders inside its own `ContributionErrorBoundary` (research D3a). A render throw records `setLifecycle(extensionId, 'failed', 'Render failed: {message}')`, which `ExtensionFailureNotice` shows. Only that item renders `null`. Sibling items, the rest of the row, and the enclosing `WorkspaceOverlay` (chat, controls) keep rendering. |

## Migration of built-ins

| Extension | Before | After |
|---|---|---|
| `viewer.boundary-confidence` | `contributeOverlay(SiteBoundaryConfidenceBadge)` | `contributeHudItem(SiteBoundaryConfidenceBadge)` |

`DECLARED_EXTENSIONS` doesn't change.
