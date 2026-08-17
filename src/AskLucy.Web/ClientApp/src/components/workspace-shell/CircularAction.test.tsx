import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { CircularAction } from './CircularAction'

function Controlled({ initialExpanded = false, disabled = false }: { initialExpanded?: boolean; disabled?: boolean }) {
  const [expanded, setExpanded] = useState(initialExpanded)
  return (
    <CircularAction
      id="layers"
      label="Layers"
      icon={<span>icon</span>}
      expanded={expanded}
      onToggle={() => setExpanded((e) => !e)}
      disabled={disabled}
    >
      <button type="button">First action</button>
    </CircularAction>
  )
}

describe('CircularAction', () => {
  it('renders collapsed with aria-expanded=false', () => {
    render(<Controlled />)
    expect(screen.getByRole('button', { name: 'Layers' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('mouse click calls onToggle and expands', async () => {
    const user = userEvent.setup()
    render(<Controlled />)
    await user.click(screen.getByRole('button', { name: 'Layers' }))
    expect(screen.getByRole('button', { name: 'Layers' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('Enter on the focused trigger expands it', async () => {
    const user = userEvent.setup()
    render(<Controlled />)
    const trigger = screen.getByRole('button', { name: 'Layers' })
    trigger.focus()
    await user.keyboard('{Enter}')
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
  })

  it('Space on the focused trigger expands it', async () => {
    const user = userEvent.setup()
    render(<Controlled />)
    const trigger = screen.getByRole('button', { name: 'Layers' })
    trigger.focus()
    await user.keyboard(' ')
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
  })

  it('Escape while expanded collapses it and returns focus to the trigger', async () => {
    const user = userEvent.setup()
    render(<Controlled initialExpanded />)
    const trigger = screen.getByRole('button', { name: 'Layers' })
    await user.click(screen.getByRole('button', { name: 'First action' }))
    await user.keyboard('{Escape}')
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(trigger).toHaveFocus()
  })

  it('disabled blocks activation', async () => {
    const user = userEvent.setup()
    render(<Controlled disabled />)
    const trigger = screen.getByRole('button', { name: 'Layers' })
    expect(trigger).toBeDisabled()
    await user.click(trigger).catch(() => {})
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
  })

  it('calls onToggle when clicking away while expanded', async () => {
    const onToggle = vi.fn()
    render(
      <div>
        <CircularAction id="layers" label="Layers" icon={<span>icon</span>} expanded onToggle={onToggle}>
          <button type="button">First action</button>
        </CircularAction>
        <button type="button">Outside</button>
      </div>,
    )
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Outside' }))
    expect(onToggle).toHaveBeenCalledTimes(1)
  })

  it('exposes aria-controls pointing at the expanded content region', () => {
    render(<Controlled initialExpanded />)
    const trigger = screen.getByRole('button', { name: 'Layers' })
    const controlsId = trigger.getAttribute('aria-controls')
    expect(controlsId).toBeTruthy()
    expect(document.getElementById(controlsId as string)).toBeInTheDocument()
  })

  it('shows a badge dot when badge is true', () => {
    render(
      <CircularAction id="chat" label="Chat" icon={<span>icon</span>} expanded={false} onToggle={() => {}} badge>
        <div />
      </CircularAction>,
    )
    const badgeDot = document.querySelector('.MuiBadge-dot')
    expect(badgeDot).not.toHaveClass('MuiBadge-invisible')
  })
})
