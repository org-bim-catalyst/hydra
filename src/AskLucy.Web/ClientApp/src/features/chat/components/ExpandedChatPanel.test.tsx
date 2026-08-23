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
        isFullHeight={false}
        onToggleHeight={() => {}}
        isMuted={false}
        onToggleMute={() => {}}
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
        isFullHeight={false}
        onToggleHeight={() => {}}
        isMuted={false}
        onToggleMute={() => {}}
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
        isFullHeight={false}
        onToggleHeight={() => {}}
        isMuted={false}
        onToggleMute={() => {}}
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
        isFullHeight={false}
        onToggleHeight={() => {}}
        isMuted={false}
        onToggleMute={() => {}}
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
        isFullHeight={false}
        onToggleHeight={() => {}}
        isMuted={false}
        onToggleMute={() => {}}
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    expect(screen.getByRole('button', { name: 'First focusable' })).toHaveFocus()
  })
})

describe('ExpandedChatPanel — full-height toggle (specs/030-composer-panel-refinements FR-007/FR-008/FR-008a)', () => {
  it('renders the "Expand to full height" affordance by default (isFullHeight=false)', () => {
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
        isFullHeight={false}
        onToggleHeight={() => {}}
        isMuted={false}
        onToggleMute={() => {}}
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    expect(screen.getByRole('button', { name: 'Expand to full height' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Collapse to half height' })).not.toBeInTheDocument()
  })

  it('renders the "Collapse to half height" affordance when isFullHeight=true', () => {
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
        isFullHeight={true}
        onToggleHeight={() => {}}
        isMuted={false}
        onToggleMute={() => {}}
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    expect(screen.getByRole('button', { name: 'Collapse to half height' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Expand to full height' })).not.toBeInTheDocument()
  })

  it('calls onToggleHeight when the resize/toggle button is clicked', () => {
    const onToggleHeight = vi.fn()
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
        isFullHeight={false}
        onToggleHeight={onToggleHeight}
        isMuted={false}
        onToggleMute={() => {}}
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    screen.getByRole('button', { name: 'Expand to full height' }).click()
    expect(onToggleHeight).toHaveBeenCalledTimes(1)
  })

  it('places the resize/toggle button immediately after the new-chat button, not next to Collapse', () => {
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
        isFullHeight={false}
        onToggleHeight={() => {}}
        isMuted={false}
        onToggleMute={() => {}}
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    const headerButtons = screen.getAllByRole('button').map((button) => button.getAttribute('aria-label'))
    const newChatIndex = headerButtons.indexOf('Start new conversation')
    const resizeIndex = headerButtons.indexOf('Expand to full height')
    expect(resizeIndex).toBe(newChatIndex + 1)
  })
})

// specs/031-voice-controls-redesign FR-011/FR-012, US6 — the mute/unmute-Lucy control
// relocated from ChatComposer's footer into this header, next to Lucy's portrait/name.
describe('ExpandedChatPanel — mute/unmute-Lucy control (specs/031-voice-controls-redesign FR-011/FR-012)', () => {
  it('renders next to the portrait/name block, showing "Mute Lucy" when not muted', () => {
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
        isFullHeight={false}
        onToggleHeight={() => {}}
        isMuted={false}
        onToggleMute={() => {}}
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    expect(screen.getByRole('button', { name: 'Mute Lucy' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Unmute Lucy' })).not.toBeInTheDocument()
  })

  it('shows "Unmute Lucy" when muted', () => {
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
        isFullHeight={false}
        onToggleHeight={() => {}}
        isMuted={true}
        onToggleMute={() => {}}
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    expect(screen.getByRole('button', { name: 'Unmute Lucy' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Mute Lucy' })).not.toBeInTheDocument()
  })

  it('calls onToggleMute when clicked', () => {
    const onToggleMute = vi.fn()
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
        isFullHeight={false}
        onToggleHeight={() => {}}
        isMuted={false}
        onToggleMute={onToggleMute}
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    screen.getByRole('button', { name: 'Mute Lucy' }).click()
    expect(onToggleMute).toHaveBeenCalledTimes(1)
  })

  it('sits immediately after the name/status block, before the language flag', () => {
    render(
      <ExpandedChatPanel
        open
        onCollapse={() => {}}
        onNewChat={() => {}}
        language="en"
        contentId="ask-lucy-assistant-content"
        isFullHeight={false}
        onToggleHeight={() => {}}
        isMuted={false}
        onToggleMute={() => {}}
      >
        <button type="button">First focusable</button>
      </ExpandedChatPanel>,
    )
    const headerButtons = screen.getAllByRole('button').map((button) => button.getAttribute('aria-label'))
    // "Ask Lucy"/"Online" (the name/status block) isn't a button, so the mute control is
    // simply the first header button after Collapse.
    expect(headerButtons.indexOf('Mute Lucy')).toBe(headerButtons.indexOf('Collapse') + 1)
  })
})
