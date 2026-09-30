import { act, cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import { registerSiteBoundaryEditRuntime, type SiteBoundaryEditRuntime } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore, type ViewState } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { SiteBoundaryEditToolbar } from './SiteBoundaryEditToolbar'

const LAT = 23.59
const LON = 58.4
const P = (east: number, north: number): GeoPoint => ({
  latitude: LAT + north / 111_320,
  longitude: LON + east / (111_320 * Math.cos((LAT * Math.PI) / 180)),
})

const viewState: ViewState = { mode: 'isometric', rotationEnabled: true, center: { latitude: LAT, longitude: LON }, zoom: 17, heading: 0, tilt: 45 }
const store = () => useSiteBoundaryEditStore.getState()

const enter = () =>
  store().enter({ chatId: 'chat-1', siteName: 'Muscat Grand Mall', revision: 'rev-1', rings: [[P(0, 0), P(100, 0), P(100, 100), P(0, 100)]], viewState })

const edit = () => store().applyChange({ op: 'move', ring: 0, index: 2, before: P(100, 100), after: P(120, 120) })

function fakeRuntime() {
  return {
    start: vi.fn().mockResolvedValue(undefined),
    done: vi.fn().mockResolvedValue(undefined),
    cancel: vi.fn(),
    undo: vi.fn(),
    redo: vi.fn(),
    addCorner: vi.fn(),
    deleteCorner: vi.fn(),
    loadLatest: vi.fn().mockResolvedValue(undefined),
  }
}

let runtime: ReturnType<typeof fakeRuntime>
let unregister: () => void

beforeEach(() => {
  runtime = fakeRuntime()
  unregister = registerSiteBoundaryEditRuntime(runtime as unknown as SiteBoundaryEditRuntime)
})

afterEach(() => {
  cleanup()
  unregister()
  store().end()
})

