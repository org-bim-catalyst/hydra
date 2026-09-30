import { create } from 'zustand'
import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import type { CameraViewMode } from '../api/commands'
import { openRing, ringAreaSquareMeters } from './ringGeometry'

/**
 * specs/079 data-model.md "siteBoundaryEditStore": one outline-edit session. Module-level, so the
 * session and its unsaved changes survive leaving and returning to /studio (FR-029, memory: keep
 * workspace state). No map dependency — the polygons, the save request and the view restore are
 * driven from hooks that read and write this store; it only holds state and applies transitions.
 */

/** The camera condition captured on entry and restored on exit (research D2). */
export interface ViewState {
  mode: CameraViewMode
  rotationEnabled: boolean
  center: GeoPoint
  zoom: number
  heading: number
  tilt: number
}

export type RingChange =
  | { op: 'move'; ring: number; index: number; before: GeoPoint; after: GeoPoint }
  | { op: 'insert'; ring: number; index: number; after: GeoPoint }
  | { op: 'delete'; ring: number; index: number; before: GeoPoint }
  /** The whole ring swapped for another: deleting several corners at once, rounding a corner, curving an edge, a circle. One undo step. */
  | { op: 'replace'; ring: number; before: GeoPoint[]; after: GeoPoint[] }
  /** Every ring swapped at once, possibly a different number of them: a circle added as a ring of its own, rings merged, a ring split by a cut. */
  | { op: 'replaceAll'; before: GeoPoint[][]; after: GeoPoint[][] }

/** The shape tools that ask for a number (a radius, a bulge) before they act. */
export type ShapeTool = 'round' | 'curve' | 'circle'

/**
 * What the map does with the pointer: `edit` moves and adds corners (Google's own handles); `select`
 * draws a box that picks corners; `arc` waits for a third point to be dropped and draws an arc through it.
 */
export type EditTool = 'edit' | 'select' | 'arc' | 'circle'

/** What a circle drawn on the map does to the outline. */
export type CircleOperation = 'add' | 'cut'

export type EditStatus =
  | { kind: 'editing' }
  | { kind: 'saving' }
  /** FR-018: the session stays open with the user's changes; Retry saves again. */
  | { kind: 'error'; message: string }
  /** FR-019: the outline changed elsewhere; "Load latest" rebases on it. */
  | { kind: 'conflict'; currentRevision: string }

export interface SiteBoundaryEditSession {
  chatId: string
  siteName: string
  /** Sent as `expectedRevision` on Done. */
  baseRevision: string
  /** Cancel's target (FR-013). Open rings — no repeated closing corner. */
  startRings: GeoPoint[][]
  rings: GeoPoint[][]
  undo: RingChange[]
  redo: RingChange[]
  activeRing: number
  /** The most recently selected corner: what Add corner, curve and round act on. */
  selectedCorner: number | null
  /** Every selected corner of the active ring (a box select picks several); `selectedCorner` is always one of them. */
  selectedCorners: number[]
  tool: EditTool
  /** The two corners (of the active ring) the arc being drawn joins; set while `tool` is `arc`. */
  arcAnchors: [number, number] | null
  /** Whether the circle being drawn is added to the outline or cut out of it; set while `tool` is `circle`. */
  circleOperation: CircleOperation | null
  viewState: ViewState
  approxAreaSquareMeters: number
  status: EditStatus
  /** The last refused local change, shown then cleared. */
  refusal: string | null
  /** The user dismissed the floating bar; the session goes on and the Outline menu has every action. */
  toolbarHidden: boolean
}

export interface EnterParams {
  chatId: string
  siteName: string
  revision: string
  /** The rings as the API carries them (closed or open). */
  rings: readonly (readonly GeoPoint[])[]
  viewState: ViewState
}

/** Lucy asked for the editor (the `siteBoundaryEdit` stream event); the viewer picks this up and enters. */
export interface EditRequest {
  chatId: string
  revision: string
}

interface State {
  session: SiteBoundaryEditSession | null
  /**
   * A request to open the editor that no viewer has acted on yet. The chat stream cannot enter edit
   * mode itself (it has no map), so it leaves the request here and the viewer-side hook consumes it.
   */
  pendingRequest: EditRequest | null
  /**
   * A message the editor owes the user that outlives the session that produced it - a forced exit, a
   * save that could not be started. Lives here, not in the session, because ending the session must
   * not swallow the explanation (constitution section 2 VIII: no silent failures).
   */
  notice: string | null
  /** Which shape tool is asking for its number, or null when none is. Cleared when the session ends. */
  shapeDialog: ShapeTool | null
}

