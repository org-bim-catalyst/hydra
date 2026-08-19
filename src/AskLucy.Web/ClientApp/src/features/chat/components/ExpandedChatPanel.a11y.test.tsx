import { render } from '@testing-library/react'
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
      >
        <p>Conversation content</p>
        <button type="button">Send</button>
      </ExpandedChatPanel>,
    )
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
