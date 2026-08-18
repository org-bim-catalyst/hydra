import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import type { MemoryListItem } from '../api/memoryApi'
import { MemoryEditDialog } from './MemoryEditDialog'

expect.extend(toHaveNoViolations)

const memory: MemoryListItem = {
  id: 'memory-1',
  category: 'PersonalFact',
  content: 'Works on BIM coordination for a mechanical contractor',
  state: 'Active',
  isSensitive: false,
  projectId: null,
  projectName: null,
  sourceType: 'PassiveConversationAnalysis',
  sourceConversationId: null,
  importance: 0.72,
  confidence: 0.9,
  lastReinforcedAtUtc: '2026-08-08T14:03:00Z',
  createdAtUtc: '2026-07-20T09:11:00Z',
}

describe('MemoryEditDialog accessibility (tasks.md T099, spec.md FR-019, User Story 2 AC2)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { baseElement, findByLabelText } = render(
      <MemoryEditDialog open memory={memory} submitting={false} errorMessage={null} onSubmit={vi.fn()} onClose={vi.fn()} />,
    )

    // The field is required, so MUI appends a visible "*" to the label text — match by prefix.
    await findByLabelText(/^Content/)

    const results = await axe(baseElement)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations while showing a save error', async () => {
    const { baseElement, findByLabelText } = render(
      <MemoryEditDialog open memory={memory} submitting={false} errorMessage="Save failed. Please try again." onSubmit={vi.fn()} onClose={vi.fn()} />,
    )

    await findByLabelText(/^Content/)

    const results = await axe(baseElement)
    expect(results).toHaveNoViolations()
  })
})
