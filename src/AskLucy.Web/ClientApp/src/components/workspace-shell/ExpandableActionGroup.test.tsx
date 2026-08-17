import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ExpandableActionGroup } from './ExpandableActionGroup'

describe('ExpandableActionGroup', () => {
  it('renders actions as an icon-only row by default', () => {
    render(
      <ExpandableActionGroup
        actions={[{ id: 'a', label: 'Action A', icon: <span aria-hidden="true">A</span> }]}
      />,
    )
    expect(screen.getByRole('button', { name: 'Action A' })).toBeInTheDocument()
  })

  it('calls onSelect when a row action is clicked', async () => {
    const onSelect = vi.fn()
    const user = userEvent.setup()
    render(<ExpandableActionGroup actions={[{ id: 'a', label: 'Action A', onSelect }]} />)
    await user.click(screen.getByRole('button', { name: 'Action A' }))
    expect(onSelect).toHaveBeenCalledTimes(1)
  })

  it('renders actions as an icon+label list when layout is list', () => {
    render(
      <ExpandableActionGroup layout="list" actions={[{ id: 'a', label: 'Profile' }]} />,
    )
    expect(screen.getByRole('button', { name: 'Profile' })).toBeInTheDocument()
  })

  it('highlights a highlighted action distinctly', () => {
    render(
      <ExpandableActionGroup
        actions={[
          { id: 'a', label: 'Normal' },
          { id: 'b', label: 'Run', highlighted: true },
        ]}
      />,
    )
    expect(screen.getByRole('button', { name: 'Run' })).toBeInTheDocument()
  })
})
