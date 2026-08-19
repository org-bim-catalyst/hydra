import { create } from 'zustand'
import { viewerEngine } from '../../engine/viewerEngineInstance'
import { panelTypeRegistry } from '../registry'
import { MAX_CONCURRENT_PANELS, type FloatingPanel, type PanelContextStatus, type PanelRequest } from '../types/panel'

/** FR-021 — cascade placement for a panel request that doesn't specify a position: each new
 * panel opens offset from the previous one, wrapping back toward the starting corner before
 * reaching the opposite edge, so panels opened in sequence stay individually reachable. */
const CASCADE_START = { x: 40, y: 40 }
const CASCADE_STEP = 32
const CASCADE_STEPS_BEFORE_WRAP = 10

function cascadePosition(cascadeIndex: number): { x: number; y: number } {
  const step = cascadeIndex % CASCADE_STEPS_BEFORE_WRAP
  return { x: CASCADE_START.x + step * CASCADE_STEP, y: CASCADE_START.y + step * CASCADE_STEP }
}

function nextZOrder(panels: FloatingPanel[]): number {
  return panels.reduce((max, panel) => Math.max(max, panel.zOrder), 0) + 1
}

/** `Date.now()` alone can tie when several panels open/focus in the same millisecond (spec Edge
 * Cases: "two or more AI panel requests arrive at nearly the same time") — a tie would make the
 * LRU eviction below fall back to array order instead of true recency. This keeps values close to
 * wall-clock time (data-model.md "epoch ms") while guaranteeing every call returns a strictly
 * later value than the last. */
let lastIssuedTimestamp = 0
function nextTimestamp(): number {
  lastIssuedTimestamp = Math.max(Date.now(), lastIssuedTimestamp + 1)
  return lastIssuedTimestamp
}

interface FloatingPanelState {
  panels: FloatingPanel[]
  cascadeIndex: number
  openPanel: (request: PanelRequest) => void
  closePanel: (id: string) => void
  focusPanel: (id: string) => void
  minimizePanel: (id: string) => void
  restorePanel: (id: string) => void
  updatePosition: (id: string, position: { x: number; y: number }) => void
  updateSize: (id: string, size: { width: number; height: number }) => void
  clampToViewport: (bounds: { width: number; height: number }) => void
  setContextStatus: (id: string, status: PanelContextStatus) => void
}

/** data-model.md "FloatingPanel" store — session-scoped only (no `persist` middleware, matches
 * `workspaceOverlayStore`'s convention per spec Assumption: panel layout is not expected to
 * survive a reload). Owns every open panel's lifecycle: creation (with registry resolution + zod
 * validation, FR-016/FR-017), cascade placement (FR-021), drag/resize/minimize/focus state
 * (FR-004/FR-005/FR-006/FR-009), and the fixed-cap LRU eviction policy (FR-022). */
