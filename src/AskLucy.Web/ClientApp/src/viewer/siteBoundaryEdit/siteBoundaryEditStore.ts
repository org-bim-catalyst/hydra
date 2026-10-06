import { create } from 'zustand'
import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import type { CameraViewMode } from '../api/commands'
import { netAreaSquareMeters, openRing, windVoidsAgainst } from './ringGeometry'

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

/**
 * Which path of a ring a corner change is on (specs/081): 0 or absent is the ring's outer edge, `k` is its
 * void `k - 1`. Rings change by the same operations whichever path they happen on.
 */
export type RingChange =
  | { op: 'move'; ring: number; path?: number; index: number; before: GeoPoint; after: GeoPoint }
  /** Several selected corners moved together (one dragged, or the arrow keys): `indices[i]` went from `before[i]` to `after[i]`. */
  | { op: 'moveMany'; ring: number; path?: number; indices: number[]; before: GeoPoint[]; after: GeoPoint[] }
  | { op: 'insert'; ring: number; path?: number; index: number; after: GeoPoint }
  | { op: 'delete'; ring: number; path?: number; index: number; before: GeoPoint }
  /** specs/081: a whole void taken away (Remove void); undo puts it back at `voidIndex`. */
  | { op: 'removeVoid'; ring: number; voidIndex: number; before: GeoPoint[] }
  /** The whole ring swapped for another: deleting several corners at once, rounding a corner, curving an edge, a circle. One undo step. */
  | { op: 'replace'; ring: number; path?: number; before: GeoPoint[]; after: GeoPoint[] }
  /** Every ring swapped at once, possibly a different number of them: a circle added as a ring of its own, rings merged, a ring split by a cut. */
  | { op: 'replaceAll'; before: GeoPoint[][]; after: GeoPoint[][]; beforeVoids: GeoPoint[][][]; afterVoids: GeoPoint[][][] }

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
  /** specs/081: each ring's voids by ring index; open rings. `startVoids` is Cancel's target. */
  startVoids: GeoPoint[][][]
  voids: GeoPoint[][][]
  undo: RingChange[]
  redo: RingChange[]
  activeRing: number
  /**
   * specs/081: which path of the active ring the selection and the corner actions are on: 0 is the ring's
   * outer edge, `k` is its void `k - 1`. Always 0 after the ring changes.
   */
  activePath: number
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
  /** specs/081: each ring's voids by ring index, closed or open. */
  voids?: readonly (readonly (readonly GeoPoint[])[])[]
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
  /** specs/081: makes this path of the active ring the one being edited (0 is the outer edge); clears the selection. */
  setActivePath(path: number): void
  /** specs/081: makes this ring and path the one being edited, as clicking a corner of a void does; clears the selection. */
  activate(ring: number, path: number): void
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
  rebase(revision: string, rings: readonly (readonly GeoPoint[])[], voids?: readonly (readonly (readonly GeoPoint[])[])[]): void
  /** Whether the shape differs from what edit mode started with. */
  isDirty(): boolean
}

const cloneRings = (rings: readonly (readonly GeoPoint[])[]): GeoPoint[][] =>
  rings.map((ring) => openRing(ring).map((p) => ({ ...p })))

/** Voids by ring index, one list per ring (padded, so `voids[i]` always exists for ring `i`). */
const cloneVoids = (
  voids: readonly (readonly (readonly GeoPoint[])[])[] | undefined,
  ringCount: number,
): GeoPoint[][][] => Array.from({ length: ringCount }, (_, i) => cloneRings(voids?.[i] ?? []))

const totalArea = (rings: readonly (readonly GeoPoint[])[], voids: readonly (readonly (readonly GeoPoint[])[])[]): number =>
  rings.reduce((sum, ring, i) => sum + netAreaSquareMeters(ring, voids[i] ?? []), 0)

interface Geometry {
  rings: GeoPoint[][]
  voids: GeoPoint[][][]
}

const copyPoints = (points: readonly GeoPoint[]): GeoPoint[] => points.map((p) => ({ ...p }))

