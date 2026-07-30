import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { useAssistantPanelStore } from '../../../store/assistantPanelStore'
import { AssistantToggleFab } from './AssistantToggleFab'

function resetStore() {
  useAssistantPanelStore.setState({ isOpen: true, hasUnreadWhileCollapsed: false })
}

describe('AssistantToggleFab', () => {
  beforeEach(() => {
    resetStore()
  })

  it('toggles the panel open/collapsed state on click', async () => {
    const user = userEvent.setup()
    render(<AssistantToggleFab />)

    expect(screen.getByRole('button', { name: 'Collapse Ask Lucy assistant' })).toBeInTheDocument()
    await user.click(screen.getByRole('button'))
    expect(useAssistantPanelStore.getState().isOpen).toBe(false)
  })

  it('shows the unread indicator when hasUnreadWhileCollapsed is true (FR-016)', () => {
    useAssistantPanelStore.setState({ isOpen: false, hasUnreadWhileCollapsed: true })
    render(<AssistantToggleFab />)

    expect(
      screen.getByRole('button', { name: 'Expand Ask Lucy assistant — new message' }),
    ).toBeInTheDocument()
    const badgeDot = document.querySelector('.MuiBadge-dot')
    expect(badgeDot).not.toHaveClass('MuiBadge-invisible')
  })

  it('has no unread indicator by default while collapsed', () => {
    useAssistantPanelStore.setState({ isOpen: false, hasUnreadWhileCollapsed: false })
    render(<AssistantToggleFab />)

    const badgeDot = document.querySelector('.MuiBadge-dot')
    expect(badgeDot).toHaveClass('MuiBadge-invisible')
  })

  it('clears the unread indicator on click (opening the panel)', async () => {
    useAssistantPanelStore.setState({ isOpen: false, hasUnreadWhileCollapsed: true })
    const user = userEvent.setup()
    render(<AssistantToggleFab />)

    await user.click(screen.getByRole('button'))

    expect(useAssistantPanelStore.getState().isOpen).toBe(true)
    expect(useAssistantPanelStore.getState().hasUnreadWhileCollapsed).toBe(false)
    const badgeDot = document.querySelector('.MuiBadge-dot')
    expect(badgeDot).toHaveClass('MuiBadge-invisible')
  })
})
