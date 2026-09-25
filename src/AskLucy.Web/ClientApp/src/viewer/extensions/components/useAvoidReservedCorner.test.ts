import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { RESERVED_ATTRIBUTE } from '../../panels/layout/reservedRegions'
import { CORNER_CHROME_ATTRIBUTE, useAvoidReservedCorner } from './useAvoidReservedCorner'

function stubRect(el: Element, rect: { left: number; top: number; width: number; height: number }) {
  ;(el as HTMLElement).getBoundingClientRect = () =>
    ({
      left: rect.left,
      top: rect.top,
      right: rect.left + rect.width,
      bottom: rect.top + rect.height,
      width: rect.width,
      height: rect.height,
      x: rect.left,
      y: rect.top,
      toJSON: () => rect,
    }) as DOMRect
}

afterEach(() => {
  document.body.innerHTML = ''
})

describe('useAvoidReservedCorner', () => {
  it('defaults to the natural corner margin when nothing else is reserved', () => {
    const { result } = renderHook(() => useAvoidReservedCorner({ current: null }))
    expect(result.current).toBe(16)
  })

  it('sits below the tallest reserved element already present at mount', async () => {
    const chrome = document.createElement('div')
    chrome.setAttribute(RESERVED_ATTRIBUTE, '')
    document.body.appendChild(chrome)
    stubRect(chrome, { left: 1800, top: 0, width: 100, height: 60 })

    const { result } = renderHook(() => useAvoidReservedCorner({ current: null }))
    await waitFor(() => expect(result.current).toBe(68)) // 60 (bottom) + 8 (MARGIN)
  })

  // specs/054 regression (2026-09-13) — found live: ExtensionToolbar and CameraAttitudeWidget both
  // call this hook unconditionally (correct) but don't render their positioned content until
  // later (a contributed toolbar entry / an active camera reading arrive asynchronously). A
  // mount-once effect measured before any of that existed and never re-ran, so the value stayed
  // stuck at DEFAULT_TOP forever — exactly the "toolbar button overlapping the account menu"
  // bug reported live. This proves the fix: reserved chrome appearing AFTER the hook has already
  // mounted and settled must still be picked up.
  it('re-measures when a reserved element is added to the document after mount', async () => {
    const { result } = renderHook(() => useAvoidReservedCorner({ current: null }))
    expect(result.current).toBe(16)

    const chrome = document.createElement('div')
    chrome.setAttribute(RESERVED_ATTRIBUTE, '')
    document.body.appendChild(chrome)
    stubRect(chrome, { left: 1800, top: 0, width: 100, height: 60 })

    await waitFor(() => expect(result.current).toBe(68))
  })

  it('re-measures when a reserved element already in the document gains the attribute later', async () => {
    const chrome = document.createElement('div')
    document.body.appendChild(chrome)
    stubRect(chrome, { left: 1800, top: 0, width: 100, height: 60 })

    const { result } = renderHook(() => useAvoidReservedCorner({ current: null }))
    expect(result.current).toBe(16)

    act(() => chrome.setAttribute(RESERVED_ATTRIBUTE, ''))

    await waitFor(() => expect(result.current).toBe(68))
  })

  it('excludes a sibling corner widget (CORNER_CHROME_ATTRIBUTE) from its own measurement', async () => {
    const sibling = document.createElement('div')
    sibling.setAttribute(RESERVED_ATTRIBUTE, '')
    sibling.setAttribute(CORNER_CHROME_ATTRIBUTE, '')
    document.body.appendChild(sibling)
    stubRect(sibling, { left: 1800, top: 0, width: 100, height: 500 })

    const { result } = renderHook(() => useAvoidReservedCorner({ current: null }))
    await waitFor(() => {
      expect(result.current).toBe(16)
    })
  })

  // specs/054 regression (2026-09-13) — found live: PanelDock is vertically centered
  // (`top: '50%'`) but still near the left edge horizontally, and still carries
  // RESERVED_ATTRIBUTE. Without also checking vertical proximity to the top, this hook swept
  // PanelDock into "chrome to avoid in the top-left corner," pushing CameraAttitudeWidget down
  // to PanelDock's mid-screen position — nowhere near the weather widget/confidence badge it's
  // actually meant to clear.
  it('ignores a reserved element that is near the edge but vertically centered, not near the top', async () => {
    const dock = document.createElement('div')
    dock.setAttribute(RESERVED_ATTRIBUTE, '')
    document.body.appendChild(dock)
    // Near the left edge (left: 16) but vertically centered on a ~1300px-tall viewport.
    stubRect(dock, { left: 16, top: 620, width: 40, height: 80 })

    const { result } = renderHook(() => useAvoidReservedCorner({ current: null }, 'left'))
    await waitFor(() => {
      expect(result.current).toBe(16)
    })
  })

  it('still picks up a reserved element that is both near the edge and near the top', async () => {
    const chrome = document.createElement('div')
    chrome.setAttribute(RESERVED_ATTRIBUTE, '')
    document.body.appendChild(chrome)
    stubRect(chrome, { left: 16, top: 0, width: 200, height: 150 })

    const { result } = renderHook(() => useAvoidReservedCorner({ current: null }, 'left'))
    await waitFor(() => {
      expect(result.current).toBe(158) // 150 (bottom) + 8 (MARGIN)
    })
  })

  // specs/073 research D9 — the studio's top-left HUD is now one 40 px row (Home, title, weather,
  // site card) whose start group carries RESERVED_ATTRIBUTE, instead of three stacked cards. A
  // left-corner widget (CameraAttitudeWidget) must sit just below that single row.
  it('sits a left-corner widget just below the one-row HUD start group', async () => {
    const row = document.createElement('div')
    row.setAttribute(RESERVED_ATTRIBUTE, '')
    document.body.appendChild(row)
    stubRect(row, { left: 16, top: 16, width: 600, height: 40 })

    const { result } = renderHook(() => useAvoidReservedCorner({ current: null }, 'left'))
    await waitFor(() => {
      expect(result.current).toBe(64) // 16 (top) + 40 (row height) + 8 (MARGIN)
    })
  })

  it('only considers elements near the requested side', async () => {
    const leftChrome = document.createElement('div')
    leftChrome.setAttribute(RESERVED_ATTRIBUTE, '')
    document.body.appendChild(leftChrome)
    stubRect(leftChrome, { left: 0, top: 0, width: 100, height: 200 })

    const { result } = renderHook(() => useAvoidReservedCorner({ current: null }, 'right'))
    await waitFor(() => {
      expect(result.current).toBe(16)
    })
  })
})
