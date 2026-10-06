import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import { fromLocalMeters, openRing, validateChange, validateRing, validateVoid, windVoidsAgainst, type Refusal } from './ringGeometry'
import { useSiteBoundaryEditStore } from './siteBoundaryEditStore'

/**
 * specs/079 research D1: one native, editable polygon per ring. The map draws the handles and does
 * the dragging; this only listens to what the ring's path reports (a corner moved, inserted,
 * removed), checks the change, and either records it or puts it back and says why.
 *
 * specs/081: a ring's voids are further paths of the same polygon. A path is numbered 0 for the ring's
 * outer edge and `k` for its void `k - 1`; every corner of every path is selected, clicked, dragged,
 * moved, added and deleted the same way, and a void is checked against its ring as well as on its own.
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

/**
 * Every corner callback below says which path of the ring it is on as its last argument: 0 (or absent) for the
 * outer edge, `k` for void `k - 1`.
 */
export interface EditableRing {
  path: EditablePath
  /** specs/081: the ring's voids, in order, as the map holds them (the polygon's inner paths). */
  voidPaths?: EditablePath[]
  setEditable(editable: boolean): void
  /** The ring became the one being edited (the user clicked it). */
  onSelect(listener: () => void): () => void
  /** A plain click or tap on a corner handle (not a drag). `additive` is true with Shift or Ctrl/Cmd held. */
  onVertexClick(listener: (vertexIndex: number, additive: boolean, path?: number) => void): () => void
  /** Marks these corners of one path as selected, and no others in the ring (an empty list clears every mark). */
  setHighlights(indices: readonly number[], path?: number): void
  /** A corner handle was pressed (the start of a click or a drag). */
  onVertexPress?(listener: (vertexIndex: number, path?: number) => void): () => void
  /**
   * While a corner handle is being dragged, where it is now. Google reports a vertex move only when it is
   * dropped, so without this nothing else (the selection's rings, the rest of a selected group) could follow.
   */
  onVertexDragMove?(listener: (vertexIndex: number, point: GeoPoint, path?: number) => void): () => void
  /** Right-click or long-press on a corner. `clientX`/`clientY` place a menu. */
  onVertexMenu(listener: (vertexIndex: number, clientX: number, clientY: number, path?: number) => void): () => void
  remove(): void
}

export interface EditablePolygonHost {
  /** `editable: false` rings are drawn dimmed, above the outline they replace. `voids` are drawn as holes in it. */
  createRing(corners: GeoPoint[], options: { editable: boolean; voids?: GeoPoint[][] }): EditableRing
  /** A left click that landed on no corner: on the map itself, or on a ring away from its corners. */
  onEmptyClick?(listener: () => void): () => void
}

export interface VertexMenuRequest {
  ring: number
  index: number
  clientX: number
  clientY: number
}

/** specs/081: the menu was opened on a corner of a void. */
export interface VoidMenuRequest {
  ring: number
  voidIndex: number
  clientX: number
  clientY: number
}

