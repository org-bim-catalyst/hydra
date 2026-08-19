import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ExpandedChatPanel } from './ExpandedChatPanel'

describe('ExpandedChatPanel', () => {
  it('shows identity, online status, and the active-language flag in its header (FR-008)', () => {
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    expect(screen.getByText('Ask Lucy')).toBeInTheDocument()
    expect(screen.getByText('Online')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'Response language: English' })).toBeInTheDocument()
  })

  it('does not render the vertical voice analyzer (FR-007)', () => {
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    expect(screen.queryByRole('img', { name: /Voice status:/ })).not.toBeInTheDocument()
  })

  it('calls onCollapse when the back control is activated', () => {
    const onCollapse = vi.fn()
    render(
      <ExpandedChatPanel
        open
        onCollapse={onCollapse}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    screen.getByRole('button', { name: 'Collapse' }).click()
    expect(onCollapse).toHaveBeenCalledTimes(1)
  })

  it('has a minimal icon-only new-chat control, not a text-labeled "+ New chat" button (FR-012/FR-014)', () => {
    const onNewChat = vi.fn()
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={onNewChat}
        language="en"
        contentId="ask-lucy-assistant-content"
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    expect(screen.queryByText('New chat')).not.toBeInTheDocument()
    screen.getByRole('button', { name: 'Start new conversation' }).click()
    expect(onNewChat).toHaveBeenCalledTimes(1)
  })

  it('moves focus to the first focusable child when it opens', () => {
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    expect(screen.getByRole('button', { name: 'First focusable' })).toHaveFocus()
  })
})
