import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { MarkerStyleSelector } from './MarkerStyleSelector'
import { RotationToggleButton } from './RotationToggleButton'

/**
 * This button sits in the workspace's top-right cluster beside the theme toggle, the rotation
 * toggle and the account menu. It was a MUI `medium` Fab (48 px) while all three of those are
 * 40 px, so it read as a different, larger control in a row meant to look uniform.
 */
describe('MarkerStyleSelector', () => {
  it('is the same size as the other buttons in its cluster', () => {
    render(
      <>
        <MarkerStyleSelector />
        <RotationToggleButton />
      </>,
    )

    const marker = screen.getByRole('button', { name: 'Change POI marker style' })
    const rotation = screen.getByRole('button', { name: /rotation/i })

    // Emotion injects the sx rules into the document, so the 40 px override resolves here.
    // `sizeSmall` is checked alongside it because that is what the width is overriding from.
    expect(marker.className).toContain('MuiFab-sizeSmall')
    expect(rotation.className).toContain('MuiFab-sizeSmall')
    expect(getComputedStyle(marker).width).toBe('40px')
    expect(getComputedStyle(marker).width).toBe(getComputedStyle(rotation).width)
  })
})
