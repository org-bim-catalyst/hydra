import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { useThemeStore } from '../../store/themeStore'
import { ThemeToggleButton } from './ThemeToggleButton'

describe('ThemeToggleButton', () => {
  beforeEach(() => {
    useThemeStore.setState({ mode: 'light' })
  })

  it('toggles the theme mode on click', async () => {
    const user = userEvent.setup()
    render(<ThemeToggleButton />)
    await user.click(screen.getByRole('button', { name: 'Switch to dark mode' }))
    expect(useThemeStore.getState().mode).toBe('dark')
  })
})
