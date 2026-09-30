import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import type { PixelProjector } from '../../../viewer/siteBoundaryEdit/googlePixelProjector'
import { registerSiteBoundaryEditRuntime, type SiteBoundaryEditRuntime } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore, type ViewState } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { SiteBoundaryArcDraw } from './SiteBoundaryArcDraw'

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

let applyArc: ReturnType<typeof vi.fn>
let unregister: () => void

function startArc() {
  store().enter({ chatId: 'chat-1', siteName: 'Muscat Grand Mall', revision: 'rev-1', rings: [SQUARE], viewState })
  store().beginArc([0, 1])
}

const layer = () => screen.getByTestId('arc-draw-layer')

function pin() {
  vi.spyOn(layer(), 'getBoundingClientRect').mockReturnValue({ left: 0, top: 0, right: 400, bottom: 300, width: 400, height: 300, x: 0, y: 0, toJSON: () => ({}) })
}

beforeEach(() => {
  applyArc = vi.fn().mockReturnValue(true)
  unregister = registerSiteBoundaryEditRuntime({ applyArc } as unknown as SiteBoundaryEditRuntime)
})

afterEach(() => {
  cleanup()
  unregister()
  store().end()
  vi.restoreAllMocks()
})

describe('SiteBoundaryArcDraw', () => {
  it('renders nothing unless the Draw arc tool is active', () => {
    store().enter({ chatId: 'chat-1', siteName: 'S', revision: 'r', rings: [SQUARE], viewState })
    const { container } = render(<SiteBoundaryArcDraw projector={projector} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('takes the pointer back from the viewer, so the click reaches this layer and not the map', () => {
    startArc()
    render(<SiteBoundaryArcDraw projector={projector} />)
    expect(layer()).toHaveStyle({ pointerEvents: 'auto' })
  })

  it('tells the user what to do', () => {
    startArc()
    render(<SiteBoundaryArcDraw projector={projector} />)
    expect(screen.getByRole('status')).toHaveTextContent('Click where the arc should pass')
  })

  it('shows the arc that would be drawn while the pointer moves', () => {
    startArc()
    render(<SiteBoundaryArcDraw projector={projector} />)
    pin()

    fireEvent.pointerMove(layer(), { clientX: 50, clientY: 20 })

    const preview = screen.getByTestId('arc-preview')
    const points = preview.getAttribute('points')!.split(' ').map((pair) => pair.split(',').map(Number))
    expect(points.length).toBeGreaterThan(3)
    // It runs from corner 0 (pixel 0,0) to corner 1 (pixel 100,0).
    expect(points[0][0]).toBeCloseTo(0, 0)
    expect(points[points.length - 1][0]).toBeCloseTo(100, 0)
  })

  it('says why no arc can be drawn when the pointer is in line with the two corners', () => {
    startArc()
    render(<SiteBoundaryArcDraw projector={projector} />)
    pin()

    fireEvent.pointerMove(layer(), { clientX: 50, clientY: 0 })

    expect(screen.queryByTestId('arc-preview')).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('straight line')
  })

  it('drops the third point where the pointer is released', () => {
    startArc()
    render(<SiteBoundaryArcDraw projector={projector} />)
    pin()

    fireEvent.pointerUp(layer(), { clientX: 50, clientY: 20, button: 0 })

    expect(applyArc).toHaveBeenCalledTimes(1)
    const through = applyArc.mock.calls[0][0] as GeoPoint
    expect(through.longitude).toBeCloseTo(P(50, -20).longitude, 7)
    expect(through.latitude).toBeCloseTo(P(50, -20).latitude, 7)
  })

  it('ignores a release of a button other than the primary one', () => {
    startArc()
    render(<SiteBoundaryArcDraw projector={projector} />)
    pin()

    fireEvent.pointerUp(layer(), { clientX: 50, clientY: 20, button: 2 })

    expect(applyArc).not.toHaveBeenCalled()
  })

  it('says the map is not ready, rather than doing nothing, when it cannot place the point', () => {
    startArc()
    render(<SiteBoundaryArcDraw projector={null} />)
    pin()

    fireEvent.pointerUp(layer(), { clientX: 50, clientY: 20, button: 0 })

    expect(applyArc).not.toHaveBeenCalled()
    expect(store().session?.refusal).toContain("map isn't ready")
  })

  it('clears the preview when the pointer leaves', () => {
    startArc()
    render(<SiteBoundaryArcDraw projector={projector} />)
    pin()
    fireEvent.pointerMove(layer(), { clientX: 50, clientY: 20 })
    expect(screen.getByTestId('arc-preview')).toBeInTheDocument()

    fireEvent.pointerLeave(layer())

    expect(screen.queryByTestId('arc-preview')).not.toBeInTheDocument()
  })
})