// getByRole is called with `hidden: true` throughout: jsdom's getComputedStyle throws on some MUI
// styles when Testing Library checks whether an element is inaccessible (memory: jsdom getComputedStyle
// crash). Axe covers the real accessibility rules in SiteBoundaryEditToolbar.a11y.test.tsx.
describe('SiteBoundaryEditToolbar', () => {
  it('renders nothing when no edit session is open', () => {
    const { container } = render(<SiteBoundaryEditToolbar />)
    expect(container).toBeEmptyDOMElement()
  })

  it('names the site and shows the area', () => {
    enter()
    render(<SiteBoundaryEditToolbar />)

    expect(screen.getByText('Editing: Muscat Grand Mall')).toBeInTheDocument()
    expect(screen.getByText(/^about 10,0\d\d m²$/)).toBeInTheDocument()
  })

  it('updates the area as the outline changes', () => {
    enter()
    render(<SiteBoundaryEditToolbar />)

    act(() => edit())

    expect(screen.getByText(/^about 1[12],\d{3} m²$/)).toBeInTheDocument()
  })

  it('is a labelled toolbar', () => {
    enter()
    render(<SiteBoundaryEditToolbar />)
    expect(screen.getByRole('toolbar', { name: 'Outline editor', hidden: true })).toBeInTheDocument()
  })

  it('has Undo, Redo and Done disabled on an untouched outline, and Cancel enabled', () => {
    enter()
    render(<SiteBoundaryEditToolbar />)

    expect(screen.getByRole('button', { name: 'Undo', hidden: true })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Redo', hidden: true })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Done', hidden: true })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Cancel', hidden: true })).toBeEnabled()
  })

  it('enables Undo and Done after a change, and Redo after an undo', () => {
    enter()
    render(<SiteBoundaryEditToolbar />)

    act(() => edit())
    expect(screen.getByRole('button', { name: 'Undo', hidden: true })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Done', hidden: true })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Redo', hidden: true })).toBeDisabled()

    act(() => {
      store().undo()
    })
    expect(screen.getByRole('button', { name: 'Redo', hidden: true })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Done', hidden: true })).toBeDisabled()
  })

  it('sends each button to its action', async () => {
    const user = userEvent.setup()
    enter()
    act(() => edit())
    render(<SiteBoundaryEditToolbar />)

    await user.click(screen.getByRole('button', { name: 'Undo', hidden: true }))
    await user.click(screen.getByRole('button', { name: 'Cancel', hidden: true }))
    expect(runtime.undo).toHaveBeenCalledTimes(1)
    expect(runtime.cancel).toHaveBeenCalledTimes(1)
  })

  it('presses Done', async () => {
    const user = userEvent.setup()
    enter()
    act(() => edit())
    render(<SiteBoundaryEditToolbar />)

    await user.click(screen.getByRole('button', { name: 'Done', hidden: true }))

    expect(runtime.done).toHaveBeenCalledTimes(1)
  })

  it('says why a change was refused', () => {
    enter()
    render(<SiteBoundaryEditToolbar />)

    act(() => store().refuse('That would make the outline cross itself.'))

    expect(screen.getByText('That would make the outline cross itself.')).toBeInTheDocument()
  })

  it('shows Saving and disables every action while a save is in progress', () => {
    enter()
    act(() => {
      edit()
      store().beginSave()
    })
    render(<SiteBoundaryEditToolbar />)

    expect(screen.getByText(/Saving…/)).toBeInTheDocument()
    // The button itself shows the save is under way (a spinner and "Saving"), so a slow save never looks like nothing happened.
    expect(screen.getByRole('button', { name: 'Saving', hidden: true })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Cancel', hidden: true })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Undo', hidden: true })).toBeDisabled()
  })

  it('keeps the editor open with the error and a Retry that saves again (FR-018)', async () => {
    const user = userEvent.setup()
    enter()
    act(() => {
      edit()
      store().beginSave()
      store().saveFailed('The outline could not be saved.')
    })
    render(<SiteBoundaryEditToolbar />)

    expect(screen.getByRole('alert', { hidden: true })).toHaveTextContent('The outline could not be saved.')
    await user.click(screen.getByRole('button', { name: 'Retry', hidden: true }))

    expect(runtime.done).toHaveBeenCalledTimes(1)
    expect(store().session).not.toBeNull()
  })

  it('hides the bar on dismiss while the session carries on, and the menu can bring it back', async () => {
    const user = userEvent.setup()
    enter()
    act(() => edit())
    render(<SiteBoundaryEditToolbar />)

    await user.click(screen.getByRole('button', { name: 'Hide edit bar', hidden: true }))

    expect(screen.queryByRole('toolbar', { hidden: true })).not.toBeInTheDocument()
    expect(store().session).not.toBeNull()
    expect(store().session?.undo).toHaveLength(1)

    act(() => store().setToolbarHidden(false))
    expect(screen.getByRole('toolbar', { name: 'Outline editor', hidden: true })).toBeInTheDocument()
  })

  it('still shows a refusal while the bar is hidden, so a message is never missed', () => {
    enter()
    act(() => {
      store().setToolbarHidden(true)
      store().refuse('That would make the outline cross itself.')
    })
    render(<SiteBoundaryEditToolbar />)

    expect(screen.queryByRole('toolbar', { hidden: true })).not.toBeInTheDocument()
    expect(screen.getByText('That would make the outline cross itself.')).toBeInTheDocument()
  })

  it('brings the bar back by itself when a save fails or conflicts', () => {
    enter()
    act(() => {
      edit()
      store().setToolbarHidden(true)
      store().beginSave()
      store().saveFailed('The outline could not be saved.')
    })
    render(<SiteBoundaryEditToolbar />)

    expect(screen.getByRole('toolbar', { name: 'Outline editor', hidden: true })).toBeInTheDocument()
    expect(screen.getByRole('alert', { hidden: true })).toHaveTextContent('The outline could not be saved.')
  })

  it('offers Load latest and Cancel on a conflict (FR-019)', async () => {
    const user = userEvent.setup()
    enter()
    act(() => {
      edit()
      store().beginSave()
      store().conflict('rev-9')
    })
    render(<SiteBoundaryEditToolbar />)

    expect(screen.getByRole('alert', { hidden: true })).toHaveTextContent('The outline changed in another tab.')
    await user.click(screen.getByRole('button', { name: 'Load latest', hidden: true }))
    expect(runtime.loadLatest).toHaveBeenCalledTimes(1)
  })
})
