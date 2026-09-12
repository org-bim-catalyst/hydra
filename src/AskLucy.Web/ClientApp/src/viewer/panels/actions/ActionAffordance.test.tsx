import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ActionAffordance } from './ActionAffordance'

const selectMock = vi.fn()

vi.mock('../../engine/viewerEngineInstance', () => ({
  viewerEngine: { select: (layerId: string, elementId: string) => selectMock(layerId, elementId) },
}))

describe('ActionAffordance (spec FR-013/FR-014)', () => {
  it('renders a valid action as activatable and keyboard reachable', async () => {
    selectMock.mockReturnValue({ ok: true })
    const user = userEvent.setup()
    render(<ActionAffordance action={{ command: 'select', args: { layerId: 'l1', elementId: 'e1' } }}>Wall</ActionAffordance>)

    const control = screen.getByRole('button', { name: 'Wall' })
    expect(control).toBeInTheDocument()

    control.focus()
    await user.keyboard('{Enter}')
    expect(selectMock).toHaveBeenCalledWith('l1', 'e1')
  })

  it('renders plain content, not activatable, when no action is given', () => {
    render(<ActionAffordance>Plain text</ActionAffordance>)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.getByText('Plain text')).toBeInTheDocument()
  })

  it('renders plain content, not activatable, when the action names a disallowed command', () => {
    render(<ActionAffordance action={{ command: 'removeLayer', args: {} }}>Danger</ActionAffordance>)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.getByText('Danger')).toBeInTheDocument()
  })

  it('renders plain content, not activatable, when the action arguments are malformed', () => {
    render(<ActionAffordance action={{ command: 'select', args: { layerId: 'l1' } }}>Bad args</ActionAffordance>)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows an inline message when the action is performed but cannot take effect', async () => {
    selectMock.mockReturnValue({ ok: false, error: 'No selectable element "e1" on layer "l1".' })
    const user = userEvent.setup()
    render(<ActionAffordance action={{ command: 'select', args: { layerId: 'l1', elementId: 'e1' } }}>Wall</ActionAffordance>)

    await user.click(screen.getByRole('button', { name: 'Wall' }))

    expect(screen.getByRole('alert')).toHaveTextContent('No selectable element "e1" on layer "l1".')
  })
})
