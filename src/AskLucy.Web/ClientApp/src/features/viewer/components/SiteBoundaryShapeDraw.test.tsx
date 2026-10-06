import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import type { PixelProjector } from '../../../viewer/siteBoundaryEdit/googlePixelProjector'
import { registerSiteBoundaryEditRuntime, type SiteBoundaryEditRuntime } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore, type ViewState } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { SiteBoundaryShapeDraw } from './SiteBoundaryShapeDraw'

const LAT = 23.59
const LON = 58.4
const M_LAT = 111_320
const M_LON = 111_320 * Math.cos((LAT * Math.PI) / 180)

/** Pixels are metres: x grows east, y grows south, origin at (0, 0) of the ring. */
const projector: PixelProjector = {
  toPixel: (p) => ({ x: (p.longitude - LON) * M_LON, y: -(p.latitude - LAT) * M_LAT }),
  toLatLng: (px) => ({ latitude: LAT - px.y / M_LAT, longitude: LON + px.x / M_LON }),
  origin: () => ({ left: 0, top: 0 }),
  dispose: () => {},
}

const P = (east: number, north: number): GeoPoint => ({ latitude: LAT + north / M_LAT, longitude: LON + east / M_LON })
const SQUARE = [P(0, 0), P(100, 0), P(100, 100), P(0, 100)]

const viewState: ViewState = { mode: 'isometric', rotationEnabled: true, center: { latitude: LAT, longitude: LON }, zoom: 17, heading: 0, tilt: 45 }
const store = () => useSiteBoundaryEditStore.getState()

let applyCircle: ReturnType<typeof vi.fn>
let applyShapePolygon: ReturnType<typeof vi.fn>
let unregister: () => void

function startCircle(operation: 'add' | 'cut' = 'add', kind: 'circle' | 'rectangle' | 'square' = 'circle') {
  store().enter({ chatId: 'chat-1', siteName: 'Muscat Grand Mall', revision: 'rev-1', rings: [SQUARE], viewState })
  store().beginCircle(operation, kind)
}

const layer = () => screen.getByTestId('circle-draw-layer')

function pin() {
  vi.spyOn(layer(), 'getBoundingClientRect').mockReturnValue({ left: 0, top: 0, right: 400, bottom: 300, width: 400, height: 300, x: 0, y: 0, toJSON: () => ({}) })
}

beforeEach(() => {
  applyCircle = vi.fn().mockResolvedValue(true)
  applyShapePolygon = vi.fn().mockResolvedValue(true)
  unregister = registerSiteBoundaryEditRuntime({ applyCircle, applyShapePolygon } as unknown as SiteBoundaryEditRuntime)
})

afterEach(() => {
  cleanup()
  unregister()
  store().end()
  vi.restoreAllMocks()
})

