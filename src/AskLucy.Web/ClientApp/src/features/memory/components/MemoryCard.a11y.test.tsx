import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import type { MemoryListItem } from '../api/memoryApi'
import { MemoryCard } from './MemoryCard'

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

describe('MemoryCard accessibility (tasks.md T099, spec.md FR-017)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container, findByTestId } = render(<MemoryCard memory={memory} onEdit={vi.fn()} onDelete={vi.fn()} />)

    await findByTestId('memory-card')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations for a sensitive, project-scoped, pending memory', async () => {
    const { container, findByTestId } = render(
      <MemoryCard
        memory={{ ...memory, isSensitive: true, state: 'PendingApproval', projectId: 'project-1', projectName: 'Riverside Tower' }}
        onEdit={vi.fn()}
        onDelete={vi.fn()}
      />,
    )

    await findByTestId('memory-card')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
