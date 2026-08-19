import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import type { WorkflowApproval } from '../api/workflowExecutionsApi'
import { ApprovalDialog } from './ApprovalDialog'

expect.extend(toHaveNoViolations)

const approval: WorkflowApproval = {
  id: 'approval-1',
  workflowExecutionNodeId: 'node-1',
  intendedActionDescription: 'Execute KnowledgeSearchTool at step \'rag\'',
  parametersJson: '{"query":"contract terms"}',
  decision: 'Pending',
  wasPolicyBased: false,
  decidedByUserId: null,
  decidedAtUtc: null,
}

describe('ApprovalDialog accessibility (spec.md User Story 5)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { baseElement, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <ApprovalDialog executionId="execution-1" approval={approval} />
      </QueryClientProvider>,
    )

    await findByText('This workflow step needs your approval')

    const results = await axe(baseElement)
    expect(results).toHaveNoViolations()
  })
})
