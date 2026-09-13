import { create } from 'zustand'
import { viewerEngine } from '../../engine/viewerEngineInstance'
import { useViewerEngineStore } from '../../store/viewerEngineStore'
import { HOST_MARGIN } from '../layout/arrangement'
import { panelTypeRegistry } from '../registry'
import { DEFAULT_CONTENT_CHROME, resolveChrome } from '../chrome/chrome'
import { panelContentSchema } from '../content/blocks'
import {
  MAX_CONCURRENT_PANELS,
  type ClosedPanelEntry,
  type FloatingPanel,
  type PanelContextStatus,
  type PanelRequest,
} from '../types/panel'

/** specs/054 FR-013 — a panel's context association can go stale while it's *closed* (the tray
 * holds it, not the live subscription below, which only touches panels currently in `panels`).
 * Rather than assuming a freshly (re)opened panel's association is `'current'`, this checks the
 * referenced layer's existence at creation time, so a reopened panel whose layer was removed
 * while it sat in the tray shows the same `'invalid'` indicator an already-open panel would —
 * without waiting for a `layerRemoved` event that already happened before this panel existed. */
function initialContextStatus(contextAssociation: { layerId: string | null } | null): PanelContextStatus {
  if (!contextAssociation) return null
  if (!contextAssociation.layerId) return 'current'
  const layerExists = useViewerEngineStore.getState().layers.some((layer) => layer.id === contextAssociation.layerId)
  return layerExists ? 'current' : 'invalid'
}

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

/** specs/054 D8 — reconstructs the `PanelRequest` that could recreate `panel`, so closing can
 * retain just enough to reopen it later. Deliberately drops position/size/zOrder/minimized (a
 * reopened panel is placed fresh, spec Assumptions) and works identically for either `kind`
 * (FR-012) — the branch below only supplies the field each kind's request shape requires. */
function buildRequestFromPanel(panel: FloatingPanel): PanelRequest {
  const contextAssociation = panel.contextAssociation
    ? { layerId: panel.contextAssociation.layerId ?? undefined, elementId: panel.contextAssociation.elementId ?? undefined }
    : null

  const common = { requestId: panel.id, title: panel.title, chrome: panel.chrome, contextAssociation }

  return panel.kind === 'content'
    ? { ...common, kind: 'content', content: panel.content }
    : { ...common, kind: 'live', typeKey: panel.typeKey ?? '', data: panel.data }
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
  /** specs/054 FR-006/FR-010 — the bounded, most-recent-first reopen tray. Closing a panel appends
   * here instead of discarding it; capped at `MAX_CONCURRENT_PANELS`, oldest dropped first. */
  closedPanels: ClosedPanelEntry[]
  openPanel: (request: PanelRequest) => void
  /** specs/054 FR-006 — moves the panel into `closedPanels` (as a reconstructed request) rather
   * than discarding it outright, so `reopenPanel` can bring it back later. */
  closePanel: (id: string) => void
  /** specs/054 FR-009 — removes the entry from `closedPanels` and reopens it via the normal
   * `openPanel` path, so validation, chrome resolution, and context-status derivation (FR-013) are
   * identical to a first-time open. A no-op if the id names no current entry. */
  reopenPanel: (id: string) => void
  focusPanel: (id: string) => void
  minimizePanel: (id: string) => void
  restorePanel: (id: string) => void
  updatePosition: (id: string, position: { x: number; y: number }) => void
  updateSize: (id: string, size: { width: number; height: number }) => void
  clampToViewport: (bounds: { width: number; height: number }) => void
  /** specs/054 FR-005e/D5 — applies a computed arrangement pass. `zOrder` (present only in cascade
   * mode, contracts/arrangement.md) carries relative ranks (1 = lowest); they're translated here
   * into real, monotonically-increasing zOrder values above every existing panel's current zOrder,
   * so a freshly-cascaded panel never ends up buried under one that was merely focused earlier. */
  applyArrangement: (positions: Map<string, { x: number; y: number }>, zOrder: Map<string, number> | null) => void
  /** specs/054 FR-005e — clears `manuallyPlaced` on every panel so the next arrangement pass treats
   * all of them as placement targets again, including ones the user previously moved. Does not
   * itself compute new positions — the caller (`FloatingPanelHost`) runs a fresh arrangement pass
   * immediately after, since only it has the DOM access `computeArrangement`'s inputs need. */
  arrangeAll: () => void
  setContextStatus: (id: string, status: PanelContextStatus) => void
  /** specs/050 FR-036 — a live panel kind withdrawn (its extension stopped) while a panel of that
   * kind is open must not keep rendering against a capability that no longer exists. Reuses the
   * `unknown-type` status `openPanel` already gives a request naming a kind that was never
   * registered — the panel becomes "shown as unavailable" rather than closed outright, since the
   * kind may register again if the extension restarts. */
  markLivePanelKindUnavailable: (typeKey: string) => void
}

