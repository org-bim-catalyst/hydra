import { act, cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import { registerSiteBoundaryEditRuntime, type SiteBoundaryEditRuntime } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore, type ViewState } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { SiteBoundaryShapeDialog } from './SiteBoundaryShapeDialog'

const LAT = 23.59
const LON = 58.4
const P = (east: number, north: number): GeoPoint => ({
  latitude: LAT + north / 111_320,
  longitude: LON + east / (111_320 * Math.cos((LAT * Math.PI) / 180)),
})

const viewState: ViewState = { mode: 'isometric', rotationEnabled: true, center: { latitude: LAT, longitude: LON }, zoom: 17, heading: 0, tilt: 45 }
const store = () => useSiteBoundaryEditStore.getState()

// A 200 x 100 m ring: 20,000 m2, so the same-area circle has a radius of about 80 m.
const enter = () =>
  store().enter({ chatId: 'chat-1', siteName: 'Muscat Grand Mall', revision: 'rev-1', rings: [[P(0, 0), P(200, 0), P(200, 100), P(0, 100)]], viewState })

let applyShape: ReturnType<typeof vi.fn>
let unregister: () => void

beforeEach(() => {
  applyShape = vi.fn().mockReturnValue(true)
  unregister = registerSiteBoundaryEditRuntime({ applyShape } as unknown as SiteBoundaryEditRuntime)
})

afterEach(() => {
  cleanup()
  unregister()
  store().end()
})

const number = () => screen.getByLabelText(/metres/i) as HTMLInputElement

describe('SiteBoundaryShapeDialog', () => {
  it('renders nothing while no shape tool is asking', () => {
    enter()
    const { container } = render(<SiteBoundaryShapeDialog />)
    expect(container).toBeEmptyDOMElement()
  })

  it.each([
    ['round', 'Round this corner', 'Radius (metres)', '10'],
    ['curve', 'Curve this edge', 'Bulge (metres)', '5'],
  ] as const)('opens the %s tool with its title, label and starting value', (tool, title, label, start) => {
    enter()
    act(() => store().setShapeDialog(tool))
    render(<SiteBoundaryShapeDialog />)

    expect(screen.getByText(title)).toBeInTheDocument()
    expect(screen.getByLabelText(label)).toHaveValue(Number(start))
  })

  it('starts the circle at the radius that gives the ring its current area', () => {
    enter()
    act(() => store().setShapeDialog('circle'))
    render(<SiteBoundaryShapeDialog />)

    // sqrt(20,000 / pi) is 79.788..., shown rounded.
    expect(number()).toHaveValue(80)
  })

  it('applies the number and closes when the tool succeeds', async () => {
    const user = userEvent.setup()
    enter()
    act(() => store().setShapeDialog('round'))
    render(<SiteBoundaryShapeDialog />)

    await user.clear(number())
    await user.type(number(), '25')
    await user.click(screen.getByText('Apply'))

    expect(applyShape).toHaveBeenCalledWith('round', 25)
    expect(store().shapeDialog).toBeNull()
  })

  it('stays open, showing the reason, when the tool refuses', async () => {
    const user = userEvent.setup()
    applyShape.mockImplementation(() => {
      store().refuse('That radius is too big for this corner. The most that fits is 18 m.')
      return false
    })
    enter()
    act(() => store().setShapeDialog('round'))
    render(<SiteBoundaryShapeDialog />)

    await user.click(screen.getByText('Apply'))

    expect(store().shapeDialog).toBe('round')
    expect(screen.getByText(/too big for this corner/)).toBeInTheDocument()
  })

  it('does not apply a radius of zero or a blank field', async () => {
    const user = userEvent.setup()
    enter()
    act(() => store().setShapeDialog('round'))
    render(<SiteBoundaryShapeDialog />)

    await user.clear(number())
    expect(screen.getByText('Apply').closest('button')).toBeDisabled()

    await user.type(number(), '0')
    expect(screen.getByText('Apply').closest('button')).toBeDisabled()
    expect(applyShape).not.toHaveBeenCalled()
  })

  it('does not accept a negative radius for round or circle', async () => {
    const user = userEvent.setup()
    enter()
    act(() => store().setShapeDialog('circle'))
    render(<SiteBoundaryShapeDialog />)

    await user.clear(number())
    await user.type(number(), '-5')

    expect(screen.getByText('Apply').closest('button')).toBeDisabled()
  })

  it('accepts a negative bulge, which bends the edge inward', async () => {
    const user = userEvent.setup()
    enter()
    act(() => store().setShapeDialog('curve'))
    render(<SiteBoundaryShapeDialog />)

    await user.clear(number())
    await user.type(number(), '-4')
    await user.click(screen.getByText('Apply'))

    expect(applyShape).toHaveBeenCalledWith('curve', -4)
  })

  it('closes on Cancel without applying', async () => {
    const user = userEvent.setup()
    enter()
    act(() => store().setShapeDialog('round'))
    render(<SiteBoundaryShapeDialog />)

    await user.click(screen.getByText('Cancel'))

    expect(store().shapeDialog).toBeNull()
    expect(applyShape).not.toHaveBeenCalled()
  })

  it('does not treat a failure to reach the editor as success', async () => {
    const user = userEvent.setup()
    unregister()
    enter()
    act(() => store().setShapeDialog('round'))
    render(<SiteBoundaryShapeDialog />)

    await user.click(screen.getByText('Apply'))

    expect(store().shapeDialog).toBe('round')
    expect(store().notice).toContain("isn't ready")
  })
})
