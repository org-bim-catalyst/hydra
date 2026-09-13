import { act, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { RESERVED_ATTRIBUTE } from '../layout/reservedRegions'
import { panelTypeRegistry } from '../registry'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import { FloatingPanelHost } from './FloatingPanelHost'

const TEST_TYPE_KEY = `host-test-panel-${Math.random()}`
panelTypeRegistry.register({
  typeKey: TEST_TYPE_KEY,
  renderer: () => null,
  schema: z.object({ label: z.string() }),
  chrome: { titleBar: true, resizable: true, defaultSize: { width: 200, height: 150 } },
})

// specs/054 T011 — capture each rendered Rnd instance's props so drag events can be invoked
// directly, the same technique FloatingPanel.test.tsx already uses. Most tests here only ever
// have one panel open, so "the last rendered instance" (`lastRndProps`) is unambiguous; a test
// with two-or-more panels open instead looks up `rndPropsByTitle[requestId]` — `FloatingPanel`'s
// own root carries `aria-label={panel.title}` and `openTestPanel` sets `title: requestId`, so the
// two happen to coincide, letting a specific panel's instance be found regardless of render order.
let lastRndProps: Record<string, unknown> = {}
const rndPropsByTitle: Record<string, Record<string, unknown>> = {}
vi.mock('react-rnd', () => ({
  Rnd: (props: Record<string, unknown> & { children: React.ReactElement<{ 'aria-label'?: string }> }) => {
    lastRndProps = props
    const label = props.children?.props?.['aria-label']
    if (typeof label === 'string') rndPropsByTitle[label] = props
    return <div>{props.children}</div>
  },
}))

function stubElementRect(el: Element, rect: { left: number; top: number; width: number; height: number }) {
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

const initialState = useFloatingPanelStore.getState()

/** Stubs the host's own measured size — jsdom's real `getBoundingClientRect` always returns
 * zeros, and `runArrangement` deliberately no-ops on a zero-size host (it isn't mounted/laid out
 * yet), so every test needs this before triggering an arrangement pass. */
function stubHostSize(container: HTMLElement, width: number, height: number): HTMLElement {
  const host = container.firstElementChild as HTMLElement
  host.getBoundingClientRect = () =>
    ({ left: 0, top: 0, right: width, bottom: height, width, height, x: 0, y: 0, toJSON: () => ({}) }) as DOMRect
  return host
}

// specs/054: a lone panel in a 1000x800 host lands at x=16 (HOST_MARGIN — the grid's first free
// candidate, since a panel can never sit flush against the viewport edge) and is then centered
// vertically — y = floor((800 - clampedHeight) / 2). The registered chrome's defaultSize height
// (150) is below MIN_PANEL_HEIGHT (160, viewer/panels/types/panel.ts), so resolveChrome clamps it
// to 160 before this math runs: floor((800 - 160) / 2) = 320.
const CENTERED_SINGLE_PANEL_POSITION = { x: 16, y: 320 }

function openTestPanel(requestId: string) {
  useFloatingPanelStore.getState().openPanel({
    kind: 'live',
    requestId,
    typeKey: TEST_TYPE_KEY,
    title: requestId,
    data: { label: requestId },
  })
}

describe('FloatingPanelHost arrangement wiring (specs/054 FR-002, FR-005, SC-005)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('opening a panel triggers an arrangement pass that overrides the store-seeded cascade position', async () => {
    const { container } = render(<FloatingPanelHost />)
    stubHostSize(container, 1000, 800)

    openTestPanel('host-open-1')

    // The store seeds a brand-new panel at the blind cascade position {40, 40}; the host's own
    // arrangement pass (with no reserved regions and no other panels in a 1000x800 host) should
    // immediately override it to the centered single-panel position. Seeing the panel land there,
    // not at {40, 40}, is what proves the host wiring ran computeArrangement.
    await waitFor(() => {
      const panel = useFloatingPanelStore.getState().panels.find((p) => p.id === 'host-open-1')
      expect(panel?.position).toEqual(CENTERED_SINGLE_PANEL_POSITION)
    })
  })

  it('closing a panel re-triggers arrangement over the remaining panels', async () => {
    const { container } = render(<FloatingPanelHost />)
    stubHostSize(container, 1000, 800)

    openTestPanel('host-close-a')
    openTestPanel('host-close-b')

    await waitFor(() => {
      const b = useFloatingPanelStore.getState().panels.find((p) => p.id === 'host-close-b')
      // Packed beside A, not on top of it.
      expect(b?.position).not.toEqual({ x: 0, y: 0 })
    })

    useFloatingPanelStore.getState().closePanel('host-close-a')

    // With A gone, B is the only panel left — a fresh arrangement pass should move it to the now
    // free, centered single-panel position rather than leaving it wherever it happened to be
    // packed alongside A.
    await waitFor(() => {
      const b = useFloatingPanelStore.getState().panels.find((p) => p.id === 'host-close-b')
      expect(b?.position).toEqual(CENTERED_SINGLE_PANEL_POSITION)
    })
  })

  it('a window resize re-triggers arrangement after clampToViewport', async () => {
    const { container } = render(<FloatingPanelHost />)
    const host = stubHostSize(container, 1000, 800)

    openTestPanel('host-resize-1')
    await waitFor(() => {
      expect(useFloatingPanelStore.getState().panels[0]?.position).toEqual(CENTERED_SINGLE_PANEL_POSITION)
    })

    const applyArrangementSpy = vi.spyOn(useFloatingPanelStore.getState(), 'applyArrangement')
    host.getBoundingClientRect = () =>
      ({ left: 0, top: 0, right: 400, bottom: 300, width: 400, height: 300, x: 0, y: 0, toJSON: () => ({}) }) as DOMRect

    window.dispatchEvent(new Event('resize'))

    await waitFor(() => {
      expect(applyArrangementSpy).toHaveBeenCalled()
    })
  })
})

