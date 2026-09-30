import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import {
  createEditablePolygonController,
  type EditablePath,
  type EditablePolygonHost,
  type EditableRing,
} from './editablePolygonController'
import { useSiteBoundaryEditStore, type ViewState } from './siteBoundaryEditStore'

const LAT = 23.59
const LON = 58.4
const P = (east: number, north: number): GeoPoint => ({
  latitude: LAT + north / 111_320,
  longitude: LON + east / (111_320 * Math.cos((LAT * Math.PI) / 180)),
})

const square = (): GeoPoint[] => [P(0, 0), P(100, 0), P(100, 100), P(0, 100)]
const triangle = (): GeoPoint[] => [P(300, 0), P(340, 0), P(320, 40)]

const viewState: ViewState = {
  mode: 'isometric',
  rotationEnabled: true,
  center: { latitude: LAT, longitude: LON },
  zoom: 17,
  heading: 0,
  tilt: 45,
}

/** A path that reports its own changes to listeners the way an MVCArray does. */
class FakePath implements EditablePath {
  points: GeoPoint[]
  private setAtListeners: ((i: number) => void)[] = []
  private insertAtListeners: ((i: number) => void)[] = []
  private removeAtListeners: ((i: number, removed: GeoPoint) => void)[] = []

  constructor(points: GeoPoint[]) {
    this.points = [...points]
  }

  getLength = () => this.points.length
  getAt = (i: number) => this.points[i]

  setAt = (i: number, point: GeoPoint) => {
    this.points[i] = point
    this.setAtListeners.forEach((l) => l(i))
  }

  insertAt = (i: number, point: GeoPoint) => {
    this.points.splice(i, 0, point)
    this.insertAtListeners.forEach((l) => l(i))
  }

  removeAt = (i: number) => {
    const [removed] = this.points.splice(i, 1)
    this.removeAtListeners.forEach((l) => l(i, removed))
  }

  onSetAt = (l: (i: number) => void) => {
    this.setAtListeners.push(l)
    return () => (this.setAtListeners = this.setAtListeners.filter((x) => x !== l))
  }

  onInsertAt = (l: (i: number) => void) => {
    this.insertAtListeners.push(l)
    return () => (this.insertAtListeners = this.insertAtListeners.filter((x) => x !== l))
  }

  onRemoveAt = (l: (i: number, removed: GeoPoint) => void) => {
    this.removeAtListeners.push(l)
    return () => (this.removeAtListeners = this.removeAtListeners.filter((x) => x !== l))
  }

  get listenerCount() {
    return this.setAtListeners.length + this.insertAtListeners.length + this.removeAtListeners.length
  }
}

class FakeRing implements EditableRing {
  editable: boolean
  removed = false
  selectListeners: (() => void)[] = []
  menuListeners: ((i: number, x: number, y: number) => void)[] = []
  clickListeners: ((i: number) => void)[] = []
  highlighted: number | null = null
  path: FakePath

  constructor(corners: GeoPoint[], editable: boolean) {
    this.path = new FakePath(corners)
    this.editable = editable
  }

  setEditable = (editable: boolean) => {
    this.editable = editable
  }

  onSelect = (l: () => void) => {
    this.selectListeners.push(l)
    return () => (this.selectListeners = this.selectListeners.filter((x) => x !== l))
  }

  onVertexClick = (l: (i: number) => void) => {
    this.clickListeners.push(l)
    return () => (this.clickListeners = this.clickListeners.filter((x) => x !== l))
  }

  setHighlight = (index: number | null) => {
    this.highlighted = index
  }

  onVertexMenu = (l: (i: number, x: number, y: number) => void) => {
    this.menuListeners.push(l)
    return () => (this.menuListeners = this.menuListeners.filter((x) => x !== l))
  }

  remove = () => {
    this.removed = true
  }
}

class FakeHost implements EditablePolygonHost {
  rings: FakeRing[] = []

  createRing(corners: GeoPoint[], options: { editable: boolean }) {
    const ring = new FakeRing(corners, options.editable)
    this.rings.push(ring)
    return ring
  }
}

const store = () => useSiteBoundaryEditStore.getState()
const session = () => {
  const s = store().session
  if (!s) throw new Error('no session')
  return s
}

function setup(rings: GeoPoint[][] = [square()], options: Parameters<typeof createEditablePolygonController>[1] = {}) {
  store().enter({ chatId: 'chat-1', siteName: 'Muscat Grand Mall', revision: 'rev-1', rings, viewState })
  const host = new FakeHost()
  const controller = createEditablePolygonController(host, options)
  controller.mount(rings, 0)
  return { host, controller }
}

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
  store().end()
  store().consumeRequest()
})

