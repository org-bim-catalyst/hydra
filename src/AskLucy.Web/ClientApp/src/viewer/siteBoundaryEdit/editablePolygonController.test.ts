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
  menuListeners: ((i: number, x: number, y: number, path?: number) => void)[] = []
  clickListeners: ((i: number, additive: boolean, path?: number) => void)[] = []
  dragMoveListeners: ((i: number, point: GeoPoint, path?: number) => void)[] = []
  pressListeners: ((i: number, path?: number) => void)[] = []

  onVertexPress = (l: (i: number, path?: number) => void) => {
    this.pressListeners.push(l)
    return () => (this.pressListeners = this.pressListeners.filter((x) => x !== l))
  }

  onVertexDragMove = (l: (i: number, point: GeoPoint, path?: number) => void) => {
    this.dragMoveListeners.push(l)
    return () => (this.dragMoveListeners = this.dragMoveListeners.filter((x) => x !== l))
  }
  /** The marks on each path of the ring. */
  highlightsByPath: number[][] = []
  get highlights() {
    return this.highlightsByPath[0] ?? []
  }

  path: FakePath
  voidPaths: FakePath[]

  constructor(corners: GeoPoint[], editable: boolean, voids: GeoPoint[][] = []) {
    this.path = new FakePath(corners)
    this.voidPaths = voids.map((v) => new FakePath(v))
    this.editable = editable
  }

  setEditable = (editable: boolean) => {
    this.editable = editable
  }

  onSelect = (l: () => void) => {
    this.selectListeners.push(l)
    return () => (this.selectListeners = this.selectListeners.filter((x) => x !== l))
  }

  onVertexClick = (l: (i: number, additive: boolean, path?: number) => void) => {
    this.clickListeners.push(l)
    return () => (this.clickListeners = this.clickListeners.filter((x) => x !== l))
  }

  setHighlights = (indices: readonly number[], path = 0) => {
    this.highlightsByPath[path] = [...indices]
  }

  onVertexMenu = (l: (i: number, x: number, y: number, path?: number) => void) => {
    this.menuListeners.push(l)
    return () => (this.menuListeners = this.menuListeners.filter((x) => x !== l))
  }

  remove = () => {
    this.removed = true
  }
}

class FakeHost implements EditablePolygonHost {
  rings: FakeRing[] = []
  emptyClickListeners: (() => void)[] = []

  onEmptyClick = (l: () => void) => {
    this.emptyClickListeners.push(l)
    return () => (this.emptyClickListeners = this.emptyClickListeners.filter((x) => x !== l))
  }

