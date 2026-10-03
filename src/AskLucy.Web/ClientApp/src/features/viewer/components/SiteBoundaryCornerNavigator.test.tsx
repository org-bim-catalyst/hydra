import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import { registerSiteBoundaryEditRuntime, type SiteBoundaryEditRuntime } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore, type ViewState } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { SiteBoundaryCornerNavigator } from './SiteBoundaryCornerNavigator'

expect.extend(toHaveNoViolations)

const P = (lat: number, lon: number): GeoPoint => ({ latitude: lat, longitude: lon })
const SQUARE = [P(23.586, 58.392), P(23.586, 58.393), P(23.587, 58.393), P(23.587, 58.392)]
const SECOND = [P(23.59, 58.4), P(23.59, 58.401), P(23.591, 58.401)]
const viewState: ViewState = { mode: 'isometric', rotationEnabled: true, center: P(23.586, 58.392), zoom: 17, heading: 0, tilt: 45 }

const store = () => useSiteBoundaryEditStore.getState()
const region = () => screen.getByTestId('corner-navigator')
const press = (key: string, init: Partial<KeyboardEventInit> = {}) => fireEvent.keyDown(region(), { key, ...init })

let runtime: { nudgeCorner: ReturnType<typeof vi.fn>; addCorner: ReturnType<typeof vi.fn>; deleteCorner: ReturnType<typeof vi.fn>; undo: ReturnType<typeof vi.fn>; redo: ReturnType<typeof vi.fn>; cancel: ReturnType<typeof vi.fn> }
let unregister: () => void

function enter(rings: GeoPoint[][] = [SQUARE]) {
  store().enter({ chatId: 'c', siteName: 'S', revision: 'r', rings, viewState })
  render(<SiteBoundaryCornerNavigator />)
}

beforeEach(() => {
  runtime = { nudgeCorner: vi.fn(), addCorner: vi.fn(), deleteCorner: vi.fn(), undo: vi.fn(), redo: vi.fn(), cancel: vi.fn() }
  unregister = registerSiteBoundaryEditRuntime(runtime as unknown as SiteBoundaryEditRuntime)
})

afterEach(() => {
  cleanup()
  unregister()
  store().end()
})