describe('SiteBoundaryShapeDraw', () => {
  it('renders nothing unless a circle tool is active', () => {
    store().enter({ chatId: 'chat-1', siteName: 'S', revision: 'r', rings: [SQUARE], viewState })
    const { container } = render(<SiteBoundaryShapeDraw projector={projector} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('takes the pointer back from the viewer, so the drag reaches this layer and not the map', () => {
    startCircle()
    render(<SiteBoundaryShapeDraw projector={projector} />)
    expect(layer()).toHaveStyle({ pointerEvents: 'auto' })
  })

  it('tells the user what to do, in the words of the chosen operation', () => {
    startCircle('cut')
    render(<SiteBoundaryShapeDraw projector={projector} />)
    expect(screen.getByRole('status')).toHaveTextContent('cut it out')
  })

  it('shows the circle and its radius while dragging, and applies it on release', () => {
    startCircle()
    render(<SiteBoundaryShapeDraw projector={projector} />)
    pin()

    fireEvent.pointerDown(layer(), { clientX: 200, clientY: 150, button: 0 })
    fireEvent.pointerMove(layer(), { clientX: 250, clientY: 150 })

    expect(screen.getByTestId('circle-preview')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Radius 50 m')

    fireEvent.pointerUp(layer(), { clientX: 250, clientY: 150, button: 0 })

    expect(applyCircle).toHaveBeenCalledTimes(1)
    const [centre, radius] = applyCircle.mock.calls[0] as [GeoPoint, number]
    expect(radius).toBeCloseTo(50, 0)
    expect(centre.latitude).toBeCloseTo(LAT - 150 / M_LAT, 7)
    expect(centre.longitude).toBeCloseTo(LON + 200 / M_LON, 7)
  })

  it('treats a click without a drag as no radius, and says so', () => {
    startCircle()
    render(<SiteBoundaryShapeDraw projector={projector} />)
    pin()

    fireEvent.pointerDown(layer(), { clientX: 200, clientY: 150, button: 0 })
    fireEvent.pointerUp(layer(), { clientX: 200, clientY: 150, button: 0 })

    expect(applyCircle).not.toHaveBeenCalled()
    expect(store().session?.refusal).toContain('drag outward')
  })

  it('swallows the right-click, so the browser menu never opens', () => {
    startCircle()
    render(<SiteBoundaryShapeDraw projector={projector} />)
    expect(fireEvent.contextMenu(layer())).toBe(false)
  })

  it('ignores a press of a button other than the primary one', () => {
    startCircle()
    render(<SiteBoundaryShapeDraw projector={projector} />)
    pin()

    fireEvent.pointerDown(layer(), { clientX: 200, clientY: 150, button: 2 })
    fireEvent.pointerMove(layer(), { clientX: 250, clientY: 150 })
    fireEvent.pointerUp(layer(), { clientX: 250, clientY: 150, button: 2 })

    expect(applyCircle).not.toHaveBeenCalled()
  })

  it('says the map is not ready, rather than doing nothing, when it cannot place the centre', () => {
    startCircle()
    render(<SiteBoundaryShapeDraw projector={null} />)
    pin()

    fireEvent.pointerDown(layer(), { clientX: 200, clientY: 150, button: 0 })

    expect(store().session?.refusal).toContain("map isn't ready")
  })

  it('draws a rectangle between two opposite corners, with its sides, and applies it on release', () => {
    startCircle('cut', 'rectangle')
    render(<SiteBoundaryShapeDraw projector={projector} />)
    pin()

    fireEvent.pointerDown(layer(), { clientX: 100, clientY: 100, button: 0 })
    fireEvent.pointerMove(layer(), { clientX: 160, clientY: 140 })

    expect(screen.getByTestId('circle-preview')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('60 × 40 m')

    fireEvent.pointerUp(layer(), { clientX: 160, clientY: 140, button: 0 })

    expect(applyCircle).not.toHaveBeenCalled()
    expect(applyShapePolygon).toHaveBeenCalledTimes(1)
    expect(applyShapePolygon.mock.calls[0][0]).toHaveLength(4)
  })

  it('draws a square from the larger side of the drag', () => {
    startCircle('add', 'square')
    render(<SiteBoundaryShapeDraw projector={projector} />)
    pin()

    fireEvent.pointerDown(layer(), { clientX: 100, clientY: 100, button: 0 })
    fireEvent.pointerMove(layer(), { clientX: 160, clientY: 140 })

    expect(screen.getByRole('status')).toHaveTextContent('60 × 60 m')

    fireEvent.pointerUp(layer(), { clientX: 160, clientY: 140, button: 0 })
    expect(applyShapePolygon).toHaveBeenCalledTimes(1)
  })

  it('refuses a rectangle click without a drag, and says how to draw one', () => {
    startCircle('add', 'rectangle')
    render(<SiteBoundaryShapeDraw projector={projector} />)
    pin()

    fireEvent.pointerDown(layer(), { clientX: 100, clientY: 100, button: 0 })
    fireEvent.pointerUp(layer(), { clientX: 100, clientY: 100, button: 0 })

    expect(applyShapePolygon).not.toHaveBeenCalled()
    expect(store().session?.refusal).toContain('opposite corner')
  })
})