describe('mount', () => {
  it('draws one ring per ring, only the active one editable', () => {
    const { host } = setup([square(), triangle()])

    expect(host.rings).toHaveLength(2)
    expect(host.rings.map((r) => r.editable)).toEqual([true, false])
  })

  it('draws the corners without a repeated closing corner', () => {
    const closed = [...square(), square()[0]]
    const { host } = setup([closed])

    expect(host.rings[0].path.getLength()).toBe(4)
  })
})

describe('moving a corner', () => {
  it('records a valid move for undo and updates the area', () => {
    const { host } = setup()
    const before = session().approxAreaSquareMeters

    host.rings[0].path.setAt(2, P(120, 120))

    expect(session().rings[0][2]).toEqual(P(120, 120))
    expect(session().undo).toHaveLength(1)
    expect(session().undo[0]).toMatchObject({ op: 'move', ring: 0, index: 2 })
    expect(session().approxAreaSquareMeters).toBeGreaterThan(before)
    expect(session().refusal).toBeNull()
  })

  it('puts a corner back, and says why, when it would cross the outline over itself', () => {
    const { host } = setup()

    host.rings[0].path.setAt(2, P(50, -50))

    expect(host.rings[0].path.getAt(2)).toEqual(P(100, 100))
    expect(session().rings[0][2]).toEqual(P(100, 100))
    expect(session().undo).toHaveLength(0)
    expect(session().refusal).toBe('That would make the outline cross itself.')
  })

  it('does not report its own revert as a second edit', () => {
    const { host } = setup()

    host.rings[0].path.setAt(2, P(50, -50))
    host.rings[0].path.setAt(2, P(110, 110))

    expect(session().undo).toHaveLength(1)
    expect(session().refusal).toBeNull()
  })

  it('refuses a corner dropped on another corner', () => {
    const { host } = setup()

    host.rings[0].path.setAt(1, P(0.01, 0))

    expect(session().refusal).toBe("Two corners can't be in the same spot.")
    expect(host.rings[0].path.getAt(1)).toEqual(P(100, 0))
  })

  it('merges the many moves of one drag into one undo step', () => {
    const { host } = setup()

    host.rings[0].path.setAt(2, P(102, 102))
    vi.advanceTimersByTime(30)
    host.rings[0].path.setAt(2, P(108, 108))
    vi.advanceTimersByTime(30)
    host.rings[0].path.setAt(2, P(115, 115))

    expect(session().undo).toHaveLength(1)
    store().undo()
    expect(session().rings[0][2]).toEqual(P(100, 100))
  })
})

describe('adding a corner', () => {
  it('records an inserted corner', () => {
    const { host } = setup()

    host.rings[0].path.insertAt(1, P(50, -20))

    expect(session().rings[0]).toHaveLength(5)
    expect(session().undo[0]).toMatchObject({ op: 'insert', ring: 0, index: 1 })
  })

  it('removes an inserted corner that would make the outline cross itself', () => {
    const { host } = setup()

    host.rings[0].path.insertAt(1, P(50, 150))

    expect(host.rings[0].path.getLength()).toBe(4)
    expect(session().rings[0]).toHaveLength(4)
    expect(session().refusal).toBe('That would make the outline cross itself.')
  })
})

describe('deleting a corner', () => {
  it('records a corner removed from the path', () => {
    const { host } = setup([[...square(), P(50, 130)]])

    host.rings[0].path.removeAt(4)

    expect(session().rings[0]).toHaveLength(4)
    expect(session().undo[0]).toMatchObject({ op: 'delete', ring: 0, index: 4 })
  })

  it('refuses to go below 3 corners and puts the corner back', () => {
    const { host } = setup([triangle()])

    host.rings[0].path.removeAt(1)

    expect(host.rings[0].path.getLength()).toBe(3)
    expect(host.rings[0].path.getAt(1)).toEqual(triangle()[1])
    expect(session().refusal).toBe('An outline needs at least 3 corners.')
    expect(session().undo).toHaveLength(0)
  })

  it('deleteCorner removes a corner, records it, and reports success', () => {
    const { host, controller } = setup([[...square(), P(50, 130)]])

    expect(controller.deleteCorner(0, 4)).toBe(true)

    expect(host.rings[0].path.getLength()).toBe(4)
    expect(session().rings[0]).toHaveLength(4)
    expect(session().undo).toHaveLength(1)
  })

  it('deleteCorner is refused below 3 corners, leaving everything as it was', () => {
    const { host, controller } = setup([triangle()])

    expect(controller.deleteCorner(0, 0)).toBe(false)

    expect(host.rings[0].path.getLength()).toBe(3)
    expect(session().refusal).toBe('An outline needs at least 3 corners.')
  })

  it('deleteCorner ignores a corner that does not exist', () => {
    const { controller } = setup()

    expect(controller.deleteCorner(0, 9)).toBe(false)
    expect(controller.deleteCorner(5, 0)).toBe(false)
  })
})

