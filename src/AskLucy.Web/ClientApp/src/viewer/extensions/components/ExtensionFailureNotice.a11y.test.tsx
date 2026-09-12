import { act, render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { createTheme, ThemeProvider } from '@mui/material'
import { afterEach, describe, expect, it } from 'vitest'
import { viewerExtensionRegistry } from '../registry'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'
import { ExtensionFailureNotice } from './ExtensionFailureNotice'

expect.extend(toHaveNoViolations)

const initialState = useViewerExtensionStore.getState()

function registerFailedExtension(id: string) {
  viewerExtensionRegistry.register({
    id,
    manifest: { displayName: 'Solar Analysis', description: 'test' },
    start: () => {},
    stop: () => {},
  })
  act(() => {
    useViewerExtensionStore.getState().setLifecycle(id, 'failed', 'start blew up')
  })
}

describe('ExtensionFailureNotice accessibility (constitution §7)', () => {
  afterEach(() => {
    useViewerExtensionStore.setState(initialState, true)
  })

  it('has no automatically detectable a11y violations in light mode', async () => {
    registerFailedExtension('ext-a11y-light')
    const { container } = render(
      <ThemeProvider theme={createTheme({ palette: { mode: 'light' } })}>
        <ExtensionFailureNotice />
      </ThemeProvider>,
    )
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in dark mode', async () => {
    registerFailedExtension('ext-a11y-dark')
    const { container } = render(
      <ThemeProvider theme={createTheme({ palette: { mode: 'dark' } })}>
        <ExtensionFailureNotice />
      </ThemeProvider>,
    )
    expect(await axe(container)).toHaveNoViolations()
  })

  it('is announced rather than purely visual — exposed with role="status"', () => {
    registerFailedExtension('ext-a11y-status')
    render(<ExtensionFailureNotice />)
    expect(screen.getByRole('status')).toHaveTextContent('unavailable')
  })
})