export interface EditablePolygonController {
  /** Draws every ring; the store's active ring is the editable one. */
  mount(rings: readonly (readonly GeoPoint[])[], activeRing: number, voids?: readonly (readonly (readonly GeoPoint[])[])[]): void
  /**
   * Replaces every ring's path with `rings` without reporting it as an edit (undo, redo, Load latest). `voids`
   * default to the session's.
   */
  setRings(rings: readonly (readonly GeoPoint[])[], activeRing: number, voids?: readonly (readonly (readonly GeoPoint[])[])[]): void
  setActiveRing(activeRing: number): void
  /** Adds a corner midway between corner `index` and the next one, and selects it. Returns false when refused. `path` is 0 for the outer edge. */
  insertCornerAfter(ring: number, index: number, path?: number): boolean
  /** Swaps a path's corners for these ones after checking the result is valid, as one undo step. Returns false when refused. */
  replaceRing(ring: number, corners: readonly GeoPoint[], path?: number): boolean
  /**
   * Swaps every ring for these, which may be a different number of them, after checking each is a valid
   * outline, as one undo step. The map is redrawn from them. Returns false when refused.
   */
  replaceAllRings(rings: readonly (readonly GeoPoint[])[], voids?: readonly (readonly (readonly GeoPoint[])[])[]): boolean
  /** Fills a void back in (Remove void), as one undo step. Returns false when there is no such void. */
  removeVoid(ring: number, voidIndex: number): boolean
  /** Moves a corner by this many metres east and north (the arrow keys), after checking the result. Returns false when refused. */
  moveCorner(ring: number, index: number, eastMeters: number, northMeters: number, path?: number): boolean
  /** Moves several corners together by this many metres, as one undo step. Returns false when refused. */
  moveCorners(ring: number, indices: readonly number[], eastMeters: number, northMeters: number, path?: number): boolean
  /** Deletes several corners at once, as one undo step. Refused if fewer than 3 would remain or the shape would cross itself. */
  deleteCorners(ring: number, indices: readonly number[], path?: number): boolean
  /** Deletes a corner after checking it (the menu, the Delete key). Returns false when refused. */
  deleteCorner(ring: number, index: number, path?: number): boolean
  unmount(): void
}

/** How long after a corner moved a click is treated as the end of that drag. */
const CLICK_AFTER_DRAG_MS = 400

export interface ControllerOptions {
  onVertexMenu?(request: VertexMenuRequest): void
  onVoidMenu?(request: VoidMenuRequest): void
}

const same = (a: GeoPoint, b: GeoPoint) => a.latitude === b.latitude && a.longitude === b.longitude

