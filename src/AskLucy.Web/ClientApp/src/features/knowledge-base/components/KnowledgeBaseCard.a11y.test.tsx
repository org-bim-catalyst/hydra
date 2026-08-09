import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import type { KnowledgeBaseSummary } from '../api/knowledgeBasesApi'
import { KnowledgeBaseCard } from './KnowledgeBaseCard'

expect.extend(toHaveNoViolations)

const knowledgeBase: KnowledgeBaseSummary = {
  id: 'kb-1',
  name: 'BIM Standards',
  description: 'Company-wide modeling standards.',
  status: 'Active',
  color: '#4F46E5',
  icon: null,
  categoryId: null,
  tags: ['revit', 'standards'],
  isFavorite: true,
  isPinned: false,
  documentCount: 12,
  totalPageCount: 340,
  storageSizeBytes: 2_097_152,
  createdAtUtc: '2026-07-01T00:00:00Z',
  lastUpdatedAtUtc: '2026-08-01T00:00:00Z',
  isDeleted: false,
}

const noop = {
  onOpen: vi.fn(),
  onEdit: vi.fn(),
  onActivate: vi.fn(),
  onArchive: vi.fn(),
  onDelete: vi.fn(),
  onRestore: vi.fn(),
  onPurge: vi.fn(),
  onToggleFavorite: vi.fn(),
  onTogglePin: vi.fn(),
  onDuplicate: vi.fn(),
  onExport: vi.fn(),
}

describe('KnowledgeBaseCard accessibility (FR-026, FR-041)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container, findByTestId } = render(<KnowledgeBaseCard knowledgeBase={knowledgeBase} {...noop} />)

    await findByTestId('knowledge-base-card')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations for a deleted knowledge base (reduced action set)', async () => {
    const { container, findByTestId } = render(
      <KnowledgeBaseCard
        knowledgeBase={{ ...knowledgeBase, isDeleted: true }}
        onEdit={noop.onEdit}
        onActivate={noop.onActivate}
        onArchive={noop.onArchive}
        onDelete={noop.onDelete}
        onRestore={noop.onRestore}
        onPurge={noop.onPurge}
      />,
    )

    await findByTestId('knowledge-base-card')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