describe('insertCornerAfter (Add corner)', () => {
  it('adds a corner midway to the next one, records it and selects it', () => {
    const { host, controller } = setup()

    expect(controller.insertCornerAfter(0, 0)).toBe(true)

    expect(host.rings[0].path.getLength()).toBe(5)
    expect(host.rings[0].path.getAt(1).longitude).toBeCloseTo((P(0, 0).longitude + P(100, 0).longitude) / 2, 9)
    expect(session().rings[0]).toHaveLength(5)
    expect(session().undo[0]).toMatchObject({ op: 'insert', ring: 0, index: 1 })
    expect(session().selectedCorner).toBe(1)
  })

  it('wraps from the last corner to the first', () => {
    const { host, controller } = setup()

    expect(controller.insertCornerAfter(0, 3)).toBe(true)

    expect(host.rings[0].path.getLength()).toBe(5)
    expect(session().undo[0]).toMatchObject({ op: 'insert', ring: 0, index: 4 })
  })

  it('ignores a corner that does not exist', () => {
    const { controller } = setup()

    expect(controller.insertCornerAfter(0, 9)).toBe(false)
    expect(controller.insertCornerAfter(4, 0)).toBe(false)
  })
})

describe('rings', () => {
  it('makes a clicked ring the editable one', () => {
    const { host, controller } = setup([square(), triangle()])

    host.rings[1].selectListeners.forEach((l) => l())
    controller.setActiveRing(session().activeRing)

    expect(session().activeRing).toBe(1)
    expect(host.rings.map((r) => r.editable)).toEqual([false, true])
  })

  it('attributes an edit to the ring that reported it', () => {
    const { host } = setup([square(), triangle()])

    host.rings[1].path.setAt(2, P(330, 60))

    expect(session().undo[0]).toMatchObject({ op: 'move', ring: 1, index: 2 })
    expect(session().rings[0]).toEqual(square())
  })
})

describe('selecting a corner', () => {
  it('a click on a corner selects it and highlights it', () => {
    const { host } = setup()

    host.rings[0].clickListeners.forEach((l) => l(2))

    expect(session().selectedCorner).toBe(2)
    expect(host.rings[0].highlighted).toBe(2)
  })

  it('a click on a corner of another ring makes that ring active and selects the corner', () => {
    const { host } = setup([square(), triangle()])

    host.rings[1].clickListeners.forEach((l) => l(1))

    expect(session().activeRing).toBe(1)
    expect(session().selectedCorner).toBe(1)
    expect(host.rings[1].highlighted).toBe(1)
    expect(host.rings[0].highlighted).toBeNull()
  })

  it('clearing the selection removes the highlight', () => {
    const { host } = setup()
    host.rings[0].clickListeners.forEach((l) => l(2))

    store().selectCorner(null)

    expect(host.rings[0].highlighted).toBeNull()
  })

  it('stops following the store after unmount', () => {
    const { host, controller } = setup()
    controller.unmount()

    store().selectCorner(1)

    expect(host.rings[0].highlighted).toBeNull()
  })
})

describe('vertex menu', () => {
  it('selects the corner and asks for the menu where the pointer is', () => {
    const onVertexMenu = vi.fn()
    const { host } = setup([square()], { onVertexMenu })

    host.rings[0].menuListeners.forEach((l) => l(2, 410, 220))

    expect(session().selectedCorner).toBe(2)
    expect(onVertexMenu).toHaveBeenCalledWith({ ring: 0, index: 2, clientX: 410, clientY: 220 })
  })
})

describe('setRings (undo, redo, Load latest)', () => {
  it('rewrites the paths without reporting an edit', () => {
    const { host, controller } = setup()
    host.rings[0].path.setAt(2, P(120, 120))
    expect(session().undo).toHaveLength(1)

    store().undo()
    controller.setRings(session().rings, session().activeRing)

    expect(host.rings[0].path.getAt(2)).toEqual(P(100, 100))
    expect(session().undo).toHaveLength(0)
    expect(session().redo).toHaveLength(1)
  })

  it('follows a change in corner count', () => {
    const { host, controller } = setup()
    host.rings[0].path.insertAt(1, P(50, -20))
    store().undo()

    controller.setRings(session().rings, session().activeRing)

    expect(host.rings[0].path.getLength()).toBe(4)
  })

  it('redraws when the number of rings differs', () => {
    const { host, controller } = setup()

    controller.setRings([square(), triangle()], 0)

    expect(host.rings.filter((r) => !r.removed)).toHaveLength(2)
  })
})

describe('unmount', () => {
  it('removes every ring and every listener', () => {
    const { host, controller } = setup([square(), triangle()])

    controller.unmount()

    expect(host.rings.every((r) => r.removed)).toBe(true)
    expect(host.rings.every((r) => r.path.listenerCount === 0 && r.selectListeners.length === 0 && r.menuListeners.length === 0 && r.clickListeners.length === 0)).toBe(true)

    host.rings[0].path.setAt(2, P(120, 120))
    expect(session().undo).toHaveLength(0)
  })
})
