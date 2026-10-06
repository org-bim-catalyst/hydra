import { afterEach, describe, expect, it, vi } from 'vitest'
import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import { closeRing } from './ringGeometry'
import { useSiteBoundaryEditStore, type ViewState } from './siteBoundaryEditStore'

const LAT = 23.59
const LON = 58.4
const P = (dEast: number, dNorth: number): GeoPoint => ({
  latitude: LAT + dNorth / 111_320,
  longitude: LON + dEast / (111_320 * Math.cos((LAT * Math.PI) / 180)),
})

const square = (): GeoPoint[] => [P(0, 0), P(100, 0), P(100, 100), P(0, 100)]

const viewState: ViewState = {
  mode: 'isometric',
  rotationEnabled: true,
  center: { latitude: LAT, longitude: LON },
  zoom: 17,
  heading: 42,
  tilt: 45,
}

const store = () => useSiteBoundaryEditStore.getState()
const session = () => {
  const s = store().session
  if (!s) throw new Error('no session')
  return s
}

const enter = (rings: GeoPoint[][] = [closeRing(square())]) =>
  store().enter({ chatId: 'chat-1', siteName: 'Muscat Grand Mall', revision: 'rev-1', rings, viewState })

afterEach(() => {
  store().end()
  store().consumeRequest()
})

