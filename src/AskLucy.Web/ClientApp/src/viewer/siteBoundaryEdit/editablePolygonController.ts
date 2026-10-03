import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import { fromLocalMeters, openRing, validateChange, validateRing } from './ringGeometry'
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
  /** A plain click or tap on a corner handle (not a drag). `additive` is true with Shift or Ctrl/Cmd held. */
  onVertexClick(listener: (vertexIndex: number, additive: boolean) => void): () => void
  /** Marks these corners as selected (an empty list clears the marks). */
  setHighlights(indices: readonly number[]): void
  /**
   * While a corner handle is being dragged, where it is now. Google reports a vertex move only when it is
   * dropped, so without this nothing else (the selection's rings, the rest of a selected group) could follow.
   */
  onVertexDragMove?(listener: (vertexIndex: number, point: GeoPoint) => void): () => void
  /** Right-click or long-press on a corner. `clientX`/`clientY` place a menu. */
  onVertexMenu(listener: (vertexIndex: number, clientX: number, clientY: number) => void): () => void
  remove(): void
}

export interface EditablePolygonHost {
  /** `editable: false` rings are drawn dimmed, above the outline they replace. */
  createRing(corners: GeoPoint[], options: { editable: boolean }): EditableRing
  /** A left click that landed on no corner: on the map itself, or on a ring away from its corners. */
  onEmptyClick?(listener: () => void): () => void
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
  /** Swaps a ring's corners for these ones after checking the result is a valid outline, as one undo step. Returns false when refused. */
  replaceRing(ring: number, corners: readonly GeoPoint[]): boolean
  /**
   * Swaps every ring for these, which may be a different number of them, after checking each is a valid
   * outline, as one undo step. The map is redrawn from them. Returns false when refused.
   */
  replaceAllRings(rings: readonly (readonly GeoPoint[])[]): boolean
  /** Moves a corner by this many metres east and north (the arrow keys), after checking the result. Returns false when refused. */
  moveCorner(ring: number, index: number, eastMeters: number, northMeters: number): boolean
  /** Moves several corners together by this many metres, as one undo step. Returns false when refused. */
  moveCorners(ring: number, indices: readonly number[], eastMeters: number, northMeters: number): boolean
  /** Deletes several corners at once, as one undo step. Refused if fewer than 3 would remain or the outline would cross itself. */
  deleteCorners(ring: number, indices: readonly number[]): boolean
  /** Deletes a corner after checking it (the menu, the Delete key). Returns false when refused. */
  deleteCorner(ring: number, index: number): boolean
  unmount(): void
}

