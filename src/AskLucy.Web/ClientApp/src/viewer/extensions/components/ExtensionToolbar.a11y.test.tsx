import { act, render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { createTheme, ThemeProvider } from '@mui/material'
import { afterEach, describe, expect, it } from 'vitest'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'
import { ExtensionToolbar } from './ExtensionToolbar'

expect.extend(toHaveNoViolations)

function TestIcon() {
  return <svg data-testid="test-icon" />
}

function contribute() {
  act(() => {
    useViewerExtensionStore.getState().addContribution({
      kind: 'toolbarEntry',
      extensionId: 'ext-a11y',
      entry: { id: 'entry-1', label: 'Toggle Solar Analysis', icon: TestIcon, onClick: () => {} },
    })
  })
}

describe('ExtensionToolbar accessibility (FR-024)', () => {
  afterEach(() => {
    useViewerExtensionStore.getState().removeContributionsFor('ext-a11y')
  })

  it('has no automatically detectable a11y violations in light mode', async () => {
    contribute()
    const { container } = render(
      <ThemeProvider theme={createTheme({ palette: { mode: 'light' } })}>
        <ExtensionToolbar />
      </ThemeProvider>,
    )
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in dark mode', async () => {
    contribute()
    const { container } = render(
      <ThemeProvider theme={createTheme({ palette: { mode: 'dark' } })}>
        <ExtensionToolbar />
      </ThemeProvider>,
    )
    expect(await axe(container)).toHaveNoViolations()
  })

  it('exposes a contributed entry as a keyboard-focusable, labeled button', () => {
    contribute()
    render(<ExtensionToolbar />)
    const button = screen.getByRole('button', { name: 'Toggle Solar Analysis' })
    expect(button.tabIndex).not.toBe(-1)
  })
})
