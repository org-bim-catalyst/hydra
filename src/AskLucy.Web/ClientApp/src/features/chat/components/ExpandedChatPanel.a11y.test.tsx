import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import { ExpandedChatPanel } from './ExpandedChatPanel'

expect.extend(toHaveNoViolations)

describe('ExpandedChatPanel accessibility', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(
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
        <p>Conversation content</p>
        <button type="button">Send</button>
      </ExpandedChatPanel>,
    )
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations at full height', async () => {
    const { container } = render(
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
        <p>Conversation content</p>
        <button type="button">Send</button>
      </ExpandedChatPanel>,
    )
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

// specs/030-composer-panel-refinements FR-009/FR-011, T027 — tooltips discoverable for every
// header icon-only button, and the resize/toggle button's placement matches spec.md's
// correction (immediately after "+", not next to Collapse).
describe('ExpandedChatPanel — header tooltips and placement (specs/030-composer-panel-refinements FR-009/FR-011)', () => {
  // userEvent.hover, not fireEvent.focus — MUI Tooltip only opens on programmatic `.focus()`
  // when the browser's focus-visible heuristic is satisfied (real keyboard navigation),
  // which fireEvent.focus() doesn't trigger in jsdom; hover has no such precondition and
  // exercises the same open/close logic. getByRole('tooltip') + waitFor (not findByText) —
  // unlike this codebase's MUI Menu/Grow-transition jsdom issue (see ChatComposer.test.tsx's
  // mode-switch menu test), Tooltip's Popper renders without a comparable jsdom crash here.
  it('shows a tooltip for Collapse on hover', async () => {
    const user = userEvent.setup()
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
    await user.hover(screen.getByRole('button', { name: 'Collapse' }))
    await waitFor(() => expect(screen.getByRole('tooltip')).toHaveTextContent('Collapse'))
  })

  it('shows a tooltip for Start new conversation on hover', async () => {
    const user = userEvent.setup()
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
    await user.hover(screen.getByRole('button', { name: 'Start new conversation' }))
    await waitFor(() => expect(screen.getByRole('tooltip')).toHaveTextContent('Start new conversation'))
  })

  it('shows a tooltip for the resize/toggle button on hover', async () => {
    const user = userEvent.setup()
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
    await user.hover(screen.getByRole('button', { name: 'Expand to full height' }))
    await waitFor(() => expect(screen.getByRole('tooltip')).toHaveTextContent('Expand to full height'))
  })

  it('positions the resize/toggle button immediately after the new-chat button, not next to Collapse', () => {
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
    expect(headerButtons.indexOf('Expand to full height')).toBe(headerButtons.indexOf('Start new conversation') + 1)
    expect(headerButtons.indexOf('Expand to full height')).not.toBe(headerButtons.indexOf('Collapse') + 1)
  })

  it('shows a tooltip for Mute Lucy on hover (specs/031-voice-controls-redesign FR-011)', async () => {
    const user = userEvent.setup()
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
    await user.hover(screen.getByRole('button', { name: 'Mute Lucy' }))
    await waitFor(() => expect(screen.getByRole('tooltip')).toHaveTextContent('Mute Lucy'))
  })
})
