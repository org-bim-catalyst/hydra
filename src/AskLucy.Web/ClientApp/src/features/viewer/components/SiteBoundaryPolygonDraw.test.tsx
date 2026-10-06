import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import type { PixelProjector } from '../../../viewer/siteBoundaryEdit/googlePixelProjector'
import { registerSiteBoundaryEditRuntime, type SiteBoundaryEditRuntime } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore, type ViewState } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { SiteBoundaryPolygonDraw } from './SiteBoundaryPolygonDraw'

const LAT = 23.59
const LON = 58.4
const M_LAT = 111_320
const M_LON = 111_320 * Math.cos((LAT * Math.PI) / 180)

/** Pixels are metres: x grows east, y grows south. */
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

let applyShapePolygon: ReturnType<typeof vi.fn>
let unregister: () => void

const layer = () => screen.getByTestId('polygon-draw-layer')

function start(operation: 'add' | 'cut' = 'cut') {
  store().enter({ chatId: 'chat-1', siteName: 'Muscat Grand Mall', revision: 'rev-1', rings: [SQUARE], viewState })
  store().beginPolygon(operation)
  render(<SiteBoundaryPolygonDraw projector={projector} />)
  vi.spyOn(layer(), 'getBoundingClientRect').mockReturnValue({ left: 0, top: 0, right: 400, bottom: 300, width: 400, height: 300, x: 0, y: 0, toJSON: () => ({}) })
}

const click = (x: number, y: number) => fireEvent.pointerDown(layer(), { clientX: x, clientY: y, button: 0 })

beforeEach(() => {
  applyShapePolygon = vi.fn().mockResolvedValue(true)
  unregister = registerSiteBoundaryEditRuntime({ applyShapePolygon } as unknown as SiteBoundaryEditRuntime)
})

afterEach(() => {
  cleanup()
  unregister()
  store().end()
  vi.restoreAllMocks()
})

describe('SiteBoundaryPolygonDraw', () => {
  it('renders nothing unless the polygon tool is active', () => {
    store().enter({ chatId: 'chat-1', siteName: 'S', revision: 'r', rings: [SQUARE], viewState })
    const { container } = render(<SiteBoundaryPolygonDraw projector={projector} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('counts the corners placed and says how many more it needs', () => {
    start()
    click(50, 50)
    expect(screen.getByRole('status')).toHaveTextContent('1 corner - place at least 3')
    click(150, 50)
    click(150, 150)
    expect(screen.getByRole('status')).toHaveTextContent('3 corners')
    expect(screen.getByRole('status')).toHaveTextContent('Enter to cut it out')
  })

  it('finishes with Enter and applies the corners', () => {
    start()
    click(50, 50)
    click(150, 50)
    click(150, 150)
    fireEvent.keyDown(window, { key: 'Enter' })

    expect(applyShapePolygon).toHaveBeenCalledTimes(1)
    expect(applyShapePolygon.mock.calls[0][0]).toHaveLength(3)
  })

  it('finishes by clicking back on the first corner', () => {
    start()
    click(50, 50)
    click(150, 50)
    click(150, 150)
    click(53, 52)

    expect(applyShapePolygon).toHaveBeenCalledTimes(1)
    expect(applyShapePolygon.mock.calls[0][0]).toHaveLength(3)
  })

  it('takes the last corner back with Backspace', () => {
    start()
    click(50, 50)
    click(150, 50)
    fireEvent.keyDown(window, { key: 'Backspace' })
    expect(screen.getByRole('status')).toHaveTextContent('1 corner')
  })

  it('refuses fewer than three corners with the reason, and sends nothing', () => {
    start()
    click(50, 50)
    click(150, 50)
    fireEvent.keyDown(window, { key: 'Enter' })

    expect(applyShapePolygon).not.toHaveBeenCalled()
    expect(store().session?.refusal).toBeTruthy()
  })

  it('refuses a polygon that crosses itself, and sends nothing', () => {
    start()
    click(50, 50)
    click(150, 150)
    click(150, 50)
    click(50, 150)
    fireEvent.keyDown(window, { key: 'Enter' })

    expect(applyShapePolygon).not.toHaveBeenCalled()
    expect(store().session?.refusal).toBeTruthy()
  })

  it('places a corner at the middle of the map on Space, for the keyboard', () => {
    start()
    fireEvent.keyDown(window, { key: ' ', code: 'Space' })
    expect(screen.getByRole('status')).toHaveTextContent('1 corner')
  })

  it('swallows the right-click and takes the last corner back', () => {
    start()
    click(50, 50)
    expect(fireEvent.contextMenu(layer())).toBe(false)
    expect(screen.getByRole('status')).toHaveTextContent('Click to place the first corner')
  })
})