export const useFloatingPanelStore = create<FloatingPanelState>()((set, get) => ({
  panels: [],
  cascadeIndex: 0,

  openPanel: (request) => {
    const definition = panelTypeRegistry.resolve(request.typeKey)

    let validationStatus: FloatingPanel['validationStatus']
    let validationError: string | null = null
    let data: unknown = request.data
    let size = { width: 400, height: 300 }
    let resizable = true

    if (!definition) {
      validationStatus = 'unknown-type'
    } else {
      size = definition.defaultSize
      resizable = definition.resizable
      const parsed = definition.schema.safeParse(request.data)
      if (parsed.success) {
        validationStatus = 'valid'
        data = parsed.data
      } else {
        validationStatus = 'invalid'
        validationError = parsed.error.issues.map((issue) => issue.message).join('; ')
      }
    }

    const state = get()
    const usesCascade = !request.position
    const position = request.position ?? cascadePosition(state.cascadeIndex)
    const contextAssociation = request.contextAssociation
      ? {
          layerId: request.contextAssociation.layerId ?? null,
          elementId: request.contextAssociation.elementId ?? null,
        }
      : null

    const panel: FloatingPanel = {
      id: request.requestId,
      typeKey: request.typeKey,
      title: request.title,
      data,
      validationStatus,
      validationError,
      position,
      size,
      resizable,
      minimized: false,
      restoreState: null,
      zOrder: nextZOrder(state.panels),
      lastFocusedAtUtc: nextTimestamp(),
      opacityOverride: null,
      contextAssociation,
      contextStatus: contextAssociation ? 'current' : null,
    }

    set((s) => {
      let panels = [...s.panels.filter((existing) => existing.id !== panel.id), panel]

      // FR-022: enforce the fixed cap by evicting the least-recently-focused *other* panel.
      if (panels.length > MAX_CONCURRENT_PANELS) {
        const evictable = panels.filter((existing) => existing.id !== panel.id)
        const leastRecentlyFocused = evictable.reduce((oldest, candidate) =>
          candidate.lastFocusedAtUtc < oldest.lastFocusedAtUtc ? candidate : oldest,
        )
        panels = panels.filter((existing) => existing.id !== leastRecentlyFocused.id)
      }

      return { panels, cascadeIndex: usesCascade ? s.cascadeIndex + 1 : s.cascadeIndex }
    })
  },

  closePanel: (id) => set((s) => ({ panels: s.panels.filter((panel) => panel.id !== id) })),

  focusPanel: (id) =>
    set((s) => {
      const zOrder = nextZOrder(s.panels)
      return {
        panels: s.panels.map((panel) =>
          panel.id === id ? { ...panel, zOrder, lastFocusedAtUtc: nextTimestamp() } : panel,
        ),
      }
    }),

  minimizePanel: (id) =>
    set((s) => ({
      panels: s.panels.map((panel) =>
        panel.id === id
          ? { ...panel, minimized: true, restoreState: { position: panel.position, size: panel.size } }
          : panel,
      ),
    })),

  restorePanel: (id) =>
    set((s) => ({
      panels: s.panels.map((panel) => {
        if (panel.id !== id || !panel.restoreState) return panel
        return {
          ...panel,
          minimized: false,
          position: panel.restoreState.position,
          size: panel.restoreState.size,
          restoreState: null,
        }
      }),
    })),

  updatePosition: (id, position) =>
    set((s) => ({ panels: s.panels.map((panel) => (panel.id === id ? { ...panel, position } : panel)) })),

  updateSize: (id, size) =>
    set((s) => ({ panels: s.panels.map((panel) => (panel.id === id ? { ...panel, size } : panel)) })),

  // FR-018/Edge Cases ("viewport resize") — react-rnd's `bounds="parent"` keeps a panel within
  // bounds while the user is actively dragging/resizing it, but doesn't retroactively move a
  // panel that's already outside after the window itself shrinks. This does that: every open
  // panel is nudged back within the current viewer bounds, never left permanently unreachable.
  clampToViewport: (bounds) =>
    set((s) => ({
      panels: s.panels.map((panel) => {
        const maxX = Math.max(0, bounds.width - panel.size.width)
        const maxY = Math.max(0, bounds.height - panel.size.height)
        const x = Math.min(Math.max(panel.position.x, 0), maxX)
        const y = Math.min(Math.max(panel.position.y, 0), maxY)
        return x === panel.position.x && y === panel.position.y ? panel : { ...panel, position: { x, y } }
      }),
    })),

  setContextStatus: (id, status) =>
    set((s) => ({
      panels: s.panels.map((panel) => (panel.id === id ? { ...panel, contextStatus: status } : panel)),
    })),
}))

// FR-014/US4-AS2, Edge Cases ("a panel's associated viewer object no longer exists") — a panel
// associated with a layer that's removed is marked invalid; one whose associated layer's content
// reloads is marked stale, since the panel's already-rendered data may no longer reflect it.
// Subscribed once at module load: `floatingPanelStore` and `viewerEngine` are both process-wide
// singletons, so this never double-subscribes.
viewerEngine.on('layerRemoved', ({ layerId }) => {
  useFloatingPanelStore.setState((s) => ({
    panels: s.panels.map((panel) =>
      panel.contextAssociation?.layerId === layerId ? { ...panel, contextStatus: 'invalid' } : panel,
    ),
  }))
})

viewerEngine.on('contentLoaded', ({ layerId }) => {
  useFloatingPanelStore.setState((s) => ({
    panels: s.panels.map((panel) =>
      panel.contextAssociation?.layerId === layerId && panel.contextStatus === 'current'
        ? { ...panel, contextStatus: 'stale' }
        : panel,
    ),
  }))
})
