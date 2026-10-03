import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import type { PixelProjector } from '../../../viewer/siteBoundaryEdit/googlePixelProjector'
import { registerSiteBoundaryEditRuntime, type SiteBoundaryEditRuntime } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore, type ViewState } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { SiteBoundaryBoxSelect } from './SiteBoundaryBoxSelect'

const LAT = 23.59
const LON = 58.4
const P = (east: number, north: number): GeoPoint => ({
  latitude: LAT + north / 111_320,
  longitude: LON + east / (111_320 * Math.cos((LAT * Math.PI) / 180)),
})

const viewState: ViewState = { mode: 'isometric', rotationEnabled: true, center: { latitude: LAT, longitude: LON }, zoom: 17, heading: 0, tilt: 45 }
const store = () => useSiteBoundaryEditStore.getState()

/** Corners 20 px apart along a row at y = 50, starting at x = 20 - a stand-in for the map's projection. */
const RING = [P(0, 0), P(20, 0), P(40, 0), P(60, 0), P(80, 0)]

const projector: PixelProjector = {
  toPixel: (point) => {
    const east = (point.longitude - LON) * 111_320 * Math.cos((LAT * Math.PI) / 180)
    return { x: 20 + east, y: 50 }
  },
  toLatLng: (pixel) => ({ latitude: LAT, longitude: LON + (pixel.x - 20) / (111_320 * Math.cos((LAT * Math.PI) / 180)) }),
  origin: () => ({ left: 0, top: 0 }),
  dispose: () => {},
}

function enter(tool: 'edit' | 'select' = 'select') {
  store().enter({ chatId: 'chat-1', siteName: 'Muscat Grand Mall', revision: 'rev-1', rings: [RING], viewState })
  store().setTool(tool)
}

const layer = () => screen.getByTestId('corner-select-layer')

/** jsdom lays nothing out, so the layer's rectangle is pinned to the page origin. */
function pinLayer() {
  vi.spyOn(layer(), 'getBoundingClientRect').mockReturnValue({ left: 0, top: 0, right: 400, bottom: 300, width: 400, height: 300, x: 0, y: 0, toJSON: () => ({}) })
}

function drag(from: [number, number], to: [number, number], init: { shiftKey?: boolean } = {}) {
  fireEvent.pointerDown(layer(), { clientX: from[0], clientY: from[1], button: 0, pointerId: 1, ...init })
  fireEvent.pointerMove(layer(), { clientX: to[0], clientY: to[1], pointerId: 1, ...init })
  fireEvent.pointerUp(layer(), { clientX: to[0], clientY: to[1], pointerId: 1, ...init })
}

afterEach(() => {
  cleanup()
  store().end()
  vi.restoreAllMocks()
})

