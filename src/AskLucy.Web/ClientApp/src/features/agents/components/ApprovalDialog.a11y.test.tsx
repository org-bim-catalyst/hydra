import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import type { AgentApproval } from '../api/agentExecutionsApi'
import { ApprovalDialog } from './ApprovalDialog'

expect.extend(toHaveNoViolations)

const approval: AgentApproval = {
  id: 'approval-1',
  agentToolCallId: 'tool-call-1',
  intendedActionDescription: 'Execute FakeHighRiskTool',
  intendedParametersJson: '{"action":"read-only"}',
  decision: 'Pending',
  decidedByUserId: null,
  wasPolicyBased: false,
  decidedAtUtc: null,
}

describe('ApprovalDialog accessibility (spec.md FR-025/FR-027, User Story 3)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { baseElement, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <ApprovalDialog executionId="execution-1" approval={approval} />
      </QueryClientProvider>,
    )

    await findByText('This agent wants to take an action')

    const results = await axe(baseElement)
    expect(results).toHaveNoViolations()
  })
})