/** Applies one change to a copy of the geometry, forward or backward. */
function applyChange(geometry: Geometry, change: RingChange, forward: boolean): Geometry {
  if (change.op === 'replaceAll') {
    return forward
      ? { rings: change.after.map(copyPoints), voids: change.afterVoids.map((v) => v.map(copyPoints)) }
      : { rings: change.before.map(copyPoints), voids: change.beforeVoids.map((v) => v.map(copyPoints)) }
  }

  const rings = geometry.rings.map((r) => [...r])
  const voids = geometry.voids.map((v) => v.map((r) => [...r]))

  if (change.op === 'removeVoid') {
    if (forward) voids[change.ring].splice(change.voidIndex, 1)
    else voids[change.ring].splice(change.voidIndex, 0, copyPoints(change.before))
    return { rings, voids }
  }

  if (change.op === 'replace') {
    const corners = copyPoints(forward ? change.after : change.before)
    if (!change.path) rings[change.ring] = corners
    else voids[change.ring][change.path - 1] = corners
    return { rings, voids }
  }

  const target = !change.path ? rings[change.ring] : voids[change.ring][change.path - 1]
  if (change.op === 'move') target[change.index] = { ...(forward ? change.after : change.before) }
  else if (change.op === 'moveMany') change.indices.forEach((index, i) => (target[index] = { ...(forward ? change.after : change.before)[i] }))
  else if (change.op === 'insert') {
    if (forward) target.splice(change.index, 0, { ...change.after })
    else target.splice(change.index, 1)
  } else if (forward) target.splice(change.index, 1)
  else target.splice(change.index, 0, { ...change.before })
  return { rings, voids }
}

const sameRings = (a: readonly (readonly GeoPoint[])[], b: readonly (readonly GeoPoint[])[]): boolean =>
  a.length === b.length &&
  a.every((ring, r) => ring.length === b[r].length && ring.every((p, i) => p.latitude === b[r][i].latitude && p.longitude === b[r][i].longitude))