describe('SiteBoundaryBoxSelect', () => {
  it('renders nothing unless the Select tool is chosen', () => {
    enter('edit')
    const { container } = render(<SiteBoundaryBoxSelect projector={projector} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing when there is no edit session', () => {
    const { container } = render(<SiteBoundaryBoxSelect projector={projector} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('takes the pointer back from the viewer, so a drag draws a box instead of panning the map', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)

    // The viewer's overlay container ignores the pointer; without this the drag reaches the map and pans it.
    expect(layer()).toHaveStyle({ pointerEvents: 'auto' })
  })

  it('a right-click goes back to editing and keeps the selection; the browser menu never opens', () => {
    enter()
    act(() => store().selectCorners([1, 2]))
    render(<SiteBoundaryBoxSelect projector={projector} />)

    const notCancelled = fireEvent.contextMenu(layer())

    expect(notCancelled).toBe(false)
    expect(store().session?.tool).toBe('edit')
    expect(store().session?.selectedCorners).toEqual([1, 2])
  })

  it('stays in the Select tool after a box, for another one', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()

    drag([10, 30], [50, 70])

    expect(store().session?.tool).toBe('select')
  })

  it('pressing on a selected corner drags the whole selection instead of drawing a box, and keeps it selected', () => {
    const nudgeCorner = vi.fn()
    const unregister = registerSiteBoundaryEditRuntime({ nudgeCorner } as unknown as SiteBoundaryEditRuntime)
    enter()
    act(() => store().selectCorners([1, 2]))
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()

    // Corner 1 sits at pixel (40, 50); drag it 10 px east.
    fireEvent.pointerDown(layer(), { clientX: 41, clientY: 50, button: 0, pointerId: 1 })
    fireEvent.pointerMove(layer(), { clientX: 51, clientY: 50, pointerId: 1 })
    fireEvent.pointerUp(layer(), { clientX: 51, clientY: 50, button: 0, pointerId: 1 })

    expect(screen.queryByTestId('corner-select-box')).not.toBeInTheDocument()
    expect(nudgeCorner).toHaveBeenCalledTimes(1)
    expect(nudgeCorner.mock.calls[0][0]).toBeCloseTo(10, 1)
    expect(store().session?.selectedCorners).toEqual([1, 2])
    unregister()
  })

  it('shows a grab cursor over a corner, and a crosshair elsewhere', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()

    fireEvent.pointerMove(layer(), { clientX: 60, clientY: 50 })
    expect(layer()).toHaveStyle({ cursor: 'grab' })

    fireEvent.pointerMove(layer(), { clientX: 200, clientY: 200 })
    expect(layer()).toHaveStyle({ cursor: 'crosshair' })
  })

  it('a click (no drag) on a selected corner ends the selection', () => {
    const nudgeCorner = vi.fn()
    const unregister = registerSiteBoundaryEditRuntime({ nudgeCorner } as unknown as SiteBoundaryEditRuntime)
    enter()
    act(() => store().selectCorners([1, 2]))
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()

    fireEvent.pointerDown(layer(), { clientX: 40, clientY: 50, button: 0, pointerId: 1 })
    fireEvent.pointerMove(layer(), { clientX: 41, clientY: 50, pointerId: 1 })
    fireEvent.pointerUp(layer(), { clientX: 41, clientY: 50, button: 0, pointerId: 1 })

    expect(nudgeCorner).not.toHaveBeenCalled()
    expect(store().session?.selectedCorners).toEqual([])
    unregister()
  })

  it('pressing a corner outside the selection leaves the multi-selection for ordinary editing', () => {
    enter()
    act(() => store().selectCorners([1, 2]))
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()

    fireEvent.pointerDown(layer(), { clientX: 100, clientY: 50, button: 0, pointerId: 1 })

    expect(store().session?.selectedCorners).toEqual([])
    expect(store().session?.tool).toBe('edit')
  })

  it('a right-button release does not end a left drag', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()
    fireEvent.pointerDown(layer(), { clientX: 10, clientY: 10, button: 0 })
    fireEvent.pointerMove(layer(), { clientX: 120, clientY: 120 })

    fireEvent.pointerUp(layer(), { clientX: 120, clientY: 120, button: 2 })

    expect(screen.getByTestId('corner-select-box')).toBeInTheDocument()
  })

  it('tells the user how it works', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    expect(screen.getByRole('status')).toHaveTextContent('Drag a box around corners to select them')
  })

  it('selects every corner inside the dragged box', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()

    // Corners are at x = 20, 40, 60, 80, 100 (y = 50). This box holds the middle three.
    drag([30, 30], [90, 70])

    expect(store().session?.selectedCorners).toEqual([1, 2, 3])
    expect(store().session?.selectedCorner).toBe(3)
  })

  it('works whichever way the box is dragged', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()

    drag([90, 70], [30, 30])

    expect(store().session?.selectedCorners).toEqual([1, 2, 3])
  })

  it('a second box replaces the first, but Shift adds to it', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()

    drag([10, 30], [50, 70])
    expect(store().session?.selectedCorners).toEqual([0, 1])

    drag([90, 30], [110, 70])
    expect(store().session?.selectedCorners).toEqual([4])

    drag([10, 30], [50, 70], { shiftKey: true })
    expect(store().session?.selectedCorners).toEqual([0, 1, 4])
  })

  it('says so, and keeps the selection when the box holds no corners', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()
    drag([10, 30], [50, 70])

    drag([200, 200], [300, 280], { shiftKey: true })

    expect(store().session?.selectedCorners).toEqual([0, 1])
    expect(store().session?.refusal).toBe('No corners inside that box. Drag a box around the corners you want.')
  })

  it('clears the selection when the box holds none and Shift is not held', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()
    drag([10, 30], [50, 70])

    drag([200, 200], [300, 280])

    expect(store().session?.selectedCorners).toEqual([])
  })

  it('treats a tiny drag as a click that clears the selection', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()
    drag([10, 30], [50, 70])

    drag([200, 200], [201, 201])

    expect(store().session?.selectedCorners).toEqual([])
    expect(store().session?.refusal).toBeNull()
  })

  it('draws the box while dragging and removes it when released', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()

    fireEvent.pointerDown(layer(), { clientX: 30, clientY: 30, button: 0, pointerId: 1 })
    fireEvent.pointerMove(layer(), { clientX: 90, clientY: 70, pointerId: 1 })
    expect(screen.getByTestId('corner-select-box')).toBeInTheDocument()

    fireEvent.pointerUp(layer(), { clientX: 90, clientY: 70, pointerId: 1 })
    expect(screen.queryByTestId('corner-select-box')).not.toBeInTheDocument()
  })

  it('ignores a drag started with a button other than the primary one', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()

    fireEvent.pointerDown(layer(), { clientX: 30, clientY: 30, button: 2, pointerId: 1 })
    fireEvent.pointerUp(layer(), { clientX: 90, clientY: 70, button: 2, pointerId: 1 })

    expect(store().session?.selectedCorners).toEqual([])
  })

  it('selects only corners of the ring being edited', () => {
    store().enter({ chatId: 'chat-1', siteName: 'S', revision: 'r', rings: [RING, [P(0, 0), P(20, 0), P(90, 0)]], viewState })
    store().setTool('select')
    store().setActiveRing(1)
    render(<SiteBoundaryBoxSelect projector={projector} />)
    pinLayer()

    drag([10, 30], [50, 70])

    expect(store().session?.selectedCorners).toEqual([0, 1])
    expect(store().session?.activeRing).toBe(1)
  })

  it('explains itself when the map is not ready to place corners', () => {
    enter()
    render(<SiteBoundaryBoxSelect projector={null} />)
    pinLayer()

    drag([30, 30], [90, 70])

    expect(store().session?.selectedCorners).toEqual([])
    expect(store().session?.refusal).toContain("map isn't ready")
  })

  it('leaves out corners the map cannot place', () => {
    enter()
    const partial: PixelProjector = { ...projector, toPixel: (point) => (point.longitude === RING[2].longitude ? null : projector.toPixel(point)) }
    render(<SiteBoundaryBoxSelect projector={partial} />)
    pinLayer()

    drag([30, 30], [90, 70])

    expect(store().session?.selectedCorners).toEqual([1, 3])
  })
})
