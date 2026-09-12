import type { ComponentType } from 'react'
import type { ZodType } from 'zod'
import type { PanelChrome } from '../chrome/chrome'
import type { PanelContent } from '../content/blocks'

/** data-model.md — panels open at most this many concurrently (FR-022); the least-recently-focused
 * open panel is evicted automatically to make room for a new one past this cap. */
export const MAX_CONCURRENT_PANELS = 10

/** FR-005/Edge Cases ("resize below a usable minimum size") — the floor `react-rnd` enforces so a
 * panel can never be resized down to something unusably small. */
export const MIN_PANEL_WIDTH = 240
export const MIN_PANEL_HEIGHT = 160

/** data-model.md "Viewer Context Association" (spec FR-013). */
export interface ViewerContextAssociation {
  layerId: string | null
  elementId: string | null
}

/** data-model.md "Live Panel Kind" (spec FR-022/FR-026, contracts/panel-request.md). Registered
 * only for panels whose content is code rather than data — a "live" panel. Content panels
 * (specs/049) need no registration at all; this narrows what `registry.ts` used to hold for
 * every panel kind down to the minority that still needs it. */
export interface PanelTypeDefinition<T = unknown> {
  typeKey: string
  renderer: ComponentType<{ data: T }>
  schema: ZodType<T>
  chrome: PanelChrome
}

/** data-model.md "Panel Request" (contracts/panel-request.md) — the wire shape a `PanelRequested`
 * push or a direct `floatingPanelStore.openPanel` call supplies. Discriminated on `kind`: a
 * `content` request carries a validated block document Lucy composed freely; a `live` request
 * names a registered panel kind by `typeKey`, exactly as every panel request did before this
 * feature (research D5). */
interface PanelRequestCommon {
  requestId: string
  title: string
  chrome?: Partial<PanelChrome> | null
  position?: { x: number; y: number } | null
  contextAssociation?: { layerId?: string; elementId?: string } | null
}

export interface ContentPanelRequest extends PanelRequestCommon {
  kind: 'content'
  content: unknown
}

export interface LivePanelRequest extends PanelRequestCommon {
  kind: 'live'
  typeKey: string
  data: unknown
}

export type PanelRequest = ContentPanelRequest | LivePanelRequest

export type PanelValidationStatus = 'valid' | 'invalid' | 'unknown-type'

export type PanelContextStatus = 'current' | 'stale' | 'invalid' | null

/** data-model.md "Floating Panel" — one open panel instance owned by `floatingPanelStore`.
 * `typeKey` is present only for a live panel; `content` only for a content panel — the two are
 * mutually exclusive, mirroring `PanelRequest`'s discriminated shape, but kept as plain optional
 * fields here (rather than a second discriminated union) because every other field — validation,
 * position, size, minimize state — is identical between the two and the store's update helpers
 * operate on them uniformly regardless of kind. */
export interface FloatingPanel {
  id: string
  kind: 'content' | 'live'
  typeKey?: string
  content?: PanelContent
  title: string
  data: unknown
  validationStatus: PanelValidationStatus
  /** Present only when `validationStatus === 'invalid'` — the zod issue summary, shown in a
   * collapsible details section (contracts/panel-hub-events.md). */
  validationError: string | null
  position: { x: number; y: number }
  size: { width: number; height: number }
  chrome: PanelChrome
  minimized: boolean
  /** The panel's size/position immediately before minimizing, restored exactly on restore (FR-006). */
  restoreState: { position: { x: number; y: number }; size: { width: number; height: number } } | null
  zOrder: number
  lastFocusedAtUtc: number
  opacityOverride: number | null
  contextAssociation: ViewerContextAssociation | null
  contextStatus: PanelContextStatus
}
