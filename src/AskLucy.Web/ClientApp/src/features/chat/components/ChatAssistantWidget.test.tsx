import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ChatAssistantWidget } from './ChatAssistantWidget'

/**
 * `ChatAssistantWidget` itself is a thin, fixed-position anchor (research.md #1/#11) — the
 * `workspaceOverlayStore` read/write and expand/collapse behavior it was originally
 * scoped to live in `ConversationView` (rendered as its `children`), which must remain
 * directly testable in isolation since many existing tests render it standalone outside
 * any wrapper (see `ChatPage.tsx`'s doc comment). That store-driven behavior — toggling
 * `expandedControlId`, mutual exclusivity with the other five controls, Escape-collapses-
 * and-returns-focus — is covered end-to-end in `ChatPage.test.tsx` ("collapsing chat
 * leaves the rest of the workspace interactive", "the collapsed chat widget is
 * keyboard-operable..."). This file covers the positioning shell's own, narrower contract.
 */
describe('ChatAssistantWidget', () => {
  it('renders its children', () => {
    render(
      <ChatAssistantWidget>
        <button type="button">Expand Ask Lucy assistant</button>
      </ChatAssistantWidget>,
    )
    expect(screen.getByRole('button', { name: 'Expand Ask Lucy assistant' })).toBeInTheDocument()
  })
})
