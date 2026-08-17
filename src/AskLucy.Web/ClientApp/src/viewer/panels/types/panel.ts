import type { ComponentType } from 'react'
import type { ZodType } from 'zod'

/** data-model.md — panels open at most this many concurrently (FR-022); the least-recently-focused
 * open panel is evicted automatically to make room for a new one past this cap. */
export const MAX_CONCURRENT_PANELS = 10

/** data-model.md "Viewer Context Association" (spec FR-013). */
export interface ViewerContextAssociation {
  layerId: string | null
  elementId: string | null
}

/** data-model.md "Panel Type Definition" (spec FR-001/FR-015, contracts/panel-type-registry.md).
 * Registered once per panel type under a unique `typeKey`; the AI selects among already-registered
 * types by key when requesting a panel. */
export interface PanelTypeDefinition<T = unknown> {
  typeKey: string
  renderer: ComponentType<{ data: T }>
  schema: ZodType<T>
  defaultSize: { width: number; height: number }
  resizable: boolean
}

/** data-model.md "Panel Request" — the wire shape a `PanelRequested` push (contracts/panel-hub-events.md)
 * or a direct `floatingPanelStore.openPanel` call supplies. */
export interface PanelRequest {
  requestId: string
  typeKey: string
  title: string
  data: unknown
  position?: { x: number; y: number } | null
  contextAssociation?: { layerId?: string; elementId?: string } | null
}

export type PanelValidationStatus = 'valid' | 'invalid' | 'unknown-type'

export type PanelContextStatus = 'current' | 'stale' | 'invalid' | null

/** data-model.md "Floating Panel" — one open panel instance owned by `floatingPanelStore`. */
export interface FloatingPanel {
  id: string
  typeKey: string
  title: string
  data: unknown
  validationStatus: PanelValidationStatus
  /** Present only when `validationStatus === 'invalid'` — the zod issue summary, shown in a
   * collapsible details section (contracts/panel-hub-events.md). */
  validationError: string | null
  position: { x: number; y: number }
  size: { width: number; height: number }
  resizable: boolean
  minimized: boolean
  /** The panel's size/position immediately before minimizing, restored exactly on restore (FR-006). */
  restoreState: { position: { x: number; y: number }; size: { width: number; height: number } } | null
  zOrder: number
  lastFocusedAtUtc: number
  opacityOverride: number | null
  contextAssociation: ViewerContextAssociation | null
  contextStatus: PanelContextStatus
}