describe('FloatingPanelHost drag-time landing placeholder (specs/054 FR-005f/g, T011)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  afterEach(() => {
    document.body.querySelectorAll(`[${RESERVED_ATTRIBUTE}]`).forEach((el) => el.remove())
  })

  it('shows a placeholder over free space and hides it over a reserved region', async () => {
    const { container } = render(<FloatingPanelHost />)
    stubHostSize(container, 1000, 800)

    const reserved = document.createElement('div')
    reserved.setAttribute(RESERVED_ATTRIBUTE, '')
    document.body.appendChild(reserved)
    stubElementRect(reserved, { left: 500, top: 0, width: 500, height: 800 })

    openTestPanel('drag-guide-1')
    await waitFor(() => {
      expect(useFloatingPanelStore.getState().panels[0]?.position).toEqual(CENTERED_SINGLE_PANEL_POSITION)
    })

    const onDragStart = lastRndProps.onDragStart as () => void
    const onDrag = lastRndProps.onDrag as (e: unknown, data: { x: number; y: number }) => void
    act(() => onDragStart())

    // Candidate slots are exactly panel-sized boxes at valid free origins — with the right half
    // reserved, the only one available for this 200x160 panel is at {0, 0} (S1: the dragged
    // panel's own current box is excluded from the obstacle set regardless of where it now sits).
    act(() => onDrag(undefined, { x: 50, y: 50 }))
    expect(screen.getByTestId('landing-placeholder')).toBeInTheDocument()

    // A point inside the reserved region — no candidate slot contains it (FR-005g).
    act(() => onDrag(undefined, { x: 700, y: 400 }))
    expect(screen.queryByTestId('landing-placeholder')).not.toBeInTheDocument()
  })

  it('snaps the drop to the shown placeholder', async () => {
    const { container } = render(<FloatingPanelHost />)
    stubHostSize(container, 1000, 800)

    openTestPanel('drag-snap-1')
    await waitFor(() => {
      expect(useFloatingPanelStore.getState().panels[0]?.position).toEqual(CENTERED_SINGLE_PANEL_POSITION)
    })

    const onDragStart = lastRndProps.onDragStart as () => void
    const onDrag = lastRndProps.onDrag as (e: unknown, data: { x: number; y: number }) => void
    const onDragStop = lastRndProps.onDragStop as (e: unknown, data: { x: number; y: number }) => void

    act(() => onDragStart())
    // With no reserved regions and only this one panel, the sole candidate slot is a 200x160 box
    // at {16, 16} — HOST_MARGIN's own inset, the only free origin. FloatingPanel reports the
    // panel's CENTER, not its raw top-left (feedback 2026-09-13) — a raw drop of {10, 10} for a
    // 200x160 panel centers at {110, 90}, which sits inside that slot but isn't equal to its
    // origin, so a resulting position of exactly {16, 16} proves a snap happened.
    act(() => onDrag(undefined, { x: 10, y: 10 }))
    expect(screen.getByTestId('landing-placeholder')).toBeInTheDocument()
    act(() => onDragStop(undefined, { x: 10, y: 10 }))

    // Snapped to the slot's own origin, not left at the raw {100, 100} drop point.
    expect(useFloatingPanelStore.getState().panels[0]?.position).toEqual({ x: 16, y: 16 })
    expect(screen.queryByTestId('landing-placeholder')).not.toBeInTheDocument()
  })

  it('leaves the panel exactly where dropped when no placeholder was shown', async () => {
    const { container } = render(<FloatingPanelHost />)
    stubHostSize(container, 1000, 800)

    const reserved = document.createElement('div')
    reserved.setAttribute(RESERVED_ATTRIBUTE, '')
    document.body.appendChild(reserved)
    stubElementRect(reserved, { left: 0, top: 0, width: 1000, height: 800 })

    openTestPanel('drag-nosnap-1')
    // The reserved region covers the entire host, so the panel itself lands via the cascade
    // fallback rather than the {0, 0} grid slot the other tests assert — either way, what matters
    // here is only that the drop point below is not inside any candidate slot.
    await waitFor(() => {
      expect(useFloatingPanelStore.getState().panels[0]).toBeDefined()
    })

    const onDragStart = lastRndProps.onDragStart as () => void
    const onDragStop = lastRndProps.onDragStop as (e: unknown, data: { x: number; y: number }) => void
    act(() => onDragStart())
    act(() => onDragStop(undefined, { x: 111, y: 222 }))

    expect(useFloatingPanelStore.getState().panels[0]?.position).toEqual({ x: 111, y: 222 })
  })
})

