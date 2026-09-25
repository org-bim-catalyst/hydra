import { alpha, ThemeProvider } from '@mui/material'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { createAppTheme } from '../../theme'
import { HudCard } from './HudCard'

function renderCard(mode: 'light' | 'dark', props: Partial<Parameters<typeof HudCard>[0]> = {}) {
  return render(
    <ThemeProvider theme={createAppTheme(mode)}>
      <HudCard {...props}>
        <span>content</span>
      </HudCard>
    </ThemeProvider>,
  )
}

function cardRoot(): HTMLElement {
  return screen.getByText('content').parentElement as HTMLElement
}

/** jsdom normalises colours to `rgb(...)`/`rgba(...)`; comparing through a throwaway element
 * normalises the expected value the same way. */
function normalise(colour: string): string {
  const probe = document.createElement('div')
  probe.style.backgroundColor = colour
  return probe.style.backgroundColor
}

describe('HudCard (specs/073 contract H1–H3)', () => {
  it('H1: is exactly 40 px tall with its content laid out in a vertically centred row', () => {
    renderCard('light')
    const style = window.getComputedStyle(cardRoot())

    expect(style.height).toBe('40px')
    expect(style.display).toBe('flex')
    expect(style.alignItems).toBe('center')
  })

  it.each(['light', 'dark'] as const)('H2: uses the workspace-chrome surface in %s theme', (mode) => {
    const theme = createAppTheme(mode)
    renderCard(mode)
    const style = window.getComputedStyle(cardRoot())

    expect(style.backgroundColor).toBe(normalise(alpha(theme.palette.background.paper, 0.97)))
    expect(style.color).toBe(normalise(theme.palette.text.primary))
  })

  it('H3: has no accent stripe — every border side is the same', () => {
    renderCard('dark')
    const style = window.getComputedStyle(cardRoot())

    expect(style.borderLeftWidth).toBe(style.borderRightWidth)
    expect(style.borderLeftColor).toBe(style.borderRightColor)
    expect(cardRoot().outerHTML).not.toMatch(/9C62DE/i)
  })

  it('lets pointer events through to the map by default and accepts opting in (FR-013)', () => {
    const { unmount } = renderCard('light')
    expect(window.getComputedStyle(cardRoot()).pointerEvents).toBe('none')
    unmount()

    renderCard('light', { pointerEvents: 'auto' })
    expect(window.getComputedStyle(cardRoot()).pointerEvents).toBe('auto')
  })

  it('passes role, aria-label and maxWidth through', () => {
    renderCard('light', { role: 'status', 'aria-label': 'Card label', maxWidth: 260 })

    const card = screen.getByRole('status', { name: 'Card label' })
    expect(card).toBe(cardRoot())
    expect(window.getComputedStyle(card).maxWidth).toBe('260px')
  })
})