  createRing(corners: GeoPoint[], options: { editable: boolean; voids?: GeoPoint[][] }) {
    const ring = new FakeRing(corners, options.editable, options.voids)
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

function setup(
  rings: GeoPoint[][] = [square()],
  options: Parameters<typeof createEditablePolygonController>[1] = {},
  voids: GeoPoint[][][] = [],
) {
  store().enter({ chatId: 'chat-1', siteName: 'Muscat Grand Mall', revision: 'rev-1', rings, voids, viewState })
  const host = new FakeHost()
  const controller = createEditablePolygonController(host, options)
  controller.mount(rings, 0, voids)
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

describe('selecting corners', () => {
  const click = (ring: FakeRing, index: number, additive = false) => ring.clickListeners.forEach((l) => l(index, additive))

  it('a click on a corner selects it and highlights it', () => {
    const { host } = setup()

    click(host.rings[0], 2)

    expect(session().selectedCorner).toBe(2)
    expect(host.rings[0].highlights).toEqual([2])
  })

  it('a Shift-click adds to the selection and a second Shift-click on the same corner removes it', () => {
    const { host } = setup()

    click(host.rings[0], 1)
    click(host.rings[0], 3, true)
    expect(session().selectedCorners).toEqual([1, 3])
    expect(host.rings[0].highlights).toEqual([1, 3])

    click(host.rings[0], 1, true)
    expect(session().selectedCorners).toEqual([3])
    expect(host.rings[0].highlights).toEqual([3])
  })

  it('a plain click on a corner ends a multiple selection (a second click selects one)', () => {
    const { host } = setup()
    store().selectCorners([0, 1, 2])

    click(host.rings[0], 3)

    expect(session().selectedCorners).toEqual([])
    click(host.rings[0], 3)
    expect(session().selectedCorners).toEqual([3])
  })

  it('a click on a corner of another ring makes that ring active and selects the corner', () => {
    const { host } = setup([square(), triangle()])

    click(host.rings[1], 1)

    expect(session().activeRing).toBe(1)
    expect(session().selectedCorner).toBe(1)
    expect(host.rings[1].highlights).toEqual([1])
    expect(host.rings[0].highlights).toEqual([])
  })

  it('highlights every corner a box select picked', () => {
    const { host } = setup()

    store().selectCorners([0, 2, 3])

    expect(host.rings[0].highlights).toEqual([0, 2, 3])
  })

  it('clearing the selection removes the highlights', () => {
    const { host } = setup()
    click(host.rings[0], 2)

    store().selectCorner(null)

    expect(host.rings[0].highlights).toEqual([])
  })

  it('stops following the store after unmount', () => {
    const { host, controller } = setup()
    controller.unmount()

    store().selectCorner(1)

    expect(host.rings[0].highlights).toEqual([])
  })
})

describe('moving a corner with the keyboard', () => {
  it('moves it by the given metres, as one undo step, and updates the map', () => {
    const { host, controller } = setup([square()])

    expect(controller.moveCorner(0, 1, 5, 0)).toBe(true)

    expect(session().rings[0][1].longitude).toBeGreaterThan(P(100, 0).longitude)
    expect(host.rings[0].path.getAt(1)).toEqual(session().rings[0][1])
    expect(session().undo).toHaveLength(1)
    expect(session().undo[0].op).toBe('move')
  })

  it('refuses a move that would make the outline cross itself, changing nothing', () => {
    const { controller } = setup([square()])

    expect(controller.moveCorner(0, 1, -200, 0)).toBe(false)

    expect(session().rings[0][1]).toEqual(P(100, 0))
    expect(session().undo).toHaveLength(0)
    expect(session().refusal).not.toBeNull()
  })

  it('ignores a corner that does not exist', () => {
    const { controller } = setup([square()])
    expect(controller.moveCorner(0, 9, 1, 1)).toBe(false)
    expect(controller.moveCorner(4, 0, 1, 1)).toBe(false)
  })
})

describe('replacing every ring at once', () => {
  const separate = (): GeoPoint[] => [P(300, 0), P(320, 0), P(320, 20)]

  it('takes a different number of rings as one undo step, and redraws the map', () => {
    const { host, controller } = setup([square()])

    expect(controller.replaceAllRings([square(), separate()])).toBe(true)

    expect(host.rings.filter((r) => !r.removed)).toHaveLength(2)
    expect(session().rings).toHaveLength(2)
    expect(session().undo).toHaveLength(1)
    expect(session().undo[0].op).toBe('replaceAll')
  })

  it('refuses, changing nothing, when one of the rings is not a valid outline', () => {
    const { controller } = setup([square()])
    const bowtie = [P(0, 0), P(100, 100), P(100, 0), P(0, 100)]

    expect(controller.replaceAllRings([square(), bowtie])).toBe(false)

    expect(session().rings).toHaveLength(1)
    expect(session().undo).toHaveLength(0)
    expect(session().refusal).toContain('Ring 2')
  })

  it('refuses an empty result', () => {
    const { controller } = setup([square()])

    expect(controller.replaceAllRings([])).toBe(false)
    expect(session().rings).toHaveLength(1)
  })
})

describe('deleting several corners at once', () => {
  /** A square with two extra corners on the bottom edge, which are safe to remove. */
  const withMidpoints = (): GeoPoint[] => [P(0, 0), P(30, 0), P(60, 0), P(100, 0), P(100, 100), P(0, 100)]

  it('removes them all as one undo step', () => {
    const { host, controller } = setup([withMidpoints()])

    expect(controller.deleteCorners(0, [1, 2])).toBe(true)

    expect(host.rings[0].path.getLength()).toBe(4)
    expect(session().rings[0]).toEqual(square())
    expect(session().undo).toHaveLength(1)
    expect(session().undo[0].op).toBe('replace')
  })

  it('one undo puts all of them back', () => {
    const { controller } = setup([withMidpoints()])
    controller.deleteCorners(0, [1, 2])

    store().undo()
    controller.setRings(session().rings, session().activeRing)

    expect(session().rings[0]).toHaveLength(6)
    expect(session().rings[0][1]).toEqual(P(30, 0))
  })

  it('deleting a single corner this way is the ordinary delete', () => {
    const { controller } = setup([withMidpoints()])

    expect(controller.deleteCorners(0, [1])).toBe(true)

    expect(session().undo[0].op).toBe('delete')
  })

  it('refuses when fewer than 3 corners would remain, changing nothing', () => {
    const { host, controller } = setup()

    expect(controller.deleteCorners(0, [0, 1])).toBe(false)

    expect(host.rings[0].path.getLength()).toBe(4)
    expect(session().refusal).toBe('An outline needs at least 3 corners.')
    expect(session().undo).toHaveLength(0)
  })

  it('refuses when what is left would cross itself, changing nothing', () => {
    // A "U" with a notch cut from the top. Removing the bottom-right corner (1) and the notch's
    // bottom-right corner (4) leaves a diagonal edge that cuts through the notch's left wall.
    const notched: GeoPoint[] = [P(0, 0), P(100, 0), P(100, 100), P(70, 100), P(70, 20), P(30, 20), P(30, 100), P(0, 100)]
    const { host, controller } = setup([notched])

    expect(controller.deleteCorners(0, [1, 4])).toBe(false)

    expect(host.rings[0].path.getLength()).toBe(8)
    expect(session().refusal).toBe('That would make the outline cross itself.')
    expect(session().undo).toHaveLength(0)
  })

  it('accepts removing the notch corners when what is left is still a clean outline', () => {
    const notched: GeoPoint[] = [P(0, 0), P(100, 0), P(100, 100), P(70, 100), P(70, 20), P(30, 20), P(30, 100), P(0, 100)]
    const { controller } = setup([notched])

    expect(controller.deleteCorners(0, [4, 5])).toBe(true)
    expect(session().rings[0]).toHaveLength(6)
  })

  it('ignores indices that do not exist, and does nothing when none do', () => {
    const { controller } = setup([withMidpoints()])

    expect(controller.deleteCorners(0, [40, 41])).toBe(false)
    expect(controller.deleteCorners(9, [0, 1])).toBe(false)
  })
})

describe('replaceRing (round a corner, curve an edge, circle)', () => {
  it('swaps the corners in one undo step after checking them', () => {
    const { host, controller } = setup()
    const rounded = [P(0, 0), P(100, 0), P(100, 80), P(80, 100), P(0, 100)]

    expect(controller.replaceRing(0, rounded)).toBe(true)

    expect(host.rings[0].path.getLength()).toBe(5)
    expect(session().rings[0]).toEqual(rounded)
    expect(session().undo).toHaveLength(1)
  })

  it('refuses a ring that crosses itself, changing nothing', () => {
    const { host, controller } = setup()

    expect(controller.replaceRing(0, [P(0, 0), P(100, 100), P(100, 0), P(0, 100)])).toBe(false)

    expect(host.rings[0].path.getLength()).toBe(4)
    expect(session().refusal).toBe('That would make the outline cross itself.')
    expect(session().undo).toHaveLength(0)
  })

  it('refuses fewer than 3 corners', () => {
    const { controller } = setup()

    expect(controller.replaceRing(0, [P(0, 0), P(100, 0)])).toBe(false)
    expect(session().refusal).toBe('An outline needs at least 3 corners.')
  })

  it('does not report its own writes as user edits', () => {
    const { controller } = setup()

    controller.replaceRing(0, [P(0, 0), P(100, 0), P(100, 80), P(80, 100), P(0, 100)])

    expect(session().undo).toHaveLength(1)
  })

  it('ignores a ring that does not exist', () => {
    const { controller } = setup()
    expect(controller.replaceRing(7, [P(0, 0), P(1, 0), P(1, 1)])).toBe(false)
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

describe('a 500-corner ring (SC-005)', () => {
  it('applies a keyboard move in under 4 ms', () => {
    const big = Array.from({ length: 500 }, (_, i) => {
      const angle = (i / 500) * Math.PI * 2
      return P(100 * Math.cos(angle), 100 * Math.sin(angle))
    })
    const { controller } = setup([big])
    controller.moveCorner(0, 1, 0.1, 0)

    const started = performance.now()
    for (let i = 0; i < 20; i++) controller.moveCorner(0, (i * 25) % 500, 0.05, 0)
    const perChange = (performance.now() - started) / 20

    expect(perChange).toBeLessThan(4)
  })
})

describe('a selected group', () => {
  const withMidpoints = (): GeoPoint[] => [P(0, 0), P(30, 0), P(60, 0), P(100, 0), P(100, 100), P(0, 100)]

  it('dragging one selected corner carries the others the same distance, as one undo step', () => {
    const { host } = setup([withMidpoints()])
    store().selectCorners([1, 2])

    // Two pointer steps of the same drag: 5 m south, then 10 m south.
    host.rings[0].path.setAt(1, P(30, -5))
    host.rings[0].path.setAt(1, P(30, -10))

    expect(host.rings[0].path.getAt(2).latitude).toBeCloseTo(P(60, -10).latitude, 9)
    expect(session().rings[0][2].latitude).toBeCloseTo(P(60, -10).latitude, 9)
    expect(session().undo).toHaveLength(1)
    expect(session().undo[0].op).toBe('moveMany')

    store().undo()
    expect(session().rings[0][1]).toEqual(P(30, 0))
    expect(session().rings[0][2]).toEqual(P(60, 0))
  })

  it('refuses a group drag that would make the outline cross itself, putting the dragged corner back', () => {
    const { host } = setup([withMidpoints()])
    store().selectCorners([1, 2])

    host.rings[0].path.setAt(1, P(30, 150))

    expect(host.rings[0].path.getAt(1)).toEqual(P(30, 0))
    expect(session().undo).toHaveLength(0)
    expect(session().refusal).not.toBeNull()
  })

  it('a corner outside the selection still moves alone', () => {
    const { host } = setup([withMidpoints()])
    store().selectCorners([1, 2])

    host.rings[0].path.setAt(4, P(110, 110))

    expect(host.rings[0].path.getAt(2)).toEqual(P(60, 0))
    expect(session().undo[0].op).toBe('move')
  })

  it('the arrow keys move every selected corner together', () => {
    const { host, controller } = setup([withMidpoints()])

    expect(controller.moveCorners(0, [1, 2], 0, -5)).toBe(true)

    expect(host.rings[0].path.getAt(1).latitude).toBeLessThan(P(30, 0).latitude)
    expect(host.rings[0].path.getAt(2).latitude).toBeLessThan(P(60, 0).latitude)
    expect(session().undo).toHaveLength(1)
  })

  it('a click away from the corners ends the selection', () => {
    const { host } = setup([withMidpoints()])
    store().selectCorners([1, 2])

    host.emptyClickListeners.forEach((l) => l())

    expect(session().selectedCorners).toEqual([])
  })

  it('a click on any corner while several are selected ends the selection', () => {
    const { host } = setup([withMidpoints()])
    store().selectCorners([1, 2])

    host.rings[0].clickListeners.forEach((l) => l(4, false))

    expect(session().selectedCorners).toEqual([])
  })

  it('the click that ends a drag does not end the selection', () => {
    const { host } = setup([withMidpoints()])
    store().selectCorners([1, 2])

    host.rings[0].path.setAt(1, P(30, -5))
    host.rings[0].clickListeners.forEach((l) => l(1, false))
    host.emptyClickListeners.forEach((l) => l())

    expect(session().selectedCorners).toEqual([1, 2])
  })

  it('while one selected corner is dragged, the rest of the group follows live; the drop records one move from the start', () => {
    const { host } = setup([withMidpoints()])
    store().selectCorners([1, 2])
    const ring = host.rings[0]

    // Google moves its own handle and reports only the pointer; nothing is recorded yet.
    ring.dragMoveListeners.forEach((l) => l(1, P(30, -8)))
    expect(ring.path.getAt(2).latitude).toBeCloseTo(P(60, -8).latitude, 9)
    expect(session().undo).toHaveLength(0)

    // The drop.
    ring.path.setAt(1, P(30, -10))

    expect(ring.path.getAt(2).latitude).toBeCloseTo(P(60, -10).latitude, 9)
    expect(session().undo).toHaveLength(1)
    store().undo()
    expect(session().rings[0][2]).toEqual(P(60, 0))
  })

  it('a refused group drop puts back the corners carried along during the drag too', () => {
    const { host } = setup([withMidpoints()])
    store().selectCorners([1, 2])
    const ring = host.rings[0]

    ring.dragMoveListeners.forEach((l) => l(1, P(30, 150)))
    ring.path.setAt(1, P(30, 150))

    expect(ring.path.getAt(1)).toEqual(P(30, 0))
    expect(ring.path.getAt(2)).toEqual(P(60, 0))
  })

  it('pressing a corner outside the selection makes it the selection, before any drag', () => {
    const { host } = setup([withMidpoints()])
    store().selectCorners([1, 2])

    host.rings[0].pressListeners.forEach((l) => l(4))

    expect(session().selectedCorners).toEqual([4])
    expect(host.rings[0].highlights).toEqual([4])
  })

  it('pressing a selected corner keeps the whole selection, so it can be dragged together', () => {
    const { host } = setup([withMidpoints()])
    store().selectCorners([1, 2])

    host.rings[0].pressListeners.forEach((l) => l(2))

    expect(session().selectedCorners).toEqual([1, 2])
  })
})

describe('voids (specs/081)', () => {
  // Wound the opposite way from the square, as the session holds every void.
  const atrium = (): GeoPoint[] => [P(40, 40), P(40, 60), P(60, 60), P(60, 40)]

  it('draws a ring with its voids, and hands the map their paths', () => {
    const { host } = setup([square()], {}, [[atrium()]])

    expect(host.rings[0].voidPaths).toHaveLength(1)
    expect(host.rings[0].voidPaths[0].getLength()).toBe(4)
  })

  it('records a valid void corner move on the void path, leaving the outer edge alone', () => {
    const { host } = setup([square()], {}, [[atrium()]])

    host.rings[0].voidPaths[0].setAt(2, P(70, 70))

    expect(session().voids[0][0][2]).toEqual(P(70, 70))
    expect(session().rings[0]).toEqual(square())
    expect(session().undo[0]).toMatchObject({ op: 'move', ring: 0, path: 1, index: 2 })
  })

  it('puts a void corner back, and says why, when it would cross the outer edge', () => {
    const { host } = setup([square()], {}, [[atrium()]])

    host.rings[0].voidPaths[0].setAt(2, P(150, 70))

    expect(host.rings[0].voidPaths[0].getAt(2)).toEqual(P(60, 60))
    expect(session().voids[0][0][2]).toEqual(P(60, 60))
    expect(session().undo).toHaveLength(0)
    expect(session().refusal).toMatch(/inside its outline/i)
  })

  it('puts a void corner back when it would cross the void over itself', () => {
    const { host } = setup([square()], {}, [[atrium()]])

    host.rings[0].voidPaths[0].setAt(2, P(40, 60))

    expect(session().voids[0][0]).toEqual(atrium())
    expect(session().refusal).not.toBeNull()
  })

  it('refuses to move a void into another void', () => {
    const other = [P(10, 10), P(10, 20), P(20, 20), P(20, 10)]
    const { host } = setup([square()], {}, [[atrium(), other]])

    host.rings[0].voidPaths[1].setAt(2, P(45, 45))

    expect(session().voids[0][1]).toEqual(other)
    expect(session().refusal).toMatch(/touch/i)
  })

  it('records an inserted and a deleted void corner', () => {
    const { host } = setup([square()], {}, [[atrium()]])

    host.rings[0].voidPaths[0].insertAt(1, P(38, 50))
    expect(session().voids[0][0]).toHaveLength(5)
    expect(session().undo.at(-1)).toMatchObject({ op: 'insert', ring: 0, path: 1, index: 1 })

    host.rings[0].voidPaths[0].removeAt(1)
    expect(session().voids[0][0]).toHaveLength(4)
    expect(session().undo.at(-1)).toMatchObject({ op: 'delete', ring: 0, path: 1, index: 1 })
  })

  it('refuses to delete a void corner below 3, pointing at Remove void', () => {
    const triangleVoid = [P(40, 40), P(50, 60), P(60, 40)]
    const { host } = setup([square()], {}, [[triangleVoid]])

    host.rings[0].voidPaths[0].removeAt(0)

    expect(host.rings[0].voidPaths[0].getLength()).toBe(3)
    expect(session().refusal).toMatch(/Remove void/)
  })

  it('refuses an outer-edge corner move that would cut through a void', () => {
    const { host } = setup([square()], {}, [[atrium()]])

    // Dragging the south-east corner far in puts the edge across the void.
    host.rings[0].path.setAt(1, P(50, 50))

    expect(session().rings[0]).toEqual(square())
    expect(session().refusal).not.toBeNull()
  })

  it('removes a void as one undo step and redraws the ring without it', () => {
    const { host, controller } = setup([square()], {}, [[atrium()]])

    expect(controller.removeVoid(0, 0)).toBe(true)

    expect(session().voids[0]).toEqual([])
    expect(session().undo).toHaveLength(1)
    expect(host.rings.at(-1)?.voidPaths).toHaveLength(0)

    store().undo()
    expect(session().voids[0]).toHaveLength(1)
  })

  it('refuses to remove a void that is not there', () => {
    const { controller } = setup()

    expect(controller.removeVoid(0, 0)).toBe(false)
  })

  it('replaceAllRings draws the voids it is given, as one undo step', () => {
    const { host, controller } = setup()

    expect(controller.replaceAllRings([square()], [[atrium()]])).toBe(true)

    expect(session().voids[0]).toHaveLength(1)
    expect(host.rings.at(-1)?.voidPaths).toHaveLength(1)
    expect(session().undo).toHaveLength(1)

    store().undo()
    expect(session().voids[0]).toEqual([])
  })

  it('replaceAllRings refuses a void that is not inside its ring', () => {
    const { controller } = setup()

    expect(controller.replaceAllRings([square()], [[[P(150, 40), P(170, 40), P(170, 60)]]])).toBe(false)
    expect(session().voids[0]).toEqual([])
  })

  it('rewrites void paths in place when the shape is unchanged (undo of a corner move)', () => {
    const { host, controller } = setup([square()], {}, [[atrium()]])
    host.rings[0].voidPaths[0].setAt(2, P(70, 70))
    store().undo()

    controller.setRings(session().rings, session().activeRing, session().voids)

    expect(host.rings).toHaveLength(1)
    expect(host.rings[0].voidPaths[0].getAt(2)).toEqual(P(60, 60))
  })
})

describe('every feature on a void corner (specs/081)', () => {
  // Wound the opposite way from the square, as the session holds every void.
  const atrium = (): GeoPoint[] => [P(40, 40), P(40, 60), P(60, 60), P(60, 40)]
  const second = (): GeoPoint[] => [P(10, 10), P(10, 20), P(20, 20), P(20, 10)]
  const clickVoid = (ring: FakeRing, index: number, additive = false, path = 1) => ring.clickListeners.forEach((l) => l(index, additive, path))

  it('a click on a void corner makes that void the one being edited, selects the corner and marks it', () => {
    const { host } = setup([square()], {}, [[atrium()]])

    clickVoid(host.rings[0], 2)

    expect(session().activePath).toBe(1)
    expect(session().selectedCorners).toEqual([2])
    expect(host.rings[0].highlightsByPath[1]).toEqual([2])
    expect(host.rings[0].highlightsByPath[0]).toEqual([])
  })

  it('a Shift-click adds a second corner of the same void, and marks both', () => {
    const { host } = setup([square()], {}, [[atrium()]])

    clickVoid(host.rings[0], 0)
    clickVoid(host.rings[0], 2, true)

    expect(session().selectedCorners).toEqual([0, 2])
    expect(host.rings[0].highlightsByPath[1]).toEqual([0, 2])
  })

  it('a Shift-click on the outer edge while a void is selected starts a new selection on the outer edge', () => {
    const { host } = setup([square()], {}, [[atrium()]])
    clickVoid(host.rings[0], 0)

    clickVoid(host.rings[0], 3, true, 0)

    expect(session().activePath).toBe(0)
    expect(session().selectedCorners).toEqual([3])
    expect(host.rings[0].highlightsByPath[1]).toEqual([])
    expect(host.rings[0].highlightsByPath[0]).toEqual([3])
  })

  it('pressing a void corner outside the selection makes it the selection before any drag', () => {
    const { host } = setup([square()], {}, [[atrium()]])
    clickVoid(host.rings[0], 0)

    host.rings[0].pressListeners.forEach((l) => l(2, 1))

    expect(session().selectedCorners).toEqual([2])
    expect(host.rings[0].highlightsByPath[1]).toEqual([2])
  })

  it('dragging one selected void corner carries the others with it, and the drop is one undo step', () => {
    const { host } = setup([square()], {}, [[atrium()]])
    clickVoid(host.rings[0], 0)
    clickVoid(host.rings[0], 1, true)
    const mvc = host.rings[0].voidPaths[0]

    // Live: the other follows the pointer without being recorded.
    host.rings[0].dragMoveListeners.forEach((l) => l(0, P(41, 41), 1))
    expect(mvc.getAt(1).latitude).not.toBe(atrium()[1].latitude)
    expect(session().undo).toHaveLength(0)

    // Dropped: both moved by the same amount, recorded once.
    mvc.setAt(0, P(42, 42))
    expect(session().voids[0][0][0]).toEqual(P(42, 42))
    expect(session().undo).toHaveLength(1)
    expect(session().undo[0]).toMatchObject({ op: 'moveMany', ring: 0, path: 1, indices: [0, 1] })

    store().undo()
    expect(session().voids[0][0]).toEqual(atrium())
  })

  it('refuses a group drag that would push the void across the outer edge, putting every corner back', () => {
    const { host } = setup([square()], {}, [[atrium()]])
    clickVoid(host.rings[0], 0)
    clickVoid(host.rings[0], 1, true)

    host.rings[0].voidPaths[0].setAt(0, P(-30, 40))

    expect(session().voids[0][0]).toEqual(atrium())
    expect(host.rings[0].voidPaths[0].getAt(1)).toEqual(atrium()[1])
    expect(session().refusal).not.toBeNull()
  })

  it('the arrow keys move the selected void corners, and refuse a move across the edge', () => {
    const { controller } = setup([square()], {}, [[atrium()]])

    expect(controller.moveCorners(0, [0, 1], 5, 0, 1)).toBe(true)
    expect(session().voids[0][0][0].longitude).toBeGreaterThan(atrium()[0].longitude)
    expect(session().rings[0]).toEqual(square())

    expect(controller.moveCorners(0, [0, 1], -80, 0, 1)).toBe(false)
    expect(session().refusal).toMatch(/inside its outline/i)
  })

  it('Add corner puts a corner midway along the next edge of the void and selects it', () => {
    const { host, controller } = setup([square()], {}, [[atrium()]])

    expect(controller.insertCornerAfter(0, 0, 1)).toBe(true)

    expect(host.rings[0].voidPaths[0].getLength()).toBe(5)
    expect(session().voids[0][0]).toHaveLength(5)
    expect(session().selectedCorner).toBe(1)
    expect(session().undo.at(-1)).toMatchObject({ op: 'insert', ring: 0, path: 1, index: 1 })
  })

  it('Delete corner removes one or several void corners, but never below 3', () => {
    const { host, controller } = setup([square()], {}, [[[P(40, 40), P(40, 60), P(50, 65), P(60, 60), P(60, 40)]]])
    const before = session().voids[0][0].length

    expect(controller.deleteCorner(0, 0, 1)).toBe(true)
    expect(host.rings[0].voidPaths[0].getLength()).toBe(before - 1)

    expect(controller.deleteCorners(0, [0, 1], 1)).toBe(false)
    expect(session().refusal).toMatch(/Remove void/)
  })

  it('replaces a void by a rounded one in one undo step (round, curve, arc act on the void like the outer edge)', () => {
    const { host, controller } = setup([square()], {}, [[atrium()]])
    const rounder = [P(40, 40), P(40, 55), P(50, 60), P(60, 55), P(60, 40)]

    expect(controller.replaceRing(0, rounder, 1)).toBe(true)

    expect(session().voids[0][0]).toEqual(rounder)
    expect(host.rings[0].voidPaths[0].getLength()).toBe(5)
    expect(session().undo.at(-1)).toMatchObject({ op: 'replace', ring: 0, path: 1 })

    store().undo()
    expect(session().voids[0][0]).toEqual(atrium())
  })

  it('refuses a replacement void that leaves the ring', () => {
    const { controller } = setup([square()], {}, [[atrium()]])

    expect(controller.replaceRing(0, [P(90, 90), P(90, 140), P(140, 140)], 1)).toBe(false)
    expect(session().voids[0][0]).toEqual(atrium())
  })

  it('the menu on a void corner selects it and asks for the void menu; on the outer edge for the corner menu', () => {
    const onVertexMenu = vi.fn()
    const onVoidMenu = vi.fn()
    const { host } = setup([square()], { onVertexMenu, onVoidMenu }, [[atrium(), second()]])

    host.rings[0].menuListeners.forEach((l) => l(2, 410, 220, 2))
    expect(onVoidMenu).toHaveBeenCalledWith({ ring: 0, voidIndex: 1, clientX: 410, clientY: 220 })
    expect(session().activePath).toBe(2)
    expect(session().selectedCorners).toEqual([2])

    host.rings[0].menuListeners.forEach((l) => l(1, 5, 6, 0))
    expect(onVertexMenu).toHaveBeenCalledWith({ ring: 0, index: 1, clientX: 5, clientY: 6 })
    expect(session().activePath).toBe(0)
  })

  it('marks the selected corners of only one path at a time, whichever way the selection moves', () => {
    const { host } = setup([square()], {}, [[atrium(), second()]])

    clickVoid(host.rings[0], 1, false, 2)
    expect(host.rings[0].highlightsByPath[2]).toEqual([1])

    store().setActivePath(1)
    store().selectCorners([0, 3])
    expect(host.rings[0].highlightsByPath[1]).toEqual([0, 3])
    expect(host.rings[0].highlightsByPath[2]).toEqual([])
    expect(host.rings[0].highlightsByPath[0]).toEqual([])
  })

  it('a click away from the corners ends a selection on a void too', () => {
    const { host } = setup([square()], {}, [[atrium()]])
    clickVoid(host.rings[0], 2)

    host.rings[0].selectListeners.forEach((l) => l())

    expect(session().selectedCorners).toEqual([])
    expect(host.rings[0].highlightsByPath[1]).toEqual([])
  })

  it('removing a void while its corner is selected clears the selection and goes back to the outer edge', () => {
    const { controller, host } = setup([square()], {}, [[atrium()]])
    clickVoid(host.rings[0], 2)

    controller.removeVoid(0, 0)

    expect(session().activePath).toBe(0)
    expect(session().selectedCorners).toEqual([])
  })
})
