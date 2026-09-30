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