describe('siteBoundaryEditStore', () => {
  describe('enter', () => {
    it('starts an editing session from the closed rings the API carries', () => {
      enter()
      const s = session()
      expect(s.status).toEqual({ kind: 'editing' })
      expect(s.rings[0]).toHaveLength(4)
      expect(s.startRings).toEqual(s.rings)
      expect(s.baseRevision).toBe('rev-1')
      expect(s.viewState).toEqual(viewState)
      expect(s.approxAreaSquareMeters).toBeCloseTo(10_000, -1)
      expect(store().isDirty()).toBe(false)
    })

    it('does not alias the rings it was given', () => {
      const rings = [square()]
      enter(rings)
      store().applyChange({ op: 'move', ring: 0, index: 0, before: rings[0][0], after: P(-10, -10) })
      expect(rings[0][0]).toEqual(P(0, 0))
    })
  })

  describe('applyChange', () => {
    it('moves a corner, records it for undo and updates the area', () => {
      enter()
      const before = session().approxAreaSquareMeters
      store().applyChange({ op: 'move', ring: 0, index: 2, before: P(100, 100), after: P(120, 120) })
      expect(session().rings[0][2]).toEqual(P(120, 120))
      expect(session().undo).toHaveLength(1)
      expect(session().approxAreaSquareMeters).toBeGreaterThan(before)
      expect(store().isDirty()).toBe(true)
    })

    it('inserts and deletes corners', () => {
      enter()
      store().applyChange({ op: 'insert', ring: 0, index: 1, after: P(50, -20) })
      expect(session().rings[0]).toHaveLength(5)
      expect(session().rings[0][1]).toEqual(P(50, -20))
      store().applyChange({ op: 'delete', ring: 0, index: 1, before: P(50, -20) })
      expect(session().rings[0]).toHaveLength(4)
    })

    it('clears redo when a new change follows an undo', () => {
      enter()
      store().applyChange({ op: 'move', ring: 0, index: 0, before: P(0, 0), after: P(-5, -5) })
      store().undo()
      expect(session().redo).toHaveLength(1)
      store().applyChange({ op: 'move', ring: 0, index: 1, before: P(100, 0), after: P(105, 0) })
      expect(session().redo).toHaveLength(0)
    })

    it('clears a previous refusal', () => {
      enter()
      store().refuse('That would make the outline cross itself.')
      store().applyChange({ op: 'move', ring: 0, index: 0, before: P(0, 0), after: P(-5, -5) })
      expect(session().refusal).toBeNull()
    })
  })

  describe('drag coalescing', () => {
    it('merges the many moves of one drag into a single undo step that returns to where the corner started', () => {
      vi.useFakeTimers()
      try {
        enter()
        store().applyChange({ op: 'move', ring: 0, index: 2, before: P(100, 100), after: P(101, 101) })
        vi.advanceTimersByTime(50)
        store().applyChange({ op: 'move', ring: 0, index: 2, before: P(101, 101), after: P(105, 105) })
        vi.advanceTimersByTime(50)
        store().applyChange({ op: 'move', ring: 0, index: 2, before: P(105, 105), after: P(110, 110) })

        expect(session().undo).toHaveLength(1)
        expect(session().rings[0][2]).toEqual(P(110, 110))
        store().undo()
        expect(session().rings[0][2]).toEqual(P(100, 100))
      } finally {
        vi.useRealTimers()
      }
    })

    it('keeps a later move of the same corner, after a pause, as its own step', () => {
      vi.useFakeTimers()
      try {
        enter()
        store().applyChange({ op: 'move', ring: 0, index: 2, before: P(100, 100), after: P(110, 110) })
        vi.advanceTimersByTime(2_000)
        store().applyChange({ op: 'move', ring: 0, index: 2, before: P(110, 110), after: P(120, 120) })
        expect(session().undo).toHaveLength(2)
      } finally {
        vi.useRealTimers()
      }
    })

    it('never merges moves of different corners', () => {
      enter()
      store().applyChange({ op: 'move', ring: 0, index: 1, before: P(100, 0), after: P(105, 0) })
      store().applyChange({ op: 'move', ring: 0, index: 2, before: P(100, 100), after: P(105, 105) })
      expect(session().undo).toHaveLength(2)
    })
  })

  describe('whole-ring changes (delete several corners, round, curve, circle)', () => {
    const shrunk = (): GeoPoint[] => [P(0, 0), P(100, 0), P(50, 100)]

    it('swaps the ring in one undo step and updates the area', () => {
      enter()
      const before = session().rings[0]

      store().applyChange({ op: 'replace', ring: 0, before, after: shrunk() })

      expect(session().rings[0]).toEqual(shrunk())
      expect(session().undo).toHaveLength(1)
      expect(session().approxAreaSquareMeters).toBeCloseTo(5_000, -1)
    })

    it('undo puts every corner back, and redo re-applies the swap', () => {
      enter()
      const before = session().rings[0]
      store().applyChange({ op: 'replace', ring: 0, before, after: shrunk() })

      store().undo()
      expect(session().rings[0]).toEqual(before)
      expect(store().isDirty()).toBe(false)

      store().redo()
      expect(session().rings[0]).toEqual(shrunk())
    })

    it('never merges with a move, and clears a selection that no longer points at anything', () => {
      enter()
      store().selectCorners([1, 2])
      store().applyChange({ op: 'move', ring: 0, index: 0, before: P(0, 0), after: P(-5, -5) })
      store().applyChange({ op: 'replace', ring: 0, before: session().rings[0], after: shrunk() })

      expect(session().undo).toHaveLength(2)
      expect(session().selectedCorners).toEqual([])
      expect(session().selectedCorner).toBeNull()
    })

    it('clears the selection again when the swap is undone', () => {
      enter()
      store().applyChange({ op: 'replace', ring: 0, before: session().rings[0], after: shrunk() })
      store().selectCorners([0])

      store().undo()

      expect(session().selectedCorners).toEqual([])
    })
  })

  describe('selecting corners', () => {
    it('selectCorner selects exactly one', () => {
      enter()
      store().selectCorners([0, 1, 2])
      store().selectCorner(3)
      expect(session().selectedCorners).toEqual([3])
      expect(session().selectedCorner).toBe(3)
    })

    it('selectCorner(null) clears the selection', () => {
      enter()
      store().selectCorners([1, 2])
      store().selectCorner(null)
      expect(session().selectedCorners).toEqual([])
      expect(session().selectedCorner).toBeNull()
    })

    it('selectCorners replaces the selection, sorted and without duplicates, with the last as the main one', () => {
      enter()
      store().selectCorners([3, 1, 3, 2])
      expect(session().selectedCorners).toEqual([1, 2, 3])
      expect(session().selectedCorner).toBe(3)
    })

    it('toggleCorner adds and removes a corner', () => {
      enter()
      store().selectCorner(1)

      store().toggleCorner(3)
      expect(session().selectedCorners).toEqual([1, 3])
      expect(session().selectedCorner).toBe(3)

      store().toggleCorner(1)
      expect(session().selectedCorners).toEqual([3])
    })

    it('changing the active ring clears the selection', () => {
      enter([square(), [P(300, 0), P(320, 0), P(320, 20)]])
      store().selectCorners([0, 1])
      store().setActiveRing(1)
      expect(session().selectedCorners).toEqual([])
    })
  })

  describe('the arc tool', () => {
    it('remembers which two corners the arc joins, and forgets them on any other tool', () => {
      enter()

      store().beginArc([0, 2])
      expect(session().tool).toBe('arc')
      expect(session().arcAnchors).toEqual([0, 2])

      store().setTool('edit')
      expect(session().tool).toBe('edit')
      expect(session().arcAnchors).toBeNull()
    })

    it('starts a session with no arc in progress', () => {
      enter()
      expect(session().arcAnchors).toBeNull()
    })
  })

  describe('the circle tool', () => {
    it('remembers whether the circle is added or cut, and forgets it on any other tool', () => {
      enter()

      store().beginCircle('cut')
      expect(session().tool).toBe('circle')
      expect(session().circleOperation).toBe('cut')

      store().setTool('edit')
      expect(session().circleOperation).toBeNull()
    })
  })

  describe('replacing every ring at once', () => {
    const separate = () => [P(300, 0), P(320, 0), P(320, 20)]

    it('swaps in a different number of rings, and undo and redo swap them back', () => {
      enter()
      const before = session().rings

      store().applyChange({ op: 'replaceAll', before, after: [square(), separate()], beforeVoids: [[]], afterVoids: [[], []] })
      expect(session().rings).toHaveLength(2)
      expect(store().isDirty()).toBe(true)

      store().undo()
      expect(session().rings).toHaveLength(1)

      store().redo()
      expect(session().rings).toHaveLength(2)
    })

    it('clears the selection and returns to the first ring, since the old indices mean nothing now', () => {
      enter([square(), separate()])
      store().setActiveRing(1)
      store().selectCorners([0, 1])

      store().applyChange({ op: 'replaceAll', before: session().rings, after: [square()], beforeVoids: session().voids, afterVoids: [[]] })

      expect(session().activeRing).toBe(0)
      expect(session().selectedCorners).toEqual([])
    })
  })

  describe('voids (specs/081)', () => {
    const atrium = (): GeoPoint[] => [P(40, 40), P(60, 40), P(60, 60), P(40, 60)]
    const second = (): GeoPoint[] => [P(10, 10), P(20, 10), P(20, 20), P(10, 20)]

    it('starts with the voids of each ring, and no voids as a ring with none', () => {
      store().enter({ chatId: 'c', siteName: 'S', revision: 'r', rings: [square(), [P(300, 0), P(320, 0), P(320, 20)]], voids: [[atrium()]], viewState })

      expect(session().voids).toHaveLength(2)
      expect(session().voids[0]).toHaveLength(1)
      expect(session().voids[1]).toEqual([])
      expect(session().startVoids).toEqual(session().voids)
    })

    it('subtracts the voids from the area', () => {
      store().enter({ chatId: 'c', siteName: 'S', revision: 'r', rings: [square()], voids: [[atrium()]], viewState })

      expect(session().approxAreaSquareMeters).toBeCloseTo(10_000 - 400, -1)
    })

    it('replaceAll carries voids, and undo and redo carry them back and forth', () => {
      enter()
      const before = session().rings

      store().applyChange({ op: 'replaceAll', before, after: [square()], beforeVoids: [[]], afterVoids: [[atrium()]] })
      expect(session().voids[0]).toHaveLength(1)
      expect(session().approxAreaSquareMeters).toBeCloseTo(9_600, -1)
      expect(store().isDirty()).toBe(true)

      store().undo()
      expect(session().voids[0]).toEqual([])
      expect(session().approxAreaSquareMeters).toBeCloseTo(10_000, -1)
      expect(store().isDirty()).toBe(false)

      store().redo()
      expect(session().voids[0]).toHaveLength(1)
    })

    it('changes a void corner by path, and undoes it', () => {
      store().enter({ chatId: 'c', siteName: 'S', revision: 'r', rings: [square()], voids: [[atrium()]], viewState })

      store().applyChange({ op: 'move', ring: 0, path: 1, index: 2, before: atrium()[2], after: P(70, 70) })
      expect(session().voids[0][0][2]).toEqual(P(70, 70))
      expect(session().rings[0]).toEqual(square())

      store().undo()
      expect(session().voids[0][0][2]).toEqual(atrium()[2])
    })

    it('inserts and deletes a void corner by path', () => {
      store().enter({ chatId: 'c', siteName: 'S', revision: 'r', rings: [square()], voids: [[atrium()]], viewState })

      store().applyChange({ op: 'insert', ring: 0, path: 1, index: 1, after: P(50, 38) })
      expect(session().voids[0][0]).toHaveLength(5)
      store().applyChange({ op: 'delete', ring: 0, path: 1, index: 1, before: P(50, 38) })
      expect(session().voids[0][0]).toHaveLength(4)

      store().undo()
      expect(session().voids[0][0]).toHaveLength(5)
      store().undo()
      expect(session().voids[0][0]).toHaveLength(4)
    })

    it('does not merge moves of different paths into one undo step', () => {
      store().enter({ chatId: 'c', siteName: 'S', revision: 'r', rings: [square()], voids: [[atrium()]], viewState })

      store().applyChange({ op: 'move', ring: 0, index: 1, before: square()[1], after: P(101, 0) })
      store().applyChange({ op: 'move', ring: 0, path: 1, index: 1, before: atrium()[1], after: P(61, 40) })

      expect(session().undo).toHaveLength(2)
    })

    it('removes a void in one undo step, and undo puts it back in the same place', () => {
      store().enter({ chatId: 'c', siteName: 'S', revision: 'r', rings: [square()], voids: [[atrium(), second()]], viewState })

      store().applyChange({ op: 'removeVoid', ring: 0, voidIndex: 0, before: atrium() })
      expect(session().voids[0]).toEqual([second()])
      expect(session().approxAreaSquareMeters).toBeCloseTo(10_000 - 100, -1)

      store().undo()
      expect(session().voids[0]).toEqual([atrium(), second()])
      expect(session().approxAreaSquareMeters).toBeCloseTo(10_000 - 500, -1)
    })

    it('rebases onto freshly loaded voids', () => {
      enter()

      store().rebase('rev-2', [square()], [[atrium()]])

      expect(session().voids[0]).toHaveLength(1)
      expect(session().startVoids).toEqual(session().voids)
      expect(store().isDirty()).toBe(false)
    })
  })

  describe('tools', () => {
    it('starts on edit and can switch to select and back', () => {
      enter()
      expect(session().tool).toBe('edit')
      store().setTool('select')
      expect(session().tool).toBe('select')
      store().setTool('edit')
      expect(session().tool).toBe('edit')
    })
  })

  describe('the floating bar', () => {
    it('starts shown, can be dismissed and restored, and never affects the edit itself', () => {
      enter()
      expect(session().toolbarHidden).toBe(false)
      store().applyChange({ op: 'move', ring: 0, index: 0, before: P(0, 0), after: P(-5, -5) })

      store().setToolbarHidden(true)
      expect(session().toolbarHidden).toBe(true)
      expect(session().undo).toHaveLength(1)

      store().setToolbarHidden(false)
      expect(session().toolbarHidden).toBe(false)
    })

    it('comes back on its own when a save fails or conflicts', () => {
      enter()
      store().setToolbarHidden(true)
      store().saveFailed('The outline could not be saved.')
      expect(session().toolbarHidden).toBe(false)

      store().setToolbarHidden(true)
      store().conflict('rev-9')
      expect(session().toolbarHidden).toBe(false)
    })

    it('starts shown again for the next session', () => {
      enter()
      store().setToolbarHidden(true)
      store().end()
      enter()
      expect(session().toolbarHidden).toBe(false)
    })
  })

  describe('refuse', () => {
    it('sets only the message and leaves the shape and undo stack alone', () => {
      enter()
      const rings = session().rings
      store().refuse("Two corners can't be in the same spot.")
      expect(session().refusal).toBe("Two corners can't be in the same spot.")
      expect(session().rings).toEqual(rings)
      expect(session().undo).toHaveLength(0)
    })

    it('can be cleared', () => {
      enter()
      store().refuse('x')
      store().clearRefusal()
      expect(session().refusal).toBeNull()
    })
  })

  describe('undo and redo', () => {
    it('reverses changes in reverse order back to the start shape (FR-012)', () => {
      enter()
      store().applyChange({ op: 'move', ring: 0, index: 0, before: P(0, 0), after: P(-5, -5) })
      store().applyChange({ op: 'insert', ring: 0, index: 1, after: P(50, -20) })
      store().applyChange({ op: 'delete', ring: 0, index: 3, before: P(100, 100) })

      expect(store().undo()?.op).toBe('delete')
      expect(store().undo()?.op).toBe('insert')
      expect(store().undo()?.op).toBe('move')
      expect(session().rings).toEqual(session().startRings)
      expect(store().isDirty()).toBe(false)
      expect(store().undo()).toBeNull()
    })

    it('redo re-applies what undo took back', () => {
      enter()
      store().applyChange({ op: 'move', ring: 0, index: 0, before: P(0, 0), after: P(-5, -5) })
      store().undo()
      expect(store().redo()?.op).toBe('move')
      expect(session().rings[0][0]).toEqual(P(-5, -5))
      expect(store().redo()).toBeNull()
    })

    it('recomputes the area on undo', () => {
      enter()
      const start = session().approxAreaSquareMeters
      store().applyChange({ op: 'move', ring: 0, index: 2, before: P(100, 100), after: P(150, 150) })
      store().undo()
      expect(session().approxAreaSquareMeters).toBeCloseTo(start, 3)
    })
  })

  describe('rings and corners', () => {
    it('switches the active ring within range and clears the selected corner', () => {
      enter([square(), [P(300, 0), P(320, 0), P(320, 20)]])
      store().selectCorner(2)
      store().setActiveRing(1)
      expect(session().activeRing).toBe(1)
      expect(session().selectedCorner).toBeNull()
    })

    it('ignores an out-of-range ring', () => {
      enter()
      store().setActiveRing(4)
      expect(session().activeRing).toBe(0)
    })

    it('edits a second ring without touching the first', () => {
      enter([square(), [P(300, 0), P(320, 0), P(320, 20)]])
      const first = session().rings[0]
      store().applyChange({ op: 'move', ring: 1, index: 2, before: P(320, 20), after: P(330, 40) })
      expect(session().rings[0]).toEqual(first)
      expect(session().rings[1][2]).toEqual(P(330, 40))
    })
  })

  describe('save lifecycle', () => {
    it('editing to saving, then an error keeps the session and shows the message (FR-018)', () => {
      enter()
      store().applyChange({ op: 'move', ring: 0, index: 0, before: P(0, 0), after: P(-5, -5) })
      store().beginSave()
      expect(session().status).toEqual({ kind: 'saving' })
      store().saveFailed('Could not save the outline.')
      expect(session().status).toEqual({ kind: 'error', message: 'Could not save the outline.' })
      expect(session().undo).toHaveLength(1)
    })

    it('a conflict carries the revision now in force (FR-019)', () => {
      enter()
      store().beginSave()
      store().conflict('rev-9')
      expect(session().status).toEqual({ kind: 'conflict', currentRevision: 'rev-9' })
    })

    it('Load latest rebases on the fetched outline, keeping the view state', () => {
      enter()
      store().applyChange({ op: 'move', ring: 0, index: 0, before: P(0, 0), after: P(-5, -5) })
      store().conflict('rev-9')
      const fresh = [closeRing([P(0, 0), P(200, 0), P(200, 200), P(0, 200)])]
      store().rebase('rev-9', fresh)
      const s = session()
      expect(s.baseRevision).toBe('rev-9')
      expect(s.rings[0]).toHaveLength(4)
      expect(s.startRings).toEqual(s.rings)
      expect(s.undo).toHaveLength(0)
      expect(s.status).toEqual({ kind: 'editing' })
      expect(s.viewState).toEqual(viewState)
      expect(store().isDirty()).toBe(false)
    })
  })

  describe('end', () => {
    it('ends the session (Cancel and forced exit)', () => {
      enter()
      store().end()
      expect(store().session).toBeNull()
    })
  })

  describe('edit requests (the siteBoundaryEdit stream event)', () => {
    it('holds a request until the viewer consumes it, once', () => {
      store().requestEdit({ chatId: 'chat-1', revision: 'rev-1' })
      expect(store().pendingRequest).toEqual({ chatId: 'chat-1', revision: 'rev-1' })
      expect(store().consumeRequest()).toEqual({ chatId: 'chat-1', revision: 'rev-1' })
      expect(store().pendingRequest).toBeNull()
      expect(store().consumeRequest()).toBeNull()
    })

    it('keeps only the latest request', () => {
      store().requestEdit({ chatId: 'chat-1', revision: 'rev-1' })
      store().requestEdit({ chatId: 'chat-1', revision: 'rev-2' })
      expect(store().consumeRequest()).toEqual({ chatId: 'chat-1', revision: 'rev-2' })
    })
  })

  it('does nothing when no session is open', () => {
    store().applyChange({ op: 'move', ring: 0, index: 0, before: P(0, 0), after: P(1, 1) })
    store().refuse('x')
    store().beginSave()
    expect(store().session).toBeNull()
    expect(store().undo()).toBeNull()
    expect(store().isDirty()).toBe(false)
  })

  it('keeps the session in a module-level store, so it survives a component unmounting (FR-029)', () => {
    enter()
    store().applyChange({ op: 'move', ring: 0, index: 0, before: P(0, 0), after: P(-5, -5) })
    // A fresh read from the same module - what a re-mounted /studio would do - sees the same session.
    expect(useSiteBoundaryEditStore.getState().session?.undo).toHaveLength(1)
  })
})
