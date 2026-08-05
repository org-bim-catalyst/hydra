import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import type { DocumentDashboardSummary } from '../api/documentsApi'
import { ProcessingDashboard } from './ProcessingDashboard'

expect.extend(toHaveNoViolations)

const summary: DocumentDashboardSummary = {
  queueDepth: 3,
  inProgressCount: 1,
  completedTodayCount: 12,
  failedCount: 2,
  retryQueue: [{ documentId: 'doc-1', fileName: 'report.pdf', failureReason: 'OCR engine timed out.' }],
  statistics: {
    totalDocuments: 40,
    totalStorageBytes: 52_428_800,
    averageProcessingDurationMs: 4200,
    fileTypeDistribution: { Pdf: 30, Word: 10 },
    languageDistribution: { en: 40 },
  },
}

describe('ProcessingDashboard accessibility (FR-045, US6 AC1, constitution §7 FR-052)', () => {
  it('has no automatically detectable a11y violations with a populated dashboard, including the retry queue', async () => {
    const { container, findByText } = render(<ProcessingDashboard data={summary} isLoading={false} isError={false} />)

    await findByText('report.pdf')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in the loading state', async () => {
    const { container } = render(<ProcessingDashboard data={undefined} isLoading={true} isError={false} />)

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in the error state', async () => {
    const { container, findByRole } = render(<ProcessingDashboard data={undefined} isLoading={false} isError={true} />)

    await findByRole('alert')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
