import { Box } from '@mui/material'
import { useEffect } from 'react'
import { siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'

/** An arrow key moves the selected corner this far; with Shift, the larger step. */
const STEP_METERS = 0.5
const BIG_STEP_METERS = 5

/** Visually hidden but still reachable by Tab and read by a screen reader. */
const visuallyHidden = {
  position: 'absolute',
  width: 1,
  height: 1,
  overflow: 'hidden',
  clip: 'rect(0 0 0 0)',
  whiteSpace: 'nowrap',
} as const

/** What the handler needs of a keyboard event, so React's and the window's both fit. */
interface KeyLike {
  key: string
  shiftKey: boolean
  ctrlKey: boolean
  metaKey: boolean
  preventDefault(): void
}

/**
 * Keys that act on the selected corner while the user is not typing and no dialog or menu is open: the map
 * has focus after a corner is clicked, not the hidden region, so the shortcuts must work from the window too.
 * Tab walks the corners from the map or the page body (not between controls), and Delete and Backspace are
 * handled by the edit-mode hook's own window listener.
 */
function acceptsWindowKey(event: globalThis.KeyboardEvent): boolean {
  const target = event.target as HTMLElement | null
  if (target?.closest('[data-testid="corner-navigator"]')) return false
  if (target && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))) return false
  if (target?.closest('[role="dialog"], [role="menu"], [role="listbox"]')) return false
  if (event.key === 'Delete' || event.key === 'Backspace') return false
  // Tab walks the corners from the map or the page, but never takes the key from a control the user is on, so the toolbar stays reachable.
  if (event.key === 'Tab' && target?.closest('button, a, input, select, textarea, summary, [role="button"], [role="menuitem"], [role="tab"]')) return false
  return useSiteBoundaryEditStore.getState().session?.tool === 'edit'
}

function handleKey(event: KeyLike, wrap = false) {
  const store = () => useSiteBoundaryEditStore.getState()
  const session = store().session
  if (!session) return
  const selectCorner = (index: number) => store().selectCorner(index)

  const step = event.shiftKey ? BIG_STEP_METERS : STEP_METERS
  const key = event.key

  if (key === 'Tab') {
    const live = store().session
    const current = live?.selectedCorner ?? null
    const liveRing = live?.rings[live.activeRing] ?? []
    const next = event.shiftKey ? (current ?? liveRing.length) - 1 : (current ?? -1) + 1
    if (liveRing.length === 0) return
    // In the region, past either end the focus simply leaves it. From the map there is no region to
    // leave, so the walk wraps round; Escape ends editing.
    if (!wrap && (next < 0 || next >= liveRing.length)) return
    event.preventDefault()
    selectCorner((next + liveRing.length) % liveRing.length)
    return
  }

  const nudges: Record<string, [number, number]> = {
    ArrowLeft: [-step, 0],
    ArrowRight: [step, 0],
    ArrowUp: [0, step],
    ArrowDown: [0, -step],
  }
  if (nudges[key]) {
    event.preventDefault()
    siteBoundaryEditActions.nudgeCorner(...nudges[key])
    return
  }

  if (key === '[' || key === ']') {
    event.preventDefault()
    const count = session.rings.length
    if (count < 2) {
      store().refuse('This outline has only one ring, so there is no other ring to switch to.')
      return
    }
    store().setActiveRing((session.activeRing + (key === ']' ? 1 : count - 1)) % count)
    return
  }

  if (key === 'Insert' || key === '+') {
    event.preventDefault()
    siteBoundaryEditActions.addCorner()
    return
  }

  if (key === 'Delete' || key === 'Backspace') {
    event.preventDefault()
    siteBoundaryEditActions.deleteCorner()
    return
  }

  if ((event.ctrlKey || event.metaKey) && key.toLowerCase() === 'z') {
    event.preventDefault()
    if (event.shiftKey) siteBoundaryEditActions.redo()
    else siteBoundaryEditActions.undo()
    return
  }

  if ((event.ctrlKey || event.metaKey) && key.toLowerCase() === 'y') {
    event.preventDefault()
    siteBoundaryEditActions.redo()
    return
  }

  if (key === 'Escape') {
    event.preventDefault()
    if (store().isDirty()) {
      store().refuse('You have unsaved changes - choose Done to save them, or Cancel to discard them.')
    } else {
      siteBoundaryEditActions.cancel()
    }
  }
}

/**
 * specs/079 (US6, WCAG 2.1 AA): edits the outline without a mouse. One focusable region while an
 * edit session is open. Tab and Shift+Tab walk the corners (Tab past the last one leaves the region),
 * [ and ] switch rings, the arrow keys move the corner 0.5 m (5 m with Shift), Insert or + adds one
 * after it, Delete removes it, Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y undo and redo. Every change goes through
 * the same actions as the mouse, so the same validation applies. The selected corner is announced.
 */
export function SiteBoundaryCornerNavigator() {
  const session = useSiteBoundaryEditStore((s) => s.session)
  const inSession = session !== null

  useEffect(() => {
    if (!inSession) return
    const onKey = (event: globalThis.KeyboardEvent) => {
      if (acceptsWindowKey(event)) handleKey(event, true)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [inSession])

  if (!session) return null

  const ring = session.rings[session.activeRing] ?? []
  const selected = session.selectedCorner
  const store = () => useSiteBoundaryEditStore.getState()

  const announcement =
    selected !== null && ring[selected]
      ? `Corner ${selected + 1} of ${ring.length}, ring ${session.activeRing + 1} of ${session.rings.length}, ` +
        `${ring[selected].latitude.toFixed(5)} north, ${ring[selected].longitude.toFixed(5)} east`
      : `Outline editor, ring ${session.activeRing + 1} of ${session.rings.length}, ${ring.length} corners. Press Tab to select a corner.`

  return (
    <Box
      data-testid="corner-navigator"
      role="application"
      aria-roledescription="outline editor"
      aria-label="Outline editor"
      tabIndex={0}
      onKeyDown={handleKey}
      onFocus={(event) => {
        // Landing on the region itself selects the first corner, so there is always one to move.
        if (event.target === event.currentTarget && selected === null && ring.length > 0) store().selectCorner(0)
      }}
      sx={visuallyHidden}
    >
      <div role="status" aria-live="polite">
        {announcement}
      </div>
    </Box>
  )
}