const MIN_OUTLINE_CORNERS = { message: 'An outline needs at least 3 corners.' }
const MIN_VOID_CORNERS = { message: 'A void needs at least 3 corners. Use Remove void to fill it in.' }

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
    /** The ring's voids as last accepted. */
    voidKnown: GeoPoint[][]
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

  // ---- a ring's paths: 0 is the outer edge, k is void k - 1 ----

  const cornersOf = (entry: Mounted, path: number): GeoPoint[] | undefined => (path === 0 ? entry.known : entry.voidKnown[path - 1])

  const setCornersOf = (entry: Mounted, path: number, corners: GeoPoint[]) => {
    if (path === 0) entry.known = corners
    else entry.voidKnown[path - 1] = corners
  }

  const pathOf = (entry: Mounted, path: number): EditablePath | undefined => (path === 0 ? entry.ring.path : entry.ring.voidPaths?.[path - 1])

  const pathCount = (entry: Mounted) => 1 + entry.voidKnown.length

  /** The op fields naming the path: nothing for the outer edge, so those changes read as they always did. */
  const onPath = (path: number) => (path === 0 ? {} : { path })

  /** The path of the ring the selection is on, when it is this ring. */
  const selectionPath = (ringIndex: number) => {
    const session = store().session
    return session && session.activeRing === ringIndex ? (session.activePath ?? 0) : -1
  }

  /** specs/081: after the outer edge changes, every void must still lie inside it. */
  function voidsRefusal(outer: readonly GeoPoint[], entry: Mounted): Refusal | null {
    for (let k = 0; k < entry.voidKnown.length; k++) {
      const refusal = validateVoid(outer, entry.voidKnown, k)
      if (refusal) return refusal
    }
    return null
  }

  /**
   * Whether `candidate` is acceptable as path `path` of the ring: valid in itself (the whole ring when `index` is
   * undefined, otherwise just around the corner that changed), and consistent with the rest of the ring - the
   * voids still inside the outer edge, or the void still inside the outer edge and clear of the other voids.
   */
  function refusalFor(entry: Mounted, path: number, candidate: GeoPoint[], index?: number): Refusal | null {
    const own = index === undefined ? validateRing(candidate) : validateChange(candidate, index)
    if (own) return own
    if (path === 0) return voidsRefusal(candidate, entry)
    const voids = entry.voidKnown.map((v, k) => (k === path - 1 ? candidate : v))
    return validateVoid(entry.known, voids, path - 1)
  }

  const tooFewMessage = (path: number) => (path === 0 ? MIN_OUTLINE_CORNERS : MIN_VOID_CORNERS)

  /** Google reports a path's own edits; each is checked, then recorded or put back with a reason. */
  function listenPath(ringIndex: number, path: number, entry: Mounted) {
    const mvc = pathOf(entry, path)
    if (!mvc) return

    entry.unsubscribe.push(
      mvc.onSetAt((index) => {
        if (writing) return
        const known = cornersOf(entry, path)!
        const before = known[index]
        const after = mvc.getAt(index)
        if (!before || same(before, after)) return
        lastDragAt = Date.now()

        // One of several selected corners was dragged: the others go the same way, as one change.
        const group = selectionPath(ringIndex) === path ? store().session!.selectedCorners : []
        if (group.length > 1 && group.includes(index)) {
          const dLat = after.latitude - before.latitude
          const dLng = after.longitude - before.longitude
          const indices = group.filter((i) => i < known.length)
          const moved = indices.map((i) =>
            i === index ? after : { latitude: known[i].latitude + dLat, longitude: known[i].longitude + dLng },
          )
          const candidate = [...known]
          indices.forEach((i, k) => (candidate[i] = moved[k]))

          const refusal = refusalFor(entry, path, candidate)
          if (refusal) {
            withWriting(() => indices.forEach((i) => mvc.setAt(i, known[i])))
            refuse(refusal.message)
            return
          }

          const previous = indices.map((i) => known[i])
          withWriting(() => indices.forEach((i, k) => i !== index && mvc.setAt(i, moved[k])))
          setCornersOf(entry, path, candidate)
          store().applyChange({ op: 'moveMany', ring: ringIndex, ...onPath(path), indices, before: previous, after: moved })
          return
        }

        const refusal = refusalFor(entry, path, pathToRing(mvc), index)
        if (refusal) {
          withWriting(() => mvc.setAt(index, before))
          refuse(refusal.message)
          return
        }

        known[index] = after
        store().applyChange({ op: 'move', ring: ringIndex, ...onPath(path), index, before, after })
      }),

      mvc.onInsertAt((index) => {
        if (writing) return
        const after = mvc.getAt(index)
        const refusal = refusalFor(entry, path, pathToRing(mvc), index)
        if (refusal) {
          withWriting(() => mvc.removeAt(index))
          refuse(refusal.message)
          return
        }

        cornersOf(entry, path)!.splice(index, 0, after)
        store().applyChange({ op: 'insert', ring: ringIndex, ...onPath(path), index, after })
      }),

      mvc.onRemoveAt((index, removed) => {
        if (writing) return
        const remaining = pathToRing(mvc)
        // A deletion is checked at the corner before the removed one, where the new edge starts.
        const refusal = remaining.length < 3
          ? tooFewMessage(path)
          : refusalFor(entry, path, remaining, Math.max(0, index - 1))
        if (refusal) {
          withWriting(() => mvc.insertAt(index, removed))
          refuse(refusal.message)
          return
        }

        cornersOf(entry, path)!.splice(index, 1)
        store().applyChange({ op: 'delete', ring: ringIndex, ...onPath(path), index, before: removed })
      }),
    )
  }

  function listen(ringIndex: number, entry: Mounted) {
    for (let path = 0; path < pathCount(entry); path++) listenPath(ringIndex, path, entry)

    entry.unsubscribe.push(
      // A click on the ring away from its corners makes it the one being edited, and ends a selection.
      entry.ring.onSelect(() => {
        if (justDragged()) return
        if (store().session?.activeRing !== ringIndex) store().setActiveRing(ringIndex)
        else store().selectCorner(null)
      }),

      entry.ring.onVertexClick((index, additive, path = 0) => {
        if (justDragged()) return
        const moved = selectionPath(ringIndex) !== path
        if (moved) store().activate(ringIndex, path)
        if (additive && !moved) store().toggleCorner(index)
        // A click on any corner while several are selected ends that selection; dragging one keeps it.
        else if ((store().session?.selectedCorners.length ?? 0) > 1) store().selectCorner(null)
        else store().selectCorner(index)
      }),

      // Pressing a corner outside the selection makes it the selection straight away: the old rings go and
      // the corner being dragged gets its own. Pressing a selected corner keeps the selection, so it can be
      // dragged as a group; the click that follows, if it was not a drag, ends it.
      ...(entry.ring.onVertexPress
        ? [
            entry.ring.onVertexPress((index, path = 0) => {
              const session = store().session
              if (!session) return
              if (selectionPath(ringIndex) !== path) store().activate(ringIndex, path)
              else if (!session.selectedCorners.includes(index)) store().selectCorner(index)
            }),
          ]
        : []),

      // The rest of a selected group follows the dragged corner as it moves, not only when it is dropped.
      // Written without recording: the drop that follows records the whole move once, from the corners'
      // positions before the drag.
      ...(entry.ring.onVertexDragMove
        ? [
            entry.ring.onVertexDragMove((index, point, path = 0) => {
              const group = selectionPath(ringIndex) === path ? store().session!.selectedCorners : []
              const known = cornersOf(entry, path)
              const mvc = pathOf(entry, path)
              const origin = known?.[index]
              if (!known || !mvc || group.length < 2 || !group.includes(index) || !origin) return
              const dLat = point.latitude - origin.latitude
              const dLng = point.longitude - origin.longitude
              withWriting(() =>
                group
                  .filter((i) => i !== index && i < known.length)
                  .forEach((i) => mvc.setAt(i, { latitude: known[i].latitude + dLat, longitude: known[i].longitude + dLng })),
              )
            }),
          ]
        : []),

      entry.ring.onVertexMenu((index, clientX, clientY, path = 0) => {
        // The corner under the pointer becomes the selection, on whichever path it is.
        if (selectionPath(ringIndex) !== path) store().activate(ringIndex, path)
        store().selectCorner(index)
        if (path === 0) options.onVertexMenu?.({ ring: ringIndex, index, clientX, clientY })
        else options.onVoidMenu?.({ ring: ringIndex, voidIndex: path - 1, clientX, clientY })
      }),
    )
  }

  function draw(
    rings: readonly (readonly GeoPoint[])[],
    activeRing: number,
    voids: readonly (readonly (readonly GeoPoint[])[])[],
  ) {
    mounted = rings.map((corners, ringIndex) => {
      const open = openRing(corners)
      const ringVoids = (voids[ringIndex] ?? []).map((v) => openRing(v))
      const entry: Mounted = {
        ring: host.createRing(open, { editable: ringIndex === activeRing, voids: ringVoids }),
        known: [...open],
        voidKnown: ringVoids.map((v) => [...v]),
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
        state.session?.activeRing !== previous.session?.activeRing ||
        state.session?.activePath !== previous.session?.activePath
      ) {
        syncHighlight()
      }
    })
    syncHighlight()
  }

  /** Shows the store's selected corners on the active path of the active ring only; every other path shows none. */
  function syncHighlight() {
    mounted.forEach((entry, ringIndex) => {
      const active = selectionPath(ringIndex)
      const selected = active >= 0 ? store().session!.selectedCorners : []
      for (let path = 0; path < pathCount(entry); path++) {
        const known = cornersOf(entry, path)!
        entry.ring.setHighlights(path === active ? selected.filter((i) => i < known.length) : [], path)
      }
    })
  }

  /** Rewrites a path in place to `corners`, without reporting it as a user edit. */
  function writePath(entry: Mounted, path: number, corners: readonly GeoPoint[]) {
    const mvc = pathOf(entry, path)
    if (!mvc) return
    withWriting(() => {
      while (mvc.getLength() > corners.length) mvc.removeAt(mvc.getLength() - 1)
      corners.forEach((corner, i) => {
        if (i < mvc.getLength()) mvc.setAt(i, corner)
        else mvc.insertAt(i, corner)
      })
    })
    setCornersOf(entry, path, [...corners])
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
    mount(rings, activeRing, voids) {
      clear()
      draw(rings, activeRing, voids ?? store().session?.voids ?? [])
    },

    setRings(rings, activeRing, voids) {
      const nextVoids = voids ?? store().session?.voids ?? []

      // Same rings and the same number of voids in each: rewrite each path in place, so the handles the user is holding survive.
      const sameShape =
        rings.length === mounted.length &&
        mounted.every((entry, i) => entry.voidKnown.length === (nextVoids[i]?.length ?? 0))
      if (sameShape) {
        rings.forEach((corners, ringIndex) => {
          writePath(mounted[ringIndex], 0, openRing(corners))
          ;(nextVoids[ringIndex] ?? []).forEach((v, k) => writePath(mounted[ringIndex], k + 1, openRing(v)))
        })
        this.setActiveRing(activeRing)
        return
      }

      clear()
      draw(rings, activeRing, nextVoids)
    },

    setActiveRing(activeRing) {
      mounted.forEach((entry, ringIndex) => entry.ring.setEditable(ringIndex === activeRing))
      syncHighlight()
    },

    moveCorners(ringIndex, indices, eastMeters, northMeters, path = 0) {
      const entry = mounted[ringIndex]
      const known = entry && cornersOf(entry, path)
      const mvc = entry && pathOf(entry, path)
      const valid = [...new Set(indices)].filter((i) => i >= 0 && i < (known?.length ?? 0))
      if (!entry || !known || !mvc || valid.length === 0) return false
      if (valid.length === 1) return this.moveCorner(ringIndex, valid[0], eastMeters, northMeters, path)

      const moved = valid.map((i) => fromLocalMeters([{ x: eastMeters, y: northMeters }], known[i])[0])
      const candidate = [...known]
      valid.forEach((i, k) => (candidate[i] = moved[k]))
      const refusal = refusalFor(entry, path, candidate)
      if (refusal) {
        refuse(refusal.message)
        return false
      }

      const previous = valid.map((i) => known[i])
      withWriting(() => valid.forEach((i, k) => mvc.setAt(i, moved[k])))
      setCornersOf(entry, path, candidate)
      store().applyChange({ op: 'moveMany', ring: ringIndex, ...onPath(path), indices: valid, before: previous, after: moved })
      return true
    },

    moveCorner(ringIndex, index, eastMeters, northMeters, path = 0) {
      const entry = mounted[ringIndex]
      const known = entry && cornersOf(entry, path)
      const mvc = entry && pathOf(entry, path)
      if (!entry || !known || !mvc || index < 0 || index >= known.length) return false

      const before = known[index]
      const [after] = fromLocalMeters([{ x: eastMeters, y: northMeters }], before)
      const candidate = [...known]
      candidate[index] = after

      const refusal = refusalFor(entry, path, candidate, index)
      if (refusal) {
        refuse(refusal.message)
        return false
      }

      withWriting(() => mvc.setAt(index, after))
      known[index] = after
      store().applyChange({ op: 'move', ring: ringIndex, ...onPath(path), index, before, after })
      return true
    },

    insertCornerAfter(ringIndex, index, path = 0) {
      const entry = mounted[ringIndex]
      const known = entry && cornersOf(entry, path)
      const mvc = entry && pathOf(entry, path)
      if (!entry || !known || !mvc || index < 0 || index >= known.length) return false

      const from = known[index]
      const to = known[(index + 1) % known.length]
      const midpoint = { latitude: (from.latitude + to.latitude) / 2, longitude: (from.longitude + to.longitude) / 2 }
      const candidate = [...known]
      candidate.splice(index + 1, 0, midpoint)

      const refusal = refusalFor(entry, path, candidate, index + 1)
      if (refusal) {
        refuse(refusal.message)
        return false
      }

      withWriting(() => mvc.insertAt(index + 1, midpoint))
      known.splice(index + 1, 0, midpoint)
      store().applyChange({ op: 'insert', ring: ringIndex, ...onPath(path), index: index + 1, after: midpoint })
      store().selectCorner(index + 1)
      return true
    },

    replaceRing(ringIndex, corners, path = 0) {
      const entry = mounted[ringIndex]
      const known = entry && cornersOf(entry, path)
      if (!entry || !known) return false

      const next = openRing(corners)
      const refusal = next.length < 3 ? tooFewMessage(path) : refusalFor(entry, path, next)
      if (refusal) {
        refuse(refusal.message)
        return false
      }

      const before = [...known]
      writePath(entry, path, next)
      store().applyChange({ op: 'replace', ring: ringIndex, ...onPath(path), before, after: next })
      return true
    },

    replaceAllRings(rings, voids) {
      if (rings.length === 0) return false

      const next = rings.map((ring) => openRing(ring))
      const nextVoids = windVoidsAgainst(next, voids ?? [])
      for (let i = 0; i < next.length; i++) {
        const refusal = next[i].length < 3 ? MIN_OUTLINE_CORNERS : validateRing(next[i])
        if (refusal) {
          refuse(rings.length > 1 ? `Ring ${i + 1}: ${refusal.message}` : refusal.message)
          return false
        }

        // specs/081: a void the server accepted still has to look right here, or the map and the session disagree.
        for (let k = 0; k < nextVoids[i].length; k++) {
          const voidRefusal = validateVoid(next[i], nextVoids[i], k)
          if (voidRefusal) {
            refuse(rings.length > 1 ? `Ring ${i + 1}: ${voidRefusal.message}` : voidRefusal.message)
            return false
          }
        }
      }

      const before = mounted.map((entry) => [...entry.known])
      const beforeVoids = mounted.map((entry) => entry.voidKnown.map((v) => [...v]))
      store().applyChange({ op: 'replaceAll', before, after: next, beforeVoids, afterVoids: nextVoids })
      // The store now holds the new rings and has made ring 0 the one being edited; redraw the map from it.
      this.setRings(next, 0, nextVoids)
      return true
    },

    removeVoid(ringIndex, voidIndex) {
      const entry = mounted[ringIndex]
      const target = entry?.voidKnown[voidIndex]
      if (!entry || !target) return false

      store().applyChange({ op: 'removeVoid', ring: ringIndex, voidIndex, before: [...target] })
      // The store dropped the void; redraw the ring without it.
      this.setRings(store().session?.rings ?? [], store().session?.activeRing ?? ringIndex)
      return true
    },

    deleteCorners(ringIndex, indices, path = 0) {
      const entry = mounted[ringIndex]
      const known = entry && cornersOf(entry, path)
      if (!entry || !known) return false

      const doomed = new Set(indices.filter((i) => i >= 0 && i < known.length))
      if (doomed.size === 0) return false
      if (doomed.size === 1) return this.deleteCorner(ringIndex, [...doomed][0], path)

      return this.replaceRing(ringIndex, known.filter((_, i) => !doomed.has(i)), path)
    },

    deleteCorner(ringIndex, index, path = 0) {
      const entry = mounted[ringIndex]
      const known = entry && cornersOf(entry, path)
      const mvc = entry && pathOf(entry, path)
      if (!entry || !known || !mvc || index < 0 || index >= known.length) return false

      const remaining = known.filter((_, i) => i !== index)
      const refusal = remaining.length < 3 ? tooFewMessage(path) : refusalFor(entry, path, remaining)
      if (refusal) {
        refuse(refusal.message)
        return false
      }

      const removed = known[index]
      withWriting(() => mvc.removeAt(index))
      known.splice(index, 1)
      store().applyChange({ op: 'delete', ring: ringIndex, ...onPath(path), index, before: removed })
      return true
    },

    unmount() {
      clear()
    },
  }
}
