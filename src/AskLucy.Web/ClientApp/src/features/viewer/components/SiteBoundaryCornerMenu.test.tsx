import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import { useCornerMenuStore } from '../../../viewer/siteBoundaryEdit/cornerMenuStore'
import { registerSiteBoundaryEditRuntime, type SiteBoundaryEditRuntime } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore, type ViewState } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { SiteBoundaryCornerMenu } from './SiteBoundaryCornerMenu'

const P = (lat: number, lon: number): GeoPoint => ({ latitude: lat, longitude: lon })
const SQUARE = [P(0, 0), P(0, 0.001), P(0.001, 0.001), P(0.001, 0)]
const PENTAGON = [...SQUARE, P(0.0015, 0.0005)]
const viewState: ViewState = { mode: 'isometric', rotationEnabled: true, center: P(0, 0), zoom: 17, heading: 0, tilt: 45 }

let deleteCorner: ReturnType<typeof vi.fn>
let removeVoid: ReturnType<typeof vi.fn>
let unregister: () => void

const enter = (ring: GeoPoint[]) => useSiteBoundaryEditStore.getState().enter({ chatId: 'c', siteName: 'S', revision: 'r', rings: [ring], viewState })

beforeEach(() => {
  deleteCorner = vi.fn()
  removeVoid = vi.fn()
  unregister = registerSiteBoundaryEditRuntime({ deleteCorner, removeVoid } as unknown as SiteBoundaryEditRuntime)
})

afterEach(() => {
  cleanup()
  unregister()
  useCornerMenuStore.getState().close()
  useSiteBoundaryEditStore.getState().end()
})

describe('SiteBoundaryCornerMenu', () => {
  it('shows nothing until opened', () => {
    enter(PENTAGON)
    render(<SiteBoundaryCornerMenu />)
    expect(screen.queryByText('Delete corner')).not.toBeInTheDocument()
  })

  it('deletes the corner and closes', () => {
    enter(PENTAGON)
    useCornerMenuStore.getState().open(100, 100)
    render(<SiteBoundaryCornerMenu />)

    fireEvent.click(screen.getByText('Delete corner'))

    expect(deleteCorner).toHaveBeenCalledTimes(1)
    expect(useCornerMenuStore.getState().anchor).toBeNull()
  })

  it('offers Remove void only on a void corner, and removes that void (specs/081)', () => {
    enter(PENTAGON)
    useCornerMenuStore.getState().open(100, 100)
    const { unmount } = render(<SiteBoundaryCornerMenu />)
    expect(screen.queryByText('Remove void')).not.toBeInTheDocument()
    unmount()
    useCornerMenuStore.getState().close()

    useCornerMenuStore.getState().openVoid(100, 100, 0, 2)
    render(<SiteBoundaryCornerMenu />)
    fireEvent.click(screen.getByText('Remove void'))

    expect(removeVoid).toHaveBeenCalledWith(0, 2)
    expect(useCornerMenuStore.getState().anchor).toBeNull()
    expect(useCornerMenuStore.getState().voidTarget).toBeNull()
  })

  it('explains why it is disabled when the ring has only 3 corners', () => {
    enter([SQUARE[0], SQUARE[1], SQUARE[2]])
    useCornerMenuStore.getState().open(100, 100)
    render(<SiteBoundaryCornerMenu />)

    expect(screen.getByText('An outline needs at least 3 corners')).toBeInTheDocument()
    fireEvent.click(screen.getByText('Delete corner'))
    expect(deleteCorner).not.toHaveBeenCalled()
  })
})