describe('SiteBoundaryCornerNavigator', () => {
  it('renders nothing outside an edit session', () => {
    const { container } = render(<SiteBoundaryCornerNavigator />)
    expect(container).toBeEmptyDOMElement()
  })

  it('is one focusable outline editor region', () => {
    enter()
    expect(region()).toHaveAttribute('role', 'application')
    expect(region()).toHaveAttribute('aria-roledescription', 'outline editor')
    expect(region()).toHaveAttribute('tabindex', '0')
  })

  it('selects the first corner when focus lands on the region', () => {
    enter()
    fireEvent.focus(region())
    expect(store().session?.selectedCorner).toBe(0)
  })

  it('Tab walks forward through the corners and Shift+Tab back', () => {
    enter()
    act(() => store().selectCorner(0))

    press('Tab')
    expect(store().session?.selectedCorner).toBe(1)
    press('Tab')
    expect(store().session?.selectedCorner).toBe(2)
    press('Tab', { shiftKey: true })
    expect(store().session?.selectedCorner).toBe(1)
  })

  it('Tab past the last corner leaves the region, and so does Shift+Tab from the first', () => {
    enter()
    act(() => store().selectCorner(3))
    const afterLast = fireEvent.keyDown(region(), { key: 'Tab' })
    expect(afterLast).toBe(true)
    expect(store().session?.selectedCorner).toBe(3)

    act(() => store().selectCorner(0))
    const beforeFirst = fireEvent.keyDown(region(), { key: 'Tab', shiftKey: true })
    expect(beforeFirst).toBe(true)
  })

  it('[ and ] switch rings, wrapping round', () => {
    enter([SQUARE, SECOND])

    press(']')
    expect(store().session?.activeRing).toBe(1)
    press(']')
    expect(store().session?.activeRing).toBe(0)
    press('[')
    expect(store().session?.activeRing).toBe(1)
  })

  it('arrow keys move the corner 0.5 m, and 5 m with Shift', () => {
    enter()
    act(() => store().selectCorner(1))

    press('ArrowRight')
    expect(runtime.nudgeCorner).toHaveBeenLastCalledWith(0.5, 0)
    press('ArrowUp')
    expect(runtime.nudgeCorner).toHaveBeenLastCalledWith(0, 0.5)
    press('ArrowLeft', { shiftKey: true })
    expect(runtime.nudgeCorner).toHaveBeenLastCalledWith(-5, 0)
    press('ArrowDown', { shiftKey: true })
    expect(runtime.nudgeCorner).toHaveBeenLastCalledWith(0, -5)
  })

  it('Insert and + add a corner; Delete and Backspace remove one', () => {
    enter()
    act(() => store().selectCorner(1))

    press('Insert')
    press('+')
    expect(runtime.addCorner).toHaveBeenCalledTimes(2)

    press('Delete')
    press('Backspace')
    expect(runtime.deleteCorner).toHaveBeenCalledTimes(2)
  })

  it('Ctrl+Z undoes; Ctrl+Shift+Z and Ctrl+Y redo', () => {
    enter()

    press('z', { ctrlKey: true })
    expect(runtime.undo).toHaveBeenCalledTimes(1)
    press('Z', { ctrlKey: true, shiftKey: true })
    press('y', { ctrlKey: true })
    expect(runtime.redo).toHaveBeenCalledTimes(2)
  })

  it('Escape from the map: the first leaves the Select tool, the next clears the selection, then nothing', () => {
    enter()
    act(() => store().selectCorners([1, 2]))
    act(() => store().setTool('select'))

    fireEvent.keyDown(document.body, { key: 'Escape' })
    expect(store().session?.tool).toBe('edit')
    expect(store().session?.selectedCorners).toEqual([1, 2])

    fireEvent.keyDown(document.body, { key: 'Escape' })
    expect(store().session?.selectedCorners).toEqual([])

    fireEvent.keyDown(document.body, { key: 'Escape' })
    expect(runtime.cancel).not.toHaveBeenCalled()
    expect(store().session).not.toBeNull()
  })

  it('Escape in the focused editor region with nothing selected leaves the editor when nothing changed', () => {
    enter()
    press('Escape')
    expect(runtime.cancel).toHaveBeenCalledTimes(1)
  })

  it('Escape asks rather than discarding when there are unsaved changes', () => {
    enter()
    act(() => store().applyChange({ op: 'move', ring: 0, index: 1, before: SQUARE[1], after: P(23.586, 58.3931) }))

    press('Escape')

    expect(runtime.cancel).not.toHaveBeenCalled()
    expect(store().session?.refusal).toContain('unsaved changes')
  })

  it('works when the map has focus, not the hidden region (a corner was just clicked)', () => {
    enter()
    act(() => store().selectCorner(1))

    fireEvent.keyDown(document.body, { key: 'ArrowRight' })
    fireEvent.keyDown(document.body, { key: 'z', ctrlKey: true })

    expect(runtime.nudgeCorner).toHaveBeenCalledWith(0.5, 0)
    expect(runtime.undo).toHaveBeenCalledTimes(1)
  })

  it('Tab from the map walks the corners and wraps round instead of leaving', () => {
    enter()
    act(() => store().selectCorner(3))

    fireEvent.keyDown(document.body, { key: 'Tab' })
    expect(store().session?.selectedCorner).toBe(0)

    fireEvent.keyDown(document.body, { key: 'Tab', shiftKey: true })
    expect(store().session?.selectedCorner).toBe(3)
  })

  it('Tab from the map container (not just the body) walks the corners', () => {
    enter()
    act(() => store().selectCorner(0))
    const mapDiv = document.createElement('div')
    mapDiv.tabIndex = 0
    document.body.appendChild(mapDiv)

    fireEvent.keyDown(mapDiv, { key: 'Tab' })

    expect(store().session?.selectedCorner).toBe(1)
    mapDiv.remove()
  })

  it('Tab on a button is left alone, so the toolbar can still be reached', () => {
    enter()
    act(() => store().selectCorner(1))
    const button = document.createElement('button')
    document.body.appendChild(button)

    fireEvent.keyDown(button, { key: 'Tab' })

    expect(store().session?.selectedCorner).toBe(1)
    button.remove()
  })

  it('[ with a single ring says there is no other ring', () => {
    enter()
    fireEvent.keyDown(document.body, { key: '[' })
    expect(store().session?.refusal).toContain('only one ring')
  })

  it('the editor takes an arrow key before the map can pan with it', () => {
    enter()
    act(() => store().selectCorner(1))
    const mapDiv = document.createElement('div')
    document.body.appendChild(mapDiv)
    const mapPans = vi.fn()
    mapDiv.addEventListener('keydown', mapPans)

    fireEvent.keyDown(mapDiv, { key: 'ArrowLeft' })

    expect(runtime.nudgeCorner).toHaveBeenCalledWith(-0.5, 0)
    expect(mapPans).not.toHaveBeenCalled()
    mapDiv.remove()
  })

  it('the arrows work in the Select tool too', () => {
    enter()
    act(() => store().selectCorners([1, 2]))
    act(() => store().setTool('select'))

    fireEvent.keyDown(document.body, { key: 'ArrowRight' })

    expect(runtime.nudgeCorner).toHaveBeenCalledWith(0.5, 0)
  })

  it('does not move a corner while the user is typing in a field', () => {
    enter()
    act(() => store().selectCorner(1))
    const input = document.createElement('input')
    document.body.appendChild(input)

    fireEvent.keyDown(input, { key: 'ArrowRight' })

    expect(runtime.nudgeCorner).not.toHaveBeenCalled()
    input.remove()
  })

  it('does nothing from the window while the arc or circle tool owns the map', () => {
    enter()
    act(() => store().selectCorner(1))
    act(() => store().setTool('arc'))

    fireEvent.keyDown(document.body, { key: 'ArrowRight' })

    expect(runtime.nudgeCorner).not.toHaveBeenCalled()
  })

  it('announces the selected corner', () => {
    enter()
    act(() => store().selectCorner(2))
    expect(screen.getByRole('status')).toHaveTextContent('Corner 3 of 4, ring 1 of 1')
  })

  it('has no automatically detectable a11y violations', async () => {
    enter()
    act(() => store().selectCorner(1))
    const results = await axe(region().parentElement as HTMLElement)
    expect(results).toHaveNoViolations()
  })
})
