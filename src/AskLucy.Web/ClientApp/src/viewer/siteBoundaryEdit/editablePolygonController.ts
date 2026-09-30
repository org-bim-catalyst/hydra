import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import { openRing, validateChange, validateRing } from './ringGeometry'
import { useSiteBoundaryEditStore } from './siteBoundaryEditStore'

/**
 * specs/079 research D1: one native, editable polygon per ring. The map draws the handles and does
 * the dragging; this only listens to what the ring's path reports (a corner moved, inserted,
 * removed), checks the change, and either records it or puts it back and says why.
 *
 * All Google Maps access sits behind {@link EditablePolygonHost}, so the rules below are tested
 * against a fake and the real adapter stays thin.
 */

/** The part of `google.maps.MVCArray<LatLng>` this needs. */
export interface EditablePath {
  getLength(): number
  getAt(index: number): GeoPoint
  setAt(index: number, point: GeoPoint): void
  insertAt(index: number, point: GeoPoint): void
  removeAt(index: number): void
  /** Each returns a function that removes the listener. */
  onSetAt(listener: (index: number) => void): () => void
  onInsertAt(listener: (index: number) => void): () => void
  onRemoveAt(listener: (index: number, removed: GeoPoint) => void): () => void
}

export interface EditableRing {
  path: EditablePath
  setEditable(editable: boolean): void
  /** The ring became the one being edited (the user clicked it). */
  onSelect(listener: () => void): () => void
  /** A plain click or tap on a corner handle (not a drag). */
  onVertexClick(listener: (vertexIndex: number) => void): () => void
  /** Marks one corner as the selected one, or clears the mark with null. */
  setHighlight(index: number | null): void
  /** Right-click or long-press on a corner. `clientX`/`clientY` place a menu. */
  onVertexMenu(listener: (vertexIndex: number, clientX: number, clientY: number) => void): () => void
  remove(): void
}

export interface EditablePolygonHost {
  /** `editable: false` rings are drawn dimmed, above the outline they replace. */
  createRing(corners: GeoPoint[], options: { editable: boolean }): EditableRing
}

export interface VertexMenuRequest {
  ring: number
  index: number
  clientX: number
  clientY: number
}

export interface EditablePolygonController {
  /** Draws every ring; the store's active ring is the editable one. */
  mount(rings: readonly (readonly GeoPoint[])[], activeRing: number): void
  /** Replaces every ring's path with `rings` without reporting it as an edit (undo, redo, Load latest). */
  setRings(rings: readonly (readonly GeoPoint[])[], activeRing: number): void
  setActiveRing(activeRing: number): void
  /** Adds a corner midway between corner `index` and the next one, and selects it. Returns false when refused. */
  insertCornerAfter(ring: number, index: number): boolean
  /** Deletes a corner after checking it (the menu, the Delete key). Returns false when refused. */
  deleteCorner(ring: number, index: number): boolean
  unmount(): void
}

export interface ControllerOptions {
  onVertexMenu?(request: VertexMenuRequest): void
}

const same = (a: GeoPoint, b: GeoPoint) => a.latitude === b.latitude && a.longitude === b.longitude

function pathToRing(path: EditablePath): GeoPoint[] {
  const corners: GeoPoint[] = []
  for (let i = 0; i < path.getLength(); i++) corners.push(path.getAt(i))
  return corners
}

