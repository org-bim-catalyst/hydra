import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { beforeEach, describe, expect, it } from 'vitest'
import { useWorkspaceOverlayStore } from '../../store/workspaceOverlayStore'
import { FloatingPanel } from './FloatingPanel'

expect.extend(toHaveNoViolations)

function resetStore() {
  useWorkspaceOverlayStore.setState({ expandedControlId: 'chat', viewMode: 'isometric', unreadControlIds: new Set() })
}

describe('FloatingPanel accessibility', () => {
  beforeEach(() => {
    resetStore()
  })

  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(
      <FloatingPanel controlId="chat" titleId="Ask Lucy assistant" onRequestClose={() => {}}>
        <p>Conversation content</p>
        <button type="button">Send</button>
      </FloatingPanel>,
    )
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