/** How long after a corner moved a click is treated as the end of that drag. */
const CLICK_AFTER_DRAG_MS = 400

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
  /** When a corner was last dragged: a click arriving right after is the end of that drag, not a click. */
  let lastDragAt = 0
  const justDragged = () => Date.now() - lastDragAt < CLICK_AFTER_DRAG_MS

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
        lastDragAt = Date.now()

        // One of several selected corners was dragged: the others go the same way, as one change.
        const session = store().session
        const group = session && session.activeRing === ringIndex ? session.selectedCorners : []
        if (group.length > 1 && group.includes(index)) {
          const dLat = after.latitude - before.latitude
          const dLng = after.longitude - before.longitude
          const indices = group.filter((i) => i < entry.known.length)
          const moved = indices.map((i) =>
            i === index ? after : { latitude: entry.known[i].latitude + dLat, longitude: entry.known[i].longitude + dLng },
          )
          const candidate = [...entry.known]
          indices.forEach((i, k) => (candidate[i] = moved[k]))

          const refusal = validateRing(candidate)
          if (refusal) {
            withWriting(() => indices.forEach((i) => path.setAt(i, entry.known[i])))
            refuse(refusal.message)
            return
          }

          const previous = indices.map((i) => entry.known[i])
          withWriting(() => indices.forEach((i, k) => i !== index && path.setAt(i, moved[k])))
          entry.known = candidate
          store().applyChange({ op: 'moveMany', ring: ringIndex, indices, before: previous, after: moved })
          return
        }

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

      // A click on the ring away from its corners makes it the one being edited, and ends a selection.
      entry.ring.onSelect(() => {
        if (justDragged()) return
        if (store().session?.activeRing !== ringIndex) store().setActiveRing(ringIndex)
        else store().selectCorner(null)
      }),

      entry.ring.onVertexClick((index, additive) => {
        if (justDragged()) return
        const changedRing = store().session?.activeRing !== ringIndex
        if (changedRing) store().setActiveRing(ringIndex)
        if (additive && !changedRing) store().toggleCorner(index)
        // A click on any corner while several are selected ends that selection; dragging one keeps it.
        else if ((store().session?.selectedCorners.length ?? 0) > 1) store().selectCorner(null)
        else store().selectCorner(index)
      }),

      // The rest of a selected group follows the dragged corner as it moves, not only when it is dropped.
      // Written without recording: the drop that follows records the whole move once, from the corners'
      // positions before the drag.
      ...(entry.ring.onVertexDragMove
        ? [
            entry.ring.onVertexDragMove((index, point) => {
              const session = store().session
              const group = session && session.activeRing === ringIndex ? session.selectedCorners : []
              const origin = entry.known[index]
              if (group.length < 2 || !group.includes(index) || !origin) return
              const dLat = point.latitude - origin.latitude
              const dLng = point.longitude - origin.longitude
              withWriting(() =>
                group
                  .filter((i) => i !== index && i < entry.known.length)
                  .forEach((i) =>
                    path.setAt(i, { latitude: entry.known[i].latitude + dLat, longitude: entry.known[i].longitude + dLng }),
                  ),
              )
            }),
          ]
        : []),

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

    // A click or right-click anywhere but a corner ends a selection, as it does in any drawing tool.
    const offEmptyClick = host.onEmptyClick?.(() => {
      if (justDragged()) return
      if ((store().session?.selectedCorners.length ?? 0) > 0) store().selectCorner(null)
    })
    if (offEmptyClick) mounted[0]?.unsubscribe.push(offEmptyClick)

    unsubscribeStore = useSiteBoundaryEditStore.subscribe((state, previous) => {
      if (
        state.session?.selectedCorners.join(',') !== previous.session?.selectedCorners.join(',') ||
        state.session?.activeRing !== previous.session?.activeRing
      ) {
        syncHighlight()
      }
    })
    syncHighlight()
  }

  /** Shows the store's selected corners on the active ring only; every other ring shows none. */
  function syncHighlight() {
    const session = store().session
    mounted.forEach((entry, ringIndex) => {
      const selected = session && session.activeRing === ringIndex ? session.selectedCorners : []
      entry.ring.setHighlights(selected.filter((i) => i < entry.known.length))
    })
  }

  /** Rewrites a ring's path in place to `corners`, without reporting it as a user edit. */
  function writePath(entry: Mounted, corners: readonly GeoPoint[]) {
    withWriting(() => {
      const { path } = entry.ring
      while (path.getLength() > corners.length) path.removeAt(path.getLength() - 1)
      corners.forEach((corner, i) => {
        if (i < path.getLength()) path.setAt(i, corner)
        else path.insertAt(i, corner)
      })
    })
    entry.known = [...corners]
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

    moveCorners(ringIndex, indices, eastMeters, northMeters) {
      const entry = mounted[ringIndex]
      const valid = [...new Set(indices)].filter((i) => i >= 0 && i < (entry?.known.length ?? 0))
      if (!entry || valid.length === 0) return false
      if (valid.length === 1) return this.moveCorner(ringIndex, valid[0], eastMeters, northMeters)

      const moved = valid.map((i) => fromLocalMeters([{ x: eastMeters, y: northMeters }], entry.known[i])[0])
      const candidate = [...entry.known]
      valid.forEach((i, k) => (candidate[i] = moved[k]))
      const refusal = validateRing(candidate)
      if (refusal) {
        refuse(refusal.message)
        return false
      }

      const previous = valid.map((i) => entry.known[i])
      withWriting(() => valid.forEach((i, k) => entry.ring.path.setAt(i, moved[k])))
      entry.known = candidate
      store().applyChange({ op: 'moveMany', ring: ringIndex, indices: valid, before: previous, after: moved })
      return true
    },

    moveCorner(ringIndex, index, eastMeters, northMeters) {
      const entry = mounted[ringIndex]
      if (!entry || index < 0 || index >= entry.known.length) return false

      const before = entry.known[index]
      const [after] = fromLocalMeters([{ x: eastMeters, y: northMeters }], before)
      const candidate = [...entry.known]
      candidate[index] = after

      const refusal = validateChange(candidate, index)
      if (refusal) {
        refuse(refusal.message)
        return false
      }

      withWriting(() => entry.ring.path.setAt(index, after))
      entry.known[index] = after
      store().applyChange({ op: 'move', ring: ringIndex, index, before, after })
      return true
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

    replaceRing(ringIndex, corners) {
      const entry = mounted[ringIndex]
      if (!entry) return false

      const next = openRing(corners)
      const refusal = next.length < 3 ? { message: 'An outline needs at least 3 corners.' } : validateRing(next)
      if (refusal) {
        refuse(refusal.message)
        return false
      }

      const before = [...entry.known]
      writePath(entry, next)
      store().applyChange({ op: 'replace', ring: ringIndex, before, after: next })
      return true
    },

    replaceAllRings(rings) {
      if (rings.length === 0) return false

      const next = rings.map((ring) => openRing(ring))
      for (let i = 0; i < next.length; i++) {
        const refusal = next[i].length < 3 ? { message: 'An outline needs at least 3 corners.' } : validateRing(next[i])
        if (refusal) {
          refuse(rings.length > 1 ? `Ring ${i + 1}: ${refusal.message}` : refusal.message)
          return false
        }
      }

      const before = mounted.map((entry) => [...entry.known])
      store().applyChange({ op: 'replaceAll', before, after: next })
      // The store now holds the new rings and has made ring 0 the one being edited; redraw the map from it.
      this.setRings(next, 0)
      return true
    },

    deleteCorners(ringIndex, indices) {
      const entry = mounted[ringIndex]
      if (!entry) return false

      const doomed = new Set(indices.filter((i) => i >= 0 && i < entry.known.length))
      if (doomed.size === 0) return false
      if (doomed.size === 1) return this.deleteCorner(ringIndex, [...doomed][0])

      return this.replaceRing(ringIndex, entry.known.filter((_, i) => !doomed.has(i)))
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