interface Actions {
  requestEdit(request: EditRequest): void
  setNotice(notice: string | null): void
  setToolbarHidden(hidden: boolean): void
  setShapeDialog(tool: ShapeTool | null): void
  consumeRequest(): EditRequest | null
  enter(params: EnterParams): void
  /** A local change that passed validation: records it for undo and clears redo. */
  applyChange(change: RingChange): void
  /** A local change that was refused: nothing changes but the message. */
  refuse(message: string): void
  clearRefusal(): void
  undo(): RingChange | null
  redo(): RingChange | null
  setActiveRing(ring: number): void
  selectCorner(index: number | null): void
  /** Replaces the selection with these corners of the active ring. */
  selectCorners(indices: readonly number[]): void
  /** Adds the corner to the selection, or removes it when it is already in. */
  toggleCorner(index: number): void
  setTool(tool: EditTool): void
  /** Starts drawing an arc between two corners: switches to the arc tool and remembers which corners. */
  beginArc(anchors: [number, number]): void
  /** Starts drawing a circle that will be added to, or cut out of, the outline. */
  beginCircle(operation: CircleOperation): void
  /** Cancel and forced exit both end the session; the caller restores the view state it gets from `session`. */
  end(): void
  beginSave(): void
  saveFailed(message: string): void
  conflict(currentRevision: string): void
  /** "Load latest": keep the session and view state, rebase on the freshly fetched outline. */
  rebase(revision: string, rings: readonly (readonly GeoPoint[])[]): void
  /** Whether the shape differs from what edit mode started with. */
  isDirty(): boolean
}

const cloneRings = (rings: readonly (readonly GeoPoint[])[]): GeoPoint[][] =>
  rings.map((ring) => openRing(ring).map((p) => ({ ...p })))

const totalArea = (rings: readonly (readonly GeoPoint[])[]): number =>
  rings.reduce((sum, ring) => sum + ringAreaSquareMeters(ring), 0)

/** Applies one change to a copy of the rings, in the forward direction. */
function applyForward(rings: GeoPoint[][], change: RingChange): GeoPoint[][] {
  if (change.op === 'replaceAll') return change.after.map((ring) => ring.map((p) => ({ ...p })))
  const next = rings.map((r) => [...r])
  const ring = next[change.ring]
  if (change.op === 'replace') next[change.ring] = change.after.map((p) => ({ ...p }))
  else if (change.op === 'move') ring[change.index] = { ...change.after }
  else if (change.op === 'insert') ring.splice(change.index, 0, { ...change.after })
  else ring.splice(change.index, 1)
  return next
}

/** Applies one change to a copy of the rings, in the reverse direction. */
function applyBackward(rings: GeoPoint[][], change: RingChange): GeoPoint[][] {
  if (change.op === 'replaceAll') return change.before.map((ring) => ring.map((p) => ({ ...p })))
  const next = rings.map((r) => [...r])
  const ring = next[change.ring]
  if (change.op === 'replace') next[change.ring] = change.before.map((p) => ({ ...p }))
  else if (change.op === 'move') ring[change.index] = { ...change.before }
  else if (change.op === 'insert') ring.splice(change.index, 1)
  else ring.splice(change.index, 0, { ...change.before })
  return next
}

const sameRings = (a: readonly (readonly GeoPoint[])[], b: readonly (readonly GeoPoint[])[]): boolean =>
  a.length === b.length &&
  a.every((ring, r) => ring.length === b[r].length && ring.every((p, i) => p.latitude === b[r][i].latitude && p.longitude === b[r][i].longitude))

/**
 * A drag reports many small moves of one corner; they are one undo step, not dozens. Moves of the
 * same corner closer together than this merge, keeping the corner's original position as `before`.
 */
export const COALESCE_MOVES_WITHIN_MS = 600

let lastChangeAt = 0

