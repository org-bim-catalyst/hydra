import type { ComponentType } from 'react'
import type { ExtensionContext } from './context'

/** contracts/viewer-extension.md — what an extension declares about itself, separate from what
 * it does. `toggleable`/`startsWithViewer` default to `false`/`true` respectively wherever a
 * manifest is read, never baked into the object at construction, so a partial manifest literal
 * stays valid as the framework's defaults change (data-model.md). */
export interface ExtensionManifest {
  displayName: string
  description: string
  toggleable?: boolean
  startsWithViewer?: boolean
}

/** contracts/viewer-extension.md — a self-contained viewer capability. The viewer starts and
 * stops it; it knows the viewer, the viewer does not know it. Produced by a factory function
 * (`createPanelsExtension()`), never a base class to extend (constitution §IV, research D1) —
 * there is no is-a relationship to model, unlike the class-inheritance shape of the reference
 * model this feature takes its structure from. */
export interface ViewerExtension {
  id: string
  manifest: ExtensionManifest
  start(context: ExtensionContext): void | Promise<void>
  stop(): void | Promise<void>
  /** Only meaningful when `manifest.toggleable` is true (FR-002, FR-004). */
  activate?(mode?: string): void
  deactivate?(): void
}

/** data-model.md "Lifecycle State" — where one extension is, tracked per id in the store.
 * `active`/`inactive` is a second, independent axis for `toggleable` extensions only — a
 * stopped extension is neither, not a sixth state. */
export type LifecycleState = 'not-started' | 'starting' | 'started' | 'failed' | 'stopped'

export type ActivationState = 'active' | 'inactive'

/** data-model.md "Extension Manifest" — the extended state the store carries per extension,
 * beyond the static contract above: what phase it's in, why it failed (if it did), and whether
 * a toggleable extension is currently active. */
export interface ExtensionRuntimeState {
  lifecycle: LifecycleState
  /** Present only when `lifecycle === 'failed'` — shown to the user via `ExtensionFailureNotice` (FR-029). */
  failureReason: string | null
  /** Present only when `manifest.toggleable` is true. */
  activation: ActivationState | null
  /** The most recent event-handler exception, if any (FR-017). Deliberately separate from
   * `failureReason`/`lifecycle`: the extension is still running — an event handler throwing
   * does not mean start or stop failed — but constitution §2.VIII forbids a caught exception
   * being logged-only, so this still needs a visible path, just not the same one as a lifecycle
   * failure. `ExtensionFailureNotice` renders both kinds, worded differently. */
  lastEventError: string | null
}

/** contracts/extension-context.md — an entry contributed to the viewer-embedded toolbar this
 * feature builds (research D6). Deliberately minimal: an id (for stable ordering and removal),
 * a label, an icon, and a click handler that reaches the viewer through the extension's own
 * context closure — the entry itself carries no reference back to the viewer. */
export interface ToolbarEntry {
  id: string
  label: string
  icon: ComponentType
  onClick: () => void
}

/** data-model.md "Contribution" — something an extension added through its context, recorded
 * against its id so it can be withdrawn in full without the author's cooperation (FR-014,
 * FR-015). A discriminated union so the store can hold a flat list of mixed contribution kinds
 * and withdraw each correctly by its own `kind`. */
export type Contribution =
  | { kind: 'overlay'; extensionId: string; component: ComponentType }
  | { kind: 'toolbarEntry'; extensionId: string; entry: ToolbarEntry }
  | { kind: 'livePanelKind'; extensionId: string; typeKey: string }
  | { kind: 'eventSubscription'; extensionId: string; unsubscribe: () => void }