describe('FloatingPanelHost minimized panels as fixed obstacles (specs/054 feedback 2026-09-13)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('a full panel opened later avoids a minimized panel’s actual on-screen footprint, not its stale pre-minimize size', async () => {
    const { container } = render(<FloatingPanelHost />)
    stubHostSize(container, 1000, 800)

    openTestPanel('min-obstacle-a')
    await waitFor(() => {
      expect(useFloatingPanelStore.getState().panels[0]?.position).toEqual(CENTERED_SINGLE_PANEL_POSITION)
    })

    act(() => {
      useFloatingPanelStore.getState().minimizePanel('min-obstacle-a')
    })
    const minimizedPosition = useFloatingPanelStore.getState().panels[0]!.position
    // MINIMIZED_BAR_WIDTH/HEIGHT from FloatingPanel.tsx — kept as literals here so this test
    // doesn't depend on that export, only on the behavior it enables.
    const minimizedRect = { x: minimizedPosition.x, y: minimizedPosition.y, width: 220, height: 40 }

    openTestPanel('min-obstacle-b')
    await waitFor(() => {
      const b = useFloatingPanelStore.getState().panels.find((p) => p.id === 'min-obstacle-b')
      expect(b?.position).toBeDefined()
    })

    const b = useFloatingPanelStore.getState().panels.find((p) => p.id === 'min-obstacle-b')!
    const bRect = { x: b.position.x, y: b.position.y, width: b.size.width, height: b.size.height }
    const overlaps =
      bRect.x < minimizedRect.x + minimizedRect.width &&
      bRect.x + bRect.width > minimizedRect.x &&
      bRect.y < minimizedRect.y + minimizedRect.height &&
      bRect.y + bRect.height > minimizedRect.y
    expect(overlaps).toBe(false)
  })
})

describe('FloatingPanelHost reflow on drop (specs/054 feedback 2026-09-13 — "push each other")', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('reflows the other (non-pinned) panel out of the way when a drop lands on top of it', async () => {
    const { container } = render(<FloatingPanelHost />)
    stubHostSize(container, 1000, 800)

    openTestPanel('reflow-a')
    openTestPanel('reflow-b')
    await waitFor(() => {
      const a = useFloatingPanelStore.getState().panels.find((p) => p.id === 'reflow-a')
      const b = useFloatingPanelStore.getState().panels.find((p) => p.id === 'reflow-b')
      expect(a?.position).toBeDefined()
      expect(b?.position).toBeDefined()
      expect(a?.position).not.toEqual(b?.position)
    })
    const bBeforeDrop = useFloatingPanelStore.getState().panels.find((p) => p.id === 'reflow-b')!.position

    // Drag A and drop it directly on top of B's current position — a free-form drop (no
    // placeholder shown, since that spot is occupied), which used to just leave an overlap.
    const onDragStart = rndPropsByTitle['reflow-a'].onDragStart as () => void
    const onDragStop = rndPropsByTitle['reflow-a'].onDragStop as (e: unknown, data: { x: number; y: number }) => void
    act(() => onDragStart())
    act(() => onDragStop(undefined, bBeforeDrop))

    expect(useFloatingPanelStore.getState().panels.find((p) => p.id === 'reflow-a')?.position).toEqual(bBeforeDrop)

    // B (still auto-placed, not the panel the user just moved) is reflowed clear of A rather than
    // left sitting underneath it.
    await waitFor(() => {
      const a = useFloatingPanelStore.getState().panels.find((p) => p.id === 'reflow-a')!
      const b = useFloatingPanelStore.getState().panels.find((p) => p.id === 'reflow-b')!
      const rectsOverlap =
        a.position.x < b.position.x + b.size.width &&
        a.position.x + a.size.width > b.position.x &&
        a.position.y < b.position.y + b.size.height &&
        a.position.y + a.size.height > b.position.y
      expect(rectsOverlap).toBe(false)
    })
  })
})