export const useSiteBoundaryEditStore = create<State & Actions>()((set, get) => {
  /** Replaces part of the session; a no-op when no session is open. */
  const update = (patch: (session: SiteBoundaryEditSession) => Partial<SiteBoundaryEditSession>) => {
    const { session } = get()
    if (session) set({ session: { ...session, ...patch(session) } })
  }

  return {
    session: null,
    pendingRequest: null,
    notice: null,
    shapeDialog: null,

    setShapeDialog(tool) {
      set({ shapeDialog: tool })
    },

    setNotice(notice) {
      set({ notice })
    },

    requestEdit(request) {
      set({ pendingRequest: request })
    },

    consumeRequest() {
      const { pendingRequest } = get()
      if (pendingRequest) set({ pendingRequest: null })
      return pendingRequest
    },

    enter({ chatId, siteName, revision, rings, viewState }) {
      const start = cloneRings(rings)
      set({
        session: {
          chatId,
          siteName,
          baseRevision: revision,
          startRings: start,
          rings: cloneRings(start),
          undo: [],
          redo: [],
          activeRing: 0,
          selectedCorner: null,
          selectedCorners: [],
          tool: 'edit',
          arcAnchors: null,
          circleOperation: null,
          viewState,
          approxAreaSquareMeters: totalArea(start),
          status: { kind: 'editing' },
          refusal: null,
          toolbarHidden: false,
        },
      })
    },

    applyChange(change) {
      const now = Date.now()
      update((s) => {
        const rings = applyForward(s.rings, change)
        const last = s.undo.at(-1)
        const continuesDrag =
          change.op === 'move' && last?.op === 'move' && last.ring === change.ring && last.index === change.index &&
          now - lastChangeAt < COALESCE_MOVES_WITHIN_MS
        const undo = continuesDrag && change.op === 'move' && last?.op === 'move'
          ? [...s.undo.slice(0, -1), { ...change, before: last.before }]
          : [...s.undo, change]
        // A whole-ring change renumbers every corner, so an old selection no longer points at anything;
        // and after every ring was swapped the ring being edited may not exist any more.
        const selection =
          change.op === 'replaceAll'
            ? { selectedCorner: null, selectedCorners: [], activeRing: 0 }
            : change.op === 'replace'
              ? { selectedCorner: null, selectedCorners: [] }
              : {}
        return { rings, undo, redo: [], approxAreaSquareMeters: totalArea(rings), refusal: null, ...selection }
      })
      lastChangeAt = now
    },

    refuse(message) {
      update(() => ({ refusal: message }))
    },

    clearRefusal() {
      update(() => ({ refusal: null }))
    },

    undo() {
      const { session } = get()
      const change = session?.undo.at(-1) ?? null
      if (!session || !change) return null
      const rings = applyBackward(session.rings, change)
      set({
        session: {
          ...session,
          rings,
          undo: session.undo.slice(0, -1),
          redo: [...session.redo, change],
          approxAreaSquareMeters: totalArea(rings),
          refusal: null,
          ...(change.op === 'replace' ? { selectedCorner: null, selectedCorners: [] } : {}),
          ...(change.op === 'replaceAll' ? { selectedCorner: null, selectedCorners: [], activeRing: 0 } : {}),
        },
      })
      return change
    },

    redo() {
      const { session } = get()
      const change = session?.redo.at(-1) ?? null
      if (!session || !change) return null
      const rings = applyForward(session.rings, change)
      set({
        session: {
          ...session,
          rings,
          undo: [...session.undo, change],
          redo: session.redo.slice(0, -1),
          approxAreaSquareMeters: totalArea(rings),
          refusal: null,
          ...(change.op === 'replace' ? { selectedCorner: null, selectedCorners: [] } : {}),
          ...(change.op === 'replaceAll' ? { selectedCorner: null, selectedCorners: [], activeRing: 0 } : {}),
        },
      })
      return change
    },

    setActiveRing(ring) {
      update((s) => (ring >= 0 && ring < s.rings.length ? { activeRing: ring, selectedCorner: null, selectedCorners: [] } : {}))
    },

    selectCorner(index) {
      update(() => ({ selectedCorner: index, selectedCorners: index === null ? [] : [index] }))
    },

    selectCorners(indices) {
      const unique = [...new Set(indices)].sort((a, b) => a - b)
      update(() => ({ selectedCorners: unique, selectedCorner: unique.at(-1) ?? null }))
    },

    toggleCorner(index) {
      update((s) => {
        const selectedCorners = s.selectedCorners.includes(index)
          ? s.selectedCorners.filter((i) => i !== index)
          : [...s.selectedCorners, index].sort((a, b) => a - b)
        return { selectedCorners, selectedCorner: selectedCorners.includes(index) ? index : (selectedCorners.at(-1) ?? null) }
      })
    },

    beginArc(anchors) {
      update(() => ({ tool: 'arc', arcAnchors: anchors, circleOperation: null }))
    },

    beginCircle(operation) {
      update(() => ({ tool: 'circle', circleOperation: operation, arcAnchors: null }))
    },

    setTool(tool) {
      update(() => ({ tool, arcAnchors: null, circleOperation: null }))
    },

    end() {
      set({ session: null, shapeDialog: null })
    },

    beginSave() {
      update(() => ({ status: { kind: 'saving' }, refusal: null }))
    },

    saveFailed(message) {
      // A failure brings the bar back: the user must see what went wrong and how to retry.
      update(() => ({ status: { kind: 'error', message }, toolbarHidden: false }))
    },

    conflict(currentRevision) {
      update(() => ({ status: { kind: 'conflict', currentRevision }, toolbarHidden: false }))
    },

    setToolbarHidden(hidden) {
      update(() => ({ toolbarHidden: hidden }))
    },

    rebase(revision, rings) {
      update(() => {
        const start = cloneRings(rings)
        return {
          baseRevision: revision,
          startRings: start,
          rings: cloneRings(start),
          undo: [],
          redo: [],
          activeRing: 0,
          selectedCorner: null,
          selectedCorners: [],
          approxAreaSquareMeters: totalArea(start),
          status: { kind: 'editing' },
          refusal: null,
        }
      })
    },

    isDirty() {
      const { session } = get()
      return session ? !sameRings(session.rings, session.startRings) : false
    },
  }
})