export function createEditablePolygonController(host: EditablePolygonHost, options: ControllerOptions = {}): EditablePolygonController {
  const store = () => useSiteBoundaryEditStore.getState()

  interface Mounted {
    ring: EditableRing
    /** The corners as last accepted, so a refused change can be put back and a move knows its `before`. */
    known: GeoPoint[]
    unsubscribe: (() => void)[]
  }

  let mounted: Mounted[] = []
  let unsubscribeStore: (() => void) | null = null
  /** True while this controller is writing to a path itself; the path's own events are then not user edits. */
  let writing = false

  const withWriting = (action: () => void) => {
    writing = true
    try {
      action()
    } finally {
      writing = false
    }
  }

  function refuse(message: string) {
    store().refuse(message)
  }

  function listen(ringIndex: number, entry: Mounted) {
    const { path } = entry.ring

    entry.unsubscribe.push(
      path.onSetAt((index) => {
        if (writing) return
        const before = entry.known[index]
        const after = path.getAt(index)
        if (!before || same(before, after)) return

        const refusal = validateChange(pathToRing(path), index)
        if (refusal) {
          withWriting(() => path.setAt(index, before))
          refuse(refusal.message)
          return
        }

        entry.known[index] = after
        store().applyChange({ op: 'move', ring: ringIndex, index, before, after })
      }),

      path.onInsertAt((index) => {
        if (writing) return
        const after = path.getAt(index)
        const refusal = validateChange(pathToRing(path), index)
        if (refusal) {
          withWriting(() => path.removeAt(index))
          refuse(refusal.message)
          return
        }

        entry.known.splice(index, 0, after)
        store().applyChange({ op: 'insert', ring: ringIndex, index, after })
      }),

      path.onRemoveAt((index, removed) => {
        if (writing) return
        const remaining = pathToRing(path)
        // A deletion is checked at the corner before the removed one, where the new edge starts.
        const refusal = remaining.length < 3
          ? { message: 'An outline needs at least 3 corners.' }
          : validateChange(remaining, Math.max(0, index - 1))
        if (refusal) {
          withWriting(() => path.insertAt(index, removed))
          refuse(refusal.message)
          return
        }

        entry.known.splice(index, 1)
        store().applyChange({ op: 'delete', ring: ringIndex, index, before: removed })
      }),

      entry.ring.onSelect(() => {
        if (store().session?.activeRing !== ringIndex) store().setActiveRing(ringIndex)
      }),

      entry.ring.onVertexClick((index) => {
        if (store().session?.activeRing !== ringIndex) store().setActiveRing(ringIndex)
        store().selectCorner(index)
      }),

      entry.ring.onVertexMenu((index, clientX, clientY) => {
        store().selectCorner(index)
        options.onVertexMenu?.({ ring: ringIndex, index, clientX, clientY })
      }),
    )
  }

  function draw(rings: readonly (readonly GeoPoint[])[], activeRing: number) {
    mounted = rings.map((corners, ringIndex) => {
      const open = openRing(corners)
      const entry: Mounted = {
        ring: host.createRing(open, { editable: ringIndex === activeRing }),
        known: [...open],
        unsubscribe: [],
      }
      listen(ringIndex, entry)
      return entry
    })

    unsubscribeStore = useSiteBoundaryEditStore.subscribe((state, previous) => {
      if (state.session?.selectedCorner !== previous.session?.selectedCorner || state.session?.activeRing !== previous.session?.activeRing) {
        syncHighlight()
      }
    })
    syncHighlight()
  }

  /** Shows the store's selected corner on the active ring only; every other ring shows none. */
  function syncHighlight() {
    const session = store().session
    mounted.forEach((entry, ringIndex) => {
      const selected = session && session.activeRing === ringIndex ? session.selectedCorner : null
      entry.ring.setHighlight(selected !== null && selected < entry.known.length ? selected : null)
    })
  }

  function clear() {
    unsubscribeStore?.()
    unsubscribeStore = null
    for (const entry of mounted) {
      entry.unsubscribe.forEach((off) => off())
      entry.ring.remove()
    }
    mounted = []
  }

  return {
    mount(rings, activeRing) {
      clear()
      draw(rings, activeRing)
    },

    setRings(rings, activeRing) {
      // Same number of rings: rewrite each path in place, so the handles the user is holding survive.
      if (rings.length === mounted.length) {
        withWriting(() => {
          rings.forEach((corners, ringIndex) => {
            const open = openRing(corners)
            const { path } = mounted[ringIndex].ring
            while (path.getLength() > open.length) path.removeAt(path.getLength() - 1)
            open.forEach((corner, i) => {
              if (i < path.getLength()) path.setAt(i, corner)
              else path.insertAt(i, corner)
            })
            mounted[ringIndex].known = [...open]
          })
        })
        this.setActiveRing(activeRing)
        return
      }

      clear()
      draw(rings, activeRing)
    },

    setActiveRing(activeRing) {
      mounted.forEach((entry, ringIndex) => entry.ring.setEditable(ringIndex === activeRing))
      syncHighlight()
    },

    insertCornerAfter(ringIndex, index) {
      const entry = mounted[ringIndex]
      if (!entry || index < 0 || index >= entry.known.length) return false

      const from = entry.known[index]
      const to = entry.known[(index + 1) % entry.known.length]
      const midpoint = { latitude: (from.latitude + to.latitude) / 2, longitude: (from.longitude + to.longitude) / 2 }
      const candidate = [...entry.known]
      candidate.splice(index + 1, 0, midpoint)

      const refusal = validateChange(candidate, index + 1)
      if (refusal) {
        refuse(refusal.message)
        return false
      }

      withWriting(() => entry.ring.path.insertAt(index + 1, midpoint))
      entry.known.splice(index + 1, 0, midpoint)
      store().applyChange({ op: 'insert', ring: ringIndex, index: index + 1, after: midpoint })
      store().selectCorner(index + 1)
      return true
    },

    deleteCorner(ringIndex, index) {
      const entry = mounted[ringIndex]
      if (!entry || index < 0 || index >= entry.known.length) return false

      const remaining = entry.known.filter((_, i) => i !== index)
      const refusal = remaining.length < 3
        ? { message: 'An outline needs at least 3 corners.' }
        : validateRing(remaining)
      if (refusal) {
        refuse(refusal.message)
        return false
      }

      const removed = entry.known[index]
      withWriting(() => entry.ring.path.removeAt(index))
      entry.known.splice(index, 1)
      store().applyChange({ op: 'delete', ring: ringIndex, index, before: removed })
      return true
    },

    unmount() {
      clear()
    },
  }
}
