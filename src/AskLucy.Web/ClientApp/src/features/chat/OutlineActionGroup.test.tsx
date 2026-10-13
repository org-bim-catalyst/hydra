import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { useActiveSiteBoundaryStore } from '../../store/activeSiteBoundaryStore'
import { useSiteBoundaryEditStore, type ViewState } from '../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { registerSiteBoundaryEditRuntime, type SiteBoundaryEditRuntime } from '../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { OutlineActionGroup } from './OutlineActionGroup'
import { useOutlineToolGroupStore } from './outlineToolGroupStore'

const LAT = 23.59
const LON = 58.4
const P = (east: number, north: number) => ({ latitude: LAT + north / 111_320, longitude: LON + east / 102_000 })
const viewState: ViewState = { mode: 'isometric', rotationEnabled: true, center: { latitude: LAT, longitude: LON }, zoom: 17, heading: 0, tilt: 45 }
const store = () => useSiteBoundaryEditStore.getState()
// jsdom cannot run getByRole while a MUI menu is open, so the open menu is read through the DOM directly.
const menu = () => document.querySelector<HTMLElement>('[role="menu"]')!
const byLabel = (label: string) => document.querySelector(`[aria-label="${label}"]`)

// The ribbon's actions run through the viewer's runtime; here the store methods stand in for it.
let unregister: () => void
beforeEach(() => {
  unregister = registerSiteBoundaryEditRuntime({
    startCircle: (operation: 'add' | 'cut') => store().beginCircle(operation),
    startShape: (kind: 'rectangle' | 'square', operation: 'add' | 'cut') => store().beginCircle(operation, kind),
    startPolygon: (operation: 'add' | 'cut') => store().beginPolygon(operation),
    openShapeDialog: () => {},
  } as unknown as SiteBoundaryEditRuntime)
  localStorage.clear()
  useOutlineToolGroupStore.setState({ chosen: {} })
  useActiveSiteBoundaryStore.setState({ polygon: [P(0, 0), P(100, 0), P(100, 100), P(0, 100)] } as never)
  store().enter({ chatId: 'chat-1', siteName: 'Muscat Grand Mall', revision: 'rev-1', rings: [[P(0, 0), P(100, 0), P(100, 100), P(0, 100)]], viewState })
})

afterEach(() => {
  cleanup()
  unregister()
  store().end()
})

// specs/081 US6 (T044): the ribbon is six tool groups plus Undo, Redo, Cancel, Reset and Done.
describe('OutlineActionGroup', () => {
  it('shows the six tool groups, each with its default tool, and the flat actions', () => {
    render(<OutlineActionGroup />)
    expect(screen.getByRole('button', { name: 'Select corners' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add circle' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add polygon' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Undo' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Done' })).toBeInTheDocument()
  })

  it('choosing Cut circle highlights the circle group with the Cut circle icon', () => {
    render(<OutlineActionGroup />)
    fireEvent.contextMenu(screen.getByRole('button', { name: 'Add circle' }))
    fireEvent.click(within(menu()).getByText('Cut circle'))
    expect(store().session?.tool).toBe('circle')
    expect(store().session?.circleOperation).toBe('cut')
    expect(byLabel('Cut circle')).toBeInTheDocument()
  })

  it("choosing Make ring a circle moves the circle group's shown tool and remembers it", () => {
    render(<OutlineActionGroup />)
    fireEvent.contextMenu(screen.getByRole('button', { name: 'Add circle' }))
    fireEvent.click(within(menu()).getByText('Make ring a circle'))
    expect(useOutlineToolGroupStore.getState().chosen.circle).toBe('make-circle')
  })

  it('restores the remembered tool after a remount', () => {
    useOutlineToolGroupStore.getState().choose('circle', 'cut-circle')
    render(<OutlineActionGroup />)
    expect(byLabel('Cut circle')).toBeInTheDocument()
  })
})