const sameVoids = (a: readonly (readonly (readonly GeoPoint[])[])[], b: readonly (readonly (readonly GeoPoint[])[])[]): boolean =>
  a.length === b.length && a.every((ringVoids, r) => ringVoids.length === b[r].length && ringVoids.every((v, k) => sameRings([v], [b[r][k]])))

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

    enter({ chatId, siteName, revision, rings, voids, viewState }) {
      const start = cloneRings(rings)
      const startVoids = windVoidsAgainst(start, cloneVoids(voids, start.length))
      set({
        session: {
          chatId,
          siteName,
          baseRevision: revision,
          startRings: start,
          rings: cloneRings(start),
          startVoids,
          voids: cloneVoids(startVoids, start.length),
          undo: [],
          redo: [],
          activeRing: 0,
          activePath: 0,
          selectedCorner: null,
          selectedCorners: [],
          tool: 'edit',
          arcAnchors: null,
          circleOperation: null,
          viewState,
          approxAreaSquareMeters: totalArea(start, startVoids),
          status: { kind: 'editing' },
          refusal: null,
          toolbarHidden: false,
        },
      })
    },

    applyChange(change) {
      const now = Date.now()
      update((s) => {
        const { rings, voids } = applyChange({ rings: s.rings, voids: s.voids }, change, true)
        const last = s.undo.at(-1)
        const recent = now - lastChangeAt < COALESCE_MOVES_WITHIN_MS
        const continuesDrag =
          change.op === 'move' && last?.op === 'move' && last.ring === change.ring && (last.path ?? 0) === (change.path ?? 0) &&
          last.index === change.index && recent
        // A group being dragged reports a move per pointer step too; same corners, same ring: one undo step.
        const continuesGroupDrag =
          change.op === 'moveMany' && last?.op === 'moveMany' && last.ring === change.ring && (last.path ?? 0) === (change.path ?? 0) &&
          last.indices.join(',') === change.indices.join(',') && recent
        const undo =
          continuesDrag && change.op === 'move' && last?.op === 'move'
            ? [...s.undo.slice(0, -1), { ...change, before: last.before }]
            : continuesGroupDrag && change.op === 'moveMany' && last?.op === 'moveMany'
              ? [...s.undo.slice(0, -1), { ...change, before: last.before }]
              : [...s.undo, change]
        // A whole-ring change renumbers every corner, so an old selection no longer points at anything;
        // and after every ring was swapped the ring being edited may not exist any more.
        const selection =
          change.op === 'replaceAll'
            ? { selectedCorner: null, selectedCorners: [], activeRing: 0, activePath: 0 }
            : change.op === 'removeVoid'
              ? { selectedCorner: null, selectedCorners: [], activePath: 0 }
              : change.op === 'replace'
                ? { selectedCorner: null, selectedCorners: [] }
              : {}
        return { rings, voids, undo, redo: [], approxAreaSquareMeters: totalArea(rings, voids), refusal: null, ...selection }
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
      const { rings, voids } = applyChange({ rings: session.rings, voids: session.voids }, change, false)
      set({
        session: {
          ...session,
          rings,
          voids,
          undo: session.undo.slice(0, -1),
          redo: [...session.redo, change],
          approxAreaSquareMeters: totalArea(rings, voids),
          refusal: null,
          ...(change.op === 'replace' ? { selectedCorner: null, selectedCorners: [] } : {}),
          ...(change.op === 'removeVoid' ? { selectedCorner: null, selectedCorners: [], activePath: 0 } : {}),
          ...(change.op === 'replaceAll' ? { selectedCorner: null, selectedCorners: [], activeRing: 0, activePath: 0 } : {}),
        },
      })
      return change
    },

    redo() {
      const { session } = get()
      const change = session?.redo.at(-1) ?? null
      if (!session || !change) return null
      const { rings, voids } = applyChange({ rings: session.rings, voids: session.voids }, change, true)
      set({
        session: {
          ...session,
          rings,
          voids,
          undo: [...session.undo, change],
          redo: session.redo.slice(0, -1),
          approxAreaSquareMeters: totalArea(rings, voids),
          refusal: null,
          ...(change.op === 'replace' ? { selectedCorner: null, selectedCorners: [] } : {}),
          ...(change.op === 'removeVoid' ? { selectedCorner: null, selectedCorners: [], activePath: 0 } : {}),
          ...(change.op === 'replaceAll' ? { selectedCorner: null, selectedCorners: [], activeRing: 0, activePath: 0 } : {}),
        },
      })
      return change
    },

    setActiveRing(ring) {
      update((s) => (ring >= 0 && ring < s.rings.length ? { activeRing: ring, activePath: 0, selectedCorner: null, selectedCorners: [] } : {}))
    },

    setActivePath(path) {
      update((s) => (path >= 0 && path <= (s.voids[s.activeRing]?.length ?? 0) ? { activePath: path, selectedCorner: null, selectedCorners: [] } : {}))
    },

    activate(ring, path) {
      update((s) =>
        ring >= 0 && ring < s.rings.length && path >= 0 && path <= (s.voids[ring]?.length ?? 0)
          ? { activeRing: ring, activePath: path, selectedCorner: null, selectedCorners: [] }
          : {},
      )
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

    rebase(revision, rings, voids) {
      update(() => {
        const start = cloneRings(rings)
        const startVoids = windVoidsAgainst(start, cloneVoids(voids, start.length))
        return {
          baseRevision: revision,
          activePath: 0,
          startRings: start,
          rings: cloneRings(start),
          startVoids,
          voids: cloneVoids(startVoids, start.length),
          undo: [],
          redo: [],
          activeRing: 0,
          selectedCorner: null,
          selectedCorners: [],
          approxAreaSquareMeters: totalArea(start, startVoids),
          status: { kind: 'editing' },
          refusal: null,
        }
      })
    },

    isDirty() {
      const { session } = get()
      return session ? !sameRings(session.rings, session.startRings) || !sameVoids(session.voids, session.startVoids) : false
    },
  }
})

/** specs/081: the corners of one path of a ring: 0 is its outer edge, `k` its void `k - 1`. */
export const pathCorners = (
  session: Pick<SiteBoundaryEditSession, 'rings' | 'voids'>,
  ring: number,
  path: number,
): GeoPoint[] => (path === 0 ? (session.rings[ring] ?? []) : (session.voids[ring]?.[path - 1] ?? []))

/** The corners the selection and the corner actions are on: the active path of the active ring. */
export const activeCorners = (session: Pick<SiteBoundaryEditSession, 'rings' | 'voids' | 'activeRing' | 'activePath'>): GeoPoint[] =>
  pathCorners(session, session.activeRing, session.activePath)

/** How many paths a ring has: its outer edge and each of its voids. */
export const pathCountOf = (session: Pick<SiteBoundaryEditSession, 'voids'>, ring: number): number => 1 + (session.voids[ring]?.length ?? 0)