/** data-model.md "FloatingPanel" store — session-scoped only (no `persist` middleware, matches
 * `workspaceOverlayStore`'s convention per spec Assumption: panel layout is not expected to
 * survive a reload). Owns every open panel's lifecycle: creation (with registry resolution + zod
 * validation, FR-016/FR-017), cascade placement (FR-021), drag/resize/minimize/focus state
 * (FR-004/FR-005/FR-006/FR-009), and the fixed-cap LRU eviction policy (FR-022). */
export const useFloatingPanelStore = create<FloatingPanelState>()((set, get) => ({
  panels: [],
  cascadeIndex: 0,
  closedPanels: [],

  openPanel: (request) => {
    // Branches on request kind (research D5/D9): a `content` request is validated against the
    // vocabulary directly; a `live` request keeps the pre-existing registry resolution path
    // unchanged. Everything below this block — cascade, z-order, LRU eviction, minimize/restore,
    // clamping, context association — is untouched by which branch ran.
    let validationStatus: FloatingPanel['validationStatus']
    let validationError: string | null = null
    let content: FloatingPanel['content']
    let typeKey: FloatingPanel['typeKey']
    let data: unknown
    let chromeBase = DEFAULT_CONTENT_CHROME

    if (request.kind === 'content') {
      const parsed = panelContentSchema.safeParse(request.content)
      if (parsed.success) {
        validationStatus = 'valid'
        content = parsed.data
      } else {
        validationStatus = 'invalid'
        validationError = parsed.error.issues.map((issue) => issue.message).join('; ')
      }
    } else {
      typeKey = request.typeKey
      data = request.data
      const definition = panelTypeRegistry.resolve(request.typeKey)
      if (!definition) {
        validationStatus = 'unknown-type'
      } else {
        chromeBase = definition.chrome
        const parsed = definition.schema.safeParse(request.data)
        if (parsed.success) {
          validationStatus = 'valid'
          data = parsed.data
        } else {
          validationStatus = 'invalid'
          validationError = parsed.error.issues.map((issue) => issue.message).join('; ')
        }
      }
    }

    const chrome = resolveChrome(request.chrome, chromeBase)

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
      kind: request.kind,
      typeKey,
      content,
      title: request.title,
      data,
      validationStatus,
      validationError,
      position,
      size: chrome.defaultSize,
      chrome,
      minimized: false,
      restoreState: null,
      zOrder: nextZOrder(state.panels),
      lastFocusedAtUtc: nextTimestamp(),
      opacityOverride: null,
      contextAssociation,
      contextStatus: initialContextStatus(contextAssociation),
      manuallyPlaced: false,
    }

    set((s) => {
      // Re-opening a panel that is ALREADY open is a refresh, not a re-creation: its content and
      // title change, but everything the *user* controls — where they dragged it, how they resized
      // it, whether they minimized it, and its place in the z-order — is preserved. Replacing those
      // wholesale made a repeat `openPanel` on a stable requestId yank the panel back to a cascade
      // position, reset its size, un-minimize it and steal focus; for any caller that refreshes a
      // live panel's content on a timer (specs/052's solar figures do so on every playback tick)
      // that is once per frame, which makes the panel unusable while it updates.
      const existing = s.panels.find((p) => p.id === panel.id)
      const nextPanel: FloatingPanel = existing
        ? {
            ...panel,
            position: existing.position,
            size: existing.size,
            minimized: existing.minimized,
            restoreState: existing.restoreState,
            zOrder: existing.zOrder,
            lastFocusedAtUtc: existing.lastFocusedAtUtc,
            opacityOverride: existing.opacityOverride,
            manuallyPlaced: existing.manuallyPlaced,
          }
        : panel

      let panels = [...s.panels.filter((p) => p.id !== nextPanel.id), nextPanel]

      // FR-022: enforce the fixed cap by evicting the least-recently-focused *other* panel.
      if (panels.length > MAX_CONCURRENT_PANELS) {
        const evictable = panels.filter((p) => p.id !== nextPanel.id)
        const leastRecentlyFocused = evictable.reduce((oldest, candidate) =>
          candidate.lastFocusedAtUtc < oldest.lastFocusedAtUtc ? candidate : oldest,
        )
        panels = panels.filter((p) => p.id !== leastRecentlyFocused.id)
      }

      // A refresh of an already-open panel consumes no new cascade slot — it never took a fresh
      // position, so advancing the cascade would push the *next* genuinely new panel off-pattern.
      return { panels, cascadeIndex: usesCascade && !existing ? s.cascadeIndex + 1 : s.cascadeIndex }
    })
  },

  closePanel: (id) =>
    set((s) => {
      const panel = s.panels.find((p) => p.id === id)
      const panels = s.panels.filter((p) => p.id !== id)
      if (!panel) return { panels }

      // FR-006/Edge Cases ("closing a panel and then closing the reopened version again") — a
      // stale entry for the same id (from a previous close, if it was reopened and closed once
      // more) is replaced, not duplicated.
      const entry: ClosedPanelEntry = { request: buildRequestFromPanel(panel), closedAtUtc: nextTimestamp() }
      const closedPanels = [entry, ...s.closedPanels.filter((e) => e.request.requestId !== id)].slice(
        0,
        MAX_CONCURRENT_PANELS,
      )
      return { panels, closedPanels }
    }),

  reopenPanel: (id) => {
    const entry = get().closedPanels.find((e) => e.request.requestId === id)
    if (!entry) return
    set((s) => ({ closedPanels: s.closedPanels.filter((e) => e.request.requestId !== id) }))
    get().openPanel(entry.request)
  },

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

  // FR-005d/D5 — these two run only from user gestures (react-rnd's onDrag/onDragStop/onResizeStop,
  // the keyboard-nudge handler): marking manuallyPlaced here, and nowhere else, is what makes the
  // flag an accurate record of "the user chose this", with no separate bookkeeping required.
  updatePosition: (id, position) =>
    set((s) => ({
      panels: s.panels.map((panel) => (panel.id === id ? { ...panel, position, manuallyPlaced: true } : panel)),
    })),

  updateSize: (id, size) =>
    set((s) => ({
      panels: s.panels.map((panel) => (panel.id === id ? { ...panel, size, manuallyPlaced: true } : panel)),
    })),

  applyArrangement: (positions, zOrder) =>
    set((s) => {
      const zOrderBase = zOrder ? nextZOrder(s.panels) : 0
      return {
        panels: s.panels.map((panel) => {
          const position = positions.get(panel.id)
          if (!position) return panel
          const rank = zOrder?.get(panel.id)
          return rank === undefined ? { ...panel, position } : { ...panel, position, zOrder: zOrderBase + rank - 1 }
        }),
      }
    }),

  arrangeAll: () => set((s) => ({ panels: s.panels.map((panel) => ({ ...panel, manuallyPlaced: false })) })),

  // FR-018/Edge Cases ("viewport resize") — react-rnd's `bounds="parent"` keeps a panel within
  // bounds while the user is actively dragging/resizing it, but doesn't retroactively move a
  // panel that's already outside after the window itself shrinks. This does that: every open
  // panel is nudged back within the current viewer bounds, never left permanently unreachable.
  clampToViewport: (bounds) =>
    set((s) => ({
      panels: s.panels.map((panel) => {
        // specs/054 (feedback 2026-09-13): keeps the same HOST_MARGIN the arrangement engine
        // uses, so a panel nudged back on-screen after a resize doesn't end up flush against the
        // raw edge either.
        const maxX = Math.max(HOST_MARGIN, bounds.width - panel.size.width - HOST_MARGIN)
        const maxY = Math.max(HOST_MARGIN, bounds.height - panel.size.height - HOST_MARGIN)
        const x = Math.min(Math.max(panel.position.x, HOST_MARGIN), maxX)
        const y = Math.min(Math.max(panel.position.y, HOST_MARGIN), maxY)
        return x === panel.position.x && y === panel.position.y ? panel : { ...panel, position: { x, y } }
      }),
    })),

  setContextStatus: (id, status) =>
    set((s) => ({
      panels: s.panels.map((panel) => (panel.id === id ? { ...panel, contextStatus: status } : panel)),
    })),

  markLivePanelKindUnavailable: (typeKey) =>
    set((s) => ({
      panels: s.panels.map((panel) =>
        panel.kind === 'live' && panel.typeKey === typeKey
          ? { ...panel, validationStatus: 'unknown-type', validationError: null }
          : panel,
      ),
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
