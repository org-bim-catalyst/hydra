import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { useViewerEngineStore } from '../../../viewer/store/viewerEngineStore'
import { RotationToggleButton } from './RotationToggleButton'

expect.extend(toHaveNoViolations)

const initialState = useViewerEngineStore.getState()

describe('RotationToggleButton (FR-014)', () => {
  beforeEach(() => {
    useViewerEngineStore.setState(initialState, true)
  })

  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(<RotationToggleButton />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('is keyboard operable and its label/aria-pressed reflect the current rotation state', async () => {
    useViewerEngineStore.setState((s) => ({ camera: { ...s.camera, rotationEnabled: true } }))
    const user = userEvent.setup()
    render(<RotationToggleButton />)

    const button = screen.getByRole('button', { name: 'Stop rotation' })
    expect(button).toHaveAttribute('aria-pressed', 'true')

    button.focus()
    await user.keyboard('{Enter}')

    expect(useViewerEngineStore.getState().camera.rotationEnabled).toBe(false)
    expect(screen.getByRole('button', { name: 'Start rotation' })).toHaveAttribute(
      'aria-pressed',
      'false',
    )
  })
})
